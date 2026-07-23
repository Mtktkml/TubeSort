using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Ekrandaki geri al butonu. Görseli koddan üretilen basit sol ok —
    /// asset yok felsefesine uygun; cila adımında gerçek ikonla değişebilir.
    /// Tıklama yakalama BoardView'dadır: buton yalnızca görsel + collider taşır.
    /// Tahtanın çocuğu değildir; tahta ekrana sığmak için ölçeklense de
    /// buton sabit boyutta kalır.
    /// </summary>
    public class UndoButtonView : MonoBehaviour
    {
        /// <summary>Butonun dünya birimindeki boyu.</summary>
        public const float Size = 0.8f;

        private const int TextureSize = 32;

        private Texture2D texture;
        private Sprite sprite;

        public void Initialize()
        {
            texture = CreateArrowTexture();
            sprite = Sprite.Create(texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / Size);

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(1f, 1f, 1f, 0.85f);
            renderer.sortingOrder = 100; // tüplerin ve akışın üstünde

            var collider = gameObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(Size, Size);
        }

        /// <summary>
        /// Koddan üretilen nesneler sahneyle birlikte temizlenmeli; Unity
        /// nesnelerini C#'ın çöp toplayıcısı toplamaz.
        /// </summary>
        private void OnDestroy()
        {
            Destroy(sprite);
            Destroy(texture);
        }

        /// <summary>Sol ok: üçgen uç + dikdörtgen kuyruk ("geri" oku).</summary>
        private static Texture2D CreateArrowTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);

            var clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < TextureSize; x++)
                for (int y = 0; y < TextureSize; y++)
                    tex.SetPixel(x, y, clear);

            const int centerY = TextureSize / 2;

            // Üçgen uç: tepesi solda (x=4), tabanı x=17'de. Yükseklik uca
            // doğru daralır: her sütunun yarı boyu tepe uzaklığıyla orantılı.
            for (int x = 4; x <= 17; x++)
            {
                int halfHeight = (x - 4) * 12 / 13;
                for (int y = centerY - halfHeight; y <= centerY + halfHeight; y++)
                    tex.SetPixel(x, y, Color.white);
            }

            // Kuyruk: sabit kalınlıkta dikdörtgen.
            for (int x = 17; x <= 27; x++)
                for (int y = centerY - 4; y <= centerY + 4; y++)
                    tex.SetPixel(x, y, Color.white);

            tex.Apply();
            return tex;
        }
    }
}
