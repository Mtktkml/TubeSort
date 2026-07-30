using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Pilot önizlemede "sıradaki level" butonu. Görseli koddan üretilen basit
    /// sağ (ileri) ok — geri al butonunun aynası (sprite asset'i yok). Yalnız
    /// pilot önizleme modunda level'ler arası gezinme yolu olarak kurulur.
    /// Tıklama yakalama BoardView'dadır: buton yalnızca görsel + collider
    /// taşır. Tahtanın çocuğu değildir; tahta ölçeklense de
    /// buton sabit boyutta kalır.
    /// </summary>
    public class PilotNextButtonView : MonoBehaviour
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
            // Yeşil: beyaz geri-al okundan ayrışsın, "geri/ileri çifti" gibi
            // okunmasın. SpriteRenderer.color renk uzayını Unity çevirir.
            renderer.color = new Color(0.30f, 0.78f, 0.45f, 0.92f);
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

        /// <summary>
        /// "Sonraki" simgesi (▶|): sağ ok + ucunda dikey çubuk. Medya "sonraki
        /// parça" simgesi gibi — "level ilerlet" okunur, geri-al okuyla (◀)
        /// çift oluşturmaz.
        /// </summary>
        private static Texture2D CreateArrowTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);

            var clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < TextureSize; x++)
                for (int y = 0; y < TextureSize; y++)
                    tex.SetPixel(x, y, clear);

            const int centerY = TextureSize / 2;

            // Kuyruk: sabit kalınlıkta dikdörtgen (solda).
            for (int x = 3; x <= 12; x++)
                for (int y = centerY - 4; y <= centerY + 4; y++)
                    tex.SetPixel(x, y, Color.white);

            // Üçgen uç: tepesi x=24'te (sağa bakar), tabanı x=12'de. Yükseklik
            // uca (sağa) doğru daralır. Çubuğa yer açmak için sağ kenardan içeride.
            for (int x = 12; x <= 24; x++)
            {
                int halfHeight = 24 - x;
                for (int y = centerY - halfHeight; y <= centerY + halfHeight; y++)
                    tex.SetPixel(x, y, Color.white);
            }

            // "Sonraki" çubuğu: okun sağında dikey çizgi, ok yüksekliğinde.
            for (int x = 27; x <= 29; x++)
                for (int y = centerY - 12; y <= centerY + 12; y++)
                    tex.SetPixel(x, y, Color.white);

            tex.Apply();
            return tex;
        }
    }
}
