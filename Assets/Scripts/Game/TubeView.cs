using TubeSort.Core;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tek bir tüpü ekranda çizer. Sıvının tamamı tek bir dörtgene çizilir;
    /// katmanları ve yüzey dalgasını Liquid shader'ı hesaplar.
    ///
    /// Bu sınıfın işi çekirdekteki Tube'u shader'ın anlayacağı dile çevirmek:
    /// "dipten yukarı [kırmızı, sarı, sarı]" -> sınırlar [0.25, 0.75] ve renkler.
    /// </summary>
    public class TubeView : MonoBehaviour
    {
        public const float Width = 0.8f;
        public const float UnitHeight = 0.5f;

        /// <summary>Shader'daki dizi boyutuyla aynı olmak zorunda.</summary>
        private const int MaxLayers = 8;

        private const float SelectedLift = 0.3f;

        /// <summary>Üstteki köşelerin yuvarlaklığı. Dünya birimi.</summary>
        private const float TopRadius = 0.08f;

        /// <summary>
        /// Dibin yuvarlaklığı. Genişliğin yarısına eşit olduğu için dip
        /// tam yarım daire olur - deney tüpü gibi.
        /// </summary>
        private const float BottomRadius = Width * 0.5f;

        /// <summary>
        /// Tüp ağzına kadar dolu olsa bile sıvı dörtgenin en fazla bu kadarını kaplar.
        /// Üstte kalan pay yüzey dalgasının yeri: pay olmadan dolu tüpte dalganın
        /// tepeleri dörtgenin dışında kalıp kırpılır ve sıvı hissi kaybolur.
        /// </summary>
        private const float FillSpan = 0.94f;

        private static readonly int LayerColorsId = Shader.PropertyToID("_LayerColors");
        private static readonly int LayerTopsId = Shader.PropertyToID("_LayerTops");
        private static readonly int FillLevelId = Shader.PropertyToID("_FillLevel");
        private static readonly int LayerCountId = Shader.PropertyToID("_LayerCount");
        private static readonly int TubeSizeId = Shader.PropertyToID("_TubeSize");
        private static readonly int TopRadiusId = Shader.PropertyToID("_TopRadius");
        private static readonly int BottomRadiusId = Shader.PropertyToID("_BottomRadius");

        private Tube tube;
        private ColorPalette palette;
        private Sprite unitSprite;

        private SpriteRenderer glass;
        private SpriteRenderer liquid;
        private MaterialPropertyBlock properties;
        private Vector3 restPosition;

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
            restPosition = transform.position;

            properties = new MaterialPropertyBlock();

            glass = CreateQuad("Glass", glassMaterial, sortingOrder: 0);
            liquid = CreateQuad("Liquid", liquidMaterial, sortingOrder: 1);

            ApplyShape(glass);
            CreateClickArea();
            Refresh();
        }

        /// <summary>
        /// Cam ve sıvı aynı boyutta iki dörtgendir. Aynı olmaları şart: ikisi de
        /// şekli kendi uv'sinden hesapladığı için, boyutları farklı olsaydı
        /// sıvı camın şekline oturmazdı.
        /// </summary>
        private SpriteRenderer CreateQuad(string name, Material material, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = unitSprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;

            float height = TubeHeight;
            go.transform.localScale = new Vector3(Width, height, 1f);
            // Sprite'ın merkezi ortada; tüpün dibi yerel sıfır noktasında dursun.
            go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            return renderer;
        }

        private float TubeHeight => tube.Capacity * UnitHeight;

        /// <summary>Şekil ölçülerini shader'a bildirir. Cam için bir kez yeter; boyutu değişmez.</summary>
        private void ApplyShape(SpriteRenderer renderer)
        {
            renderer.GetPropertyBlock(properties);
            WriteShape();
            renderer.SetPropertyBlock(properties);
        }

        private void WriteShape()
        {
            properties.SetVector(TubeSizeId, new Vector4(Width, TubeHeight, 0f, 0f));
            properties.SetFloat(TopRadiusId, TopRadius);
            properties.SetFloat(BottomRadiusId, BottomRadius);
        }

        /// <summary>Tıklamayı yakalayacak görünmez alan. Cam gövdenin tamamını kaplar.</summary>
        private void CreateClickArea()
        {
            var box = gameObject.AddComponent<BoxCollider2D>();

            box.size = new Vector2(Width, TubeHeight);
            box.offset = new Vector2(0f, TubeHeight * 0.5f);
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
            properties.SetFloat(FillLevelId, tube.Count / (float)tube.Capacity * FillSpan);
            properties.SetInt(LayerCountId, layerCount);
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

        /// <summary>Seçili tüp yukarı kalkar; oyuncu neyi seçtiğini görsün.</summary>
        public void SetSelected(bool selected)
        {
            transform.position = selected
                ? restPosition + Vector3.up * SelectedLift
                : restPosition;
        }
    }
}
