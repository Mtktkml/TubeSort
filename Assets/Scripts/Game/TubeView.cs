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
        private const float LayerInset = 0.06f;

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

        public void Initialize(int index, Tube tube, ColorPalette palette, Sprite unitSprite, Material liquidMaterial)
        {
            Index = index;
            this.tube = tube;
            this.palette = palette;
            this.unitSprite = unitSprite;
            restPosition = transform.position;

            properties = new MaterialPropertyBlock();

            CreateGlass();
            CreateLiquid(liquidMaterial);
            CreateClickArea();
            Refresh();
        }

        /// <summary>Tüpün arka planı: sıvının nerede durduğunu belli eden koyu bir gövde.</summary>
        private void CreateGlass()
        {
            var go = new GameObject("Glass");
            go.transform.SetParent(transform, false);

            glass = go.AddComponent<SpriteRenderer>();
            glass.sprite = unitSprite;
            glass.color = new Color(0.16f, 0.18f, 0.20f);
            glass.sortingOrder = 0;

            float height = tube.Capacity * UnitHeight;
            go.transform.localScale = new Vector3(Width, height, 1f);
            // Sprite'ın merkezi ortada; tüpün dibi yerel sıfır noktasında dursun.
            go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>
        /// Sıvının tamamını kaplayan tek dörtgen. Tüpün boyu kadar uzundur;
        /// içinde ne kadarının dolu göründüğüne shader karar verir.
        /// </summary>
        private void CreateLiquid(Material liquidMaterial)
        {
            var go = new GameObject("Liquid");
            go.transform.SetParent(transform, false);

            liquid = go.AddComponent<SpriteRenderer>();
            liquid.sprite = unitSprite;
            liquid.sharedMaterial = liquidMaterial;
            liquid.sortingOrder = 1;

            float height = tube.Capacity * UnitHeight;
            go.transform.localScale = new Vector3(Width - LayerInset, height, 1f);
            go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>Tıklamayı yakalayacak görünmez alan. Cam gövdenin tamamını kaplar.</summary>
        private void CreateClickArea()
        {
            var box = gameObject.AddComponent<BoxCollider2D>();
            float height = tube.Capacity * UnitHeight;

            box.size = new Vector2(Width, height);
            box.offset = new Vector2(0f, height * 0.5f);
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

                    layerColors[layerCount] = palette.Get(color);
                    layerCount++;
                }

                // Katmanın üst sınırı, o katmandaki son birimin üstüdür.
                layerTops[layerCount - 1] = (i + 1) / (float)tube.Capacity * FillSpan;
            }

            liquid.GetPropertyBlock(properties);
            properties.SetVectorArray(LayerColorsId, layerColors);
            properties.SetFloatArray(LayerTopsId, layerTops);
            properties.SetFloat(FillLevelId, tube.Count / (float)tube.Capacity * FillSpan);
            properties.SetInt(LayerCountId, layerCount);
            liquid.SetPropertyBlock(properties);
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
