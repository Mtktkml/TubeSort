using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Dökme sırasında kaynak tüpün ağzından hedef tüpe akan sıvıyı çizer.
    /// Tek bir SpriteRenderer quad'ı üzerinde Bezier eğrisi shader'ı çalışır.
    /// BoardView tarafından bir kez oluşturulur, her dökmede Show/Hide ile açılıp kapanır.
    /// </summary>
    public class StreamView : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int P0Id = Shader.PropertyToID("_P0");
        private static readonly int P1Id = Shader.PropertyToID("_P1");
        private static readonly int P2Id = Shader.PropertyToID("_P2");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");

        private SpriteRenderer quad;
        private MaterialPropertyBlock properties;

        /// <summary>Akışın kalınlığı ve salınımı için dışarı taşma payı.</summary>
        private const float Padding = 0.25f;

        public void Initialize(Sprite unitSprite, Material streamMaterial)
        {
            properties = new MaterialPropertyBlock();

            var go = new GameObject("Stream");
            go.transform.SetParent(transform, false);

            quad = go.AddComponent<SpriteRenderer>();
            quad.sprite = unitSprite;
            quad.sharedMaterial = streamMaterial;
            // Hedef tüpün (0-1) üstünde, kaynak tüpün (10-11) altında.
            quad.sortingOrder = 5;
            quad.enabled = false;
        }

        /// <summary>
        /// Akışı görünür yapar. Kaynak ve hedef ağız konumları board-local uzaydadır.
        /// </summary>
        public void Show(Color liquidColor, Vector3 sourceMouthLocal, Vector3 destMouthLocal)
        {
            // Bezier kontrol noktaları board-local uzayda.
            Vector2 p0 = sourceMouthLocal;
            Vector2 p2 = destMouthLocal;
            // Sıvı önce aşağı düşer, sonra hedefe kıvrılır.
            Vector2 p1 = new Vector2(p0.x, Mathf.Lerp(p0.y, p2.y, 0.35f));

            // Quad boyutu: üç noktanın AABB'si + padding.
            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - Padding;
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) + Padding;
            float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) - Padding;
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) + Padding;

            float quadW = maxX - minX;
            float quadH = maxY - minY;
            Vector2 center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            // Quad'ı konumlandır ve boyutlandır.
            quad.transform.localPosition = new Vector3(center.x, center.y, 0f);
            quad.transform.localScale = new Vector3(quadW, quadH, 1f);

            // Bezier noktalarını quad merkezine göre ayarla.
            Vector2 rel0 = p0 - center;
            Vector2 rel1 = p1 - center;
            Vector2 rel2 = p2 - center;

            quad.GetPropertyBlock(properties);
            properties.SetVector(ColorId, ToShaderColor(liquidColor));
            properties.SetVector(P0Id, new Vector4(rel0.x, rel0.y, 0f, 0f));
            properties.SetVector(P1Id, new Vector4(rel1.x, rel1.y, 0f, 0f));
            properties.SetVector(P2Id, new Vector4(rel2.x, rel2.y, 0f, 0f));
            properties.SetVector(QuadSizeId, new Vector4(quadW, quadH, 0f, 0f));
            quad.SetPropertyBlock(properties);

            quad.enabled = true;
        }

        public void Hide()
        {
            quad.enabled = false;
        }

        /// <summary>
        /// Rengi shader'ın beklediği uzaya çevirir. TubeView'daki ile aynı mantık:
        /// SetVectorArray/SetVector renk dönüşümü yapmaz, Linear projede
        /// elle çevirmek gerekir.
        /// </summary>
        private static Vector4 ToShaderColor(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear
                ? (Vector4)color.linear
                : (Vector4)color;
        }
    }
}
