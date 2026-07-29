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

        [Header("Level")]
        [Tooltip("Oynanacak level (Resources/levels.json'dan). 0 = level yok, " +
                 "elle kurulmuş test tahtası kullanılır.")]
        [SerializeField] private int levelNumber;

        [Header("Pilot Önizleme (geçici)")]
        [Tooltip("C aşamasının pilot merdivenini (pilot_levels.json) yükle ve " +
                 "ok tuşlarıyla (← →) gez. levelNumber ve test tahtasının önüne " +
                 "geçer, LoadBoard'ın arkasında kalır. D'nin level dosyası " +
                 "(levels.json) bundan etkilenmez.")]
        [SerializeField] private bool pilotPreview;

        [Header("Teşhis")]
        [Tooltip("Çözülemez test tahtasını kur: solver'ın 'ÇÖZÜLEMEZ' kararını " +
                 "ve oyun içi çıkmazı elle denemek için. Yalnız levelNumber = 0 " +
                 "iken etkilidir.")]
        [SerializeField] private bool useUnsolvableBoard;

        private Board board;
        private readonly MoveHistory history = new MoveHistory();
        private ColorPalette palette;
        private Sprite unitSprite;
        private Material liquidMaterial;
        private Material streamMaterial;
        private Sprite tubeSprite;              // ekip görselleri (Resources/Sprites)
        private Sprite collarSprite;
        private Sprite corkSprite;
        private Sprite collarFrontTopSprite;    // yakadan eğri maskeyle üretilen ön
        private Sprite collarFrontBottomSprite; // sandviç parçaları; doku + sprite bizde,
                                                // OnDestroy temizler
        private readonly List<TubeView> tubeViews = new List<TubeView>();
        private StreamView streamView;
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
        private bool isAnimating;
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

            // Cam, yaka ve tıpa artık shader değil ekip görseli (feature/asset-tube).
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

            // Tahta önceliği: dışarıdan verilen (LoadBoard) > pilot önizleme >
            // seçili level > elle kurulmuş test tahtası (teşhis anahtarına göre
            // çözülebilir ya da çözülemez olan). Level yüklenemezse test tahtasına düşer.
            if (board == null && pilotPreview)
            {
                pilotCount = LevelLibrary.LevelCount(PilotResource);
                board = LoadPilot(pilotIndex);
            }

            if (board == null && levelNumber > 0)
                board = LevelLibrary.Load(levelNumber);

            if (board == null)
                board = useUnsolvableBoard ? CreateUnsolvableTestBoard() : CreateTestBoard();

            pristineBoard = board.Clone();   // restart bu kopyayı geri yükler

            BuildViews();
            BuildStreamView();
            BuildUndoButton();
            BuildRestartButton();
            BuildAddTubeButton();
            if (pilotPreview)
            {
                BuildPilotNextButton();
                BuildPrevButton();
            }
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
        /// Çıkmaz uyarı banner'ını kurar (TMP, gizli başlar). Dünya uzayında,
        /// butonlar gibi kameraya göre konumlanır; TMP varsayılan fontunu kullanır
        /// (TMP Essentials import edilmiş olmalı). Boyut/konum placeholder — 1C'de cila.
        /// </summary>
        /// <summary>
        /// Çıkmaz uyarısını iki TMP parçası olarak kurar (gizli başlar): üst parça
        /// ekranın üstünde, alt parça altında, ikisi de yatayda ortada. Uzun mesaj
        /// dar ekrana sığsın diye ikiye bölündü. Boyut/konum placeholder — 1C'de cila.
        /// </summary>
        private void BuildDeadlockBanner()
        {
            deadlockBannerTop = CreateBannerPart("DeadlockBannerTop", "Çıkmaz!", 4f);
            deadlockBannerBottom = CreateBannerPart(
                "DeadlockBannerBottom", "Geri al, tüp ekle ya da yeniden başla", 3.2f);
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
        /// level değişiminde tazelenir. Boyut/konum placeholder — 1C'de cila.
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

        /// <summary>Mevcut levelin ekran adı: pilot modunda label ("1.1"), yoksa numara.</summary>
        private string CurrentLabel()
        {
            if (pilotPreview && pilotCount > 0)
                return LevelLibrary.LabelOf(PilotResource, pilotIndex);
            return levelNumber > 0 ? levelNumber.ToString() : "";
        }

        /// <summary>
        /// Butonları görüş alanının üst köşelerine dizer. Sol küme (soldan sağa):
        /// geri al, restart, +tüp — oyun aksiyonları. Sağ küme (sağdan sola):
        /// skip, önceki — level navigasyonu. Üst-orta level başlığına (1B) ayrık.
        /// Butonlar tahtanın çocuğu değil; tahta ölçeklense de sabit kalırlar.
        /// (Placeholder yerleşim; kalabalık ve hizalama 1C'de sprite'larla cilalanır.)
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
            if (levelTitle != null)
                levelTitle.transform.position = new Vector3(cam.x, cam.y + view.y * 0.35f, 0f);

            // Çıkmaz uyarısı: başlığın (0.35) hemen altında iki satır — üstte
            // "Çıkmaz!", hemen altında talimat; ikisi de yatayda ortada.
            if (deadlockBannerTop != null)
                deadlockBannerTop.transform.position = new Vector3(cam.x, cam.y + view.y * 0.30f, 0f);
            if (deadlockBannerBottom != null)
                deadlockBannerBottom.transform.position = new Vector3(cam.x, cam.y + view.y * 0.255f, 0f);
        }

        private static void PlaceButton(MonoBehaviour button, float x, float y)
        {
            if (button != null) button.transform.position = new Vector3(x, y, 0f);
        }

        /// <summary>Aktif tahta. Testlerin ve dış katmanların durum sorgusu için.</summary>
        public Board Board => board;

        /// <summary>Dökme animasyonu sürüyor mu? Testler bitişini beklemek için kullanır.</summary>
        public bool IsAnimating => isAnimating;

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
            if (isAnimating) return false;

            PourResult result = board.Pour(fromIndex, toIndex);
            if (!result.Success) return false;

            history.Record(result);
            StartCoroutine(AnimatePour(result));
            return true;
        }

        /// <summary>
        /// Son hamleyi geri alır. Animasyon sürerken ve geçmiş boşken çağrı
        /// yok sayılır. Tıpa (varsa) anında kalkar ama sıvı ışınlanmaz:
        /// seviyeler dökmedeki gibi kademeli akar (sektör standardı,
        /// kullanıcı bulgusu). Kayma/eğilme/akış görseli yok — geri alma
        /// bir hamle değil düzeltmedir.
        /// </summary>
        public void UndoLastMove()
        {
            if (isAnimating) return;
            if (!history.TryUndo(board, out PourResult undone)) return;

            ClearSelection();
            HideDeadlock();   // önceki durum zaten çözülebilirdi: uyarı kalksın
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

            isAnimating = true;

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

            isAnimating = false;
        }

        /// <summary>
        /// Mevcut leveli baştan yükler (çıkmaz/kötü gidişten kaçış). Levelin
        /// yüklenirken saklanan bozulmamış kopyasını geri kurar; hamleler ve
        /// eklenen +tüp geri alınır (LoadBoard geçmişi de temizler).
        /// </summary>
        public void RestartLevel()
        {
            if (isAnimating) return;
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
            if (isAnimating) return;
            if (board.TubeCount == 0) return;

            board.AddTube(board[0].Capacity);
            ClearSelection();
            if (initialized)
                RebuildViews();
        }

        /// <summary>Mevcut tüp görünümlerini yıkıp tahtayı baştan kurar.</summary>
        private void RebuildViews()
        {
            // Yarıda kalan dökme animasyonu yeni tahtaya sızmasın.
            StopAllCoroutines();
            isAnimating = false;
            selectedIndex = -1;
            HideDeadlock();   // restart/+tüp/level geçişi çıkmaz uyarısını sıfırlar
            if (streamView != null)
                streamView.Hide();

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

        /// <summary>
        /// Elle kurulmuş geçici bir tahta. Level üretici gelene kadar bununla test ediyoruz.
        /// Kasıtlı olarak karışık: hem tam dökme, hem kısmi dökme denenebilsin.
        /// </summary>
        private Board CreateTestBoard()
        {
            const int Red = 0, Yellow = 1, Blue = 2, Green = 3;

            return new Board(new[]
            {
                new Tube(4, Red, Yellow, Yellow, Blue),
                new Tube(4, Green, Red, Blue, Yellow),
                new Tube(4, Blue, Green, Green, Red),
                new Tube(4, Yellow, Blue, Red, Green),
                new Tube(4),
                new Tube(4)
            });
        }

        /// <summary>
        /// Kanıtlanmış çözülemez tahta: rastgele üretilip solver ile tarandı
        /// (58 durumun tamamı gezildi, çözüm yok). Hamleler var, yani oyuncu
        /// bir süre oynayıp çıkmaza girebilir — "hamle var ama kazanılamaz"
        /// durumunun elle test edilebilir örneği.
        /// </summary>
        private Board CreateUnsolvableTestBoard()
        {
            const int Red = 0, Yellow = 1, Blue = 2, Green = 3;

            return new Board(new[]
            {
                new Tube(4, Blue, Blue, Green, Red),
                new Tube(4, Blue, Red, Green, Yellow),
                new Tube(4, Green, Red, Green, Yellow),
                new Tube(4, Blue, Red, Yellow, Yellow),
                new Tube(4)
            });
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

        private void BuildStreamView()
        {
            var go = new GameObject("Stream");
            go.transform.SetParent(transform, false);
            streamView = go.AddComponent<StreamView>();
            streamView.Initialize(unitSprite, streamMaterial);
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
        /// Bu tahtanın ekrana sığması için gereken ölçek. 1'den büyükse tahta
        /// zaten sığıyor ve etrafında o kadar boşluk kalıyor demektir.
        /// </summary>
        private float FitScale(Vector2 boardSize)
        {
            if (boardSize.x <= 0f || boardSize.y <= 0f) return 1f;

            Vector2 view = CameraView;
            float available = 1f - screenMargin;

            return Mathf.Min(view.x * available / boardSize.x, view.y * available / boardSize.y);
        }

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
            if (isAnimating) return;
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

            // Pilot önizleme: ok tuşlarıyla level gezme. Geçiş yapıldıysa bu kare
            // tıklama işlenmez (yeni tahta zaten kuruldu).
            if (pilotPreview && !isAnimating && HandlePilotBrowse())
                return;

            // Pointer, Mouse ve Touchscreen'in ortak atasıdır: masaüstünde fare,
            // telefonda (ve Device Simulator'da) parmak aynı kodla okunur.
            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            if (isAnimating) return;
            if (!pointer.press.wasPressedThisFrame) return;

            HandleClick(pointer.position.ReadValue());
        }

        /// <summary>
        /// Ekran koordinatındaki dokunuşu ilgili hedefe yönlendirir: geri al
        /// butonu ya da tüp. Tüplerde BoxCollider2D hızlı eleme yapar; ardından
        /// SDF ile dokunuşun gerçekten tüp şekli içinde olduğu doğrulanır.
        /// </summary>
        private void HandleClick(Vector2 screenPosition)
        {
            Vector3 worldPoint = mainCamera.ScreenToWorldPoint(screenPosition);
            Collider2D hit = Physics2D.OverlapPoint(worldPoint);
            if (hit == null)
            {
                // Boşluğa tıklamak vazgeçmektir: mevcut seçimi iptal et.
                ClearSelection();
                return;
            }

            if (hit.GetComponent<UndoButtonView>() != null)
            {
                UndoLastMove();
                return;
            }

            if (hit.GetComponent<PilotNextButtonView>() != null)
            {
                StepPilot(1);   // skip: tamamlamadan sonraki level
                return;
            }

            var button = hit.GetComponent<ButtonView>();
            if (button != null)
            {
                HandleActionButton(button.Kind);
                return;
            }

            var view = hit.GetComponent<TubeView>();
            if (view != null && view.ContainsPoint(worldPoint))
            {
                HandleTubeClick(view.Index);
                return;
            }

            // Collider'a girip tüpün gerçek şekline (SDF) girmeyen tıklama da
            // boşluk sayılır: seçim iptal.
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

        /// <summary>Dökme kaynağı olabilecek tüp: boş değil ve tıpalı (complete)
        /// değil. Tıpalı tüp kilitlidir — Board.PourableAmount da aynı kuralı
        /// uygular; buradaki kontrol seçim/kaldırma davranışı için.</summary>
        private bool IsSelectable(int index) =>
            !board[index].IsEmpty && !board[index].IsComplete;

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
                if (pilotPreview && pilotCount > 0)
                    StartCoroutine(AutoAdvanceToNext());
            }
            // Gerçek çıkmaz: hamle olsun olmasın, bu tahtadan artık kazanılamaz.
            // Varlık kontrolü ucuz (ilk çözümde durur); dökmeler ayrık olduğu için
            // hamle-başına-bir-kez çalışır.
            else if (!Solver.IsSolvable(board))
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
        private IEnumerator AnimatePour(PourResult result)
        {
            const float slideDuration = 0.24f;
            const float pourDuration = 0.4f;

            // SmoothDamp tepki süresi. Kritik sönümleme: aşım yok, hızlı yakınsama.
            // Hem ilk eğilme hem dökme sırasındaki açı değişimi tek parametre.
            const float angleSmoothTime = 0.12f;

            isAnimating = true;
            ClearSelection();

            Debug.Log($"{result.Amount} birim renk#{result.Color}: tüp {result.FromIndex} -> tüp {result.ToIndex}");

            TubeView fromView = tubeViews[result.FromIndex];
            TubeView toView = tubeViews[result.ToIndex];

            // Board hamleyi zaten uyguladı; tube verileri yeni durumu yansıtıyor.
            float fromTarget = fromView.TargetFillLevel;
            float toTarget = toView.TargetFillLevel;

            // Hedef tüp: katmanları şimdi güncelle (yeni renk görünsün),
            // ama seviyeyi eski yerine geri al (oradan yükselecek). Tıpa bu
            // Refresh'te ERKEN gelmesin: veri dökme başında değiştiği için tüp
            // "tamamlanmış" sayılır ama görsel dökme daha sürüyor — tıpa dökme
            // bitince takılma animasyonuyla gelir (kullanıcı bulgusu).
            float toStart = toView.CurrentFill;
            toView.SetCorkSuppressed(true);
            toView.SetMouthOverlay(true);   // akış sandviçi: ön dilimler açılır
            toView.Refresh();
            toView.SetFillLevel(toStart);

            // Kaynak tüpü üstte çiz.
            fromView.SetSortingOffset(10);

            // Eğilme yönü: hedefe doğru eğil.
            // Aynı sütundaysa (dx ≈ 0) sağa doğru eğil.
            float dx = toView.RestPosition.x - fromView.RestPosition.x;
            float direction = Mathf.Abs(dx) < 0.01f ? 1f : Mathf.Sign(dx);

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
            float fromStart = fromView.CurrentFill;
            float currentAngle = 0f;
            float angleVelocity = 0f;
            float moveElapsed = 0f;

            // Emniyet kemeri: hiçbir formül hatası animasyonu bir daha
            // kilitleyemesin. Doğru işleyişte asla tetiklenmez; tetiklenirse
            // hata loglanır ve animasyon son değerlerle zorla tamamlanır.
            const float watchdogSeconds = 4f;
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

                // pourPos her kare güncel açıya göre hesaplanır.
                // mouth.y = pourPos.y + mouthRise(angle) = destMouthY + margin (sabit).
                // Açı SmoothDamp ile pürüzsüz değiştiği için pourPos da pürüzsüz kayar.
                float angleForPos = Mathf.Abs(currentAngle) > 0.05f
                    ? currentAngle : initialSignedAngle;
                pourPos = CalculatePourPosition(fromView, toView, angleForPos, pivotHeight);

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
                    toView.SetFillLevel(Mathf.Lerp(toStart, toTarget, pourT));
                }

                // Pozisyon: kayma + tilt offset birlikte uygulanır.
                ApplyTiltWithPivot(fromView, currentAngle, currentBase, pivotHeight);
                // Görsel yüzey fiziksel modele demirlenir: kolon sıvıdan kopmasın.
                AnchorLiquidToLip(fromView, currentAngle);

                // Akış görseli: kayma tamamlanınca göster.
                // Kayma sırasında stream gösterilmez — tüp henüz pourPos'ta değil.
                if (pourStarted && moveElapsed >= slideDuration)
                {
                    Vector3 sourcePoint = CalculateSourceMouth(fromView, currentAngle);
                    // Kolon tüpte kalan sıvıya YAPIŞIK kalsın: üst uç, döken
                    // kenardaki sıvı yüzeyine kadar uzar — aradaki bölge yaka/
                    // delik üstünden köprülenir (kopma bulgusu, dökme sonuna
                    // doğru belirginleşiyordu).
                    Vector3 liquidEdge = CalculateStreamSource(fromView, currentAngle);
                    sourcePoint.y = Mathf.Max(sourcePoint.y, liquidEdge.y);
                    Vector3 destMouth = CalculateDestMouth(toView);
                    Vector3 destSurface = CalculateDestSurface(toView, toView.CurrentFill);
                    if (sourcePoint.y > destSurface.y)
                        streamView.Show(streamColor, sourcePoint, destMouth, destSurface);
                    else
                        streamView.Hide();
                }

                if (pourStarted && pourElapsed >= pourDuration)
                    break;

                yield return null;
            }

            // Son değerleri kesin uygula.
            fromView.SetFillLevel(fromTarget);
            toView.SetFillLevel(toTarget);
            streamView.Hide();
            // Dökme bitti: tüp tamamlandıysa tıpa ŞİMDİ, takılma animasyonuyla;
            // akış sandviçi kapanır (tıpalıysa dilimler tıpa üzerinden açık kalır).
            toView.SetCorkSuppressed(false);
            toView.SetMouthOverlay(false);

            // --- Faz 4+5: Doğrulma ve geri dönüş eş zamanlı ---
            // Giderken kayma+eğilme eş zamanlıydı; dönüşte de doğrulma+kayma
            // eş zamanlı. Tilt offset tüpü kaldırır, hedef tüple çakışma olmaz.
            fromView.Refresh();
            float returnDuration = Mathf.Max(
                fromTarget < 0.001f ? slideDuration * 0.7f : slideDuration,
                slideDuration);
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
            isAnimating = false;
            ReportBoardState();
        }

        /// <summary>
        /// Kaynak tüpün dökme sırasında duracağı konum.
        /// Eğildikten sonra kaynak ağzı hedefin ağzının biraz üstüne düşer.
        /// </summary>
        private Vector3 CalculatePourPosition(TubeView from, TubeView to,
            float signedAngle, float pivotHeight)
        {
            Vector3 dest = to.RestPosition;

            // --- X: döken ağız ucu (deliğin hedefe bakan kenarı) tam hedefin
            // MERKEZİNİN üstüne gelsin: akış kolonu dikey düştüğü için ancak
            // böyle hem kaynağın deliğinden çıkar hem hedef deliğin ortasına
            // iner. ApplyTiltWithPivot modeli: nokta = taban + pivotTelafisi +
            // R(açı)·yerel — buradan taban çözülür. (Eski yaklaşık mouthReach +
            // pullBack cam ağzını kabaca hizalıyordu; kolonla ağız arasında
            // yatay kayma bırakıyordu.)
            float lipSide = -Mathf.Sign(signedAngle);
            Vector3 spout = from.CollarMouthLip(lipSide);
            float cos = Mathf.Cos(signedAngle);
            float sin = Mathf.Sin(signedAngle);
            float spoutOffsetX = pivotHeight * sin + (cos * spout.x - sin * spout.y);
            float xTarget = dest.x - spoutOffsetX;

            // --- Y: kaynak tüpün dibi hedefin ağzının üstünde kalsın ---
            // Böylece kayma sırasında (henüz eğilmeden) gövdeler çakışmaz.
            // Eğilince ağız hedefin üstüne doğru iner. Eski pay -0.25 çıplak
            // cam dönemindendi: yaka/tıpa tüpten taştığı için en dik açıda
            // (~100°) kaynak yaka hedefe DEĞİYORDU (kullanıcı bulgusu) —
            // dökme artık hedef ağzının 0.2 üstünden yapılır.
            float destMouthY = dest.y + TallestTube;
            float yTarget = destMouthY + 0.2f;

            return new Vector3(xTarget, yTarget, 0f);
        }

        /// <summary>
        /// Tüpteki sıvı miktarına göre eğim açısını hesaplar.
        /// Az sıvıda sıvının ağza ulaşması için daha fazla eğilme gerekir.
        /// Dolu tüpte 50°, neredeyse boş tüpte 110°'ye kadar çıkar.
        /// </summary>
        private static float CalculatePourAngle(TubeView fromView)
        {
            const float minAngle = 60f * Mathf.Deg2Rad;
            const float maxAngle = 100f * Mathf.Deg2Rad;

            // Doluluk oranı (0 = boş, 1 = dolu). Açı aralığı, eğik sıvının
            // tüpün fiziksel ağız ucuna (1.0) ulaşması için yeterli marjla
            // seçildi. Eski 50-90° aralığı FillSpan eşiğine göre tasarlanmıştı;
            // 1.0 eşiğiyle yarı dolu tüpte 0.001 eksik kalıp deadlock'a giriyordu.
            float fillSpan = 1f - 0.2f / fromView.Height; // FillHeadroom = 0.2
            float fillRatio = Mathf.Clamp01(fromView.CurrentFill / fillSpan);

            return Mathf.Lerp(maxAngle, minAngle, fillRatio);
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
        /// korunumlu) TiltedEdgeLevel modelinden ALÇAK gösterir — dökme kapısı
        /// "sıvı dudakta" derken görsel geride kalıyor, akış kolonu sıvıdan
        /// kopuk görünüyordu (kullanıcı bulgusu). Fark her kare kaldırma olarak
        /// shader'a yazılır; açı sıfıra dönünce fark da kendiliğinden sıfırlanır.
        /// TiltedEdgeLevel 90°+ açıda sonsuz döner: kaldırma tüp tepesinin
        /// hemen üstüyle sınırlanır (görsel zaten ağızda kırpılıyor).
        /// (Denenen ve geri alınan: kenarı ağza SABİT kilitlemek — kullanıcı
        /// beğenmedi, "hiç olmadı".)
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
                TubeView.MouthWidth * 0.5f * lipSide,
                surfaceNorm * fromView.Height, 0f);

            Vector3 worldPos = fromView.transform.TransformPoint(localPos);
            return transform.InverseTransformPoint(worldPos);
        }

        /// <summary>
        /// Dökme sırasında her kare: eğim açısını sıvı seviyesine göre artır,
        /// akışın uç noktalarını güncelle. Fill animasyonlarıyla paralel çalışır.
        /// </summary>
        private IEnumerator AnimateStream(Color color, TubeView fromView, TubeView toView,
            float direction, Vector3 pourPos, float pivotHeight, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                // Sıvı azaldıkça eğim artar — sıvı her zaman ağza ulaşır.
                float angle = -CalculatePourAngle(fromView) * direction;
                ApplyTiltWithPivot(fromView, angle, pourPos, pivotHeight);
                AnchorLiquidToLip(fromView, angle);

                Vector3 sourceMouth = CalculateSourceMouth(fromView, angle);
                // Kolonun tepesi döken kenardaki sıvı yüzeyine yapışık kalır
                // (bkz. ana döngüdeki not).
                Vector3 liquidEdge = CalculateStreamSource(fromView, angle);
                sourceMouth.y = Mathf.Max(sourceMouth.y, liquidEdge.y);
                Vector3 destMouth = CalculateDestMouth(toView);
                Vector3 destSurface = CalculateDestSurface(toView, toView.CurrentFill);

                // Kaynak ağız hedef yüzeyinin üstündeyse akış göster,
                // altına düştüyse gizle (kolon ters dönerdi).
                if (sourceMouth.y > destSurface.y)
                    streamView.Show(color, sourceMouth, destMouth, destSurface);
                else
                    streamView.Hide();

                yield return null;
            }
        }

        /// <summary>
        /// Hedef tüpteki sıvı yüzeyinin board-local konumu.
        /// Tüpler saydam olduğu için akış ağızda değil, sıvının
        /// olduğu seviyede bitmeli.
        /// </summary>
        private static Vector3 CalculateDestSurface(TubeView toView, float fillLevel)
        {
            float surfaceY = fillLevel * toView.Height;
            return toView.RestPosition + new Vector3(0f, surfaceY, 0f);
        }

        /// <summary>Tüpü A noktasından B noktasına pürüzsüzce kaydırır.</summary>
        private static IEnumerator AnimateMove(TubeView view, Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                view.transform.localPosition = Vector3.Lerp(from, to, t);
                yield return null;
            }

            view.transform.localPosition = to;
        }

        /// <summary>
        /// Tüpü verilen açıdan hedef açıya eğer. Dönüş noktası tüpün dibinde
        /// değil ağzına yakın bir noktada olmalı: pivotHeight kadar yukarıda
        /// sanal bir eksen etrafında döner gibi pozisyon telafisi uygulanır.
        /// </summary>
        private static IEnumerator AnimateTilt(TubeView view, float fromAngle, float toAngle,
            float duration, Vector3 basePosition, float pivotHeight)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                float angle = Mathf.Lerp(fromAngle, toAngle, t);

                ApplyTiltWithPivot(view, angle, basePosition, pivotHeight);
                yield return null;
            }

            ApplyTiltWithPivot(view, toAngle, basePosition, pivotHeight);
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
