using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// "Önceki level" nav butonu (test amaçlı, sağ üst köşe). Görseli koddan
    /// çizilen sola bakan ok (sprite asset'i yok). PilotNextButtonView'in aynası;
    /// tıklama yakalama BoardView'dadır (buton yalnız görsel + collider taşır).
    /// Tahtanın çocuğu değildir; tahta ölçeklense de sabit boyutta kalır.
    ///
    /// (Eski undo/restart/+tüp aksiyonları artık alt aksiyon çubuğunda —
    /// ButtonBarView; bu sınıf yalnız level gezinme okuna indirgendi.)
    /// </summary>
    public class ButtonView : MonoBehaviour
    {
        /// <summary>Butonun dünya birimindeki boyu (next ile aynı).</summary>
        public const float Size = 0.8f;

        private const int TextureSize = 32;

        private Texture2D texture;
        private Sprite sprite;
        private SpriteRenderer spriteRenderer;

        public void Initialize()
        {
            texture = CreateTexture();
            sprite = Sprite.Create(texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / Size);

            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            // Yeşil: next ile aynı (nav çifti). SpriteRenderer.color renk uzayını Unity çevirir.
            spriteRenderer.color = new Color(0.30f, 0.78f, 0.45f, 0.92f);
            spriteRenderer.sortingOrder = 100; // tüplerin ve akışın üstünde

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

        private static Texture2D CreateTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);

            var clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < TextureSize; x++)
                for (int y = 0; y < TextureSize; y++)
                    tex.SetPixel(x, y, clear);

            DrawPrevIcon(tex);

            tex.Apply();
            return tex;
        }

        /// <summary>"Önceki" (|◀): solda çubuk + sola bakan ok — next'in aynası.</summary>
        private static void DrawPrevIcon(Texture2D tex)
        {
            const int centerY = TextureSize / 2;

            // Çubuk: solda dikey çizgi.
            for (int x = 2; x <= 4; x++)
                for (int y = centerY - 12; y <= centerY + 12; y++)
                    tex.SetPixel(x, y, Color.white);

            // Üçgen uç: tepesi solda (x=7, sola bakar), tabanı x=19'da.
            for (int x = 7; x <= 19; x++)
            {
                int halfHeight = x - 7;
                for (int y = centerY - halfHeight; y <= centerY + halfHeight; y++)
                    tex.SetPixel(x, y, Color.white);
            }

            // Kuyruk: okun sağında dikdörtgen.
            for (int x = 19; x <= 28; x++)
                for (int y = centerY - 4; y <= centerY + 4; y++)
                    tex.SetPixel(x, y, Color.white);
        }
    }
}
