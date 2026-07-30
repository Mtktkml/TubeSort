using System.Collections;
using System.Collections.Generic;
using TMPro;
using TubeSort.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TubeSort.Game
{
    /// <summary>
    /// Tahtayı ekranda kurar ve tıklamaları çekirdeğe iletir.
    ///
    /// Etkileşim: bir tüpe tıkla (seçilir, yukarı kalkar), sonra başka bir tüpe
    /// tıkla (dökülür). Aynı tüpe tekrar tıklarsan seçim iptal olur.
    ///
    /// Sahne kurulumu gerektirmez: boş bir GameObject'e bu bileşeni ekleyip
    /// Play'e basman yeterli, gerisini kod yapar.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("Yerleşim")]
        [Tooltip("Yan yana duran iki tüp arasındaki boşluk.")]
        [SerializeField] private float horizontalGap = 0.35f;

        [Tooltip("İki satır arasındaki boşluk.")]
        [SerializeField] private float verticalGap = 1f;

        [Tooltip("Tahtanın etrafında bırakılacak boşluk, ekranın oranı olarak.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float screenMargin = 0.1f;

        [Header("Animasyon süreleri (sn)")]
        [Tooltip("Kaynak tüpün hedefe kayma süresi.")]
        [SerializeField] private float slideDuration = 0.24f;
        [Tooltip("Sıvının dökülme (seviye değişimi) süresi.")]
        [SerializeField] private float pourDuration = 0.4f;
        [Tooltip("Eğim açısı SmoothDamp tepki süresi (kritik sönümleme, aşım yok).")]
        [SerializeField] private float angleSmoothTime = 0.12f;
        [Tooltip("Emniyet: dökme bu süre içinde bitmezse hata loglanıp zorla tamamlanır. " +
                 "Süreler play mode'da yavaşlatıldığında bunu da büyütmen gerekebilir.")]
        [SerializeField] private float watchdogSeconds = 6f;

        private Board board;
        private readonly MoveHistory history = new MoveHistory();
        private ColorPalette palette;
        private Sprite unitSprite;
        private Material liquidMaterial;
        private Material streamMaterial;
        private Sprite tubeSprite;              // Resources/Sprites görselleri
        private Sprite collarSprite;
        private Sprite corkSprite;
        private Sprite collarFrontTopSprite;    // yakadan eğri maskeyle üretilen ön
        private Sprite collarFrontBottomSprite; // sandviç parçaları; doku + sprite bizde,
                                                // OnDestroy temizler
        private readonly List<TubeView> tubeViews = new List<TubeView>();

        // Akış görselleri havuzu: her aktif dökme kendi akışını kullanır.
        // Ayrık eşzamanlı dökmeler için gerektikçe büyür, tüpler yeniden
        // kurulunca yok edilmez (yeniden kullanılır).
        private readonly List<StreamView> streamPool = new List<StreamView>();
        private readonly HashSet<StreamView> streamsInUse = new HashSet<StreamView>();

        private UndoButtonView undoButton;
        private PilotNextButtonView pilotNextButton;
        private ButtonView prevButton;      // önceki level (nav)
        private ButtonView restartButton;   // leveli baştan
        private ButtonView addTubeButton;   // +boş tüp
        private TextMeshPro deadlockBannerTop;    // çıkmaz uyarısı üst parça (gizli başlar)
        private TextMeshPro deadlockBannerBottom; // çıkmaz uyarısı alt parça
        private TextMeshPro levelTitle;     // üst-orta "LEVEL x.y"
        private Board pristineBoard;         // mevcut levelin bozulmamış kopyası (restart için)

        private int selectedIndex = -1;

        // Süren dökme/geri-alma animasyonları. Bir tüp herhangi bir işin kaynağı
        // ya da hedefiyse meşguldür ve yeni dökmeye kapalıdır. Ayrık tüp çiftleri
        // aynı anda dökülebilir.
        private readonly List<PourJob> activeJobs = new List<PourJob>();
        private bool AnyAnimating => activeJobs.Count > 0;

        /// <summary>Süren bir dökme/geri-alma animasyonu: kaynak/hedef indeksleri,
        /// kaynağın çizim-sırası offset'i (eşzamanlı kaynaklar farklı bantta durur)
        /// ve eğim yönü (+1 hedef sağda, -1 solda; 2. gelen ilkinin tersini alır).</summary>
        private sealed class PourJob
        {
            public int FromIndex;
            public int ToIndex;
            public int SortingOffset;
            public float Direction;
            // Dökme fazı sürüyor mu? Geri dönüş (kaynak doğrulma) fazında false
            // olur ama iş hâlâ activeJobs'ta kalır (kaynak meşgul). Hedef
            // tamamlanmasının "son dökücü" kararı buna bakar.
            public bool Pouring;
            // Bu dökmenin hedefe eklediği doluluk miktarı (dünya ölçüsü) ve o an
            // uygulanmış ilerlemesi (0-1). Hedef doluluğu, aktif dökmelerin
            // katkılarının TOPLAMIyla artar (max değil) — her kaynak hedefi kendi
            // payınca yükseltir, biri erken bitse de kalan artışı sürer.
            public float FillAmount;
            public float FillProgress;
        }

        /// <summary>Aktif işlerin kullanmadığı en küçük offset bandını (10, 20, …) verir:
        /// eşzamanlı eğik kaynaklar birbirinin üstünde tutarlı katmanlansın.</summary>
        private int AllocateSortingOffset()
        {
            for (int band = 1; ; band++)
            {
                int offset = band * 10;
                bool taken = false;
                foreach (PourJob job in activeJobs)
                {
                    if (job.SortingOffset == offset) { taken = true; break; }
                }
                if (!taken) return offset;
            }
        }

        /// <summary>Verilen tüp o an bir dökme/geri-almanın kaynağı ya da hedefi mi?</summary>
        private bool IsBusy(int index)
        {
            foreach (PourJob job in activeJobs)
                if (job.FromIndex == index || job.ToIndex == index)
                    return true;
            return false;
        }

        /// <summary>Bu tüpe o an kaç dökme akıyor (0, 1 ya da 2).</summary>
        private int IncomingCount(int index)
        {
            int count = 0;
            foreach (PourJob job in activeJobs)
                if (job.ToIndex == index) count++;
            return count;
        }

        /// <summary>Bu tüp başka bir dökmenin kaynağı mı (boşalıyor)?</summary>
        private bool IsDraining(int index)
        {
            foreach (PourJob job in activeJobs)
                if (job.FromIndex == index) return true;
            return false;
        }

        /// <summary>Bu hedefe, verilen iş DIŞINDA hâlâ döken başka bir iş var mı?
        /// Hedef tamamlanması (tıpa/halka) yalnız son dökücü bitince yapılsın diye.</summary>
        private bool TargetHasOtherPouring(int toIndex, PourJob self)
        {
            foreach (PourJob job in activeJobs)
                if (job != self && job.ToIndex == toIndex && job.Pouring) return true;
            return false;
        }

        /// <summary>Dökmenin eğim yönü: hedefe o an bir dökme akıyorsa ilkinin
        /// TERSİ (biri sağdan, biri soldan); yoksa doğal yön (hedef sağdaysa +1).</summary>
        private float ComputePourDirection(int fromIndex, int toIndex)
        {
            foreach (PourJob job in activeJobs)
                if (job.ToIndex == toIndex) return -job.Direction;

            float dx = tubeViews[toIndex].RestPosition.x - tubeViews[fromIndex].RestPosition.x;
            return Mathf.Abs(dx) < 0.01f ? 1f : Mathf.Sign(dx);
        }

        private Camera mainCamera;

        /// <summary>pilot_levels.json Resources kaynak adı (uzantısız).</summary>
        private const string PilotResource = "pilot_levels";
        private int pilotIndex = 1;   // 1-tabanlı; hangi pilot leveli gösteriliyor
        private int pilotCount;       // pilot dosyasındaki level sayısı (gezinme sınırı)

        /// <summary>
        /// Start kurulumu tamamlandı mı? LoadBoard bununla karar verir:
        /// kurulumdan önce çağrıldıysa tahtayı saklamak yeter (Start kuracak),
        /// sonra çağrıldıysa görünümlerin yıkılıp yeniden kurulması gerekir.
        /// </summary>
        private bool initialized;

        /// <summary>
        /// Yerleşimin son yapıldığı görüş alanı. Değiştiği kareyi yakalamak için
        /// saklanır: cihaz döndüğünde, katlanabilir telefon açıldığında ya da
        /// ekran bölündüğünde tahtanın yeniden yerleşmesi gerekir.
        /// </summary>
        private Vector2 lastFittedView;

        private void Start()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("MainCamera etiketli bir kamera yok; tıklama çalışamaz.");
                enabled = false;
                return;
            }

            palette = new ColorPalette();
            unitSprite = CreateSquareSprite();

            liquidMaterial = CreateMaterial("Liquid");
            streamMaterial = CreateMaterial("Stream");

            // Cam, yaka ve tıpa sprite (Resources/Sprites); sıvı ve akış shader.
            tubeSprite = LoadSprite(TubeView.TubeSpritePath);
            collarSprite = LoadSprite(TubeView.CollarSpritePath);
            corkSprite = LoadSprite(TubeView.CorkSpritePath);

            if (liquidMaterial == null || streamMaterial == null
                || tubeSprite == null || collarSprite == null || corkSprite == null)
            {
                enabled = false;
                return;
            }

            // Ön sandviç parçaları yakadan BİR kez üretilir ve tüm tüplere
            // paylaştırılır; kesim bilgisi TubeView'da, sahiplik (OnDestroy) burada.
            collarFrontTopSprite = TubeView.CreateCollarFrontTopSprite(collarSprite);
            collarFrontBottomSprite = TubeView.CreateCollarFrontBottomSprite(collarSprite);

            // Tahta önceliği: dışarıdan verilen (LoadBoard) önce; yoksa pilot
            // merdiveni (pilot_levels.json) yüklenir. Testler kendi tahtalarını
            // LoadBoard ile enjekte eder.
            if (board == null)
            {
                pilotCount = LevelLibrary.LevelCount(PilotResource);
                board = LoadPilot(pilotIndex);   // pilotIndex 1'den başlar
            }

            if (board == null)
            {
                Debug.LogError("Hiçbir tahta yüklenemedi (pilot_levels.json eksik olabilir).");
                enabled = false;
                return;
            }

            pristineBoard = board.Clone();   // restart bu kopyayı geri yükler

            BuildViews();
            BuildUndoButton();
            BuildRestartButton();
            BuildAddTubeButton();
            BuildPilotNextButton();
            BuildPrevButton();
            BuildDeadlockBanner();
            BuildLevelTitle();
            ApplyLayout();
            UpdateLevelTitle();
            initialized = true;
        }

        /// <summary>
        /// Geri al butonunu kurar. Buton tahtanın çocuğu değildir: ApplyLayout
        /// tahtayı ekrana sığdırmak için ölçeklerken buton sabit kalmalı.
        /// </summary>
        private void BuildUndoButton()
        {
            var go = new GameObject("UndoButton");
            undoButton = go.AddComponent<UndoButtonView>();
            undoButton.Initialize();
        }

        /// <summary>
        /// Pilot önizlemede sıradaki level butonunu kurar (sağ üst). Yalnız
        /// pilot modunda; telefonda ok tuşu olmadığı için level gezmenin yolu.
        /// </summary>
        private void BuildPilotNextButton()
        {
            var go = new GameObject("PilotNextButton");
            pilotNextButton = go.AddComponent<PilotNextButtonView>();
            pilotNextButton.Initialize();
        }

        private void BuildPrevButton()
        {
            var go = new GameObject("PrevButton");
            prevButton = go.AddComponent<ButtonView>();
            prevButton.Initialize(ButtonView.ButtonKind.Previous);
        }

        private void BuildRestartButton()
        {
            var go = new GameObject("RestartButton");
            restartButton = go.AddComponent<ButtonView>();
            restartButton.Initialize(ButtonView.ButtonKind.Restart);
        }

        private void BuildAddTubeButton()
        {
            var go = new GameObject("AddTubeButton");
            addTubeButton = go.AddComponent<ButtonView>();
            addTubeButton.Initialize(ButtonView.ButtonKind.AddTube);
        }

        /// <summary>
        /// Çıkmaz uyarısını iki TMP parçası olarak kurar (gizli başlar): üst parça
        /// ekranın üstünde, alt parça altında, ikisi de yatayda ortada. Uzun mesaj
        /// dar ekrana sığsın diye ikiye bölündü. TMP varsayılan fontunu kullanır
        /// (TMP Essentials import edilmiş olmalı).
        /// </summary>
        private void BuildDeadlockBanner()
        {
            // Boyut başlık-tahta bandına göre seçilir: uyarı bandın ortasında,
            // okunur ve tüplere değmeden durur.
            deadlockBannerTop = CreateBannerPart("DeadlockBannerTop", "Çıkmaz!", 3.2f);
            deadlockBannerBottom = CreateBannerPart(
                "DeadlockBannerBottom", "Geri al, tüp ekle ya da yeniden başla", 2.3f);
        }

        private TextMeshPro CreateBannerPart(string name, string text, float fontSize)
        {
            var go = new GameObject(name);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = new Color(1f, 0.45f, 0.35f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(7.5f, 3f);
            tmp.sortingOrder = 101;   // tüplerin/butonların üstünde
            go.SetActive(false);      // yalnız çıkmazda görünür
            return tmp;
        }

        /// <summary>
        /// Üst-orta level başlığını kurar (TMP). Metin UpdateLevelTitle ile her
        /// level değişiminde tazelenir.
        /// </summary>
        private void BuildLevelTitle()
        {
            var go = new GameObject("LevelTitle");
            levelTitle = go.AddComponent<TextMeshPro>();
            levelTitle.fontSize = 3.5f;
            levelTitle.color = new Color(1f, 1f, 1f, 0.9f);
            levelTitle.alignment = TextAlignmentOptions.Center;
            levelTitle.rectTransform.sizeDelta = new Vector2(6f, 2f);
            levelTitle.sortingOrder = 101;
        }

        /// <summary>Başlığı mevcut levelin adına göre günceller ("LEVEL 1.1").</summary>
        private void UpdateLevelTitle()
        {
            if (levelTitle == null) return;
            string label = CurrentLabel();
            levelTitle.text = string.IsNullOrEmpty(label) ? "" : $"LEVEL {label}";
        }

        /// <summary>Mevcut levelin ekran adı: pilot merdiveninin label'ı ("1.1").</summary>
        private string CurrentLabel()
        {
            return pilotCount > 0 ? LevelLibrary.LabelOf(PilotResource, pilotIndex) : "";
        }

        /// <summary>
        /// Butonları görüş alanının üst köşelerine dizer. Sol küme (soldan sağa):
        /// geri al, restart, +tüp — oyun aksiyonları. Sağ küme (sağdan sola):
        /// skip, önceki — level navigasyonu. Üst-orta level başlığından ayrık.
        /// Butonlar tahtanın çocuğu değil; tahta ölçeklense de sabit kalırlar.
        /// </summary>
        private void PositionButtons()
        {
            Vector2 view = CameraView;
            Vector3 cam = mainCamera.transform.position;
            float inset = ButtonView.Size;
            float gap = ButtonView.Size * 1.15f;
            float topY = cam.y + view.y * 0.5f - inset;
            float leftX = cam.x - view.x * 0.5f + inset;
            float rightX = cam.x + view.x * 0.5f - inset;

            // Sol küme: geri al, restart, +tüp (soldan sağa).
            PlaceButton(undoButton, leftX, topY);
            PlaceButton(restartButton, leftX + gap, topY);
            PlaceButton(addTubeButton, leftX + 2f * gap, topY);

            // Sağ küme: skip, önceki (sağdan sola).
            PlaceButton(pilotNextButton, rightX, topY);
            PlaceButton(prevButton, rightX - gap, topY);

            // Level başlığı: üst-orta, buton sırasından biraz boşluklu.
            float titleY = cam.y + view.y * TitleFraction;
            if (levelTitle != null)
                levelTitle.transform.position = new Vector3(cam.x, titleY, 0f);

            // Çıkmaz uyarısı: başlık ile tahta tavanı arasındaki bandın ORTASINA
            // yerleşen iki satır. Ofsetler dünya biriminde: TitleClearance da
            // dünya biriminde olduğundan tahta tavanı her zoom'da uyarının
            // altından geçer — uyarı tüplerin üstüne binmez.
            if (deadlockBannerTop != null)
                deadlockBannerTop.transform.position =
                    new Vector3(cam.x, titleY - 0.45f, 0f);
            if (deadlockBannerBottom != null)
                deadlockBannerBottom.transform.position =
                    new Vector3(cam.x, titleY - 0.78f, 0f);
        }

        private static void PlaceButton(MonoBehaviour button, float x, float y)
        {
            if (button != null) button.transform.position = new Vector3(x, y, 0f);
        }

        /// <summary>Aktif tahta. Testlerin ve dış katmanların durum sorgusu için.</summary>
        public Board Board => board;

        /// <summary>Herhangi bir dökme/geri-alma animasyonu sürüyor mu? Testler
        /// bitişini beklemek için kullanır.</summary>
        public bool IsAnimating => AnyAnimating;

        /// <summary>
        /// Dışarıdan tahta yükler: level üreticinin ve testlerin giriş kapısı.
        /// Start'tan önce çağrılırsa kurulum bu tahtayla yapılır; oyun
        /// sırasında çağrılırsa mevcut görünümler yıkılıp yenisi kurulur
        /// (level geçişi de bu yoldan yapılacak).
        /// </summary>
        public void LoadBoard(Board newBoard)
        {
            if (newBoard == null)
            {
                Debug.LogError("LoadBoard'a null tahta verildi; mevcut tahta korunuyor.");
                return;
            }

            board = newBoard;
            pristineBoard = newBoard.Clone();   // restart bu kopyayı geri yükler
            history.Clear(); // eski tahtanın hamleleri yeni tahtada geri alınamaz
            if (initialized)
                RebuildViews();
        }

        /// <summary>
        /// Pilot merdiveninin verilen levelini pilot_levels.json'dan yükler ve
        /// hangi levelde olduğumuzu Console'a yazar. Yalnız pilot önizleme modunda.
        /// </summary>
        private Board LoadPilot(int index)
        {
            Board loaded = LevelLibrary.LoadFrom(PilotResource, index);
            if (loaded != null)
            {
                string label = LevelLibrary.LabelOf(PilotResource, index);
                Debug.Log($"<color=cyan>LEVEL {label}</color> (pilot önizleme {index}/{pilotCount}) — " +
                          $"kapasite {loaded[0].Capacity}, {loaded.TubeCount} tüp");
            }

            return loaded;
        }

        /// <summary>
        /// Pilot önizlemede ok tuşlarıyla (← →) leveller arasında gezer;
        /// 1..pilotCount arasında sarar. Bir geçiş yaptıysa true döner (o kare
        /// tıklama işlenmesin). Klavye yoksa (telefon) sessizce false — dokunuş
        /// akışı bozulmaz.
        /// </summary>
        private bool HandlePilotBrowse()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || pilotCount <= 0) return false;

            int step = 0;
            if (keyboard.rightArrowKey.wasPressedThisFrame) step = 1;
            else if (keyboard.leftArrowKey.wasPressedThisFrame) step = -1;
            if (step == 0) return false;

            StepPilot(step);
            return true;
        }

        /// <summary>
        /// Pilot levelleri arasında verilen adım kadar ilerler (ör. +1 sonraki)
        /// ve 1..pilotCount arasında sarar. Ok tuşları, skip ve önceki butonları
        /// aynı kapıyı kullanır.
        /// </summary>
        private void StepPilot(int step)
        {
            if (pilotCount <= 0) return;
            if (AnyAnimating) return;   // meta aksiyon: animasyon sürerken level değişmez

            pilotIndex = ((pilotIndex - 1 + step + pilotCount) % pilotCount) + 1;
            Board next = LoadPilot(pilotIndex);
            if (next != null)
            {
                LoadBoard(next);
                UpdateLevelTitle();
            }
        }

        /// <summary>
        /// Hamleyi dener: geçerliyse uygular, geçmişe yazar ve animasyonu
        /// başlatır. Dokunuş yöneticisi ve testler aynı kapıyı kullanır.
        /// </summary>
        public bool TryPour(int fromIndex, int toIndex)
        {
            // Kaynak tamamen serbest olmalı (ne boşalıyor ne dolduruluyor).
            if (IsBusy(fromIndex)) return false;
            // Hedef başka bir dökmenin kaynağı olamaz (boşalan tüpe dökülmez).
            if (IsDraining(toIndex)) return false;
            // Bir hedef en fazla 2 gelen alır (karşı taraflardan); 3. reddedilir.
            if (IncomingCount(toIndex) >= 2) return false;

            // Bu dökmenin hedefe ekleyeceği doluluk miktarı: board.Pour öncesi/
            // sonrası hedef seviyesinin farkı (TubeView tüpü canlı board tüpü,
            // Push yerinde değiştirir). Diğer dökmelerden bağımsız kendi payı.
            float fillBefore = tubeViews[toIndex].TargetFillLevel;
            PourResult result = board.Pour(fromIndex, toIndex);
            if (!result.Success) return false;
            float fillAmount = tubeViews[toIndex].TargetFillLevel - fillBefore;

            history.Record(result);
            var job = new PourJob
            {
                FromIndex = fromIndex,
                ToIndex = toIndex,
                SortingOffset = AllocateSortingOffset(),
                Direction = ComputePourDirection(fromIndex, toIndex),
                Pouring = true,
                FillAmount = fillAmount,
            };
            activeJobs.Add(job);
            StartCoroutine(AnimatePour(result, job));
            return true;
        }

        /// <summary>
        /// Son hamleyi geri alır. Animasyon sürerken ve geçmiş boşken çağrı
        /// yok sayılır. Tıpa (varsa) anında kalkar ama sıvı ışınlanmaz:
        /// seviyeler dökmedeki gibi kademeli akar. Kayma/eğilme/akış görseli
        /// yok — geri alma bir hamle değil düzeltmedir.
        /// </summary>
        public void UndoLastMove()
        {
            if (AnyAnimating) return;
            if (!history.TryUndo(board, out PourResult undone)) return;

            ClearSelection();
            // Çıkmaz uyarısını KOŞULLU güncelle (koşulsuz gizleme değil):
            // eşzamanlı dökmede çıkmaza iki hamle birlikte sokmuş olabilir, tek
            // geri alma yetmeyebilir. Banner yalnız tahta gerçekten çözülebilir
            // hâle dönünce kalkar; hâlâ çıkmazsa kalır (undo verisi hemen işlendi).
            RefreshDeadlockBanner();
            StartCoroutine(AnimateUndo(undone));
        }

        /// <summary>
        /// Geri almanın görseli: geri dönen sıvı eski tüpünde kademeli yükselir,
        /// alınan tüpte kademeli alçalır (ikisi paralel). Katman hileleri
        /// dökmedekiyle aynı: yükselen tüpte yeni katman önce görünür, seviye
        /// eski yerinden çıkar; alçalan tüpte üstten alınan birimlerin altı
        /// değişmediği için Refresh sonrası alçalma doğru renklerle akar.
        /// </summary>
        private IEnumerator AnimateUndo(PourResult undone)
        {
            const float fillDuration = 0.3f;

            var job = new PourJob { FromIndex = undone.FromIndex, ToIndex = undone.ToIndex };
            activeJobs.Add(job);

            TubeView fromView = tubeViews[undone.FromIndex]; // sıvı buraya geri döner
            TubeView toView = tubeViews[undone.ToIndex];     // sıvı buradan alınır

            float fromStart = fromView.CurrentFill;
            fromView.Refresh();
            fromView.SetFillLevel(fromStart);

            // Refresh, tıpalı tüpte tıpayı anında kaldırır: veri geri alındı,
            // tüp artık tamamlanmış değil.
            float toStart = toView.CurrentFill;
            toView.Refresh();
            toView.SetFillLevel(toStart);

            Coroutine rise = StartCoroutine(
                fromView.AnimateFill(fromView.TargetFillLevel, fillDuration));
            Coroutine fall = StartCoroutine(
                toView.AnimateFill(toView.TargetFillLevel, fillDuration));
            yield return rise;
            yield return fall;

            activeJobs.Remove(job);
        }

        /// <summary>
        /// Mevcut leveli baştan yükler (çıkmaz/kötü gidişten kaçış). Levelin
        /// yüklenirken saklanan bozulmamış kopyasını geri kurar; hamleler ve
        /// eklenen +tüp geri alınır (LoadBoard geçmişi de temizler).
        /// </summary>
        public void RestartLevel()
        {
            if (AnyAnimating) return;
            if (pristineBoard == null) return;

            LoadBoard(pristineBoard.Clone());
        }

        /// <summary>
        /// Tahtaya bir boş tüp ekler (+tüp meta). Board.AddTube tahtayı değiştirir;
        /// görünümler yeniden kurulur. Geçmiş temizlenmez: önceki hamleler hâlâ
        /// geri alınabilir (yeni tüp sona eklendi, indeksler kaymadı). +tüp'ün
        /// kendisi geri alınmaz; restart onu da temizler (pristine kopyada yok).
        /// </summary>
        public void AddEmptyTube()
        {
            if (AnyAnimating) return;
            if (board.TubeCount == 0) return;

            board.AddTube(board[0].Capacity);
            ClearSelection();
            if (initialized)
            {
                RebuildViews();
                // +tüp genelde çıkmazı çözer (boş tüp = daha çok yer) ama garanti
                // değil: RebuildViews banner'ı gizledi; yeni tahta hâlâ çıkmazsa
                // banner geri gelir (undo'daki koşullu temizlemenin aynısı).
                RefreshDeadlockBanner();
            }
        }

        /// <summary>Mevcut tüp görünümlerini yıkıp tahtayı baştan kurar.</summary>
        private void RebuildViews()
        {
            // Yarıda kalan dökme animasyonu yeni tahtaya sızmasın.
            StopAllCoroutines();
            activeJobs.Clear();
            selectedIndex = -1;
            HideDeadlock();   // restart/+tüp/level geçişi çıkmaz uyarısını sıfırlar
            foreach (StreamView stream in streamPool)
                stream.Hide();
            streamsInUse.Clear();

            foreach (TubeView view in tubeViews)
                Destroy(view.gameObject);
            tubeViews.Clear();

            BuildViews();
            ApplyLayout();
        }

        /// <summary>
        /// Tüm tüpler aynı malzemeleri paylaşır; tüpe özel değerler (doluluk,
        /// katman renkleri, ölçüler) MaterialPropertyBlock ile gönderilir.
        /// Shader'lar Resources altında olduğu için build'e de dahil edilir.
        /// </summary>
        private static Material CreateMaterial(string shaderName)
        {
            var shader = Resources.Load<Shader>(shaderName);
            if (shader == null)
            {
                Debug.LogError($"{shaderName} shader bulunamadı (Assets/Resources/{shaderName}.shader).");
                return null;
            }

            return new Material(shader);
        }

        private static Sprite LoadSprite(string resourcePath)
        {
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
            {
                Debug.LogError($"{resourcePath} sprite bulunamadı (Assets/Resources/{resourcePath}.png).");
            }

            return sprite;
        }

        /// <summary>Çalışma anında üretilen yaka parçasını dokusuyla yok eder.
        /// Asset sprite'larına UYGULANMAZ: onların dokusu paylaşımlıdır.</summary>
        private static void DestroyCollarPiece(Sprite piece)
        {
            if (piece == null) return;
            Destroy(piece.texture);
            Destroy(piece);
        }

        private void BuildViews()
        {
            for (int i = 0; i < board.TubeCount; i++)
            {
                var go = new GameObject($"Tube{i}");
                go.transform.SetParent(transform, false);

                var view = go.AddComponent<TubeView>();
                view.Initialize(i, board[i], palette, unitSprite, liquidMaterial, tubeSprite,
                    collarSprite, collarFrontTopSprite, collarFrontBottomSprite, corkSprite);
                tubeViews.Add(view);
            }
        }

        /// <summary>Boşta bir akış görseli verir; yoksa yenisini kurup havuza ekler.</summary>
        private StreamView AcquireStream()
        {
            foreach (StreamView stream in streamPool)
            {
                if (!streamsInUse.Contains(stream))
                {
                    streamsInUse.Add(stream);
                    return stream;
                }
            }

            var go = new GameObject($"Stream{streamPool.Count}");
            go.transform.SetParent(transform, false);
            var created = go.AddComponent<StreamView>();
            created.Initialize(unitSprite, streamMaterial);
            streamPool.Add(created);
            streamsInUse.Add(created);
            return created;
        }

        /// <summary>Akış görselini gizleyip havuza geri verir.</summary>
        private void ReleaseStream(StreamView stream)
        {
            if (stream == null) return;
            stream.Hide();
            streamsInUse.Remove(stream);
        }

        /// <summary>
        /// Tüpleri ekrana en uygun ızgaraya dizer ve gerekiyorsa tahtayı küçültür.
        /// Ekranın görüş alanı değiştikçe yeniden çağrılır: yalnızca ölçek değil,
        /// dizilişin kendisi de ekrana bağlı. Yatay ekranda satır azalıp sütun
        /// artmalı, dikeyde tersi.
        /// </summary>
        private void ApplyLayout()
        {
            if (board.TubeCount == 0) return;

            int rows = ChooseRowCount();
            int columns = Mathf.CeilToInt(board.TubeCount / (float)rows);
            Vector2 boardSize = MeasureBoard(columns, rows);

            for (int i = 0; i < tubeViews.Count; i++)
                tubeViews[i].SetRestPosition(LayoutPosition(i, columns, boardSize.y));

            // Sığıyorsa büyütmüyoruz: tüp boyu level'dan level'a zıplamasın.
            // Ölçek kameraya değil tahtaya uygulanır; kamerayı oynatsaydık
            // ileride gelecek arayüz de onunla birlikte kayardı.
            transform.localScale = Vector3.one * Mathf.Min(1f, FitScale(boardSize));

            // Tahta, kendine ayrılan bandın ortasına oturur — ekran merkezine
            // değil: üstte HUD bandı (butonlar + LEVEL başlığı) tahtaya kapalı.
            Vector3 cam = mainCamera.transform.position;
            transform.position = new Vector3(cam.x, cam.y + BoardAreaCenterY, 0f);

            lastFittedView = CameraView;

            // Buton görüş alanına bağlı: yerleşim her tazelendiğinde o da tazelenir.
            PositionButtons();
        }

        /// <summary>
        /// Kaç satıra dizileceği. Sabit bir sayı yerine tüm olasılıklar denenir;
        /// tüp sayısı küçük olduğu için bu hesap bedavadır.
        ///
        /// Kural iki kademeli:
        /// 1. Tüpler doğal boyutunda sığıyorsa, en az satırlı diziliş seçilir -
        ///    yani mümkün olan en geniş satırlar.
        /// 2. Hiçbir diziliş sığmıyorsa, tüpleri en büyük bırakan seçilir.
        ///
        /// Sabit bir "satır başına en fazla 5" kuralı bunu yapamazdı: yatay
        /// ekranda gereksiz yere satır açıp tüpleri küçültür, yanlardaki boş
        /// alanı kullanmazdı.
        /// </summary>
        private int ChooseRowCount()
        {
            int count = board.TubeCount;
            int roomiest = 1;
            float bestScale = 0f;

            for (int rows = 1; rows <= count; rows++)
            {
                int columns = Mathf.CeilToInt(count / (float)rows);
                float scale = FitScale(MeasureBoard(columns, rows));

                // Satır sayısı artan sırada denendiği için sığan ilk diziliş
                // aynı zamanda en az satırlı olandır.
                if (scale >= 1f) return rows;

                if (scale > bestScale)
                {
                    bestScale = scale;
                    roomiest = rows;
                }
            }

            return roomiest;
        }

        /// <summary>Tahtadaki en uzun tüpün boyu. Satır yüksekliğini bu belirler.</summary>
        private float TallestTube
        {
            get
            {
                float tallest = 0f;
                foreach (Tube tube in board.Tubes)
                    tallest = Mathf.Max(tallest, TubeView.HeightFor(tube.Capacity));

                return tallest;
            }
        }

        /// <summary>Tüpleri satırlara böler; her satırı yatayda, tahtayı dikeyde ortalar.</summary>
        private Vector3 LayoutPosition(int index, int columns, float boardHeight)
        {
            int row = index / columns;
            int column = index % columns;
            int tubesInThisRow = Mathf.Min(columns, board.TubeCount - row * columns);

            float x = (column - (tubesInThisRow - 1) * 0.5f) * (TubeView.FullWidth + horizontalGap);

            // Tüpün konumu dibini gösterir, ortasını değil: sıvı dipten yukarı doluyor.
            // O yüzden satırın üst kenarından — üstte tıpa/yaka taşması için
            // ayrılan payın (TopOverhang) altından — tüp boyu kadar aşağı iniyoruz.
            float rowHeight = TallestTube + verticalGap;
            float y = boardHeight * 0.5f - TubeView.TopOverhang - row * rowHeight - TallestTube;

            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Verilen ızgaranın kaplayacağı alan. Tüplerin gerçek ölçülerinden
        /// hesaplanır; gövdeye ek olarak üstte tıpa/yaka dörtgenlerinin taşması
        /// (TopOverhang), yanlarda yaka dörtgeninin payı (SideOverhang) da
        /// dahildir. Yaka/tıpa görselleri gövdeden taştığı için bunlar
        /// sayılmayınca dar ekranda tahta üstten/yandan taşar (LayoutFitTests
        /// yakalar).
        /// </summary>
        private Vector2 MeasureBoard(int columns, int rows)
        {
            float width = columns * TubeView.FullWidth + (columns - 1) * horizontalGap
                + 2f * TubeView.SideOverhang;
            float height = rows * TallestTube + (rows - 1) * verticalGap
                + TubeView.TopOverhang;

            return new Vector2(width, height);
        }

        /// <summary>
        /// Bu tahtanın kendine ayrılan alana sığması için gereken ölçek.
        /// 1'den büyükse tahta zaten sığıyor ve etrafında boşluk kalıyor.
        /// </summary>
        private float FitScale(Vector2 boardSize)
        {
            if (boardSize.x <= 0f || boardSize.y <= 0f) return 1f;

            Vector2 area = BoardAreaSize;
            float available = 1f - screenMargin;

            return Mathf.Min(area.x * available / boardSize.x, area.y * available / boardSize.y);
        }

        /// <summary>Başlığın dikey hizası (görüş yüksekliği oranı, kamera
        /// merkezine göre). Hem PositionButtons hem tahta tavanı bunu kullanır
        /// — 0.35'ti, banner'a yer açmak için yukarı alındı.</summary>
        private const float TitleFraction = 0.38f;

        /// <summary>LEVEL başlığının altında bırakılması gereken pay: başlık
        /// metninin yarı boyu + altındaki boşluk + iki çıkmaz uyarı satırı
        /// (0.45 ve 0.78 ofsetli) + nefes. Tahta tavanı bunların hepsinin
        /// altından geçer.</summary>
        private const float TitleClearance = 0.95f;

        /// <summary>Tahta tavanı, kamera merkezine göre: başlık hizasının
        /// payla altı. Tüpler NE OLURSA OLSUN bu çizgiyi geçemez; uzun tüplü
        /// levellerde tahta LEVEL başlığının altında kalır.</summary>
        private float BoardCeiling => CameraView.y * TitleFraction - TitleClearance;

        /// <summary>Tahtaya ayrılan alan: yatayda tüm görüş, dikeyde ekran
        /// altından tavana kadar. FitScale bu alana sığdırır.</summary>
        private Vector2 BoardAreaSize =>
            new Vector2(CameraView.x, BoardCeiling + CameraView.y * 0.5f);

        /// <summary>Ayrılan alanın dikey ortası (kamera merkezine göre) —
        /// tahta buraya ortalanır, ekran merkezine değil.</summary>
        private float BoardAreaCenterY => (BoardCeiling - CameraView.y * 0.5f) * 0.5f;

        /// <summary>Kameranın dünya birimindeki görüş alanı: yerleşimin tek girdisi.</summary>
        private Vector2 CameraView
        {
            get
            {
                float height = mainCamera.orthographicSize * 2f;

                return new Vector2(height * mainCamera.aspect, height);
            }
        }

        /// <summary>
        /// Görüş alanı değiştiyse tahtayı yeniden yerleştirir.
        ///
        /// Ekran boyutunu değil kameranın görüş alanını izliyoruz: yerleşim
        /// hesabının gerçek girdisi bu. Kamera yakınlaşsa da tahta uyum sağlar,
        /// üstelik testten de değiştirilebildiği için doğrulanabilir kalır.
        /// </summary>
        private void RefitIfViewChanged()
        {
            if (AnyAnimating) return;
            if (CameraView != lastFittedView)
                ApplyLayout();
        }

        /// <summary>
        /// Çalışma anında yaratılan malzeme ve dokuları temizler.
        /// Unity nesnelerini C#'ın çöp toplayıcısı toplamaz; elle yok edilmezlerse
        /// bu bileşen her yeniden kurulduğunda (level geçişi, test) birikirler.
        /// </summary>
        private void OnDestroy()
        {
            // Butonlar tahtanın çocuğu olmadığı için kendiliğinden yok olmaz.
            if (undoButton != null)
                Destroy(undoButton.gameObject);

            if (pilotNextButton != null)
                Destroy(pilotNextButton.gameObject);

            Destroy(liquidMaterial);
            Destroy(streamMaterial);

            // Yaka/tıpa görselleri asset olduğu için yok edilmez; ama ön parçalar
            // (sprite + kendi dokuları) çalışma anında üretildi, birikmesinler.
            DestroyCollarPiece(collarFrontTopSprite);
            DestroyCollarPiece(collarFrontBottomSprite);

            if (unitSprite != null)
                Destroy(unitSprite.texture);

            Destroy(unitSprite);
        }

        private void Update()
        {
            RefitIfViewChanged();

            // Ok tuşlarıyla level gezme. Geçiş yapıldıysa bu kare tıklama
            // işlenmez (yeni tahta zaten kuruldu).
            if (!AnyAnimating && HandlePilotBrowse())
                return;

            // Pointer, Mouse ve Touchscreen'in ortak atasıdır: masaüstünde fare,
            // telefonda (ve Device Simulator'da) parmak aynı kodla okunur.
            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            // Animasyon sürerken de tıklama işlenir: dökmeye dahil olmayan tüpler
            // arasında eşzamanlı dökme yapılabilsin. Meşgul tüp/meta kuralları
            // aşağıda (HandleTubeClick / meta aksiyon guard'ları) uygulanır.
            if (!pointer.press.wasPressedThisFrame) return;

            HandleClick(pointer.position.ReadValue());
        }

        /// <summary>
        /// Ekran koordinatındaki dokunuşu ilgili hedefe yönlendirir: geri al
        /// butonu ya da tüp. Tüplerde BoxCollider2D hızlı eleme yapar; ardından
        /// SDF ile dokunuşun gerçekten tüp şekli içinde olduğu doğrulanır.
        ///
        /// Tek bir OverlapPoint yetmez: dökme sırasında kaynak tüp hedefin
        /// ÜSTÜNE asılır, collider'ları çakışır. Tüm collider'lara bakıp gerçek
        /// şekline (SDF) girilen tüpü seçeriz — yoksa hedefe yapılan tıklama,
        /// asılı kaynağın collider'ına takılıp boşluk sanılır (2. dökme başlamaz).
        /// </summary>
        private void HandleClick(Vector2 screenPosition)
        {
            Vector3 worldPoint = mainCamera.ScreenToWorldPoint(screenPosition);
            Collider2D[] hits = Physics2D.OverlapPointAll(worldPoint);

            // Butonlar tüplerin/akışın üstünde; herhangi bir collider buysa öncelikli.
            foreach (Collider2D hit in hits)
            {
                if (hit.GetComponent<UndoButtonView>() != null) { UndoLastMove(); return; }
                if (hit.GetComponent<PilotNextButtonView>() != null) { StepPilot(1); return; }
                var button = hit.GetComponent<ButtonView>();
                if (button != null) { HandleActionButton(button.Kind); return; }
            }

            // Gerçek şekline (SDF) dokunulan tüp: asılı kaynak üstte olsa da
            // altındaki durağan tüp doğru bulunur.
            foreach (Collider2D hit in hits)
            {
                var view = hit.GetComponent<TubeView>();
                if (view != null && view.ContainsPoint(worldPoint))
                {
                    HandleTubeClick(view.Index);
                    return;
                }
            }

            // Hiçbir tüpün şekline girmeyen tıklama boşluktur: seçim iptal.
            ClearSelection();
        }

        private void HandleActionButton(ButtonView.ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonView.ButtonKind.Previous: StepPilot(-1); break;   // önceki level
                case ButtonView.ButtonKind.Restart: RestartLevel(); break;
                case ButtonView.ButtonKind.AddTube: AddEmptyTube(); break;
            }
        }

        private void HandleTubeClick(int index)
        {
            // Henüz seçim yok: boş tüpten dökme yapılamaz, tıpalı (complete)
            // tüp kilitlidir — ikisi de seçtirilmez (yukarı kalkmaz).
            if (selectedIndex == -1)
            {
                if (!IsSelectable(index)) return;

                selectedIndex = index;
                tubeViews[index].SetSelected(true);
                return;
            }

            // Aynı tüpe tekrar tıklandı: seçimi iptal et.
            if (selectedIndex == index)
            {
                ClearSelection();
                return;
            }

            if (TryPour(selectedIndex, index))
                return;

            // Hamle geçersizdi. Oyuncu muhtemelen yeni bir kaynak seçmek istiyor:
            // seçimi iptal etmek yerine seçimi tıklanan tüpe taşımak daha rahat.
            // Boş ya da tıpalı tüpe tıklandıysa bu bir vazgeçmedir: seçim sıfır kalır.
            ClearSelection();
            if (IsSelectable(index))
            {
                selectedIndex = index;
                tubeViews[index].SetSelected(true);
            }
        }

        /// <summary>Dökme kaynağı olabilecek tüp: boş değil, tıpalı (complete)
        /// değil ve o an bir dökmeye dahil (meşgul) değil. Tıpalı tüp kilitlidir —
        /// Board.PourableAmount da aynı kuralı uygular; buradaki kontrol
        /// seçim/kaldırma davranışı için.</summary>
        private bool IsSelectable(int index) =>
            !board[index].IsEmpty && !board[index].IsComplete && !IsBusy(index);

        private void ClearSelection()
        {
            if (selectedIndex == -1) return;

            tubeViews[selectedIndex].SetSelected(false);
            selectedIndex = -1;
        }

        /// <summary>Level tamamlanınca oto-geçişten önceki bekleme (sn).</summary>
        private const float AutoAdvanceDelay = 0.7f;

        private void ReportBoardState()
        {
            if (board.IsSolved)
            {
                HideDeadlock();
                Debug.Log("<color=lime>Tahta çözüldü!</color>");
                if (pilotCount > 0)
                    StartCoroutine(AutoAdvanceToNext());
                return;
            }

            RefreshDeadlockBanner();
        }

        /// <summary>Çıkmaz uyarısını tahtanın çözülebilirliğine göre günceller:
        /// çözülemezse gösterir, çözülebilirse gizler. Hem dökme sonrası
        /// (ReportBoardState) hem geri alma sonrası çağrılır — bu yüzden banner
        /// yalnız gerçekten çıkmazdan çıkınca kalkar, her undo'da körü körüne değil.
        /// Varlık kontrolü ucuz (ilk çözümde durur).</summary>
        private void RefreshDeadlockBanner()
        {
            if (!Solver.IsSolvable(board))
            {
                Debug.Log("<color=orange>Çıkmaz: bu tahtadan kazanılamaz.</color>");
                ShowDeadlock();
            }
            else
            {
                HideDeadlock();
            }
        }

        /// <summary>Çıkmaz uyarısını gösterir: banner + kaçış butonlarını yanıp söndür.</summary>
        private void ShowDeadlock()
        {
            if (deadlockBannerTop != null) deadlockBannerTop.gameObject.SetActive(true);
            if (deadlockBannerBottom != null) deadlockBannerBottom.gameObject.SetActive(true);
            if (undoButton != null) undoButton.SetHighlight(true);
            if (restartButton != null) restartButton.SetHighlight(true);
            if (addTubeButton != null) addTubeButton.SetHighlight(true);
        }

        /// <summary>Çıkmaz uyarısını gizler ve butonların yanıp sönmesini durdurur.</summary>
        private void HideDeadlock()
        {
            if (deadlockBannerTop != null) deadlockBannerTop.gameObject.SetActive(false);
            if (deadlockBannerBottom != null) deadlockBannerBottom.gameObject.SetActive(false);
            if (undoButton != null) undoButton.SetHighlight(false);
            if (restartButton != null) restartButton.SetHighlight(false);
            if (addTubeButton != null) addTubeButton.SetHighlight(false);
        }

        /// <summary>
        /// Level tamamlanınca kısa bir bekleme sonrası sonraki levele geçer.
        /// Bekleme oyuncunun tamamlanan tahtayı görmesi için (tebrik pop-up'ı
        /// 1B'de / ertelendi). Oyuncu bu arada skip/undo ile araya girerse
        /// StepPilot'un RebuildViews'i StopAllCoroutines ile bunu iptal eder.
        /// </summary>
        private IEnumerator AutoAdvanceToNext()
        {
            yield return new WaitForSeconds(AutoAdvanceDelay);
            StepPilot(1);
        }

        /// <summary>
        /// Dökme animasyonu. Board hamleyi zaten yaptı; bu coroutine sadece
        /// görsel geçişi yönetir.
        ///
        /// Beş fazda çalışır:
        /// 1. Kaynak tüp kalkıp hedefin yanına kayar.
        /// 2. Hedefe doğru ~70° eğilir.
        /// 3. Seviyeler değişir (kaynak düşer, hedef yükselir).
        /// 4. Tüp doğrulur.
        /// 5. Yerine geri kayar.
        ///
        /// Katman güncelleme zamanlaması:
        /// - Hedef: animasyon öncesi Refresh (yeni renk hemen görünsün).
        /// - Kaynak: dökme sonrası Refresh (dökülen renk seviye düştükçe kaybolsun).
        /// </summary>
        private IEnumerator AnimatePour(PourResult result, PourJob job)
        {
            // slideDuration/pourDuration/angleSmoothTime Inspector'dan gelir
            // (sabit değil): görsel izlemek için play mode'da yavaşlatılabilir.
            // angleSmoothTime = eğim açısı SmoothDamp tepki süresi (kritik sönüm).
            ClearSelection();

            TubeView fromView = tubeViews[result.FromIndex];
            TubeView toView = tubeViews[result.ToIndex];
            StreamView stream = AcquireStream();
            // Akış üst parçası kaynağın offset bandının önünde (ön dilimler
            // offset+4'ün üstü); alt parça hedefin sandviçinde (tıpa katmanı 3,
            // hedef offset almaz). Böylece öndeki kaynağın akışı arkasında kalmaz.
            stream.SetSortingOrders(job.SortingOffset + 5, 3);

            // Board hamleyi zaten uyguladı; tube verileri yeni durumu yansıtıyor.
            float fromTarget = fromView.TargetFillLevel;

            // Hedef tüp: katmanları şimdi güncelle (yeni renk görünsün),
            // ama seviyeyi eski yerine geri al (oradan yükselecek). Tıpa bu
            // Refresh'te ERKEN gelmesin: veri dökme başında değiştiği için tüp
            // "tamamlanmış" sayılır ama görsel dökme daha sürüyor — tıpa dökme
            // bitince takılma animasyonuyla gelir.
            float toStart = toView.CurrentFill;
            toView.SetCorkSuppressed(true);
            toView.SetMouthOverlay(true);   // akış sandviçi: ön dilimler açılır
            toView.Refresh();
            toView.SetFillLevel(toStart);

            // Kaynak tüpü üstte çiz (eşzamanlı kaynaklar farklı bantta).
            fromView.SetSortingOffset(job.SortingOffset);

            // Eğilme yönü işte hazır: doğal yön ya da (2. gelen ise) ilkinin tersi.
            // Zıt yönler iki kaynağı hedefin iki yanına ayırır, üst üste binmezler.
            float direction = job.Direction;

            // Dönüş noktası tüpün ortasında.
            float pivotHeight = fromView.Height * 0.5f;

            // --- Faz 1+2+3: Kayma, eğilme ve dökme eş zamanlı ---
            // Kayma ve eğilme aynı anda başlar. Tilt'in pivot offset'i tüpü
            // yukarı kaldırır — kayma sırasında hedef tüple çakışma olmaz.
            // pourPos'a ulaşınca kayma biter, eğilme ve dökme devam eder.
            Vector3 startPos = fromView.RestPosition;
            float initialSignedAngle = -CalculatePourAngle(fromView) * direction;
            Vector3 pourPos = startPos;

            Color streamColor = palette.Get(result.Color);
            bool pourStarted = false;
            float pourElapsed = 0f;
            float splashStrength = 0f;   // hedefteki sıçrama (yumuşak aç/kapa)
            float fromStart = fromView.CurrentFill;
            float currentAngle = 0f;
            float angleVelocity = 0f;
            float moveElapsed = 0f;

            // 2 kaynak → 1 hedef: kolonu kaynağın tarafına kaydır (biri sağa, biri
            // sola) ki ağızlar hedefin merkezinde üst üste binmesin. Tek dökmede 0.
            // İkinci kaynak katılınca yumuşak kaysın diye MoveTowards ile ilerler.
            const float mouthSeparation = TubeView.Width * 0.2f;
            float lateral = 0f;

            // Emniyet kemeri (watchdogSeconds Inspector'dan): hiçbir formül hatası
            // animasyonu bir daha kilitleyemesin. Doğru işleyişte asla tetiklenmez;
            // tetiklenirse hata loglanır ve animasyon son değerlerle zorla tamamlanır.
            float watchdogElapsed = 0f;

            while (true)
            {
                float dt = Time.deltaTime;

                watchdogElapsed += dt;
                if (watchdogElapsed >= watchdogSeconds)
                {
                    Debug.LogError(
                        $"AnimatePour watchdog: dökme {watchdogSeconds} sn içinde bitmedi " +
                        $"({result.FromIndex} -> {result.ToIndex}, açı={currentAngle:F2}, " +
                        $"pourStarted={pourStarted}). Animasyon zorla tamamlanıyor.");
                    break;
                }

                // Hedef açı: fill'e göre dinamik, tek kaynak.
                float targetAngle = -CalculatePourAngle(fromView) * direction;

                // Lateral kayma: hedefe 2 gelen varsa kolonu kaynağın tarafına
                // (soldan gelen sola, sağdan gelen sağa) kaydır; yumuşak ilerle.
                float lateralTarget = IncomingCount(result.ToIndex) >= 2
                    ? -direction * mouthSeparation : 0f;
                lateral = Mathf.MoveTowards(
                    lateral, lateralTarget, mouthSeparation / slideDuration * dt);

                // pourPos her kare güncel açıya göre hesaplanır.
                // mouth.y = pourPos.y + mouthRise(angle) = destMouthY + margin (sabit).
                // Açı SmoothDamp ile pürüzsüz değiştiği için pourPos da pürüzsüz kayar.
                float angleForPos = Mathf.Abs(currentAngle) > 0.05f
                    ? currentAngle : initialSignedAngle;
                pourPos = CalculatePourPosition(
                    fromView, toView, angleForPos, pivotHeight, lateral);

                // Kayma: startPos'tan pourPos'a pürüzsüz geçiş.
                moveElapsed += dt;
                float moveT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(moveElapsed / slideDuration));
                Vector3 currentBase = Vector3.Lerp(startPos, pourPos, moveT);

                currentAngle = Mathf.SmoothDamp(
                    currentAngle, targetAngle, ref angleVelocity, angleSmoothTime);

                // Sıvı ağza ulaştığında dökmeyi başlat.
                if (!pourStarted)
                {
                    if (HasLiquidReachedMouth(fromView, currentAngle))
                        pourStarted = true;
                }

                // Dökme: sıvı ağza ulaştıysa seviyeler güncellenir.
                // Dökme hızı açıya bağlı: fill düşürülmeden önce açının
                // sıvıyı lip'te tutmaya yetip yetmediği kontrol edilir.
                // Yetersizse dökme duraklar, açı büyüyünce devam eder.
                if (pourStarted)
                {
                    float nextElapsed = pourElapsed + dt;
                    float nextT = Mathf.Clamp01(nextElapsed / pourDuration);
                    float nextFill = Mathf.Lerp(fromStart, fromTarget, nextT);

                    // Bu fill'de sıvı lip'e ulaşıyor mu? (gerçek geometri —
                    // açı 90°'yi aşınca koşulsuz evet, bekleme sonlu kalır)
                    bool liquidAtLip =
                        TiltedEdgeLevel(nextFill, currentAngle, fromView.Height) >= 0.95f;

                    // Pour ilerlesin: sıvı lip'te VEYA pour tamamlanmak üzere.
                    if (liquidAtLip || nextT >= 0.98f)
                        pourElapsed = nextElapsed;

                    float pourT = Mathf.Clamp01(pourElapsed / pourDuration);
                    fromView.SetFillLevel(Mathf.Lerp(fromStart, fromTarget, pourT));
                    // Hedef: bu dökmenin bu kareki katkı ARTIŞINI ekle (toplamalı).
                    // İki gelen aynı hedefi kendi payınca yükseltir; biri erken
                    // bitse de kalan dökmenin artışı sürer. Tek dökmede toplam
                    // katkı = FillAmount, eski lineer Lerp'e eşittir.
                    float fillDelta = job.FillAmount * (pourT - job.FillProgress);
                    job.FillProgress = pourT;
                    toView.SetFillLevel(toView.CurrentFill + fillDelta);
                }

                // Pozisyon: kayma + tilt offset birlikte uygulanır.
                ApplyTiltWithPivot(fromView, currentAngle, currentBase, pivotHeight);
                // Görsel yüzey fiziksel modele demirlenir: kolon sıvıdan kopmasın.
                AnchorLiquidToLip(fromView, currentAngle);

                // Akış görseli: kayma tamamlanınca göster.
                // Kayma sırasında stream gösterilmez — tüp henüz pourPos'ta değil.
                bool streamingNow = false;
                if (pourStarted && moveElapsed >= slideDuration)
                {
                    Vector3 sourcePoint = CalculateSourceMouth(fromView, currentAngle);
                    // Kolon tüpte kalan sıvıya YAPIŞIK kalsın: üst uç, döken
                    // kenardaki sıvı yüzeyine kadar uzar — aradaki bölge yaka/
                    // delik üstünden köprülenir (kopma bulgusu, dökme sonuna
                    // doğru belirginleşiyordu).
                    Vector3 liquidEdge = CalculateStreamSource(fromView, currentAngle);
                    sourcePoint.y = Mathf.Max(sourcePoint.y, liquidEdge.y);
                    // Hedef uçlarını da lateral kadar kaydır: kolon dikey kalıp
                    // hedefin kaynağa bakan yanına insin (kaynak ağzıyla aynı x).
                    Vector3 destMouth = CalculateDestMouth(toView);
                    destMouth.x += lateral;
                    Vector3 destSurface = CalculateDestSurface(toView, toView.CurrentFill);
                    destSurface.x += lateral;
                    streamingNow = sourcePoint.y > destSurface.y;
                    if (streamingNow)
                        stream.Show(streamColor, sourcePoint, destMouth, destSurface);
                    else
                        stream.Hide();
                }

                // Sıçrama: akış hedef yüzeye aktığı sürece değme noktasından
                // iki yana damlacıklar; güç yumuşak açılır/kapanır. Halkalar
                // dökme SIRASINDA değil, bitince patlama olarak oynar.
                splashStrength = Mathf.MoveTowards(
                    splashStrength, streamingNow ? 1f : 0f, dt * 6f);
                toView.SetSplashStrength(splashStrength);

                if (pourStarted && pourElapsed >= pourDuration)
                    break;

                yield return null;
            }

            // Kaynağı ve kendi akışını kesin bitir (her dökme kendi işi).
            fromView.SetFillLevel(fromTarget);
            ReleaseStream(stream);
            // Bu dökmenin kalan katkısını kesin uygula (pourT tam 1'e ulaşmadıysa).
            toView.SetFillLevel(toView.CurrentFill + job.FillAmount * (1f - job.FillProgress));
            job.FillProgress = 1f;
            job.Pouring = false;   // dökme fazı bitti (kaynak doğrulma fazına geçer)

            // Hedef tamamlanması (final doluluk, halka, tıpa, sandviç kapanışı)
            // yalnız SON dökücü bitince yapılır: 2 kaynak → 1 hedef durumunda ilki
            // biterken diğeri hâlâ döküyorsa atlanır, sonuncu yapar.
            if (!TargetHasOtherPouring(result.ToIndex, job))
            {
                // Birikimli toplamın kayan noktada sürüklenmesini gidermek için
                // hedefi tam veri seviyesine oturt.
                toView.SetFillLevel(toView.TargetFillLevel);
                toView.SetSplashStrength(0f);   // sıçrama durur
                // Sıvı yüzeye oturdu: damla halkası patlaması şimdi oynar
                // (efekt dökme bitince hissedilmeli).
                toView.PlayRippleBurst();
                // Tüp tamamlandıysa tıpa ŞİMDİ, takılma animasyonuyla; akış
                // sandviçi kapanır (tıpalıysa dilimler tıpa üzerinden açık kalır).
                toView.SetCorkSuppressed(false);
                toView.SetMouthOverlay(false);
            }

            // --- Faz 4+5: Doğrulma ve geri dönüş eş zamanlı ---
            // Giderken kayma+eğilme eş zamanlıydı; dönüşte de doğrulma+kayma
            // eş zamanlı. Tilt offset tüpü kaldırır, hedef tüple çakışma olmaz.
            fromView.Refresh();
            float returnDuration = slideDuration;
            float returnElapsed = 0f;
            float returnStartAngle = currentAngle;

            while (returnElapsed < returnDuration)
            {
                returnElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnElapsed / returnDuration));

                float angle = Mathf.Lerp(returnStartAngle, 0f, t);
                Vector3 returnBase = Vector3.Lerp(pourPos, startPos, t);
                ApplyTiltWithPivot(fromView, angle, returnBase, pivotHeight);
                // Demirleme açıyla birlikte kendiliğinden söner (açı 0 → fark 0).
                AnchorLiquidToLip(fromView, angle);

                yield return null;
            }

            ApplyTiltWithPivot(fromView, 0f, startPos, pivotHeight);
            fromView.SetSurfaceLift(0f);

            fromView.SetSortingOffset(0);
            activeJobs.Remove(job);
            ReportBoardState();
        }

        /// <summary>
        /// Kaynak tüpün dökme sırasında duracağı konum.
        /// Eğildikten sonra kaynak ağzı hedefin ağzının biraz üstüne düşer.
        /// </summary>
        private Vector3 CalculatePourPosition(TubeView from, TubeView to,
            float signedAngle, float pivotHeight, float lateralOffset)
        {
            Vector3 dest = to.RestPosition;

            // --- X: döken ağız ucu (deliğin hedefe bakan kenarı) hedefin
            // merkezi + lateralOffset üstüne gelsin: akış kolonu dikey düştüğü
            // için ancak böyle hem kaynağın deliğinden çıkar hem hedefte istenen
            // noktaya iner. lateralOffset 2 kaynak → 1 hedef durumunda kolonu
            // kaynağın tarafına kaydırır (tek dökmede 0). ApplyTiltWithPivot
            // modeli: nokta = taban + pivotTelafisi + R(açı)·yerel — taban çözülür.
            float lipSide = -Mathf.Sign(signedAngle);
            Vector3 spout = from.CollarMouthLip(lipSide);
            float cos = Mathf.Cos(signedAngle);
            float sin = Mathf.Sin(signedAngle);
            float spoutOffsetX = pivotHeight * sin + (cos * spout.x - sin * spout.y);
            float xTarget = dest.x + lateralOffset - spoutOffsetX;

            // --- Y: kaynak tüpün dibi hedefin ağzının üstünde kalsın ---
            // Böylece kayma sırasında (henüz eğilmeden) gövdeler çakışmaz.
            // Eğilince ağız hedefin üstüne doğru iner. Dökme, hedef ağzının 0.2
            // üstünden yapılır: en dik açıda (~100°) kaynağın yaka/tıpası tüpten
            // taştığı için daha alçakta hedefe değerdi.
            float destMouthY = dest.y + TallestTube;
            float yTarget = destMouthY + 0.2f;

            return new Vector3(xTarget, yTarget, 0f);
        }

        /// <summary>
        /// Tüpteki sıvı miktarına göre eğim açısını hesaplar. Az sıvıda sıvının
        /// ağza ulaşması için daha fazla eğilme gerekir. İki bileşenin büyüğü:
        /// (1) sezgisel doluluk→açı aralığı (dolu 60°, boş 100°); (2) yükseklik-
        /// farkında GEOMETRİK alt sınır — sıvının döken kenarda ağza ulaşması
        /// için gereken açı. (2) uzun tüpler (kap 8) için şart: sabit aralık
        /// oralarda mid-fill'de yetmiyor, dökme hiç başlamıyordu (watchdog).
        /// </summary>
        private static float CalculatePourAngle(TubeView fromView)
        {
            const float minAngle = 60f * Mathf.Deg2Rad;
            const float maxAngle = 100f * Mathf.Deg2Rad;

            // (1) Sezgisel: doluluk oranıyla açı (dolu az, boş çok eğilir).
            float fillSpan = 1f - 0.2f / fromView.Height; // FillHeadroom = 0.2
            float fillRatio = Mathf.Clamp01(fromView.CurrentFill / fillSpan);
            float heuristic = Mathf.Lerp(maxAngle, minAngle, fillRatio);

            // (2) Geometrik alt sınır: eğik yüzeyin döken kenarı ağza (1.05, küçük
            // pay) ulaşsın diye gereken açı = atan(2·(1.05-fill)·yükseklik/genişlik).
            // Kısa tüplerde sezgisel bunu zaten aşar (max() etkisiz), uzun tüplerde
            // devreye girip dökmenin başlamasını garantiler.
            float needed = Mathf.Atan2(
                2f * (1.05f - fromView.CurrentFill) * fromView.Height, TubeView.Width);

            return Mathf.Max(heuristic, needed);
        }

        /// <summary>
        /// Verilen eğim açısında sıvının tüpün ağzına ulaşıp ulaşmadığını kontrol eder.
        /// Shader'daki tiltOffset formülünün C# karşılığı: eğik taraftaki
        /// sıvı yüzeyi FillSpan'e ulaştıysa sıvı ağızdan taşmaya hazır demektir.
        /// </summary>
        private static bool HasLiquidReachedMouth(TubeView view, float angle)
        {
            // Eşik FillSpan değil 1.0: sıvı tüpün fiziksel ağız ucuna (genişleyen
            // kısmın en tepesine) ulaşmalı, FillHeadroom sınırına değil.
            return TiltedEdgeLevel(view.CurrentFill, angle, view.Height) >= 1f;
        }

        /// <summary>
        /// Eğik tüpte sıvının döken kenardaki normalize yüzey yüksekliği.
        /// Gerçek geometri (hacim korunumu, dünyada yatay yüzey), iki rejim:
        /// yüzey iki duvarı da kesiyorsa kenar doğrusal yükselir; sıvı az ya da
        /// eğim çoksa sıvı döken duvarın dibinde üçgen toplanır. 90° ve
        /// ötesinde ağız ufkun altındadır: sıvı koşulsuz kenardadır.
        ///
        /// Shader'daki 0.2 kelepçeli eğim BİLEREK kullanılmaz: o kelepçe çizim
        /// güvenliğidir; karar mantığına taşınınca eğimi ~tan(78.7°) ile
        /// tavanlıyor ve uzun tüplerde (kapasite >= 5) az sıvı dökülürken
        /// "açı asla yetmiyor" kilidine — donmaya — yol açıyordu.
        /// </summary>
        private static float TiltedEdgeLevel(float fill, float angle, float height)
        {
            float a = Mathf.Abs(angle);
            if (a >= Mathf.PI * 0.5f) return float.PositiveInfinity;

            // Normalize eğim: yerel tan(açı), tüp en-boy oranıyla ölçekli.
            float slope = Mathf.Tan(a) * (TubeView.Width / height);
            float halfRise = 0.5f * slope;

            // Yüzey iki duvarı da kesiyor: kenar = seviye + yarım yükselme.
            if (fill >= halfRise)
                return fill + halfRise;

            // Üçgen rejimi: hacim korunumundan kenar yüksekliği.
            return Mathf.Sqrt(2f * slope * fill);
        }

        /// <summary>
        /// Görsel yüzeyi fiziksel modele demirler. Shader eğik yüzeyi düzlem
        /// kaydırmasıyla çizer ve dik açılarda dudaktaki sıvıyı gerçek (hacim
        /// korunumlu) TiltedEdgeLevel modelinden ALÇAK gösterir; böylece dökme
        /// kapısı "sıvı dudakta" derken görsel geride kalır ve akış kolonu
        /// sıvıdan kopuk görünür. Fark her kare kaldırma olarak shader'a yazılır;
        /// açı sıfıra dönünce fark da kendiliğinden sıfırlanır. TiltedEdgeLevel
        /// 90°+ açıda sonsuz döner: kaldırma tüp tepesinin hemen üstüyle
        /// sınırlanır (görsel zaten ağızda kırpılıyor).
        /// </summary>
        private static void AnchorLiquidToLip(TubeView fromView, float signedAngle)
        {
            float tiltSlope = Mathf.Sin(signedAngle)
                / Mathf.Max(Mathf.Abs(Mathf.Cos(signedAngle)), 0.2f);
            float shearEdge = fromView.CurrentFill
                + Mathf.Abs(0.5f * tiltSlope * (TubeView.Width / fromView.Height));
            float physicalEdge = Mathf.Min(1.05f, TiltedEdgeLevel(
                fromView.CurrentFill, signedAngle, fromView.Height));
            fromView.SetSurfaceLift(Mathf.Max(0f, physicalEdge - shearEdge));
        }

        /// <summary>
        /// Kaynağın döken ağız ucu (deliğin hedefe bakan kenarı), board-local.
        /// TransformPoint tüpün eğimini ve pivot telafisini otomatik hesaba katar.
        /// </summary>
        private Vector3 CalculateSourceMouth(TubeView fromView, float signedAngle)
        {
            // Döken taraf: tüp sağa eğiliyorsa (negatif açı) sağ kenar döker.
            float lipSide = -Mathf.Sign(signedAngle);
            Vector3 mouthWorld = fromView.transform.TransformPoint(
                fromView.CollarMouthLip(lipSide));
            return transform.InverseTransformPoint(mouthWorld);
        }

        /// <summary>Hedefin yaka deliğinin merkezi, board-local: akış kolonunun
        /// hedefte indiği nokta ve üst/alt parçaların birleşme hizası.</summary>
        private Vector3 CalculateDestMouth(TubeView toView)
        {
            Vector3 mouthWorld = toView.transform.TransformPoint(toView.CollarMouth);
            return transform.InverseTransformPoint(mouthWorld);
        }

        /// <summary>
        /// Akış kaynağı: sıvı yüzeyinin döken kenardaki konumu.
        /// Sıvı ağızdaysa lip ile aynıdır. Sıvı ağzın altındaysa akış
        /// sıvı yüzeyinden başlar — böylece akış her zaman sıvıya bağlı kalır.
        /// </summary>
        private Vector3 CalculateStreamSource(TubeView fromView, float signedAngle)
        {
            float lipSide = -Mathf.Sign(signedAngle);

            // Eğik sıvı yüzeyinin döken kenardaki yüksekliği (normalize, 0-1).
            // Shader'ın düzlem kaydırması ile fiziksel modelin (TiltedEdgeLevel)
            // BÜYÜĞÜ: AnchorLiquidToLip görseli fiziksel modele kaldırdığı için
            // ekranda görünen kenar ikisinin maksimumudur — kolonun çapası da
            // aynı gerçeğe bakmalı ki sıvıya yapışık kalsın.
            float tiltSlope = Mathf.Sin(signedAngle)
                / Mathf.Max(Mathf.Abs(Mathf.Cos(signedAngle)), 0.2f);
            float shearEdge = fromView.CurrentFill
                + Mathf.Abs(0.5f * tiltSlope * (TubeView.Width / fromView.Height));
            float physicalEdge = Mathf.Min(1.05f, TiltedEdgeLevel(
                fromView.CurrentFill, signedAngle, fromView.Height));
            float surfaceNorm = Mathf.Max(shearEdge, physicalEdge);

            // Tüp tepesini (1.0) aşamaz, tabandan (0) inemez.
            surfaceNorm = Mathf.Clamp(surfaceNorm, 0f, 1f);

            Vector3 localPos = new Vector3(
                TubeView.Width * 0.5f * lipSide,
                surfaceNorm * fromView.Height, 0f);

            Vector3 worldPos = fromView.transform.TransformPoint(localPos);
            return transform.InverseTransformPoint(worldPos);
        }

        /// <summary>
        /// Hedef tüpteki sıvı yüzeyinin board-local konumu.
        /// Tüpler saydam olduğu için akış ağızda değil, sıvının
        /// olduğu seviyede bitmeli.
        /// </summary>
        // Boş hedefte kolon dibinin oturacağı iç-dip payı: RestPosition tüpün
        // pivot (dış) dibidir; görünür iç dip biraz yukarıda, kolon oraya otursun.
        private const float DestBottomInset = 0.06f;

        private static Vector3 CalculateDestSurface(TubeView toView, float fillLevel)
        {
            float surfaceY = fillLevel * toView.Height;
            // Boş/az dolu hedefte kolonun dibi tüpün altına taşmasın: kolon dibi
            // yüzeyden SurfacePlunge kadar aşağı iner; yüzey en az bu + iç-dip payı
            // kadar tüp dibinin (RestPosition) üstünde tutulur.
            surfaceY = Mathf.Max(surfaceY, StreamView.SurfacePlunge + DestBottomInset);
            return toView.RestPosition + new Vector3(0f, surfaceY, 0f);
        }

        /// <summary>
        /// Eğim açısını ve pivot telafisini tek seferde uygular.
        /// Unity dönüşü transform.position (tüpün dibi) etrafında yapar;
        /// ağızdan eğilmiş gibi görünsün diye pozisyonu kaydırırız.
        /// </summary>
        private static void ApplyTiltWithPivot(TubeView view, float angle,
            Vector3 basePosition, float pivotHeight)
        {
            view.SetTiltAngle(angle);
            float ox = pivotHeight * Mathf.Sin(angle);
            float oy = pivotHeight * (1f - Mathf.Cos(angle));
            view.transform.localPosition = basePosition + new Vector3(ox, oy, 0f);
        }

        /// <summary>
        /// Tek beyaz pikselden sprite üretir. SpriteRenderer'ın ölçeğiyle
        /// istediğimiz dikdörtgene dönüşür; hazır bir dosyaya ihtiyaç kalmaz.
        /// </summary>
        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
