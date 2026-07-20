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
        /// Tüp ağzına doğru yatayda genişler. Ağız gövdeden bu kadar geniştir;
        /// ayrı bir parça değil, camın devamıdır.
        /// </summary>
        private const float MouthWidth = Width * 1.16f;

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
            Material glassMaterial, Material liquidMaterial)
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
        /// Tüpün ekranda kapladığı toplam genişlik: genişleyen ağız ve yumuşak
        /// birleşimin taşma payı dahil. Yerleşimi hesaplayan taraf bunu bilmeli,
        /// yoksa tüpler birbirine girer.
        /// </summary>
        public static float FullWidth => MouthWidth + 2f * MouthBlend;

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
        private static float QuadWidth => FullWidth;

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

        private float SdTube(Vector2 p)
        {
            Vector2 quadSize = new Vector2(QuadWidth, QuadHeight);
            Vector2 bodySize = new Vector2(Width, BodyHeight);
            Vector2 mouthSize = new Vector2(MouthWidth, MouthHeight);

            Vector2 bodyCenter = new Vector2(0f, -quadSize.y * 0.5f + bodySize.y * 0.5f);
            float bodyDist = SdRoundedBox(p - bodyCenter, bodySize * 0.5f,
                TopRadius, BottomRadius);

            Vector2 mouthCenter = new Vector2(0f, quadSize.y * 0.5f - mouthSize.y * 0.5f);
            float mouthDist = SdRoundedBox(p - mouthCenter, mouthSize * 0.5f,
                MouthRadius, MouthRadius);

            return SdSmoothUnion(bodyDist, mouthDist, MouthBlend);
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

        private static float SdSmoothUnion(float d1, float d2, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (d2 - d1) / k);
            return Mathf.Lerp(d2, d1, h) - k * h * (1f - h);
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
