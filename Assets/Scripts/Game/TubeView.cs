using System.Collections;
using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tek bir tüpü ekranda çizer: cam/yaka/tıpa sprite'ları
    /// (SpriteRenderer katmanları), sıvı ve 2.5D yüzeyi (damla halkaları
    /// dahil) shader ile.
    ///
    /// Bu sınıfın işi çekirdekteki Tube'u görünümün diline çevirmek:
    /// "dipten yukarı [kırmızı, sarı, sarı]" -> sınırlar [0.25, 0.75] ve renkler.
    /// </summary>
    public class TubeView : MonoBehaviour
    {
        public const float Width = 0.8f;
        private const float UnitHeight = 0.5f;

        /// <summary>
        /// Shader'daki MAX_LAYERS ile aynı olmak zorunda.
        ///
        /// En kötü durumda katman sayısı kapasiteye eşittir: her birim bir
        /// öncekinden farklı renkse hiçbiri birleşmez. Yani bu sayı aynı zamanda
        /// desteklenen en büyük tüp kapasitesidir.
        ///
        /// Sekiz, oynanabilir tüp boylarını (4-6 birim) rahatça karşılar.
        /// Büyütmenin bedeli var: shader döngüsü her piksel için bu kadar tur
        /// döner. Daha büyük kapasite gerekirse burayı ve shader'daki MAX_LAYERS'ı
        /// birlikte artır; aşım durumunda Initialize hata basar.
        /// </summary>
        private const int MaxLayers = 8;

        private const float SelectedLift = 0.3f;

        /// <summary>
        /// Gövdenin üst köşelerinin yuvarlaklığı. Dünya birimi.
        /// Küçük tutulur: ağız bileziği zaten üstte durduğu için gövdenin tepesi
        /// neredeyse düz kesilmiş görünmeli.
        /// </summary>
        private const float TopRadius = 0.04f;

        /// <summary>
        /// Dibin yuvarlaklığı. Genişliğin yarısına eşit olduğu için dip
        /// tam yarım daire olur - deney tüpü gibi.
        /// </summary>
        private const float BottomRadius = Width * 0.5f;

        /// <summary>
        /// Sıvı/tıklama dörtgeninin gövde dışına yan payı: kenar yumuşaması
        /// kırpılmasın.
        /// </summary>
        private const float QuadPadding = 0.06f;

        /// <summary>
        /// Tüp ağzına kadar dolu olsa bile sıvının tepesiyle tüpün ucu arasında
        /// kalan boşluk (dünya birimi, tüpün boyuyla ölçeklenmez). Ölçüden
        /// türer: tıpanın tüpe sarkan kısmı + görünür pay — tıpa takılıyken
        /// üst sıvı katmanı da tamamen görünür kalır. HeightFor tüpü bu pay
        /// kadar uzatır; birim sıvı boyu etkilenmez.
        /// </summary>
        private static float FillHeadroom => CorkSpriteHeight - TopOverhang + CorkLiquidGap;

        /// <summary>Tıpa dibi ile dolu sıvının tepesi arasında istenen görünür
        /// boşluk (2.5D yüzey elipsine de yer bırakır).</summary>
        private const float CorkLiquidGap = 0.12f;

        /// <summary>
        /// Cam görselinin üst kenarı yakanın ARKASINA bu kadar uzar: yaka tüpün
        /// üstüne oturur, tüp ağzı/kenarı yakanın altından sırıtmaz, arada fon
        /// boşluğu kalmaz. Sıvı matematiği etkilenmez — uzantı yalnız cam
        /// sprite'ının boyuna eklenir, sıvı kendi kısa gövde ölçüsüyle kırpılır.
        /// </summary>
        private const float MouthExtension = Width * 0.15f;

        // ── Görsel katman: cam/yaka/tıpa PNG'leri (Resources/Sprites),
        // SpriteRenderer ile. Ölçek çapası: yaka görselinin tam genişliği =
        // FullWidth (1.2 birim); PPU'lar buna göre girildi (collar 244,
        // cork 229, tube 247.5 — birleşik referans `tube (2).png` piksel
        // oranlarından). Görsel ya da PPU değişirse buradaki piksel/PPU
        // sabitleri de birlikte güncellenmeli. ──
        /// <summary>Yakanın yarı genişliği (yerleşim çapası; FullWidth = 2×bu).</summary>
        private const float CollarRx = Width * 0.75f;
        /// <summary>Yaka merkezinin tüp tepesine göre y'si: yakanın alt kenarı
        /// tüp ağzını örter, arada fon boşluğu kalmaz.</summary>
        private const float CollarCenterY = Width * 0.21f;
        /// <summary>Resources yolları — BoardView yükler, TubeView kullanır.</summary>
        public const string CollarSpritePath = "Sprites/collar";
        public const string CorkSpritePath = "Sprites/cork";
        /// <summary>Cam tüp görseli (204×766, PPU 247.5 → gövde 0.824 birim;
        /// 9-slice alt border 88 px import'ta tanımlı — dip kavisi sabit kalır,
        /// düz gövde kapasiteye göre uzar).</summary>
        public const string TubeSpritePath = "Sprites/tube";
        /// <summary>Yaka görselinin PPU'su ve satır sayısı — piksel sabitlerini
        /// dünya birimine çevirmek için.</summary>
        private const float CollarPpu = 244f;
        private const float CollarSpriteRows = 113f;
        /// <summary>Yaka görselinin dünya yüksekliği.</summary>
        private const float CollarSpriteHeight = CollarSpriteRows / CollarPpu;
        /// <summary>Tıpa görselinin dünya yüksekliği (201 px / 229 PPU) —
        /// FillHeadroom türetimi statik kalsın diye sabit; görsel/PPU değişirse
        /// birlikte güncellenir (CreateCork konumu çalışma anında sprite'tan okur).</summary>
        private const float CorkSpriteHeight = 201f / 229f;
        // collar.png eğri sınırları (piksel, satırlar ÜSTTEN). Ön parçalar
        // dikdörtgen DEĞİL eğri maskeyle kesilir: düz kenarlar tıpayı cetvelle
        // kesilmiş gösterir. Görsel değişirse bu sayılar yeniden ölçülmeli
        // (koyu piksel sütun taraması; delik altı 40→48, seam 59→66 parabolü).
        /// <summary>Delik elipsinin merkezi (x).</summary>
        private const float CollarHoleCx = 146f;
        /// <summary>Delik elipsinin merkezi (satır).</summary>
        private const float CollarHoleCy = 25f;
        /// <summary>Delik dış yarıçapları (ön yay bu elipsi izler).</summary>
        private const float CollarHoleRx = 100f;
        private const float CollarHoleRy = 24f;
        /// <summary>Deliğin ön kenar çizgisi kalınlığı: pencere bu kadar içeride
        /// biter, koyu yay tıpanın ÖNÜNDE kalır → "deliğe girmiş" okunur.</summary>
        private const float CollarHoleRim = 3.5f;
        // Parantez (seam) sınırı sabit eğri DEĞİL: çizginin alt kenarı çalışma
        // anında sütun sütun ölçülür (MeasureSeamBottom). El çizimi çizgi
        // simetrik değil (sol uç dx-60'ta satır 63, sağ uçta 61); sabit parabol
        // uçlarda 1-2 px bej sızdırır.
        /// <summary>Çizgi araması bu satır aralığında yapılır (üstten).</summary>
        private const int CollarSeamScanTop = 56;
        private const int CollarSeamScanBottom = 76;
        /// <summary>Aramanın yatay sınırı (delik merkezine göre): ötesinde aynı
        /// satırlardan yan kontur geçiyor, çizgiyle karışırdı.</summary>
        private const float CollarSeamScanHalfWidth = 130f;
        /// <summary>Hiç çizgi bulunamayan sütun için yedek sınır (görsel
        /// değişirse sessiz bozulma yerine makul varsayılan).</summary>
        private const float CollarSeamFallbackRow = 67f;
        /// <summary>Alt şeridin üst kenarı: aşağı-dışbükey oval — yanlarda bu
        /// satırdan, ortada +dip kadar aşağıdan geçer (tıpa dip ovali hissi).</summary>
        private const float CollarStripSideRow = 88f;
        private const float CollarStripDip = 15f;
        private const float CollarStripHalfWidth = 96f;
        /// <summary>Tıpa tepesinin yaka üst kenarına göre yüksekliği (birleşik
        /// referansta 41 px; referans ölçeği 247.5 px/birim).</summary>
        private const float CorkTopAboveCollarTop = 0.166f;

        private static readonly int LayerColorsId = Shader.PropertyToID("_LayerColors");
        private static readonly int LayerTopsId = Shader.PropertyToID("_LayerTops");
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int BodySizeId = Shader.PropertyToID("_BodySize");
        private static readonly int TopRadiusId = Shader.PropertyToID("_TopRadius");
        private static readonly int BottomRadiusId = Shader.PropertyToID("_BottomRadius");
        private static readonly int TiltAngleId = Shader.PropertyToID("_TiltAngle");
        private static readonly int SurfaceLiftId = Shader.PropertyToID("_SurfaceLift");
        private static readonly int RippleStrengthId = Shader.PropertyToID("_RippleStrength");
        private static readonly int SplashStrengthId = Shader.PropertyToID("_SplashStrength");
        private static readonly int SwaySlopeId = Shader.PropertyToID("_SwaySlope");

        private Tube tube;
        private ColorPalette palette;
        private Sprite unitSprite;

        private SpriteRenderer glass;
        private SpriteRenderer liquid;
        private SpriteRenderer collar;
        private SpriteRenderer cork;
        private SpriteRenderer collarFrontTop;
        private SpriteRenderer collarFrontBottom;
        private MaterialPropertyBlock properties;
        private Vector3 restPosition;
        private bool isSelected;
        private float currentFill;
        private float tiltAngle;
        private float surfaceLift;
        private float rippleStrength;
        private float splashStrength;
        private float swaySlope;
        private Coroutine rippleRoutine;
        private Coroutine sloshRoutine;
        private Vector3 corkRestPosition;
        private bool corkSuppressed;
        private bool mouthOverlayVisible;
        private Coroutine corkRoutine;
        /// <summary>İlk Refresh (kurulum) tamamlandı mı? Kurulumda tıpa
        /// animasyonsuz oturur; oyun sırasında belirince animasyon oynar.</summary>
        private bool viewReady;

        // Shader'a gönderilecek diziler. Her yenilemede yeniden ayırmamak için
        // bir kez oluşturulup tekrar tekrar doldurulur.
        private readonly Vector4[] layerColors = new Vector4[MaxLayers];
        private readonly float[] layerTops = new float[MaxLayers];

        /// <summary>Bu görünümün tahtadaki tüp sırası. Tıklama olayında kullanılır.</summary>
        public int Index { get; private set; }

        public void Initialize(int index, Tube tube, ColorPalette palette, Sprite unitSprite,
            Material liquidMaterial, Sprite tubeSprite, Sprite collarSprite,
            Sprite collarFrontTopSprite, Sprite collarFrontBottomSprite, Sprite corkSprite)
        {
            Index = index;
            this.tube = tube;
            this.palette = palette;
            this.unitSprite = unitSprite;

            // Kapasite katman sınırını aşarsa sıvı sessizce yanlış çizilir:
            // sığmayan katmanlar shader'a hiç gitmez, ama doluluk seviyesi
            // tüpün dolu olduğunu söylediği için üstteki sıvı son sığan
            // katmanın rengiyle boyanır. Tahtada mavi duran birim ekranda
            // sarı görünür. Sessiz kalmaktansa bağıralım.
            if (tube.Capacity > MaxLayers)
            {
                Debug.LogError($"Tüp {index} kapasitesi {tube.Capacity}, katman sınırı {MaxLayers}. " +
                    "Sıvı yanlış çizilecek: TubeView.MaxLayers ve shader'daki MAX_LAYERS artırılmalı.");
            }

            properties = new MaterialPropertyBlock();

            // Cam 9-slice görsel; tepesi MouthExtension kadar yakanın arkasına
            // uzanır. Sıvı shader'ı kendi şeklini kendisi çizer, kısa gövdeyle
            // kırpılır (fill matematiği camdan bağımsız).
            CreateGlass(tubeSprite);
            liquid = CreateQuad("Liquid", liquidMaterial, sortingOrder: 1, QuadHeight);

            CreateCollar(collarSprite, collarFrontTopSprite, collarFrontBottomSprite);
            CreateCork(corkSprite);
            CreateClickArea();
            Refresh();
            viewReady = true;

            // Level başı çalkantısı: görünüm kurulur kurulmaz sıvı sallanır,
            // sönümlenip durulur (boş tüpte sıvı yok, etki görünmez).
            PlaySlosh(0.15f, 1.6f);
        }

        /// <summary>
        /// Cam ve sıvı iki ayrı dörtgendir; gövde ölçüsü her birinin kendi property
        /// block'una yazılır. Cam, yakanın arkasına MouthExtension kadar uzar; sıvı
        /// kısa gövdeyle kalır — böylece sıvı hiçbir zaman uzantıya taşmaz.
        /// </summary>
        private SpriteRenderer CreateQuad(string name, Material material, int sortingOrder,
            float quadHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = unitSprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;

            go.transform.localScale = new Vector3(QuadWidth, quadHeight, 1f);
            // Sprite'ın merkezi ortada; tüpün dibi yerel sıfır noktasında dursun.
            go.transform.localPosition = new Vector3(0f, quadHeight * 0.5f, 0f);

            return renderer;
        }

        /// <summary>
        /// Bej yakayı üç katman kurar — tıpa yakanın içinden geçiyormuş gibi
        /// okunsun diye sandviç:
        ///   arka (order 2): görselin tamamı (delik dahil), tıpanın ARKASINDA;
        ///   ön-üst parça (order 4): delik ön yayı (kenar çizgisi dahil, delik
        ///     içi pencere şeffaf) → parantez çizgisinin altı — tıpa deliğe
        ///     girmiş okunur, delikle parantez arasında bej tıpayı örter;
        ///   ön-alt şerit (order 4): üst kenarı oval, altta yakanın konturu —
        ///     tıpanın önünde.
        /// Ön parçalar tıpasız tüpte kapalı: arka katman zaten aynı pikselleri
        /// gösterir, açık kalsalar AA kenarları çift binerdi. Parça sprite'ları
        /// BoardView'da bir kez üretilir (CreateCollarFront*Sprite) ve paylaşılır.
        /// </summary>
        private void CreateCollar(Sprite full, Sprite frontTop, Sprite frontBottom)
        {
            collar = CreateCollarPiece("Collar", full, 2);
            collarFrontTop = CreateCollarPiece("CollarFrontTop", frontTop, 4);
            collarFrontBottom = CreateCollarPiece("CollarFrontBottom", frontBottom, 4);
            collarFrontTop.enabled = false;
            collarFrontBottom.enabled = false;
        }

        /// <summary>
        /// Yaka parçasını, pikselleri tam görselle birebir hizalanacak şekilde
        /// yerleştirir: parçanın doku dikdörtgeni tam görselin merkezinden ne
        /// kadar aşağıdaysa dünya konumu da o kadar kaydırılır (tam görsel için
        /// kayma sıfırdır; dilimler üst üste bindiğinde pikseller örtüşür).
        /// </summary>
        private SpriteRenderer CreateCollarPiece(string name, Sprite sprite, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = sortingOrder;

            float offset = (sprite.textureRect.center.y - sprite.texture.height * 0.5f)
                / sprite.pixelsPerUnit;
            go.transform.localPosition = new Vector3(0f, BodyHeight + CollarCenterY + offset, 0f);
            return r;
        }

        /// <summary>Ön-üst parça: delik merkezinden parantezin altına kadar, delik
        /// içi PENCERE (kenar çizgisi hariç) şeffaf — tıpa pencereden görünür,
        /// deliğin ön yayı tıpanın önünde kalır. BoardView bir kez yaratır,
        /// yaşam döngüsünü (doku dahil) o yönetir.</summary>
        public static Sprite CreateCollarFrontTopSprite(Sprite collar)
        {
            float[] seamBottom = MeasureSeamBottom(collar.texture);
            return MaskedCollarPiece(collar, (x, row) => FrontTopKeep(x, row, seamBottom));
        }

        /// <summary>Ön-alt şerit: üst kenarı aşağı-dışbükey oval, altta yakanın
        /// kendi konturuna kadar.</summary>
        public static Sprite CreateCollarFrontBottomSprite(Sprite collar) =>
            MaskedCollarPiece(collar, FrontBottomKeep);

        private static bool FrontTopKeep(int x, int row, float[] seamBottom)
        {
            float dx = x - CollarHoleCx;
            // Delik merkezinin üstü arka bandın malı: tıpa arka kenarın önünde.
            if (row < CollarHoleCy) return false;
            // Parça, çizginin ölçülen ALT kenarına kadar iner (son koyu satır
            // dahil); bir alt satırdan itibaren tıpa — çizgiye tam değer.
            if (row > seamBottom[x]) return false;
            // Delik içi pencere: kenar çizgisi kadar içeride biten elips.
            float nx = dx / (CollarHoleRx - CollarHoleRim);
            float ny = (row - CollarHoleCy) / (CollarHoleRy - CollarHoleRim);
            return nx * nx + ny * ny >= 1f;
        }

        /// <summary>
        /// Parantez çizgisinin ALT kenarını sütun sütun ölçer: koyu piksel
        /// araması (çizgi rengi koyu kahve, bej zeminden net ayrışır). Çizginin
        /// bittiği/olmadığı sütunlar en yakın ölçülü değeri sürdürür; hiç
        /// ölçüm yoksa yedek sabit kullanılır.
        /// </summary>
        private static float[] MeasureSeamBottom(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            Color[] pixels = tex.GetPixels();
            var bottom = new float[w];
            for (int x = 0; x < w; x++)
            {
                bottom[x] = -1f;
                if (Mathf.Abs(x - CollarHoleCx) > CollarSeamScanHalfWidth) continue;
                for (int row = CollarSeamScanTop; row <= CollarSeamScanBottom; row++)
                {
                    Color c = pixels[(h - 1 - row) * w + x];
                    if (c.a > 0.5f && c.r < 0.52f && c.g < 0.42f)
                        bottom[x] = row;
                }
            }

            // Boş sütunları komşu ölçümlerle doldur: soldan sürdür, sonra baştaki
            // boşluklar için sağdan sürdür.
            float last = -1f;
            for (int x = 0; x < w; x++)
            {
                if (bottom[x] >= 0f) last = bottom[x];
                else bottom[x] = last;
            }
            last = CollarSeamFallbackRow;
            for (int x = w - 1; x >= 0; x--)
            {
                if (bottom[x] >= 0f) last = bottom[x];
                else bottom[x] = last;
            }
            return bottom;
        }

        private static bool FrontBottomKeep(int x, int row)
        {
            float nx = (x - CollarHoleCx) / CollarStripHalfWidth;
            float top = CollarStripSideRow
                + CollarStripDip * Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx));
            return row >= top;
        }

        /// <summary>
        /// Yaka dokusundan eğri sınırlı parça üretir: tam boyutlu kopya, bölge
        /// dışı alfa sıfır (tam boyut = konum kaymaz, CreateCollarPiece hizası
        /// kendiliğinden doğru). collar.png Read/Write açık olmalı.
        /// </summary>
        private static Sprite MaskedCollarPiece(Sprite source, System.Func<int, int, bool> keep)
        {
            var src = source.texture;
            int w = src.width, h = src.height;
            Color[] pixels = src.GetPixels();
            for (int ty = 0; ty < h; ty++)
            {
                int row = h - 1 - ty;   // üstten satır numarası (tarama tablosuyla aynı)
                for (int x = 0; x < w; x++)
                {
                    if (keep(x, row)) continue;
                    // Yalnız alfa sıfırlanır, RGB korunur: Color.clear (şeffaf
                    // SİYAH) kullanılırsa bilinear filtre kenar pikselini siyahla
                    // harmanlar ve maske sınırları boyunca soluk koyu çizgiler
                    // bırakır.
                    Color c = pixels[ty * w + x];
                    c.a = 0f;
                    pixels[ty * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = src.filterMode,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(pixels);
            tex.Apply(false, makeNoLongerReadable: true);
            return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Mantar tıpa sprite'ını kurar (order 3: arka yakanın önünde, ön
        /// dilimlerin arkasında — sandviç). Konum birleşik referanstan: tepe, yaka
        /// üst kenarının CorkTopAboveCollarTop kadar üstünde; boy PPU'dan gelir,
        /// alt ucu tüp ağzından içeri sarkar. Başlangıçta gizli; yalnız tamamlanan
        /// tüpte görünür (bkz. Refresh).
        /// </summary>
        private void CreateCork(Sprite sprite)
        {
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);

            cork = go.AddComponent<SpriteRenderer>();
            cork.sprite = sprite;
            cork.sortingOrder = 3;   // yakanın ÖNÜNDE, ön dilimlerin arkasında
            cork.enabled = false;

            float collarTop = BodyHeight + CollarCenterY + CollarSpriteHeight * 0.5f;
            float corkTop = collarTop + CorkTopAboveCollarTop;
            corkRestPosition = new Vector3(0f, corkTop - sprite.bounds.extents.y, 0f);
            go.transform.localPosition = corkRestPosition;
        }

        /// <summary>
        /// Tüpün ekranda kapladığı toplam genişlik. En geniş parça bej yaka
        /// olduğu için yerleşim ona göre yapılır; yoksa komşu tüplerin
        /// yakaları birbirine girer. (Cam/sıvı daha dar.)
        /// </summary>
        public static float FullWidth => 2f * CollarRx;

        /// <summary>
        /// Görsellerin tüp tepesinin (BodyHeight) üstüne taşan kısmı: tıpanın
        /// tepesi (yaka üst kenarı + CorkTopAboveCollarTop). Tıpa gizliyken de
        /// yer ayrılır — hem tüp tamamlanınca tahta yeniden ölçeklenmesin, hem de
        /// kapalı renderer'ın sınırları da ekran ölçümüne girer (LayoutFitTests
        /// bounds toplar).
        /// </summary>
        public static float TopOverhang =>
            CollarCenterY + CollarSpriteHeight * 0.5f + CorkTopAboveCollarTop;

        /// <summary>
        /// FullWidth dışına taşan yan pay. Yaka görseli tam FullWidth
        /// genişliğinde (PPU çapası öyle seçildi), tıpa daha dar — taşma yok.
        /// </summary>
        public static float SideOverhang => 0f;

        /// <summary>Verilen kapasitedeki bir tüpün ekranda kaplayacağı yükseklik:
        /// sıvı alanı (kapasite × birim) + tepe payı (FillHeadroom). Böylece her
        /// birim sıvı tam UnitHeight boyundadır ve dolu tüpte bile tepede tıpaya
        /// yetecek boşluk kalır.</summary>
        public static float HeightFor(int capacity) =>
            capacity * UnitHeight + FillHeadroom;

        /// <summary>Sıvının durduğu gövdenin yüksekliği; tüpün tam boyu.</summary>
        private float BodyHeight => HeightFor(tube.Capacity);

        /// <summary>Sıvı gövdenin en fazla bu kadarını kaplar. Gövde uzadıkça 1'e yaklaşır.</summary>
        private float FillSpan => 1f - FillHeadroom / BodyHeight;

        /// <summary>Sıvı/tıklama dörtgeninin genişliği: gövde + iki yanda pay.</summary>
        private static float QuadWidth => Width + 2f * QuadPadding;

        /// <summary>Sıvı dörtgeninin boyu: gövdenin boyu (fill matematiği buna göre).</summary>
        private float QuadHeight => BodyHeight;

        /// <summary>Cam dörtgeninin boyu: gövde + yaka arkasına saklanan uzantı.</summary>
        private float GlassQuadHeight => BodyHeight + MouthExtension;

        /// <summary>
        /// Cam tüp sprite'ını kurar (order 0, sıvının arkasında). 9-slice:
        /// import'ta tanımlı alt border dip kavisini korur, yalnız düz gövde
        /// kapasiteye göre uzar (görseldeki parlama şeritleri de orantılı
        /// uzar — dikey çizgiler, doğal durur). Pivot Bottom olduğu için yerel
        /// sıfır tüpün dibidir; genişlik görselin doğal genişliği (0.824 birim,
        /// birleşik referans gövde/yaka oranı).
        /// </summary>
        private void CreateGlass(Sprite sprite)
        {
            var go = new GameObject("Glass");
            go.transform.SetParent(transform, false);

            glass = go.AddComponent<SpriteRenderer>();
            glass.sprite = sprite;
            glass.sortingOrder = 0;
            glass.drawMode = SpriteDrawMode.Sliced;
            glass.size = new Vector2(sprite.bounds.size.x, GlassQuadHeight);
        }

        private void WriteShape(float bodyHeight, float quadHeight)
        {
            properties.SetVector(QuadSizeId, new Vector4(QuadWidth, quadHeight, 0f, 0f));
            properties.SetVector(BodySizeId, new Vector4(Width, bodyHeight, 0f, 0f));
            properties.SetFloat(TopRadiusId, TopRadius);
            properties.SetFloat(BottomRadiusId, BottomRadius);
        }

        /// <summary>Tıklamayı yakalayacak görünmez alan. Cam gövdenin tamamını kaplar.</summary>
        private void CreateClickArea()
        {
            var box = gameObject.AddComponent<BoxCollider2D>();

            box.size = new Vector2(QuadWidth, QuadHeight);
            box.offset = new Vector2(0f, QuadHeight * 0.5f);
        }

        /// <summary>
        /// Çekirdekteki tüpün güncel içeriğini shader'a bildirir.
        /// Bitişik aynı renkler tek katmanda birleştirilir: sıvı görünsün diye,
        /// aralarında sınır çizgisi olmamalı.
        /// </summary>
        public void Refresh()
        {
            int layerCount = 0;

            for (int i = 0; i < tube.Count; i++)
            {
                int color = tube.Liquid[i];
                bool sameAsPrevious = layerCount > 0 && tube.Liquid[i - 1] == color;

                if (!sameAsPrevious)
                {
                    if (layerCount >= MaxLayers) break;

                    layerColors[layerCount] = ToShaderColor(palette.Get(color));
                    layerCount++;
                }

                // Katmanın üst sınırı, o katmandaki son birimin üstüdür.
                layerTops[layerCount - 1] = (i + 1) / (float)tube.Capacity * FillSpan;
            }

            liquid.GetPropertyBlock(properties);
            WriteShape(BodyHeight, QuadHeight); // sıvı kısa gövdeyle kırpılır
            properties.SetVectorArray(LayerColorsId, layerColors);
            properties.SetFloatArray(LayerTopsId, layerTops);
            currentFill = tube.Count / (float)tube.Capacity * FillSpan;
            properties.SetFloat(FillLevelId, currentFill);
            properties.SetInt(LayerCountId, layerCount);
            properties.SetFloat(TiltAngleId, tiltAngle);
            properties.SetFloat(SurfaceLiftId, surfaceLift);
            properties.SetFloat(RippleStrengthId, rippleStrength);
            properties.SetFloat(SplashStrengthId, splashStrength);
            properties.SetFloat(SwaySlopeId, swaySlope);
            liquid.SetPropertyBlock(properties);

            RefreshCork();
        }

        /// <summary>
        /// Dökme animasyonu boyunca tıpanın erken belirmesini engeller: veri
        /// dökme başında değiştiği için, bastırılmazsa tıpa akış daha akarken
        /// belirir. false'a dönerken tüp tamamlandıysa tıpa takılma
        /// animasyonuyla gelir.
        /// </summary>
        public void SetCorkSuppressed(bool suppressed)
        {
            corkSuppressed = suppressed;
            RefreshCork();
        }

        /// <summary>
        /// Tıpa görünürlüğü: tamamlanmış (dolu + tek renk) ve bastırılmamış
        /// tüpte açık. Tube.IsComplete boş tüpte de true döner; boşta tıpa
        /// istemiyoruz (!IsEmpty). Ön yaka dilimleri yalnız tıpayla birlikte
        /// çizilir. Oyun sırasında AÇILIRKEN takılma animasyonu oynar;
        /// kapanış ve kurulumdaki ilk çizim anlıktır.
        /// </summary>
        private void RefreshCork()
        {
            bool shouldCork = tube.IsComplete && !tube.IsEmpty && !corkSuppressed;

            // Ön dilimler tıpayla YA DA ağız sandviçi isteğiyle açılır: dökme
            // sırasında akışın hedef parçası tıpa katmanında (order 3) çizilir,
            // dilimler onu tıpa gibi sarar — delikten girer, bej bandın
            // arkasından geçer, tüpte yeniden görünür.
            bool fronts = shouldCork || mouthOverlayVisible;
            collarFrontTop.enabled = fronts;
            collarFrontBottom.enabled = fronts;

            if (shouldCork == cork.enabled)
                return;

            if (corkRoutine != null)
            {
                StopCoroutine(corkRoutine);
                corkRoutine = null;
            }

            cork.enabled = shouldCork;
            cork.transform.localPosition = corkRestPosition;
            cork.transform.localScale = Vector3.one;

            if (shouldCork && viewReady)
                corkRoutine = StartCoroutine(AnimateCorkIn());
        }

        /// <summary>Dökme sırasında hedefin ön yaka dilimlerini tıpasız da açar
        /// (akış sandviçi — bkz. RefreshCork). Dökme bitince kapatılır.</summary>
        public void SetMouthOverlay(bool visible)
        {
            mouthOverlayVisible = visible;
            RefreshCork();
        }

        /// <summary>
        /// Eğik yüzeyin dudak demirlemesi (normalize, gövde oranı): BoardView,
        /// fiziksel modelle (TiltedEdgeLevel) shader'ın düzlem kaydırması
        /// arasındaki farkı her kare buraya yazar; yüzey o kadar kaldırılır,
        /// akış kolonu tüpte kalan sıvıdan kopmaz. Dökme dışında 0.
        /// </summary>
        public void SetSurfaceLift(float normalizedLift)
        {
            surfaceLift = normalizedLift;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(SurfaceLiftId, surfaceLift);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>Damla halkalarının anlık gücü (0-1); patlama zarfını
        /// PlayRippleBurst sürer.</summary>
        private void SetRippleStrength(float strength)
        {
            rippleStrength = strength;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(RippleStrengthId, rippleStrength);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Sıçrama gücü (0-1): akış bu tüpün yüzeyine aktığı sürece BoardView
        /// 1'e sürer, akış kesilince 0'a — değme noktasından iki yana damlacık.
        /// </summary>
        public void SetSplashStrength(float strength)
        {
            splashStrength = strength;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(SplashStrengthId, splashStrength);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Dökme bittiğinde damla halkası patlaması: halkalar hızla belirir,
        /// dışa yayılırken ~1 sn'de sönümlenir. Efekt dökme sırasında değil,
        /// sıvı yüzeye oturduğunda tetiklenir.
        /// </summary>
        public void PlayRippleBurst()
        {
            if (rippleRoutine != null)
                StopCoroutine(rippleRoutine);
            rippleRoutine = StartCoroutine(RippleBurst());
        }

        private IEnumerator RippleBurst()
        {
            const float attack = 0.06f;
            const float decay = 1.1f;

            float elapsed = 0f;
            while (elapsed < attack)
            {
                elapsed += Time.deltaTime;
                SetRippleStrength(Mathf.Clamp01(elapsed / attack));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < decay)
            {
                elapsed += Time.deltaTime;
                SetRippleStrength(1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01(elapsed / decay)));
                yield return null;
            }

            SetRippleStrength(0f);
            rippleRoutine = null;
        }

        /// <summary>
        /// Sıvıyı çalkalar (sönümlü salınım): level başında ve tüp
        /// seçildiğinde çağrılır. Süren bir çalkantı varsa yenisi onu keser.
        /// </summary>
        private void PlaySlosh(float amplitude, float duration)
        {
            if (sloshRoutine != null)
                StopCoroutine(sloshRoutine);
            sloshRoutine = StartCoroutine(Slosh(amplitude, duration));
        }

        /// <summary>
        /// Çalkantı gövdesi. Faz tüp sırasına bağlı: level başında tüpler
        /// birlikte ama birebir aynı anda sallanmaz, organik durur. Sönüm
        /// süreye bağlanır: verilen süre sonunda genlik ~%4'e iner.
        /// </summary>
        private IEnumerator Slosh(float amplitude, float duration)
        {
            // Hızlı salınım + süreye bağlı sönüm: kısa çalkantıda bile 2-3 yön
            // değişimi olsun — tek salınım "çalkalanma" hissi vermez. Sönüm
            // sonu ~%7 genlik: kuyruk hafif oynar.
            const float omega = 9f;      // salınım hızı (rad/sn)
            float damping = 2.6f / duration;

            float phase = Index * 0.9f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float slope = amplitude * Mathf.Sin(omega * elapsed + phase)
                    * Mathf.Exp(-damping * elapsed);
                SetSwaySlope(slope);
                yield return null;
            }

            SetSwaySlope(0f);
            sloshRoutine = null;
        }

        private void SetSwaySlope(float slope)
        {
            swaySlope = slope;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(SwaySlopeId, swaySlope);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Tıpanın takılma animasyonu: yukarıdan hızlanarak düşer (ease-in,
        /// yerçekimi hissi), oturunca kısa bir ezilme-esneme yapar. Süreler
        /// kısa tutulur: tıpa "tak" diye oturmalı, süzülmemeli.
        /// </summary>
        private IEnumerator AnimateCorkIn()
        {
            const float dropHeight = 0.5f;
            const float dropDuration = 0.16f;
            const float settleDuration = 0.12f;

            float elapsed = 0f;
            while (elapsed < dropDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dropDuration);
                cork.transform.localPosition =
                    corkRestPosition + Vector3.up * (dropHeight * (1f - t * t));
                yield return null;
            }
            cork.transform.localPosition = corkRestPosition;

            // Oturma esnemesi: hafif yassılıp geri toparlar (merkez pivotlu
            // ölçek — %8'lik esneme, abartısız).
            elapsed = 0f;
            while (elapsed < settleDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / settleDuration);
                float squash = Mathf.Sin(t * Mathf.PI) * 0.08f;
                cork.transform.localScale = new Vector3(1f + squash, 1f - squash, 1f);
                yield return null;
            }
            cork.transform.localScale = Vector3.one;
            corkRoutine = null;
        }

        /// <summary>Tube'un güncel verisine göre doluluk seviyesinin olması gereken değer.</summary>
        public float TargetFillLevel => tube.Count / (float)tube.Capacity * FillSpan;

        /// <summary>Shader'a en son gönderilen doluluk seviyesi.</summary>
        public float CurrentFill => currentFill;

        /// <summary>
        /// Sıvı seviyesini mevcut değerden hedef değere pürüzsüz kaydırır.
        /// Katman güncellemeyi (Refresh) kendisi yapmaz; çağıran taraf
        /// kaynak ve hedef tüp için farklı zamanlarda Refresh çağırır.
        /// </summary>
        public IEnumerator AnimateFill(float targetFill, float duration)
        {
            float startFill = currentFill;

            if (Mathf.Approximately(startFill, targetFill))
                yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // SmoothStep: başta ve sonda yavaşlar, ortada hızlanır.
                t = t * t * (3f - 2f * t);

                SetFillLevel(Mathf.Lerp(startFill, targetFill, t));
                yield return null;
            }

            SetFillLevel(targetFill);
        }

        /// <summary>Shader'a sadece doluluk seviyesini gönderir. Animasyon döngüsünde her kare çağrılır.</summary>
        public void SetFillLevel(float fill)
        {
            currentFill = fill;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(FillLevelId, fill);
            properties.SetFloat(TiltAngleId, tiltAngle);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Rengi shader'ın beklediği uzaya çevirir.
        ///
        /// SetColor çağrılsaydı Unity bunu kendisi yapardı, ama katman renklerini
        /// dizi olarak gönderiyoruz ve SetVectorArray bunların renk olduğunu
        /// bilmez - dört sayı olarak geçirir. Linear projede çevirmezsek shader
        /// sRGB değerleri linear sanır ve her renk olduğundan açık çıkar:
        /// kırmızı pembeye döner, paletteki tonlar birbirine yaklaşır.
        /// </summary>
        private static Vector4 ToShaderColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear
                ? (Vector4)color.linear
                : (Vector4)color;
        }

        // ────────────────────────────────────────────────────────────────
        // SDF — TubeShape.hlsl'deki fonksiyonların C# karşılığı.
        // Tıklamanın tüp şekli içinde olup olmadığını doğrulamak için kullanılır.
        // ────────────────────────────────────────────────────────────────

        /// <summary>Dünya koordinatındaki bir noktanın tüp şekli içinde olup olmadığını döner.</summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            // Dörtgenin merkezi (0, QuadHeight/2) yerel konumunda; noktayı oraya taşı.
            Vector2 p = new Vector2(local.x, local.y - QuadHeight * 0.5f);

            return SdTube(p) <= 0f;
        }

        // Düz tüp: shader'daki SdTube ile aynı — yalnızca gövde kutusu, ağız
        // kaynaşması yok (bkz. TubeShape.hlsl açıklaması).
        private float SdTube(Vector2 p)
        {
            Vector2 quadSize = new Vector2(QuadWidth, QuadHeight);
            Vector2 bodySize = new Vector2(Width, BodyHeight);

            Vector2 bodyCenter = new Vector2(0f, -quadSize.y * 0.5f + bodySize.y * 0.5f);
            return SdRoundedBox(p - bodyCenter, bodySize * 0.5f, TopRadius, BottomRadius);
        }

        /// <summary>Yuvarlak köşeli dikdörtgenin SDF'i. Üst ve alt köşe yarıçapları ayrı.</summary>
        private static float SdRoundedBox(Vector2 p, Vector2 halfSize,
            float topRadius, float bottomRadius)
        {
            float r = p.y > 0f ? topRadius : bottomRadius;

            Vector2 q = new Vector2(Mathf.Abs(p.x) - halfSize.x + r,
                                    Mathf.Abs(p.y) - halfSize.y + r);

            return Mathf.Min(Mathf.Max(q.x, q.y), 0f)
                + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude
                - r;
        }

        /// <summary>Tüpün dinlenme konumu. Animasyon sırasında hedef hesaplamak için.</summary>
        public Vector3 RestPosition => restPosition;

        /// <summary>Tüpün gövde yüksekliği. Dökme pozisyonu hesaplamak için.</summary>
        public float Height => BodyHeight;

        /// <summary>
        /// Yaka ağzındaki kahverengi deliğin MERKEZİ, tüp-yerel konum. Hedef
        /// tüpte akış kolonunun indiği nokta.
        /// </summary>
        public Vector3 CollarMouth => new Vector3(
            0f,
            BodyHeight + CollarCenterY + (CollarSpriteRows * 0.5f - CollarHoleCy) / CollarPpu,
            0f);

        /// <summary>
        /// Deliğin döken kenarı (hedefe en yakın nokta), tüp-yerel konum.
        /// KAYNAK tüpte akış buradan başlar: merkezden başlarsa kolon deliğin
        /// ortasından fışkırıyor görünür — sıvı deliğin hedefe bakan dudağından
        /// taşmalı. side = ±1, döken taraf.
        /// </summary>
        public Vector3 CollarMouthLip(float side) => new Vector3(
            CollarHoleRx / CollarPpu * side,
            BodyHeight + CollarCenterY + (CollarSpriteRows * 0.5f - CollarHoleCy) / CollarPpu,
            0f);

        /// <summary>
        /// Tüpü verilen açıda eğer (radyan). Transform döner ve aynı açı
        /// shader'a gönderilir. Shader bu açıyla yüzeyi ters yöne eğerek
        /// sıvının dünya uzayında yatay kalmasını sağlar.
        /// </summary>
        public void SetTiltAngle(float angleRadians)
        {
            tiltAngle = angleRadians;
            transform.localRotation = Quaternion.Euler(0f, 0f, angleRadians * Mathf.Rad2Deg);

            liquid.GetPropertyBlock(properties);
            properties.SetFloat(TiltAngleId, angleRadians);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Cam ve sıvının çizim sırasını geçici olarak yükseltir. Dökme sırasında
        /// kaynak tüp diğerlerinin üstünde görünmeli.
        /// </summary>
        public void SetSortingOffset(int offset)
        {
            glass.sortingOrder = 0 + offset;
            liquid.sortingOrder = 1 + offset;
            collar.sortingOrder = 2 + offset;
            cork.sortingOrder = 3 + offset;
            collarFrontTop.sortingOrder = 4 + offset;
            collarFrontBottom.sortingOrder = 4 + offset;
        }

        /// <summary>Seçili tüp yukarı kalkar; oyuncu neyi seçtiğini görsün.
        /// Hem kalkışın hem inişin sarsıntısı sıvıyı çalkalar (iniş, boşluğa
        /// tıklayıp seçimi iptal etmeyi de kapsar). Genlik level başındakiyle
        /// aynı, süresi daha kısa.</summary>
        public void SetSelected(bool selected)
        {
            bool changed = isSelected != selected;
            isSelected = selected;
            ApplyPosition();

            if (changed)
                PlaySlosh(0.15f, 0.9f);
        }

        /// <summary>
        /// Tüpün duracağı yeri değiştirir. Yerleşim ekran değiştikçe yeniden
        /// hesaplandığı için konum bir kez verilip unutulamaz.
        ///
        /// Dünya konumu değil yerel konum: tahta ekrana sığsın diye
        /// ölçeklendiğinde tüpün yeri de onunla birlikte kaymalı.
        /// </summary>
        public void SetRestPosition(Vector3 localPosition)
        {
            restPosition = localPosition;
            ApplyPosition();
        }

        /// <summary>
        /// Seçim durumu ile dinlenme yerini birleştirir. İkisi de değişebildiği
        /// için tek yerden uygulanır: yeniden yerleşen seçili bir tüp kalkık kalmalı.
        /// </summary>
        private void ApplyPosition()
        {
            transform.localPosition = isSelected
                ? restPosition + Vector3.up * SelectedLift
                : restPosition;
        }
    }
}
