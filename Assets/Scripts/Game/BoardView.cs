using System.Collections;
using System.Collections.Generic;
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

        private Board board;
        private ColorPalette palette;
        private Sprite unitSprite;
        private Material glassMaterial;
        private Material liquidMaterial;
        private Material streamMaterial;
        private readonly List<TubeView> tubeViews = new List<TubeView>();
        private StreamView streamView;

        private int selectedIndex = -1;
        private bool isAnimating;
        private Camera mainCamera;

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

            glassMaterial = CreateMaterial("Glass");
            liquidMaterial = CreateMaterial("Liquid");
            streamMaterial = CreateMaterial("Stream");

            if (glassMaterial == null || liquidMaterial == null || streamMaterial == null)
            {
                enabled = false;
                return;
            }

            board = CreateTestBoard();
            BuildViews();
            BuildStreamView();
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

        private void BuildViews()
        {
            for (int i = 0; i < board.TubeCount; i++)
            {
                var go = new GameObject($"Tube{i}");
                go.transform.SetParent(transform, false);

                var view = go.AddComponent<TubeView>();
                view.Initialize(i, board[i], palette, unitSprite, glassMaterial, liquidMaterial);
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
            // O yüzden satırın üst kenarından tüp boyu kadar aşağı iniyoruz.
            float rowHeight = TallestTube + verticalGap;
            float y = boardHeight * 0.5f - row * rowHeight - TallestTube;

            return new Vector3(x, y, 0f);
        }

        /// <summary>Verilen ızgaranın kaplayacağı alan. Tüplerin gerçek ölçülerinden hesaplanır.</summary>
        private Vector2 MeasureBoard(int columns, int rows)
        {
            float width = columns * TubeView.FullWidth + (columns - 1) * horizontalGap;
            float height = rows * TallestTube + (rows - 1) * verticalGap;

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
            Destroy(glassMaterial);
            Destroy(liquidMaterial);
            Destroy(streamMaterial);

            if (unitSprite != null)
                Destroy(unitSprite.texture);

            Destroy(unitSprite);
        }

        private void Update()
        {
            RefitIfViewChanged();

            // Pointer, Mouse ve Touchscreen'in ortak atasıdır: masaüstünde fare,
            // telefonda (ve Device Simulator'da) parmak aynı kodla okunur.
            Pointer pointer = Pointer.current;
            if (pointer == null) return;

            if (isAnimating) return;
            if (!pointer.press.wasPressedThisFrame) return;

            TubeView clicked = RaycastTube(pointer.position.ReadValue());
            if (clicked != null)
                HandleTubeClick(clicked.Index);
        }

        /// <summary>
        /// Ekran koordinatındaki dokunuşun hangi tüpe denk geldiğini bulur.
        /// BoxCollider2D hızlı eleme yapar; ardından SDF ile tıklamanın gerçekten
        /// tüp şekli içinde olup olmadığı doğrulanır.
        /// </summary>
        private TubeView RaycastTube(Vector2 screenPosition)
        {
            Vector3 worldPoint = mainCamera.ScreenToWorldPoint(screenPosition);
            Collider2D hit = Physics2D.OverlapPoint(worldPoint);
            if (hit == null) return null;

            var view = hit.GetComponent<TubeView>();
            if (view == null) return null;

            return view.ContainsPoint(worldPoint) ? view : null;
        }

        private void HandleTubeClick(int index)
        {
            // Henüz seçim yok: boş tüpten dökme yapılamayacağı için boş tüp seçtirmiyoruz.
            if (selectedIndex == -1)
            {
                if (board[index].IsEmpty) return;

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

            PourResult result = board.Pour(selectedIndex, index);

            if (result.Success)
            {
                StartCoroutine(AnimatePour(result));
                return;
            }

            // Hamle geçersizdi. Oyuncu muhtemelen yeni bir kaynak seçmek istiyor:
            // seçimi iptal etmek yerine seçimi tıklanan tüpe taşımak daha rahat.
            ClearSelection();
            if (!board[index].IsEmpty)
            {
                selectedIndex = index;
                tubeViews[index].SetSelected(true);
            }
        }

        private void ClearSelection()
        {
            if (selectedIndex == -1) return;

            tubeViews[selectedIndex].SetSelected(false);
            selectedIndex = -1;
        }

        private void ReportBoardState()
        {
            if (board.IsSolved)
                Debug.Log("<color=lime>Tahta çözüldü!</color>");
            else if (!board.HasAnyValidMove)
                Debug.Log("<color=orange>Çıkmaz: oynanacak hamle kalmadı.</color>");
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
            const float slideDuration = 0.25f;
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
            // ama seviyeyi eski yerine geri al (oradan yükselecek).
            float toStart = toView.CurrentFill;
            toView.Refresh();
            toView.SetFillLevel(toStart);

            // Kaynak tüpü üstte çiz.
            fromView.SetSortingOffset(10);

            // Eğilme yönü: hedefe doğru eğil.
            // Aynı sütundaysa (dx ≈ 0) sağa doğru eğil.
            float dx = toView.RestPosition.x - fromView.RestPosition.x;
            float direction = Mathf.Abs(dx) < 0.01f ? 1f : Mathf.Sign(dx);

            // Dönüş noktası tüpün ağzına yakın olmalı; dipten döndürmek doğal durmaz.
            float pivotHeight = fromView.Height * 0.8f;

            // --- Faz 1: Kalkış + Kayma ---
            Vector3 startPos = fromView.RestPosition;
            float initialSignedAngle = -CalculatePourAngle(fromView) * direction;
            Vector3 pourPos = CalculatePourPosition(fromView, toView, initialSignedAngle, pivotHeight);
            yield return StartCoroutine(AnimateMove(fromView, startPos, pourPos, slideDuration));

            // --- Faz 2+3: Eğilme ve dökme, tek açı sistemi ---
            // Açı her zaman CalculatePourAngle'dan gelir, SmoothDamp ile takip edilir.
            // Ayrı "eğilme fazı" yok: tüp doğal hızında eğilir, sıvı ağza
            // ulaşınca dökme başlar. Sıvı azaldıkça açı artar (daha çok eğilir),
            // sıvı neredeyse bittiğinde fillFade açıyı sıfıra indirir.
            Color streamColor = palette.Get(result.Color);
            bool pourStarted = false;
            float pourElapsed = 0f;
            float fromStart = fromView.CurrentFill;
            float currentAngle = 0f;
            float angleVelocity = 0f;

            while (true)
            {
                float dt = Time.deltaTime;

                // Hedef açı: fill'e göre dinamik, tek kaynak.
                float targetAngle = -CalculatePourAngle(fromView) * direction;

                currentAngle = Mathf.SmoothDamp(
                    currentAngle, targetAngle, ref angleVelocity, angleSmoothTime);

                // Sıvı ağza ulaştığında dökmeyi başlat.
                if (!pourStarted)
                {
                    if (HasLiquidReachedMouth(fromView, currentAngle))
                        pourStarted = true;
                }

                // Dökme: sıvı ağza ulaştıysa seviyeler güncellenir.
                if (pourStarted)
                {
                    pourElapsed += dt;
                    float pourT = Mathf.Clamp01(pourElapsed / pourDuration);

                    fromView.SetFillLevel(Mathf.Lerp(fromStart, fromTarget, pourT));
                    toView.SetFillLevel(Mathf.Lerp(toStart, toTarget, pourT));
                }

                ApplyTiltWithPivot(fromView, currentAngle, pourPos, pivotHeight);

                // Akış görseli: kaynak sıvı yüzeyinden başlar.
                if (pourStarted)
                {
                    Vector3 sourcePoint = CalculateStreamSource(fromView, currentAngle);
                    Vector3 destSurface = CalculateDestSurface(toView, toView.CurrentFill);
                    if (sourcePoint.y > destSurface.y)
                        streamView.Show(streamColor, sourcePoint, destSurface);
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

            // --- Faz 4: Doğrulma ---
            // Boş/dolu fark etmez, her durumda tilt pürüzsüzce sıfıra iner.
            // Boş tüpte sıvı zaten yok (shader fill≤0 discard eder),
            // cam tüp doğalca yerine döner.
            fromView.Refresh();
            float returnDuration = fromTarget < 0.001f ? slideDuration * 0.7f : slideDuration;
            yield return StartCoroutine(
                AnimateTilt(fromView, currentAngle, 0f, returnDuration, pourPos, pivotHeight));

            // --- Faz 5: Geri dönüş ---
            yield return StartCoroutine(AnimateMove(fromView, pourPos, startPos, slideDuration));

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
            float bodyHeight = from.Height;

            // --- X: eğildikten sonra ağız hedefin üstüne düşsün ---
            // Pivot sabit kalır; ağız (BH - pH) uzaklıkta döner.
            // Kaynak tüpü biraz geriye çekerek tüplerin iç içe girmesini önle.
            // signedAngle'dan yönü çıkar: negatif açı → sağa eğilir → kaynak soldan gelir.
            float side = signedAngle < 0f ? -1f : 1f;
            float pullBack = side * TubeView.FullWidth * 0.2f;
            float mouthReach = (bodyHeight - pivotHeight) * Mathf.Sin(signedAngle);
            float xTarget = dest.x + mouthReach + pullBack;

            // --- Y: kaynak ağzı hedefin ağzının üstünde kalsın ---
            // Eğilmiş ağzın Y'si: pourPos.y + pH + (BH-pH)*cos(angle).
            // Hedefin ağzının biraz üstüne koyuyoruz ki sıvı aşağı aksın.
            float mouthRise = pivotHeight + (bodyHeight - pivotHeight) * Mathf.Cos(signedAngle);
            float destMouthY = dest.y + TallestTube;
            float yTarget = destMouthY - mouthRise + bodyHeight * 0.35f;

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
            float tiltSlope = Mathf.Sin(angle) / Mathf.Max(Mathf.Abs(Mathf.Cos(angle)), 0.2f);
            float maxOffset = Mathf.Abs(0.5f * tiltSlope * (TubeView.Width / view.Height));
            return view.CurrentFill + maxOffset >= 1f;
        }

        /// <summary>
        /// Eğik kaynak tüpün döken ağız ucunun board-local konumu.
        /// TransformPoint tilt ve pivot telafisini otomatik hesaplar.
        /// </summary>
        private Vector3 CalculateSourceMouth(TubeView fromView, float signedAngle)
        {
            // Döken taraf: tüp sağa eğiliyorsa (negatif açı) sağ kenar döker.
            float lipSide = -Mathf.Sign(signedAngle);
            Vector3 lipLocal = new Vector3(
                TubeView.MouthWidth * 0.5f * lipSide, fromView.Height, 0f);

            // TransformPoint: tüpün eğimi ve pozisyon telafisini hesaba katar.
            Vector3 lipWorld = fromView.transform.TransformPoint(lipLocal);
            return transform.InverseTransformPoint(lipWorld);
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
            // HasLiquidReachedMouth'daki formülün aynısı: maxOffset döken taraftaki
            // yükselmeyi verir. tiltSlope zaten signedAngle'dan işaretli geldiği
            // için lipSide ile çarpmak işareti iki kez çevirir — bunun yerine
            // mutlak değer alıp ekliyoruz (döken taraf her zaman yüksek taraftır).
            float tiltSlope = Mathf.Sin(signedAngle)
                / Mathf.Max(Mathf.Abs(Mathf.Cos(signedAngle)), 0.2f);
            float maxOffset = Mathf.Abs(0.5f * tiltSlope * (TubeView.Width / fromView.Height));
            float surfaceNorm = fromView.CurrentFill + maxOffset;

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

                Vector3 sourceMouth = CalculateSourceMouth(fromView, angle);
                Vector3 destSurface = CalculateDestSurface(toView, toView.CurrentFill);

                // Kaynak ağız hedef yüzeyinin üstündeyse akış göster,
                // altına düştüyse gizle (ters Bezier olur).
                if (sourceMouth.y > destSurface.y)
                    streamView.Show(color, sourceMouth, destSurface);
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
