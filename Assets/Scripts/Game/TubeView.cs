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
        /// Toon tasarımda cam DÜZ tüp: ağız genişlemesi yok, genişleme ayrı bej
        /// halkaya (collar) devredildi. MouthWidth = Width olunca ağız kutusu
        /// gövdeyle aynı genişliğe iner ve SdSmoothUnion düz bir tüp verir.
        /// Şekil hem shader'a (WriteShape) hem CPU tıklama SDF'ine (SdTube) bu
        /// sabitten gider; ikisi birlikte güncellenir.
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

        /// <summary>Tüm elipslerin ortak basıklık oranı (ry/rx) — yaka, delik, tıpa
        /// üst/dip elipsleri, tüp dibi aynı perspektifi paylaşsın (tutarlılık, req 6).</summary>
        private const float EllipseRatio = 0.34f;

        // ── Kalın bej yaka (collar) — üst elips yüz + yan yükseklik + alt dudak. ──
        /// <summary>Yaka üst/alt elipsinin x yarıçapı (tüpten yanlara taşar).</summary>
        public const float CollarRx = Width * 0.66f;
        /// <summary>Yaka elipsinin y yarıçapı (ortak basıklık).</summary>
        private const float CollarRy = CollarRx * EllipseRatio;
        /// <summary>Yan duvarın yarı-yüksekliği (yakaya kalınlık verir).</summary>
        private const float CollarSideHalf = Width * 0.15f;
        /// <summary>Koyu eliptik deliğin x yarıçapı.</summary>
        private const float CollarHoleRx = Width * 0.40f;
        /// <summary>Deliğin y yarıçapı (ortak basıklık).</summary>
        private const float CollarHoleRy = CollarHoleRx * EllipseRatio;
        /// <summary>Delik merkezinin yaka merkezine göre y'si — üst yüzün ortasına yakın,
        /// üstte bir miktar bej kalsın (arka rim görünsün).</summary>
        private const float CollarHoleCenterY = CollarSideHalf - CollarRy * 0.05f;
        /// <summary>Yaka merkezinin tüp tepesine göre y'si (ince ayar).</summary>
        private const float CollarCenterY = 0.14f;
        /// <summary>Konturun kırpılmaması için dörtgene bırakılan pay.</summary>
        private const float CollarPadding = 0.06f;

        // ── Mantar tıpa (cork) — tepe elipsi + büyük koni + kademe + küçük koni + dışbükey
        // ön yay taban. Alt genişlikler Cork.shader'da CapRx oranı olarak türetilir. ──
        /// <summary>Tıpanın en geniş yeri (tepe elipsi x yarıçapı).</summary>
        private const float CorkCapRx = Width * 0.42f;
        /// <summary>Tıpanın yarı-yüksekliği (üst yüzden dibe).</summary>
        private const float CorkHalfHeight = Width * 0.60f;
        /// <summary>Kademenin collar deliğine hizasına eklenen ince-ayar payı (0 = tam hiza).</summary>
        private const float CorkCenterY = 0f;
        /// <summary>Kontur/pay.</summary>
        private const float CorkPadding = 0.06f;

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
        private static readonly int CollarQuadId = Shader.PropertyToID("_CollarQuad");
        private static readonly int TopRadiiId = Shader.PropertyToID("_TopRadii");
        private static readonly int HoleRadiiId = Shader.PropertyToID("_HoleRadii");
        private static readonly int SideHalfId = Shader.PropertyToID("_SideHalf");
        private static readonly int HoleCenterYId = Shader.PropertyToID("_HoleCenterY");
        private static readonly int FrontOnlyId = Shader.PropertyToID("_FrontOnly");
        private static readonly int CorkQuadId = Shader.PropertyToID("_CorkQuad");
        private static readonly int CapRadiiId = Shader.PropertyToID("_CapRadii");
        private static readonly int CorkYsId = Shader.PropertyToID("_CorkYs");

        private Tube tube;
        private ColorPalette palette;
        private Sprite unitSprite;

        private SpriteRenderer glass;
        private SpriteRenderer liquid;
        private SpriteRenderer collar;
        private SpriteRenderer cork;
        private SpriteRenderer collarFront;
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
            Material glassMaterial, Material liquidMaterial, Material collarMaterial,
            Material corkMaterial)
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

            glass = CreateQuad("Glass", glassMaterial, sortingOrder: 0);
            liquid = CreateQuad("Liquid", liquidMaterial, sortingOrder: 1);

            ApplyShape(glass);
            CreateCork(corkMaterial);      // yakanın arkasında (order 2)
            CreateCollar(collarMaterial);  // tıpanın önünde (order 3)
            CreateClickArea();
            Refresh();
        }

        /// <summary>
        /// Cam ve sıvı aynı boyutta iki dörtgendir. Aynı olmaları şart: ikisi de
        /// gövdenin yerini kendi uv'sinden hesapladığı için, boyutları farklı
        /// olsaydı sıvı camın şekline oturmazdı.
        /// </summary>
        private SpriteRenderer CreateQuad(string name, Material material, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = unitSprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;

            go.transform.localScale = new Vector3(QuadWidth, QuadHeight, 1f);
            // Sprite'ın merkezi ortada; tüpün dibi yerel sıfır noktasında dursun.
            go.transform.localPosition = new Vector3(0f, QuadHeight * 0.5f, 0f);

            return renderer;
        }

        /// <summary>
        /// Kalın bej yakayı iki quad ile kurar: arka/tam yaka (order 2, tıpanın
        /// ARKASINDA — koyu delik burada) ve ön yüz overlay'i (order 4, tıpanın
        /// ÖNÜNDE — collar'ın ön kenarı tıpayı sarsın). Böylece tıpa halkadan geçer:
        /// kapak önde, ortada collar önde, tüp içinde tekrar görünür. Ön overlay yalnız
        /// mantar varken açılır (bkz. Refresh).
        /// </summary>
        private void CreateCollar(Material material)
        {
            collar = CreateCollarQuad("Collar", material, 2, frontOnly: false);
            collarFront = CreateCollarQuad("CollarFront", material, 4, frontOnly: true);
            collarFront.enabled = false;
        }

        private SpriteRenderer CreateCollarQuad(string name, Material material, int sortingOrder, bool frontOnly)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = unitSprite;
            r.sharedMaterial = material;
            r.sortingOrder = sortingOrder;
            go.transform.localScale = new Vector3(CollarQuadWidth, CollarQuadHeight, 1f);
            go.transform.localPosition = new Vector3(0f, BodyHeight + CollarCenterY, 0f);

            r.GetPropertyBlock(properties);
            properties.SetVector(CollarQuadId, new Vector4(CollarQuadWidth, CollarQuadHeight, 0f, 0f));
            properties.SetVector(TopRadiiId, new Vector4(CollarRx, CollarRy, 0f, 0f));
            properties.SetVector(HoleRadiiId, new Vector4(CollarHoleRx, CollarHoleRy, 0f, 0f));
            properties.SetFloat(SideHalfId, CollarSideHalf);
            properties.SetFloat(HoleCenterYId, CollarHoleCenterY);
            properties.SetFloat(FrontOnlyId, frontOnly ? 1f : 0f);
            r.SetPropertyBlock(properties);
            return r;
        }

        private static float CollarQuadWidth => 2f * CollarRx + 2f * CollarPadding;
        private static float CollarQuadHeight => 2f * (CollarSideHalf + CollarRy) + 2f * CollarPadding;

        /// <summary>
        /// Mantar tıpayı kurar (yakanın ÖNÜNDE, sortingOrder 3). Başlangıçta gizli;
        /// yalnız tüp tamamlanınca görünür (bkz. Refresh). Şekil: kesik koni — üst elips
        /// yüz (geniş) + daralan yan + oval dar dip. Tüpün ağzından içeri girer.
        /// </summary>
        private void CreateCork(Material material)
        {
            var go = new GameObject("Cork");
            go.transform.SetParent(transform, false);

            cork = go.AddComponent<SpriteRenderer>();
            cork.sprite = unitSprite;
            cork.sharedMaterial = material;
            cork.sortingOrder = 3;   // yakanın ÖNÜNDE
            cork.enabled = false;

            // Spec geometrisi (dörtgen merkezine göre):
            // tepe elipsi ry ≈ W/5; büyük koni üst 2/3, küçük koni alt 1/3; kademe
            // alttaki 1/3'ün başında; taban dışbükey ön yay.
            float W = CorkCapRx;
            float capRy = 0.20f * W;
            float baseRy = 0.30f * (0.70f * W);
            float halfH = CorkHalfHeight;
            float yTop = halfH - capRy;
            float yBase = -halfH + baseRy;
            float yStep = yBase + (yTop - yBase) / 3f;   // kademe = küçük koninin başı

            float quadW = 2f * W + 2f * CorkPadding;
            float quadH = 2f * (halfH + CorkPadding);
            go.transform.localScale = new Vector3(quadW, quadH, 1f);
            // Kademe (deliğe giriş seviyesi) tam collar deliğine otursun. CorkCenterY
            // ince ayar payı.
            float centerY = BodyHeight + CollarCenterY + CollarHoleCenterY - yStep + CorkCenterY;
            go.transform.localPosition = new Vector3(0f, centerY, 0f);

            cork.GetPropertyBlock(properties);
            properties.SetVector(CorkQuadId, new Vector4(quadW, quadH, 0f, 0f));
            properties.SetVector(CapRadiiId, new Vector4(W, capRy, 0f, 0f));
            properties.SetVector(CorkYsId, new Vector4(yTop, yStep, yBase, yStep));
            cork.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Tüpün ekranda kapladığı toplam genişlik. Toon tasarımda en geniş parça
        /// bej yaka olduğu için yerleşim ona göre yapılır; yoksa komşu tüplerin
        /// yakaları birbirine girer. (Cam/sıvı dörtgeni daha dar: QuadWidth.)
        /// </summary>
        public static float FullWidth => 2f * CollarRx;

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

        /// <summary>
        /// Ağız gövdenin üstüne biner, üstüne eklenmez: tüpün boyu gövdenin boyudur.
        /// </summary>
        private float QuadHeight => BodyHeight;

        /// <summary>Şekil ölçülerini shader'a bildirir. Cam için bir kez yeter; boyutu değişmez.</summary>
        private void ApplyShape(SpriteRenderer renderer)
        {
            renderer.GetPropertyBlock(properties);
            WriteShape();
            renderer.SetPropertyBlock(properties);
        }

        private void WriteShape()
        {
            properties.SetVector(QuadSizeId, new Vector4(QuadWidth, QuadHeight, 0f, 0f));
            properties.SetVector(BodySizeId, new Vector4(Width, BodyHeight, 0f, 0f));
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
            WriteShape();
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
            collarFront.enabled = cork.enabled;
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
            collarFront.sortingOrder = 4 + offset;
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
