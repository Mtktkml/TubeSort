using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Tam ekran arka plan görseli (game_background). Kameranın görüş alanını
    /// "cover" ile doldurur: en-boy oranı korunur, taşan kısım kırpılır — asla
    /// kenarda boşluk bırakmaz. Her şeyin arkasında çizilir (en düşük sortingOrder,
    /// sıvının 0'ının altında).
    ///
    /// Koddan kurulur (collider'ı yok, sahne etkileşimi gerektirmez) — tüp/akış
    /// görünümleriyle aynı desen, aksiyon çubuğu gibi sahne nesnesi DEĞİL. Tahtanın
    /// çocuğu da değildir: tahta ekrana sığmak için ölçeklenince arka plan onunla
    /// büyümesin. BoardView Start'ta oluşturur, ApplyLayout görüş alanı değiştikçe
    /// Layout ile yeniden boyutlar (cihaz döndürme / katlanır ekran).
    /// </summary>
    public class BackgroundView : MonoBehaviour
    {
        private const string SpritePath = "UI/game_background";
        private const int SortingOrder = -100;   // sıvı (0) ve diğer her şeyin arkasında

        private SpriteRenderer spriteRenderer;
        private Vector2 nativeSize;

        /// <summary>Sprite'ı yükler ve renderer'ı kurar. Sprite yoksa false döner
        /// (çağıran nesneyi yok eder — arka plansız oyun yine çalışır).</summary>
        public bool Initialize()
        {
            var sprite = Resources.Load<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError($"BackgroundView: {SpritePath} sprite bulunamadı " +
                               $"(Assets/Resources/{SpritePath}.png).");
                return false;
            }

            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = SortingOrder;
            nativeSize = sprite.bounds.size;   // ölçeklenmemiş dünya boyu (PPU'dan bağımsız)
            return true;
        }

        /// <summary>Görüş alanını cover ile doldurur: iki boyutu da kaplayan en
        /// büyük ölçek seçilir (boşluk kalmaz, fazlası kırpılır).</summary>
        public void Layout(Camera cam)
        {
            if (spriteRenderer == null || cam == null) return;

            float viewH = cam.orthographicSize * 2f;
            float viewW = viewH * cam.aspect;
            Vector3 c = cam.transform.position;

            float scale = Mathf.Max(viewW / nativeSize.x, viewH / nativeSize.y);
            transform.localScale = Vector3.one * scale;
            transform.position = new Vector3(c.x, c.y, 0f);
        }
    }
}
