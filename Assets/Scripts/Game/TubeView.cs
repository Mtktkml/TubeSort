using System.Collections;
using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tek bir tüpü ekranda çizer: cam gövde/halka/tıpa sprite'ları
    /// (SpriteRenderer katmanları), sıvı ve 2.5D yüzeyi (damla halkaları
    /// dahil) shader ile.
    ///
    /// Bu sınıfın işi çekirdekteki Tube'u görünümün diline çevirmek:
    /// "dipten yukarı [kırmızı, sarı, sarı]" -> sınırlar [0.25, 0.75] ve renkler.
    /// </summary>
    public class TubeView : MonoBehaviour
    {
        // ── Ölçek çapası: v2 sprite seti TEK ölçekte çizildi ve tek PPU ile
        // import edildi (halka tam genişliği 152 px = FullWidth 1.2 birim →
        // 152/1.2). Aşağıdaki piksel sabitleri bu PPU ile dünyaya çevrilir;
        // görsel ya da import PPU'su değişirse burası da birlikte güncellenir. ──
        private const float SpritePpu = 126.67f;

        /// <summary>Sıvı gövdesinin genişliği: sıvının kabı camın İÇ KONTURUDUR
        /// (görselde iki kontur var — dış kabuk x19-23/x128-132 ve içteki ince
        /// çizgi x30-34/x116-120; sıvı iç çizgiye dayanır, merkezler x32..118 →
        /// 86 px). Cam önde çizildiği için çizgi sıvının kenarını örter. Sıvı
        /// SDF'i ve dökme fiziği (açı/hacim) bu genişlikle çalışır.</summary>
        public const float Width = 86f / SpritePpu;
        private const float UnitHeight = 0.5f;

        /// <summary>
        /// Shader'daki MAX_LAYERS ile aynı olmak zorunda.
        ///
        /// En kötü durumda katman sayısı kapasiteye eşittir: her birim bir
        /// öncekinden farklı renkse hiçbiri birleşmez. Yani bu sayı aynı zamanda
        /// desteklenen en büyük tüp kapasitesidir.
        ///
        /// On iki, oyunun hedeflediği en derin tüpleri (kapasite 12) karşılar.
        /// Büyütmenin bedeli var: shader döngüsü her piksel için bu kadar tur
        /// döner (12, eski 8'e göre ~%50 daha fazla piksel maliyeti). Daha büyük
        /// kapasite gerekirse burayı ve shader'daki MAX_LAYERS'ı birlikte artır;
        /// aşım durumunda Initialize hata basar.
        /// </summary>
        private const int MaxLayers = 12;

        private const float SelectedLift = 0.3f;

        /// <summary>
        /// Gövdenin üst köşelerinin yuvarlaklığı. Dünya birimi.
        /// Küçük tutulur: sıvının üst kırpılma çizgisi halkanın opak bandının
        /// arkasına saklandığı için tepe neredeyse düz kesilebilir.
        /// </summary>
        private const float TopRadius = 0.04f;

        /// <summary>
        /// Dibin yuvarlaklığı. İÇ KONTURUN çanak profili ölçüldü (iç çizgi
        /// satır 435'te kıvrılmaya başlar, 463'te kapanır): 38 px köşe
        /// yarıçaplı yuvarlak kutu profile ±2 px oturuyor — sıvı iç çanağı
        /// boşluksuz doldurur, çizgiye en fazla ~2 px biner (cam önde, örter).
        /// </summary>
        private const float BottomRadius = 38f / SpritePpu;

        /// <summary>
        /// Sıvı/tıklama dörtgeninin gövde dışına yan payı: kenar yumuşaması
        /// kırpılmasın.
        /// </summary>
        private const float QuadPadding = 0.06f;

        /// <summary>
        /// Sıvı tabanının tüpün (pivot) dibinden yüksekliği: İÇ KONTURUN çanağı
        /// satır ~463'te kapanır (463-468'deki beyaz parlama bandı iç tabanın
        /// kendisidir) → alttan 32 px; taban bandın 2 px İÇİNE gömülür (30) ki
        /// sıvıyla çanak arasında tek piksellik bile boşluk kalmasın — bindirme
        /// önde çizilen bandın arkasında görünmez. (Tarihçe: önce dış kabuğun
        /// tabanı [satır 478] sanıldı — sıvı iç çizginin dışına taşıyordu;
        /// doğru kap İÇ kontur.)
        /// </summary>
        private const float FloorInset = 30f / SpritePpu;

        /// <summary>
        /// Sıvı ağzına kadar dolu olsa bile sıvının tepesiyle gövde tepesi
        /// (BodyHeight) arasında kalan boşluk (dünya birimi, tüpün boyuyla
        /// ölçeklenmez). Ölçüden türer: tıpanın tüpe sarkan kısmı + görünür pay
        /// — tıpa takılıyken üst sıvı katmanı da tamamen görünür kalır.
        /// </summary>
        private static float FillHeadroom => CorkSpriteHeight - TopOverhang + CorkLiquidGap;

        /// <summary>Tıpa dibi ile dolu sıvının tepesi arasında istenen görünür
        /// boşluk (2.5D yüzey elipsine de yer bırakır).</summary>
        private const float CorkLiquidGap = 0.12f;

        // ── Görsel katman: v2 PNG'leri (Resources/Sprites/v2). Tüp görseli
        // (152×496) cam gövde + gri halkayı TEK parçada taşır; çalışma anında
        // Sprite.Create ile İKİYE bölünür (CreateBodySprite/CreateRingSprite).
        // SIVI EN ARKADA, cam önde çizilir: cam yarı saydam olduğu için içerik
        // içinden görünür ve camın GÖMÜLÜ parlamaları içeriğin üstüne
        // kendiliğinden düşer — parlama/etkileşim shader'da ya da ek parçayla
        // ÇİZİLMEZ, kompozit halleder (taklit denendi, hiza asla birebir
        // tutmadı; mimari buna geçildi). TIPA HALKANIN ÖNÜNDE, CAMIN ARKASINDA
        // (referans kompozitle birebir): yaka bölgesinde çıplak tıpa görünür
        // (delik gölgesi tıpanın kendi koyu bandı), tüp içine giren kısım ise
        // cam + pus arkasında buzlanır. Tam sıra:
        //   sıvı 0 < akış-alt 1 < halka 2 < tıpa 3 < pus 4 < cam gövde 5.
        // (Halka ile cam gövde ayrık parçalar — aralarındaki sıra görsel fark
        // yaratmaz; halka öne alınmış sıvı/akışı örtmesi için.) Akış-alt kolonu
        // halkanın ve camın arkasında: deliğe girip camın içinden süzülerek
        // yüzeye iner; dudağa tırmanan sıvı da halkanın arkasında kalır.
        // Gövde+halka AYNI dokudan kesildiği için kesim çizgisinde bilinear
        // filtre komşu pikseli yine doğru satırdan okur — dikiş görünmez. ──
        /// <summary>Resources yolları — BoardView yükler, TubeView kullanır.
        /// (v2/collar.png bilerek kullanılmıyor: ağız-önü parçası halkanın
        /// kendi dokusundan kesiliyor, referans kompozit öyle kurulmuş.)</summary>
        public const string TubeSpritePath = "Sprites/v2/tube";
        public const string CorkSpritePath = "Sprites/v2/cork";
        /// <summary>Tıpanın camın içinde kalan kısmına binen beyaz pus perdesi.</summary>
        public const string CorkVeilSpritePath = "Sprites/v2/shadow";

        // Tüp dokusu bölme sınırları (piksel, satırlar ÜSTTEN; doku 152×496).
        /// <summary>Halka parçası: üstten bu kadar satır. 58'e kadar halka önü
        /// TAM OPAK (ölçüm: satır 32..58 min alfa ≥254), 59 AA kenarı — kesim
        /// 60'ta, opak bandın hemen altında.</summary>
        private const int RingRows = 60;
        /// <summary>Gövde parçası 9-slice: dip kavisi (alt) ve kesim altındaki
        /// küçük geçiş (üst) sabit kalır, arası kapasiteyle uzar.</summary>
        private const int BodyBottomBorderPx = 54;
        private const int BodyTopBorderPx = 8;
        /// <summary>Gövde tepesi (dikiş çizgisi) BodyHeight'ın bu kadar (satır)
        /// ALTINDA durur: sıvının üst kırpılma çizgisi (BodyHeight) halkanın
        /// tam opak bandına (satır ~55) denk gelir, kenar asla görünmez.</summary>
        private const float SeamDropRows = 4f;
        /// <summary>Halkanın dünya yüksekliği.</summary>
        private const float RingHeight = RingRows / SpritePpu;
        /// <summary>Yarı genişlik (yerleşim çapası; FullWidth = 2×bu = 152 px).</summary>
        private const float RingHalfWidth = 0.6f;
        /// <summary>Ağız deliğinin merkezi: halka tepesinden satır (delik
        /// boşluğu satır 3..27, merkez ~15) ve x yarıçapı (x45..105 → 30 px).</summary>
        private const float RingHoleCenterRows = 15f;
        private const float RingHoleRx = 30f / SpritePpu;
        /// <summary>Sıvının gövde tepesinin üstüne (halka arkasında, delik
        /// merkezine dek: 41 satır) tırmanabildiği pay. Dökme eğiminde dudağa
        /// bastırılan sıvı gövde tepesinde kırpılmayıp akış kolonuyla ağızda
        /// buluşur; dinlenmede yüzey FillSpan'i aşamadığından hiç boyanmaz.</summary>
        private const float MouthOverflow = 41f / SpritePpu;

        // Tıpa etkileşimleri için EK PARÇA YOK (dudak yayı ve halka-önü
        // pencere sandviçi denendi, söküldü): tıpa arkada olduğundan halka ve
        // cam kendi pikselleriyle tıpayı örter — referanstaki görünümün
        // reçetesi kompozitin kendisidir.

        /// <summary>Tıpa görselinin dünya boyu (97 satır) — FillHeadroom
        /// türetimi statik kalsın diye sabit (CreateCork konumu çalışma anında
        /// sprite'tan okur).</summary>
        private const float CorkSpriteHeight = 97f / SpritePpu;
        /// <summary>Tıpa tepesi halka tepesinin bu kadar üstünde (birleşik
        /// referans `tube reference.png`: 18 satır).</summary>
        private const float CorkTopAboveRingTop = 18f / SpritePpu;
        // Pus perdesi (shadow.png, 77×47): tıpanın tüp içinde kalan kısmının
        // buzlu-cam rampası. Konum referans ölçümünden: derin bölgedeki (tıpa
        // satır 82-92) ~%30 beyazlık, shadow alfa rampasının 34-44. satırlarına
        // denk → perde tepesi tıpa satır ~48 (cam ağzı çizgisinin hemen altı).
        // Tıpanın koyu kahve alt kenarı (satır 90-92) perdenin altında kalır.
        // Perde tıpanın önünde ama CAMIN ARKASINDA: camın parlamaları pusun
        // üstünden geçer.
        private const float CorkRows = 97f;
        private const float VeilRows = 47f;
        private const float VeilTopFromCorkTopRows = 48f;

        private static readonly int LayerColorsId = Shader.PropertyToID("_LayerColors");
        private static readonly int LayerTopsId = Shader.PropertyToID("_LayerTops");
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int BodySizeId = Shader.PropertyToID("_BodySize");
        private static readonly int TopRadiusId = Shader.PropertyToID("_TopRadius");
        private static readonly int BottomRadiusId = Shader.PropertyToID("_BottomRadius");
        private static readonly int MouthOverflowId = Shader.PropertyToID("_MouthOverflow");
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
        private SpriteRenderer ring;
        private SpriteRenderer cork;
        private SpriteRenderer corkVeil;
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
            Material liquidMaterial, Sprite glassBodySprite, Sprite ringSprite,
            Sprite corkSprite, Sprite corkVeilSprite)
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

            // Cam gövde 9-slice; tepesi (dikiş) halkanın opak bandının altında
            // kalır. Sıvı shader'ı kendi şeklini kendisi çizer, iç taban
            // (FloorInset) üstünde kendi gövde ölçüsüyle kırpılır ve CAMIN
            // ARKASINDA durur (bkz. katman açıklaması yukarıda).
            CreateGlass(glassBodySprite);
            liquid = CreateQuad("Liquid", liquidMaterial, sortingOrder: 0, QuadHeight);

            CreateRing(ringSprite);
            CreateCork(corkSprite, corkVeilSprite);
            CreateClickArea();
            Refresh();
            viewReady = true;

            // Level başı çalkantısı: görünüm kurulur kurulmaz sıvı sallanır,
            // sönümlenip durulur (boş tüpte sıvı yok, etki görünmez).
            PlaySlosh(0.15f, 1.6f);
        }

        /// <summary>
        /// Sıvı dörtgeni: gövde ölçüsü kendi property block'una yazılır. Dibi
        /// tüpün pivot dibine değil camın İÇ TABANINA (FloorInset) oturur —
        /// yeni camın dibi kalın, sıvı orada duramaz.
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
            // Sprite'ın merkezi ortada; dörtgenin dibi iç tabana otursun.
            go.transform.localPosition = new Vector3(0f, FloorInset + quadHeight * 0.5f, 0f);

            return renderer;
        }

        /// <summary>
        /// Gri halkayı kurar (order 2: sıvının ve akışın ÖNÜNDE, tıpanın
        /// ARKASINDA): tüp dokusunun üst parçası, gövdenin tepesine (dikişe)
        /// bitişik oturur. Dökme sırasında dudağa tırmanan sıvı ve ağza giren
        /// akış kolonu (order 1) halkanın arkasında kalır — eski bej yakanın
        /// mimari rolü; tıpa ise halkanın önündedir (referans kompozit).
        /// </summary>
        private void CreateRing(Sprite ringSprite)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(transform, false);

            ring = go.AddComponent<SpriteRenderer>();
            ring.sprite = ringSprite;   // pivot Bottom: dikişe doğrudan oturur
            ring.sortingOrder = 2;
            go.transform.localPosition = new Vector3(0f, GlassTop, 0f);
        }

        /// <summary>
        /// Tüp dokusunun ALT parçası: cam gövde. 9-slice border'ları Sprite
        /// üzerinde tanımlanır (import ayarı bütün dokuya aitti, parçaya
        /// geçmez): dip kavisi ve tepe geçişi sabit, düz gövde uzar.
        /// BoardView bir kez yaratıp paylaştırır; doku asset'tir, yok edilmez —
        /// yalnız Sprite nesnesi yok edilir.
        /// </summary>
        public static Sprite CreateBodySprite(Sprite tubeSprite)
        {
            Texture2D tex = tubeSprite.texture;
            // Rect alttan tanımlı: üstteki RingRows satır hariç kalan gövde.
            var rect = new Rect(0f, 0f, tex.width, tex.height - RingRows);
            return Sprite.Create(tex, rect, new Vector2(0.5f, 0f), SpritePpu, 0,
                SpriteMeshType.FullRect,
                new Vector4(0f, BodyBottomBorderPx, 0f, BodyTopBorderPx));
        }

        /// <summary>Tüp dokusunun ÜST parçası: gri halka + ağız. Aynı dokudan
        /// kesildiği için gövdeyle kesim çizgisinde dikiş görünmez.</summary>
        public static Sprite CreateRingSprite(Sprite tubeSprite)
        {
            Texture2D tex = tubeSprite.texture;
            var rect = new Rect(0f, tex.height - RingRows, tex.width, RingRows);
            return Sprite.Create(tex, rect, new Vector2(0.5f, 0f), SpritePpu, 0,
                SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Mantar tıpayı ve pus perdesini kurar (tıpa order 3: HALKANIN önünde
        /// ama CAMIN arkasında; perde order 4: tıpanın önünde, camın arkasında).
        /// Böylece yaka bölgesinde tıpa çıplak görünür (referans kompozitle
        /// birebir — delik gölgesi tıpanın kendi koyu bandı), tüp içine giren
        /// kısımsa cam + pus arkasında buzlanır. Konum birleşik referanstan:
        /// tıpa tepesi halka tepesinin CorkTopAboveRingTop kadar üstünde; alt
        /// ucu ağızdan içeri sarkar. Perde tıpanın ÇOCUĞUDUR: takılma
        /// animasyonu ve ezilme esnemesi perdeyi kendiliğinden taşır. İkisi de
        /// gizli başlar; yalnız tamamlanan tüpte görünürler (bkz. RefreshCork).
        /// </summary>
        private void CreateCork(Sprite sprite, Sprite veilSprite)
        {
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);

            cork = go.AddComponent<SpriteRenderer>();
            cork.sprite = sprite;
            cork.sortingOrder = 3;   // halkanın ÖNÜNDE, camın ARKASINDA
            cork.enabled = false;

            float corkTop = RingTop + CorkTopAboveRingTop;
            corkRestPosition = new Vector3(0f, corkTop - sprite.bounds.extents.y, 0f);
            go.transform.localPosition = corkRestPosition;

            // Pus perdesi: tıpanın tüp içinde kalan kısmında, derine indikçe
            // güçlenen beyaz sis. Konum tıpa-yerel (tıpa pivot'u merkez):
            // perde tepesi tıpa tepesinin VeilTopFromCorkTopRows kadar altında.
            var veilGo = new GameObject("CorkVeil");
            veilGo.transform.SetParent(go.transform, false);
            corkVeil = veilGo.AddComponent<SpriteRenderer>();
            corkVeil.sprite = veilSprite;
            corkVeil.sortingOrder = 4;
            corkVeil.enabled = false;
            veilGo.transform.localPosition = new Vector3(0f,
                (CorkRows * 0.5f - VeilTopFromCorkTopRows - VeilRows * 0.5f)
                    / SpritePpu, 0f);
        }

        /// <summary>
        /// Tüpün ekranda kapladığı toplam genişlik. En geniş parça gri halka
        /// olduğu için yerleşim ona göre yapılır; yoksa komşu tüplerin
        /// halkaları birbirine girer. (Cam/sıvı daha dar.)
        /// </summary>
        public static float FullWidth => 2f * RingHalfWidth;

        /// <summary>
        /// Görsellerin gövde tepesinin (BodyHeight) üstüne taşan kısmı: halkanın
        /// BodyHeight üstünde kalan bandı + tıpa payı. Tıpa gizliyken de yer
        /// ayrılır — hem tüp tamamlanınca tahta yeniden ölçeklenmesin, hem de
        /// kapalı renderer'ın sınırları da ekran ölçümüne girer (LayoutFitTests
        /// bounds toplar).
        /// </summary>
        public static float TopOverhang =>
            RingHeight - SeamDropRows / SpritePpu + CorkTopAboveRingTop;

        /// <summary>
        /// FullWidth dışına taşan yan pay. Halka görseli tam FullWidth
        /// genişliğinde (PPU çapası öyle seçildi), tıpa daha dar — taşma yok.
        /// </summary>
        public static float SideOverhang => 0f;

        /// <summary>Verilen kapasitedeki bir tüpün ekranda kaplayacağı yükseklik:
        /// kalın cam dip (FloorInset) + sıvı alanı (kapasite × birim) + tepe payı
        /// (FillHeadroom). Böylece her birim sıvı tam UnitHeight boyundadır ve
        /// dolu tüpte bile tepede tıpaya yetecek boşluk kalır.</summary>
        public static float HeightFor(int capacity) =>
            FloorInset + capacity * UnitHeight + FillHeadroom;

        /// <summary>Sıvı gövdesinin (dörtgeninin) boyu: iç tabandan gövde
        /// tepesine. Fill matematiği ve dökme fiziği bu boyla çalışır.</summary>
        public float LiquidHeight => tube.Capacity * UnitHeight + FillHeadroom;

        /// <summary>İç tabanın tüp dibinden yüksekliği — BoardView akış/yüzey
        /// hesaplarında sıvı-yerel değerleri tüp-yerele çevirmek için.</summary>
        public static float LiquidFloor => FloorInset;

        /// <summary>Tüpün tam boyu: iç taban + sıvı gövdesi. Ağız/halka bunun üstüne oturur.</summary>
        private float BodyHeight => FloorInset + LiquidHeight;

        /// <summary>Gövde parçasının tepesi = halkanın dibi (dikiş çizgisi):
        /// BodyHeight'ın SeamDropRows kadar altında — sıvının üst kırpılma
        /// çizgisi halkanın tam opak bandına denk gelir.</summary>
        private float GlassTop => BodyHeight - SeamDropRows / SpritePpu;

        /// <summary>Halkanın üst kenarı (tüp görselinin en üst noktası).</summary>
        private float RingTop => GlassTop + RingHeight;

        /// <summary>Ağız deliğinin merkezi (y): akış kolonunun hizası.</summary>
        private float MouthY => RingTop - RingHoleCenterRows / SpritePpu;

        /// <summary>Sıvı gövdenin en fazla bu kadarını kaplar. Gövde uzadıkça 1'e yaklaşır.</summary>
        private float FillSpan => 1f - FillHeadroom / LiquidHeight;

        /// <summary>Sıvı/tıklama dörtgeninin genişliği: gövde + iki yanda pay.</summary>
        private static float QuadWidth => Width + 2f * QuadPadding;

        /// <summary>Sıvı dörtgeninin boyu: sıvı gövdesi + ağız tırmanma payı
        /// (dörtgen yalnız üstten uzar; dip iç tabanda kalır).</summary>
        private float QuadHeight => LiquidHeight + MouthOverflow;

        /// <summary>
        /// Cam gövde sprite'ını kurar (order 5 — sıvının, akışın, tıpanın ve
        /// pusun ÖNÜNDE): yarı saydam cam, arkasındaki içeriği gösterirken
        /// gömülü parlamalarını üstüne düşürür — dolu tüpün parlaması boş
        /// tüple birebirdir, tıpanın tüp-içi kısmı camın arkasında buzlanır.
        /// 9-slice: parça sprite'ında tanımlı alt border dip kavisini korur,
        /// düz gövde kapasiteye göre uzar (parlama şeritleri de orantılı uzar).
        /// Pivot Bottom olduğu için yerel sıfır tüpün dibidir; genişlik
        /// görselin doğal genişliği (1.2 birim, halka genişliğiyle aynı).
        /// Tepesi dikişte biter; halka oradan devam eder.
        /// </summary>
        private void CreateGlass(Sprite sprite)
        {
            var go = new GameObject("Glass");
            go.transform.SetParent(transform, false);

            glass = go.AddComponent<SpriteRenderer>();
            glass.sprite = sprite;
            glass.sortingOrder = 5;
            glass.drawMode = SpriteDrawMode.Sliced;
            glass.size = new Vector2(sprite.bounds.size.x, GlassTop);
        }

        private void WriteShape(float bodyHeight, float quadHeight)
        {
            properties.SetVector(QuadSizeId, new Vector4(QuadWidth, quadHeight, 0f, 0f));
            properties.SetVector(BodySizeId, new Vector4(Width, bodyHeight, 0f, 0f));
            properties.SetFloat(TopRadiusId, TopRadius);
            properties.SetFloat(BottomRadiusId, BottomRadius);
            properties.SetFloat(MouthOverflowId, MouthOverflow);
        }

        /// <summary>
        /// Tıklamayı yakalayacak görünmez alan. Kutu KABA elemedir: cam gövde +
        /// halkayı birlikte kapsar; asıl karar ContainsPoint'teki SDF'te verilir
        /// (gövde ∪ halka) — kutunun görselden taşan kısımları orada elenir.
        /// </summary>
        private void CreateClickArea()
        {
            var box = gameObject.AddComponent<BoxCollider2D>();

            // Üst kenar halkanın tepesi, genişlik halkanın tam genişliği.
            float top = RingTop;
            box.size = new Vector2(FullWidth, top);
            box.offset = new Vector2(0f, top * 0.5f);
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
            WriteShape(LiquidHeight, QuadHeight); // sıvı kendi gövde boyuyla kırpılır
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
        /// istemiyoruz (!IsEmpty). Pus perdesi tıpayla birlikte yaşar. Oyun
        /// sırasında AÇILIRKEN takılma animasyonu oynar; kapanış ve kurulumdaki
        /// ilk çizim anlıktır.
        /// </summary>
        private void RefreshCork()
        {
            bool shouldCork = tube.IsComplete && !tube.IsEmpty && !corkSuppressed;

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

            // Pus perdesi tıpa OTURDUĞUNDA görünür: düşüş boyunca tıpa havada,
            // cam pusunun onu örtmesi ancak tüpe girince anlamlı. Animasyonlu
            // yolda AnimateCorkIn düşüş bitince açar; anlık yolda (kurulum /
            // kapanış) burada.
            bool animate = shouldCork && viewReady;
            corkVeil.enabled = shouldCork && !animate;

            if (animate)
                corkRoutine = StartCoroutine(AnimateCorkIn());
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

            // Tıpa oturdu ("tak" anı): pus perdesi ŞİMDİ görünür — tıpanın
            // tüpe giren kısmını cam pusu örtüyor (bkz. RefreshCork).
            corkVeil.enabled = true;

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

        /// <summary>Tam dolu tüpün doluluk seviyesi (normalize). CurrentFill/MaxFill
        /// = doluluk oranı; eğim açısı bundan türer.</summary>
        public float MaxFill => FillSpan;

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

        /// <summary>Tıklama şekli CAM SİLÜETİNİ izler, sıvı kutusunu değil:
        /// sıvı iç kontura dar, ama parmak tüpün camına basar — dış gövde
        /// (127 px) ve dış dip kavisi tıklanabilir kalmalı.</summary>
        private const float ClickWidth = 127f / SpritePpu;
        private const float ClickBottomRadius = 50f / SpritePpu;

        /// <summary>Dünya koordinatındaki bir noktanın tüp şekli içinde olup olmadığını döner.</summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            // Cam silüeti: pivottan (dış dip) gövde tepesine yuvarlak kutu.
            Vector2 p = new Vector2(local.x, local.y - BodyHeight * 0.5f);
            if (SdRoundedBox(p, new Vector2(ClickWidth * 0.5f, BodyHeight * 0.5f),
                    TopRadius, ClickBottomRadius) <= 0f)
                return true;

            // Halka da tıklanabilir: gövdeyle birleşim. Görselden taşma yok —
            // stadyum, halkanın görünür silüetini izler.
            return SdRing(local) <= 0f;
        }

        // Halka tıklama şekli: FullWidth × RingHeight boyutlu STADYUM (köşe
        // yarıçapı = yarı yükseklik, uçlar tam yuvarlak), dikişten halka
        // tepesine. Sprite sınırlarının köşeleri şeffaf; stadyum o köşeleri
        // dışarıda bırakır, tıklama görünür halkadan taşmaz. Tıpa bilerek
        // DAHİL DEĞİL (tıpalı tüp zaten kilitli).
        private float SdRing(Vector3 local)
        {
            Vector2 center = new Vector2(0f, GlassTop + RingHeight * 0.5f);
            Vector2 half = new Vector2(RingHalfWidth, RingHeight * 0.5f);
            return SdRoundedBox(new Vector2(local.x, local.y) - center, half,
                half.y, half.y);
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

        /// <summary>Tüpün tam boyu (iç taban + sıvı gövdesi). Dökme pozisyonu
        /// hesaplamak için.</summary>
        public float Height => BodyHeight;

        /// <summary>
        /// Halka ağzındaki deliğin MERKEZİ, tüp-yerel konum. Hedef tüpte akış
        /// kolonunun indiği nokta.
        /// </summary>
        public Vector3 MouthCenter => new Vector3(0f, MouthY, 0f);

        /// <summary>
        /// Deliğin döken kenarı (hedefe en yakın nokta), tüp-yerel konum.
        /// KAYNAK tüpte akış buradan başlar: merkezden başlarsa kolon deliğin
        /// ortasından fışkırıyor görünür — sıvı deliğin hedefe bakan dudağından
        /// taşmalı. side = ±1, döken taraf.
        /// </summary>
        public Vector3 MouthLip(float side) => new Vector3(
            RingHoleRx * side, MouthY, 0f);

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
            liquid.sortingOrder = 0 + offset;
            ring.sortingOrder = 2 + offset;
            cork.sortingOrder = 3 + offset;
            corkVeil.sortingOrder = 4 + offset;
            glass.sortingOrder = 5 + offset;
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
