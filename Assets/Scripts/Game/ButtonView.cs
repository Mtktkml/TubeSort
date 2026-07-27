using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Faz 1'in yeni butonları (önceki level, restart, +tüp) için ortak görsel.
    /// Görseller koddan çizili basit placeholder — "asset yok" felsefesine uygun;
    /// 1C'de gerçek sprite'larla değişecek (undo/next dahil hepsi birleşecek).
    ///
    /// Var olan UndoButtonView / PilotNextButtonView deseniyle aynı (görsel +
    /// collider taşır, tıklama yakalama BoardView'da); tek fark birden çok butonu
    /// tek sınıfta <see cref="Kind"/> ile toplaması — placeholder tekrarını azaltır.
    /// </summary>
    public class ButtonView : MonoBehaviour
    {
        public enum ButtonKind { Previous, Restart, AddTube }

        /// <summary>Butonun dünya birimindeki boyu (undo/next ile aynı).</summary>
        public const float Size = 0.8f;

        private const int TextureSize = 32;

        public ButtonKind Kind { get; private set; }

        private Texture2D texture;
        private Sprite sprite;

        public void Initialize(ButtonKind kind)
        {
            Kind = kind;
            texture = CreateTexture(kind);
            sprite = Sprite.Create(texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), TextureSize / Size);

            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = TintFor(kind);
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

        /// <summary>Placeholder renkleri: butonlar birbirinden ayrışsın.</summary>
        private static Color TintFor(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Previous: return new Color(0.30f, 0.78f, 0.45f, 0.92f); // next ile ayni yesil (nav cifti)
                case ButtonKind.Restart:  return new Color(0.95f, 0.75f, 0.30f, 0.92f); // sari
                case ButtonKind.AddTube:  return new Color(0.40f, 0.70f, 0.95f, 0.92f); // mavi
                default:                  return Color.white;
            }
        }

        private static Texture2D CreateTexture(ButtonKind kind)
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);

            var clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < TextureSize; x++)
                for (int y = 0; y < TextureSize; y++)
                    tex.SetPixel(x, y, clear);

            switch (kind)
            {
                case ButtonKind.Previous: DrawPrevIcon(tex); break;
                case ButtonKind.Restart:  DrawRestartIcon(tex); break;
                case ButtonKind.AddTube:  DrawPlusIcon(tex); break;
            }

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

        /// <summary>"Restart" (↻): kesikli halka + ok başı — döngü/yeniden.</summary>
        private static void DrawRestartIcon(Texture2D tex)
        {
            const float cx = 16f, cy = 16f, r = 10f;

            for (int x = 0; x < TextureSize; x++)
                for (int y = 0; y < TextureSize; y++)
                {
                    float dx = x - cx, dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // -180..180
                    // Üst-sağda ~60°'lik boşluk (ok başına yer).
                    bool inGap = ang > 20f && ang < 80f;
                    if (dist >= r - 1.6f && dist <= r + 1.6f && !inGap)
                        tex.SetPixel(x, y, Color.white);
                }

            // Ok başı: boşluğun üst ucunda küçük dolu üçgen.
            for (int x = 20; x <= 26; x++)
                for (int y = 22; y <= 22 + (26 - x); y++)
                    tex.SetPixel(x, y, Color.white);
        }

        /// <summary>"+Tüp" (+): artı işareti.</summary>
        private static void DrawPlusIcon(Texture2D tex)
        {
            const int c = TextureSize / 2;

            for (int x = 6; x <= 25; x++)       // yatay kol
                for (int y = c - 3; y <= c + 3; y++)
                    tex.SetPixel(x, y, Color.white);

            for (int y = 6; y <= 25; y++)       // dikey kol
                for (int x = c - 3; x <= c + 3; x++)
                    tex.SetPixel(x, y, Color.white);
        }
    }
}
