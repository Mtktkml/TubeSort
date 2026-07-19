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
        private readonly List<TubeView> tubeViews = new List<TubeView>();

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

            if (glassMaterial == null || liquidMaterial == null)
            {
                enabled = false;
                return;
            }

            board = CreateTestBoard();
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
        /// Kaynak ve hedef tüp farklı davranır:
        /// - Kaynak: eski katmanları korur (dökülen renk görünmeye devam eder),
        ///   seviye düştükçe o renk kaybolur. Animasyon bitince katmanlar güncellenir.
        /// - Hedef: katmanlar hemen güncellenir (yeni renk eklenir), seviye eski
        ///   yerinden yükselmeye başlar.
        /// </summary>
        private IEnumerator AnimatePour(PourResult result)
        {
            const float duration = 1.5f;

            isAnimating = true;
            ClearSelection();

            Debug.Log($"{result.Amount} birim renk#{result.Color}: tüp {result.FromIndex} -> tüp {result.ToIndex}");

            TubeView fromView = tubeViews[result.FromIndex];
            TubeView toView = tubeViews[result.ToIndex];

            // Board hamleyi zaten uyguladı; tube verileri yeni durumu yansıtıyor.
            // Hedef fill'leri şimdi hesapla.
            float fromTarget = fromView.TargetFillLevel;
            float toTarget = toView.TargetFillLevel;

            // Hedef tüp: katmanları şimdi güncelle (yeni renk görünsün),
            // ama seviyeyi eski yerine geri al (oradan yükselecek).
            float toStart = toView.CurrentFill;
            toView.Refresh();
            toView.SetFillLevel(toStart);

            // Kaynak tüp: katmanları güncelleme! Eski renkler görünmeye devam
            // etsin, seviye düştükçe dökülen renk kaybolsun.

            // İki animasyonu paralel başlat.
            Coroutine from = StartCoroutine(fromView.AnimateFill(fromTarget, duration));
            Coroutine to = StartCoroutine(toView.AnimateFill(toTarget, duration));

            yield return from;
            yield return to;

            // Kaynak tüpün katmanlarını şimdi güncelle: dökülen renk artık
            // veride yok, görseli de temizlensin.
            fromView.Refresh();

            isAnimating = false;
            ReportBoardState();
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
