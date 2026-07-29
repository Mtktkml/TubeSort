using System.Collections;
using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tek bir tüpü ekranda çizer. Tüp tek parçadır: dibi yarım daire, ağzına
    /// doğru yatayda hafifçe genişler. Şekli, katmanları ve yüzey dalgasını
    /// shader'lar hesaplar.
    ///
    /// Bu sınıfın işi çekirdekteki Tube'u shader'ın anlayacağı dile çevirmek:
    /// "dipten yukarı [kırmızı, sarı, sarı]" -> sınırlar [0.25, 0.75] ve renkler.
    /// </summary>
    public class TubeView : MonoBehaviour
    {
        public const float Width = 0.8f;
        public const float UnitHeight = 0.5f;

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
        /// Sıvının gövdesi DÜZ tüp: ağız genişlemesi yok (MouthWidth = Width
        /// olunca SdSmoothUnion düz bir tüp verir; görseldeki genişleme bej
        /// yakanın işi). Şekil hem sıvı shader'ına (WriteShape) hem CPU tıklama
        /// SDF'ine (SdTube) bu sabitten gider; ikisi birlikte güncellenir.
        /// </summary>
        public const float MouthWidth = Width;

        /// <summary>Genişlemenin başladığı yükseklik: tüpün üst ucundan bu kadar aşağısı.</summary>
        private const float MouthHeight = 0.22f;

        private const float MouthRadius = 0.05f;

        /// <summary>
        /// Gövde ile ağzın kaynaşma yumuşaklığı. Büyüdükçe genişleme daha yayvan
        /// bir huniye dönüşür; sıfıra yaklaştıkça basamak gibi keskinleşir.
        /// </summary>
        private const float MouthBlend = 0.06f;

        /// <summary>
        /// Tüp ağzına kadar dolu olsa bile sıvının tepesiyle tüpün ucu arasında
        /// kalan boşluk. Dünya birimi: hem yüzey dalgasının yeri hem de sıvıyı
        /// genişleyen ağzın altında tutar, ikisi de tüpün boyuyla ölçeklenmez.
        /// Oran olarak tutulsaydı uzun tüpte tepede kocaman bir boşluk kalırdı.
        /// </summary>
        private const float FillHeadroom = 0.2f;

        /// <summary>
        /// Cam görselinin üst kenarı yakanın ARKASINA bu kadar uzar: yaka tüpün
        /// üstüne oturur, tüp ağzı/kenarı yakanın altından sırıtmaz, arada fon
        /// boşluğu kalmaz. Sıvı matematiği etkilenmez — uzantı yalnız cam
        /// sprite'ının boyuna eklenir, sıvı kendi kısa gövde ölçüsüyle kırpılır.
        /// </summary>
        private const float MouthExtension = Width * 0.15f;

        // ── Görsel katman: cam/yaka/tıpa ekip PNG'leri (Resources/Sprites),
        // SpriteRenderer ile. Ölçek çapası: yaka görselinin tam genişliği =
        // FullWidth (1.2 birim); PPU'lar buna göre girildi (collar 244,
        // cork 229, tube 247.5 — birleşik referans `tube (2).png` piksel
        // ölçümleri, 29 Tem 2026). Görsel ya da PPU değişirse buradaki
        // piksel/PPU sabitleri de birlikte güncellenmeli. ──
        /// <summary>Yakanın yarı genişliği (yerleşim çapası; FullWidth = 2×bu).</summary>
        public const float CollarRx = Width * 0.75f;
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
        /// <summary>Yaka görselinin dünya yüksekliği (113 px / 244 PPU).</summary>
        private const float CollarSpriteHeight = 113f / 244f;
        // collar.png eğri sınırları (piksel, satırlar ÜSTTEN; 29 Tem taraması).
        // Ön parçalar dikdörtgen DEĞİL eğri maskeyle kesilir: düz kenarlar
        // tıpayı cetvelle kesilmiş gösteriyordu (kullanıcı bulgusu). Görsel
        // değişirse bu sayılar yeniden ölçülmeli (tarama: koyu piksel sütun
        // taraması, delik altı 40→48, seam 59→66 parabolü).
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
        // anında sütun sütun ölçülür (MeasureSeamBottom) — el çizimi çizgi
        // simetrik değil (sol uç dx-60'ta satır 63, sağ uçta 61), parabol
        // uydurması uçlarda 1-2 px bej sızdırıyordu.
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
        private static readonly int MouthSizeId = Shader.PropertyToID("_MouthSize");
        private static readonly int TopRadiusId = Shader.PropertyToID("_TopRadius");
        private static readonly int BottomRadiusId = Shader.PropertyToID("_BottomRadius");
        private static readonly int MouthRadiusId = Shader.PropertyToID("_MouthRadius");
        private static readonly int MouthBlendId = Shader.PropertyToID("_MouthBlend");
        private static readonly int TiltAngleId = Shader.PropertyToID("_TiltAngle");

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

            // Cam artık ekip görseli (9-slice); tepesi MouthExtension kadar
            // yakanın arkasına uzanır. Sıvı shader'ı aynı kalır: kendi şeklini
            // kendisi çizer, kısa gövdeyle kırpılır (fill matematiği aynı).
            CreateGlass(tubeSprite);
            liquid = CreateQuad("Liquid", liquidMaterial, sortingOrder: 1, QuadHeight);

            CreateCollar(collarSprite, collarFrontTopSprite, collarFrontBottomSprite);
            CreateCork(corkSprite);
            CreateClickArea();
            Refresh();
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
                    // SİYAH) kullanılınca bilinear filtre kenar pikselini siyahla
                    // harmanlıyor ve maske sınırları boyunca soluk koyu çizgiler
                    // beliriyordu (kullanıcı bulgusu).
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
        /// Mantar tıpayı ekip görseliyle kurar (order 3: arka yakanın önünde, ön
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
            go.transform.localPosition =
                new Vector3(0f, corkTop - sprite.bounds.extents.y, 0f);
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

        /// <summary>Verilen kapasitedeki bir tüpün ekranda kaplayacağı yükseklik.</summary>
        public static float HeightFor(int capacity) => capacity * UnitHeight;

        /// <summary>Sıvının durduğu gövdenin yüksekliği; tüpün tam boyu.</summary>
        private float BodyHeight => HeightFor(tube.Capacity);

        /// <summary>Sıvı gövdenin en fazla bu kadarını kaplar. Gövde uzadıkça 1'e yaklaşır.</summary>
        private float FillSpan => 1f - FillHeadroom / BodyHeight;

        /// <summary>
        /// Dörtgen genişleyen ağzı kapsayacak kadar geniştir. Yumuşak birleşim
        /// kavis oluştururken şekli bir miktar dışarı taşırdığı için ayrıca
        /// harmanlama payı kadar boşluk bırakılır; yoksa kavis kenardan kırpılır.
        /// </summary>
        private static float QuadWidth => MouthWidth + 2f * MouthBlend;

        /// <summary>Sıvı dörtgeninin boyu: gövdenin boyu (fill matematiği buna göre).</summary>
        private float QuadHeight => BodyHeight;

        /// <summary>Cam dörtgeninin boyu: gövde + yaka arkasına saklanan uzantı.</summary>
        private float GlassQuadHeight => BodyHeight + MouthExtension;

        /// <summary>
        /// Cam tüpü ekip görseliyle kurar (order 0, sıvının arkasında). 9-slice:
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
            properties.SetVector(MouthSizeId, new Vector4(MouthWidth, MouthHeight, 0f, 0f));
            properties.SetFloat(TopRadiusId, TopRadius);
            properties.SetFloat(BottomRadiusId, BottomRadius);
            properties.SetFloat(MouthRadiusId, MouthRadius);
            properties.SetFloat(MouthBlendId, MouthBlend);
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
            liquid.SetPropertyBlock(properties);

            // Mantar yalnız tamamlanmış tüpte (dolu + tek renk) görünür. Tube.IsComplete
            // boş tüp için de true döner; boşta mantar istemiyoruz, o yüzden !IsEmpty.
            // Collar'ın ön overlay'i de yalnız mantar varken çizilir.
            cork.enabled = tube.IsComplete && !tube.IsEmpty;
            collarFrontTop.enabled = cork.enabled;
            collarFrontBottom.enabled = cork.enabled;
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

        /// <summary>Seçili tüp yukarı kalkar; oyuncu neyi seçtiğini görsün.</summary>
        public void SetSelected(bool selected)
        {
            isSelected = selected;
            ApplyPosition();
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
