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

        // ── Görsel katman: PNG'leri (Resources/Sprites). Cam gövde (tube) ve
        // ring/yaka (collar) AYRI asset'lerdir (birleşik tüpten içeriğe göre
        // ayrıldılar; kanvasları dikiş çevresinde bilerek örtüşür — konum
        // sabitleri aşağıda). SIVI EN ARKADA, cam önde çizilir: cam yarı saydam
        // olduğu için içerik içinden görünür ve camın GÖMÜLÜ parlamaları içeriğin
        // üstüne kendiliğinden düşer (taklit denendi, hiza tutmadı; kompozit
        // halleder). Tam sıra:
        //   sıvı 0 < akış-alt 1 < ring/yaka 2 < cam gövde 5 < tıpa 6.
        // TIPA (tamamlanan tüpte) HER ŞEYİN ÖNÜNDE, opak: cork_seated (referanstan
        // tıpa silüetine kırpma). Yakayı ring verir (arkada, cama dikişsiz bağlı);
        // tıpa ağza oturur, "iki şekli uç uca birleştirme" çizgisi yok. Akış-alt
        // kolonu ring'in ve camın arkasında (deliğe girip camdan süzülür). ──
        /// <summary>Resources yolları — BoardView yükler, TubeView kullanır.
        /// Cam gövde ve mor halka AYRI asset'lerdir: tube.png'den İÇERİĞE GÖRE
        /// ayrıldılar (düz kesim değil — geçiş bölgesinde mor pikseller
        /// halkaya, camlar gövdeye; runtime Sprite.Create bölmesi kaldırıldı).</summary>
        public const string TubeBodySpritePath = "Sprites/tube";
        public const string TubeRingSpritePath = "Sprites/collar";
        public const string CorkSpritePath = "Sprites/cork";
        /// <summary>Oturmuş tıpa: cork_mouth'tan TIPA SİLÜETİNE kırpılmış (yaka
        /// atıldı; camsı-mat alt referanstan). Yakayı ring verir; bu yalnız ağza
        /// oturan mantar → "iki şekil birleşimi" çizgisi olmaz. cork.png ile aynı
        /// 90×97, pivot Center.</summary>
        public const string CorkSeatedSpritePath = "Sprites/cork_seated";

        // Parça geometrisi (piksel, satırlar orijinal tube.png'ye göre ÜSTTEN).
        /// <summary>Halkanın DİKİŞE kadarki yüksekliği (satır 0-59; dikiş 60).
        /// RingTop/TopOverhang/tıklama stadyumu bu ölçüyle çalışır.</summary>
        private const int RingRows = 60;
        /// <summary>Halka parçası kanvası dikişin altına taşan mor kuyruğu da
        /// taşır (152×70: satır 0-69) — pivot Bottom kanvasın dibinde, halka
        /// dikişten bu kadar AŞAĞI kaydırılarak konur.</summary>
        private const float RingPieceBelowSeamRows = 10f;
        /// <summary>Gövde parçası kanvası dikişin üstüne taşan duvar tepelerini
        /// taşır (152×456: satır 40-495) — sliced boyu dikişten bu kadar
        /// YUKARI uzatılır. (İki parça içerikçe ayrık; üst üste binince
        /// orijinali boşluksuz kurarlar. Gövde 9-slice border'ları import'ta:
        /// alt 54 dip kavisi, üst 36 duvar tepeleri + ağız gölgesi gradyanı.)</summary>
        private const float BodyPieceAboveSeamRows = 20f;
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

        // TIPA: tamamlanan tüpte ring (yaka) hep açık kalır; onun ÖNÜNE cork_seated
        // (cork_mouth'tan tıpa silüetine kırpılmış — camsı-mat alt referanstan)
        // biner (order 6). Ağza oturan mantar: kenarı doğal, "iki şekil birleşimi"
        // çizgisi yok. Düşüşte ham cork.png, oturunca cork_seated. Eski boyama/pus
        // makinesi kaldırıldı (referans-kırpma yeterli).

        /// <summary>Tıpa görselinin dünya boyu (97 satır) — FillHeadroom
        /// türetimi statik kalsın diye sabit (CreateCork konumu çalışma anında
        /// sprite'tan okur).</summary>
        private const float CorkSpriteHeight = 97f / SpritePpu;
        /// <summary>Tıpa tepesi halka tepesinin bu kadar üstünde (birleşik
        /// referans `tube reference.png`: 18 satır).</summary>
        private const float CorkTopAboveRingTop = 18f / SpritePpu;

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
        private Sprite corkRawSprite;     // düşüş animasyonundaki ham tıpa
        private Sprite corkSeatedSprite;  // oturmuş tıpa (cork_seated; düşüş bitince ham'ın yerine)
        private bool corked;              // mantıksal tıpa durumu (cork.enabled değil: oturunca cork kapalı)
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
        private Coroutine completionRoutine;   // tamamlanma efekti (~3s zarf; halka/spiral/kıvılcım — adım B-F)
        private float completionProgress;      // 0..1: tamamlanma efekt zarfı (B-F shader'lara gönderir)
        /// <summary>Tamamlanma efektinin toplam süresi (sn; video ~3s).</summary>
        private const float CompletionDuration = 3f;
        /// <summary>Tıpanın düşmeye başladığı efekt ilerlemesi: spiral RiseEnd=0.60'da
        /// tepeye ulaşır; düşüş ~0.16s (~0.05 progress) sürdüğü için buradan başlarsa
        /// "tak" anı ~spiral zirvesine denk gelir (adım E re-time).</summary>
        private const float CorkStartProgress = 0.55f;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int CompletionProgressId = Shader.PropertyToID("_CompletionProgress");

        // Tamamlanma efekti quad'ları (koddan; materyalleri TubeView SAHİPLENİR,
        // OnDestroy temizler — Initialize/BoardView'e her adımda dokunmamak için).
        // Adım B: dip halkası (büyü çemberi).
        private SpriteRenderer completionRing;
        private Material ringMaterial;
        /// <summary>Dip halkası quad'ı: FullWidth'in bu katı geniş (yassı elips).
        /// GÖZLE AYARLANABİLİR (boyut/oran/konum).</summary>
        private const float CompletionRingWidthScale = 1.7f;
        private const float CompletionRingAspect = 0.42f;   // yükseklik/genişlik (yassı)
        private const float CompletionRingBaseY = 0.18f;    // dipten yükseklik (dünya birimi)

        // Adım C: ışık spirali (tüpü saran helis, dipten tepeye tırmanır).
        private SpriteRenderer completionSpiral;
        private Material spiralMaterial;
        /// <summary>Spiral quad'ı: FullWidth'in bu katı geniş (helis tüp kenarında
        /// sarılsın). GÖZLE AYARLANABİLİR.</summary>
        private const float CompletionSpiralWidthScale = 1.3f;

        // Adım D: yükselen kıvılcımlar (tüp boyunca akan altın yıldızlar).
        private SpriteRenderer completionSparkles;
        private Material sparklesMaterial;
        /// <summary>Kıvılcım quad'ı: FullWidth'in bu katı geniş (tüpü biraz taşsın,
        /// kenarlarda da parlasın). GÖZLE AYARLANABİLİR.</summary>
        private const float CompletionSparklesWidthScale = 1.4f;

        // Adım E: oturma flaşı (tıpa "tak" ettiği an yıldız patlaması, tek atışlık).
        private SpriteRenderer completionFlash;
        private Material flashMaterial;
        private Coroutine flashRoutine;
        /// <summary>Flaş quad'ı: FullWidth'in bu katı (ağzın çevresine taşsın).</summary>
        private const float CompletionFlashWidthScale = 1.9f;
        /// <summary>Flaş süresi (sn): tek "tak" patlaması, kısa.</summary>
        private const float FlashDuration = 0.4f;

        /// <summary>Tamamlanma efekti (~3s büyülü) hâlâ oynuyor mu? Kazanma
        /// pop-up'ı bunun bitmesini bekler (BoardView).</summary>
        public bool IsCompletionPlaying => completionRoutine != null;
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
            Sprite corkSprite, Sprite seatedCorkSprite)
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

            corkRawSprite = corkSprite;
            corkSeatedSprite = seatedCorkSprite;
            CreateRing(ringSprite);
            CreateCork(corkSprite);
            CreateCompletionRing();
            CreateCompletionSpiral();
            CreateCompletionSparkles();
            CreateCompletionFlash();
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
        /// Collar'ı (gri halkayı) kurar (order 2: sıvının ve akışın ÖNÜNDE,
        /// tıpanın ARKASINDA — her zaman OPAK): tüp dokusunun üst parçası,
        /// gövdenin tepesine (dikişe) bitişik oturur. Dökme sırasında dudağa
        /// tırmanan sıvı ve ağza giren akış kolonu (order 1) collar'ın
        /// arkasında kalır. Tıpa collar'ın önünde çizilir; "arkasında" hissi
        /// oturmuş tıpanın aşamalı boyalı kopyasıyla verilir.
        /// </summary>
        private void CreateRing(Sprite ringSprite)
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(transform, false);

            ring = go.AddComponent<SpriteRenderer>();
            ring.sprite = ringSprite;   // pivot Bottom: kanvas dibi
            ring.sortingOrder = 2;
            // Kanvasın alt 10 satırı dikişin altına taşan mor kuyruk: pivot o
            // kadar aşağı kaydırılır ki halkanın gövdesi dikişe otursun.
            go.transform.localPosition = new Vector3(
                0f, GlassTop - RingPieceBelowSeamRows / SpritePpu, 0f);
        }

        /// <summary>
        /// Mantar tıpayı ve pus perdesini kurar (tıpa order 2: HALKANIN VE
        /// CAMIN ARKASINDA — başı halkanın üstünde serbest, deliğe girince
        /// deliğin camsı pikselleri onu tonlar, halkanın opak gövdesi örter;
        /// perde order 4: tıpanın önünde, camın arkasında, YALNIZ tüp-içi
        /// bölgede). Konum birleşik referanstan: tıpa tepesi halka tepesinin
        /// CorkTopAboveRingTop kadar üstünde; alt ucu ağızdan içeri sarkar.
        /// Perde tıpanın ÇOCUĞUDUR: takılma animasyonu ve ezilme esnemesi
        /// perdeyi kendiliğinden taşır. İkisi de gizli başlar; yalnız
        /// tamamlanan tüpte görünürler (bkz. RefreshCork).
        /// </summary>
        private void CreateCork(Sprite sprite)
        {
            // Tıpa: düşüşte HAM cork.png, oturunca cork_seated (camsı-mat alt,
            // referanstan tıpa-silüetine kırpma). Cam gövdenin ve ring'in ÖNÜNDE
            // (order 6) → opak mantar. Yakayı RING verir (arkada); tıpa ağzın
            // içine oturur, kenarı "iki şekil birleşimi" değil doğal mantar kenarı.
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);

            cork = go.AddComponent<SpriteRenderer>();
            cork.sprite = sprite;
            cork.sortingOrder = 6;   // ring'in ve camın önünde
            cork.enabled = false;

            // Tıpanın gri yaka bandı ring'in yakasıyla çakışacak şekilde hesaplı
            // konumlanır. Ham ve seated aynı 90×97, aynı merkez → oturmada tıpa
            // oynamaz, yalnız camsı-mat alt belirir.
            corkRestPosition = new Vector3(0f, CorkSeatedCenterY, 0f);
            go.transform.localPosition = corkRestPosition;
        }

        /// <summary>
        /// Tamamlanma efekti — DİP HALKASI (adım B): tüpün dibine, zeminde altın
        /// parlayan yassı elips (CompletionRing shader). Tüpün çocuğu (onunla
        /// ölçeklenir), sortingOrder −10 ile içeriğin ARKASINDA (zemin). Gizli
        /// başlar; SetCompletionProgress yoğunluğu (_Progress) sürer. Materyal
        /// TubeView'e ait (OnDestroy temizler).
        /// </summary>
        private void CreateCompletionRing()
        {
            var shader = Resources.Load<Shader>("CompletionRing");
            if (shader == null)
            {
                Debug.LogError("CompletionRing shader bulunamadı (Assets/Resources/CompletionRing.shader).");
                return;
            }
            ringMaterial = new Material(shader);

            var go = new GameObject("CompletionRing");
            go.transform.SetParent(transform, false);
            completionRing = go.AddComponent<SpriteRenderer>();
            completionRing.sprite = unitSprite;
            completionRing.sharedMaterial = ringMaterial;
            completionRing.sortingOrder = -10;   // zeminde, sıvının/camın arkasında
            completionRing.enabled = false;

            float w = FullWidth * CompletionRingWidthScale;
            go.transform.localScale = new Vector3(w, w * CompletionRingAspect, 1f);
            go.transform.localPosition = new Vector3(0f, CompletionRingBaseY, 0f);
        }

        /// <summary>
        /// Tamamlanma efekti — IŞIK SPİRALİ (adım C): tüpü saran altın helis,
        /// dipten (0) ağza (RingTop) uzanan quad. Tüpün ÖNÜNDE (sortingOrder 7),
        /// gizli başlar; _Progress ile tırmanır/söner. Materyal TubeView'e ait.
        /// </summary>
        private void CreateCompletionSpiral()
        {
            var shader = Resources.Load<Shader>("CompletionSpiral");
            if (shader == null)
            {
                Debug.LogError("CompletionSpiral shader bulunamadı (Assets/Resources/CompletionSpiral.shader).");
                return;
            }
            spiralMaterial = new Material(shader);

            var go = new GameObject("CompletionSpiral");
            go.transform.SetParent(transform, false);
            completionSpiral = go.AddComponent<SpriteRenderer>();
            completionSpiral.sprite = unitSprite;
            completionSpiral.sharedMaterial = spiralMaterial;
            completionSpiral.sortingOrder = 7;   // tüpün önünde (cam/tıpa üstünde ışıldar)
            completionSpiral.enabled = false;

            float h = RingTop;   // dipten ağza
            go.transform.localScale = new Vector3(FullWidth * CompletionSpiralWidthScale, h, 1f);
            go.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
        }

        /// <summary>
        /// Tamamlanma efekti — YÜKSELEN KIVILCIMLAR (adım D): tüp boyunca akan
        /// altın yıldızlar. Spiralle aynı dikey açıklık (dipten RingTop'a), tüpün
        /// ÖNÜNDE (sortingOrder 8 — spiralin de önünde parıldar). Gizli başlar;
        /// _Progress ile belirir/söner. Materyal TubeView'e ait.
        /// </summary>
        private void CreateCompletionSparkles()
        {
            var shader = Resources.Load<Shader>("CompletionSparkles");
            if (shader == null)
            {
                Debug.LogError("CompletionSparkles shader bulunamadı (Assets/Resources/CompletionSparkles.shader).");
                return;
            }
            sparklesMaterial = new Material(shader);

            var go = new GameObject("CompletionSparkles");
            go.transform.SetParent(transform, false);
            completionSparkles = go.AddComponent<SpriteRenderer>();
            completionSparkles.sprite = unitSprite;
            completionSparkles.sharedMaterial = sparklesMaterial;
            completionSparkles.sortingOrder = 8;   // spiralin (7) de önünde
            completionSparkles.enabled = false;

            float h = RingTop;   // dipten ağza (spiralle aynı açıklık)
            go.transform.localScale = new Vector3(FullWidth * CompletionSparklesWidthScale, h, 1f);
            go.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
        }

        /// <summary>
        /// Tamamlanma efekti — OTURMA FLAŞI (adım E): tıpa ağza oturduğu an patlayan
        /// kısa yıldız flaşı. Tıpanın oturduğu yere (CorkSeatedCenterY) merkezli
        /// kare quad, HER ŞEYİN önünde (sortingOrder 9). Gizli başlar; PlayFlash tek
        /// atışlık _Flash (0..1) sürer. Materyal TubeView'e ait.
        /// </summary>
        private void CreateCompletionFlash()
        {
            var shader = Resources.Load<Shader>("CompletionFlash");
            if (shader == null)
            {
                Debug.LogError("CompletionFlash shader bulunamadı (Assets/Resources/CompletionFlash.shader).");
                return;
            }
            flashMaterial = new Material(shader);

            var go = new GameObject("CompletionFlash");
            go.transform.SetParent(transform, false);
            completionFlash = go.AddComponent<SpriteRenderer>();
            completionFlash.sprite = unitSprite;
            completionFlash.sharedMaterial = flashMaterial;
            completionFlash.sortingOrder = 9;   // kıvılcımların (8) ve tıpanın (6) önünde
            completionFlash.enabled = false;

            float w = FullWidth * CompletionFlashWidthScale;   // kare patlama
            go.transform.localScale = new Vector3(w, w, 1f);
            go.transform.localPosition = new Vector3(0f, CorkSeatedCenterY, 0f);
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

        /// <summary>Oturmuş tıpanın (cork_seated, 90×97, pivot Center) merkezinin
        /// RingTop'un ne kadar ALTINA konacağı. HESAPLANDI (tahmin değil): tıpanın
        /// gri yaka bandı merkezi (üstten satır 49) oyunun ring'inin (collar.png
        /// 152×70, pivot Bottom, GlassTop−10px'te) gri yaka merkeziyle (tabandan
        /// satır 38) ÇAKIŞIR → (118.5 − 38 − 49) = 31.5px. İki yaka da 40 satır
        /// olduğundan tam biner. Ham tıpa da bu merkeze konur ki oturmada tıpa
        /// oynamasın. (RingTop=GlassTop+60px, ring dibi GlassTop−10px, h=97.)</summary>
        private const float CorkSeatedCenterBelowRingTop = 31.5f / SpritePpu;
        private float CorkSeatedCenterY => RingTop - CorkSeatedCenterBelowRingTop;

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
        /// Cam gövde sprite'ını kurar (order 4 — sıvının, akışın ve düşen
        /// tıpanın ÖNÜNDE): yarı saydam cam, arkasındaki içeriği gösterirken
        /// gömülü parlamalarını üstüne düşürür — dolu tüpün parlaması boş
        /// tüple birebirdir.
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
            // Kanvasın üst 20 satırı dikişin üstüne taşan duvar tepeleri:
            // sliced boy o kadar uzatılır (taşan bölge içerikçe halkayla
            // ayrık — üst üste binince orijinal ağız kurulur).
            glass.size = new Vector2(sprite.bounds.size.x,
                GlassTop + BodyPieceAboveSeamRows / SpritePpu);
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

            if (shouldCork == corked)
                return;
            corked = shouldCork;

            if (corkRoutine != null)
            {
                StopCoroutine(corkRoutine);
                corkRoutine = null;
            }
            if (completionRoutine != null)
            {
                StopCoroutine(completionRoutine);
                completionRoutine = null;
                SetCompletionProgress(0f);   // yarıda kalan tamamlanma efektini temizle
            }
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
                if (completionFlash != null) completionFlash.enabled = false;
            }

            if (!shouldCork)
            {
                cork.enabled = false;   // tıpa kalktı (yaka=ring zaten hep açık)
                return;
            }

            cork.enabled = true;
            cork.transform.localPosition = corkRestPosition;
            cork.transform.localScale = Vector3.one;

            // Oyun sırasında AÇILIRKEN tamamlanma efekti (~3s: dip halkası + kıvılcım
            // + spiral + tıpa + flaş — adım B-F). Kurulum/kapanışta anlık (else).
            if (viewReady)
            {
                // Tıpa spiral tepesine dek GİZLİ (AnimateCorkIn belirtip düşürür);
                // rest pozisyonunda erkenden görünmesin (re-time, adım E).
                cork.sprite = corkRawSprite;
                cork.enabled = false;
                completionRoutine = StartCoroutine(AnimateCompletion());
            }
            else
            {
                cork.sprite = corkSeatedSprite;   // anlık oturuş (kurulum/kapanış)
            }
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
        /// yerçekimi hissi), oturunca pus perdesi açılır ve kısa bir
        /// ezilme-esneme oynar. Süreler kısa tutulur: tıpa "tak" diye
        /// oturmalı, süzülmemeli.
        /// </summary>
        private IEnumerator AnimateCorkIn()
        {
            const float dropHeight = 0.5f;
            const float dropDuration = 0.16f;
            const float settleDuration = 0.12f;

            // Re-time (adım E): tıpa spiral tepesine dek gizliydi; şimdi belirip düşer.
            cork.enabled = true;
            cork.sprite = corkRawSprite;   // düşerken ham tıpa

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

            // "tak" anı: ham tıpa yerini cork_seated'e bırakır (camsı-mat alt gelir;
            // aynı 90×97, aynı merkez → tıpa oynamaz). Aynı an yıldız flaşı patlar.
            cork.sprite = corkSeatedSprite;
            flashRoutine = StartCoroutine(PlayFlash());

            // Oturma esnemesi: %8, abartısız.
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

        /// <summary>Tıpa oturma flaşı (adım E): tek atışlık yıldız patlaması. _Flash'ı
        /// 0→1 sürer (shader hızlı parlar, genişleyerek söner), sonra quad'ı kapatır.
        /// Tamamlanma zarfından BAĞIMSIZ (kendi kısa süresi) — "tak" anına demirli.</summary>
        private IEnumerator PlayFlash()
        {
            if (completionFlash == null || flashMaterial == null)
                yield break;

            completionFlash.enabled = true;

            float elapsed = 0f;
            while (elapsed < FlashDuration)
            {
                elapsed += Time.deltaTime;
                flashMaterial.SetFloat(FlashId, Mathf.Clamp01(elapsed / FlashDuration));
                yield return null;
            }

            completionFlash.enabled = false;
            flashRoutine = null;
        }

        /// <summary>
        /// Tüp TAMAMLANDIĞINDA oynayan büyülü efekt (~3s, videoya sadık): dip
        /// halkası + yükselen kıvılcım + tüpü saran ışık spirali + tıpa oturuşu +
        /// yıldız flaşı, sonra sönümlenme. Zarf ilerlemesini (completionProgress
        /// 0→1) sürer; görsel efektler bu ilerlemeyi shader quad'larına gönderir.
        /// Tıpa CorkStartProgress'e (spiral tepesi) kadar gizli, sonra düşer ve
        /// oturunca flaş patlar (adım E). Sıvı içinde kabarcıklar yükselir (F).
        /// Tortu YOK (kullanıcı isteği).
        /// </summary>
        private IEnumerator AnimateCompletion()
        {
            bool corkStarted = false;

            float t = 0f;
            while (t < CompletionDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / CompletionDuration);
                SetCompletionProgress(p);

                // Tıpa spiral tepeye ulaşırken (CorkStartProgress) düşmeye başlar;
                // "tak" ~spiral zirvesine denk gelir ve oturunca flaş patlar.
                if (!corkStarted && p >= CorkStartProgress)
                {
                    corkStarted = true;
                    corkRoutine = StartCoroutine(AnimateCorkIn());
                }

                yield return null;
            }

            SetCompletionProgress(0f);
            completionRoutine = null;
        }

        /// <summary>Tamamlanma efekt zarfını (0..1) günceller ve görsel efektleri
        /// sürer: dip halkası (B), spiral (C), kıvılcımlar (D) quad'ları (görünürlük
        /// + _Progress) ve sıvı içi kabarcıklar (F, _CompletionProgress). Flaş (E)
        /// buradan DEĞİL, "tak" anına demirli ayrı PlayFlash ile.</summary>
        private void SetCompletionProgress(float p)
        {
            completionProgress = p;

            if (completionRing != null)
            {
                completionRing.enabled = p > 0.001f;
                if (ringMaterial != null) ringMaterial.SetFloat(ProgressId, p);
            }
            if (completionSpiral != null)
            {
                completionSpiral.enabled = p > 0.001f;
                if (spiralMaterial != null) spiralMaterial.SetFloat(ProgressId, p);
            }
            if (completionSparkles != null)
            {
                completionSparkles.enabled = p > 0.001f;
                if (sparklesMaterial != null) sparklesMaterial.SetFloat(ProgressId, p);
            }
            SetLiquidCompletion(p);   // sıvı içi kabarcıklar (adım F)
        }

        /// <summary>Tamamlanma ilerlemesini SIVI shader'ına gönderir (kabarcıklar).
        /// Property block ile (tüp-tüp): SetSwaySlope kalıbı — mevcut değerleri
        /// koruyarak yalnız _CompletionProgress'i günceller.</summary>
        private void SetLiquidCompletion(float p)
        {
            if (liquid == null) return;
            liquid.GetPropertyBlock(properties);
            properties.SetFloat(CompletionProgressId, p);
            liquid.SetPropertyBlock(properties);
        }

        /// <summary>Tamamlanma efekt materyalleri koddan üretildi (TubeView'e
        /// ait); tüp yok edilince (level geçişi/test) elle temizlenmeli — Unity
        /// nesnelerini C#'ın çöp toplayıcısı toplamaz.</summary>
        private void OnDestroy()
        {
            if (ringMaterial != null) Destroy(ringMaterial);
            if (spiralMaterial != null) Destroy(spiralMaterial);
            if (sparklesMaterial != null) Destroy(sparklesMaterial);
            if (flashMaterial != null) Destroy(flashMaterial);
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
            glass.sortingOrder = 5 + offset;
            cork.sortingOrder = 6 + offset;
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
