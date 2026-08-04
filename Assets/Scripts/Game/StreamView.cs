using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Dökme sırasında kaynağın yaka ağzından hedef sıvı yüzeyine DİK inen
    /// dikdörtgen akış kolonunu çizer (tek SpriteRenderer quad'ı + Stream.shader).
    /// BoardView tarafından bir kez oluşturulur, her dökmede Show/Hide ile açılıp kapanır.
    /// </summary>
    public class StreamView : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int StreamHalfId = Shader.PropertyToID("_StreamHalf");
        private static readonly int ColumnHalfId = Shader.PropertyToID("_ColumnHalf");
        private static readonly int CapTopId = Shader.PropertyToID("_CapTop");
        private static readonly int CapBottomId = Shader.PropertyToID("_CapBottom");
        private static readonly int CenterYId = Shader.PropertyToID("_CenterY");

        // Akış İKİ parça: kaynak tarafı her şeyin önünde, hedef tarafı hedefin
        // CAMININ VE HALKASININ ARKASINDA (order 1: sıvı 0 ile cam 2 arası).
        // Tek quad iki tüpün farklı katman ihtiyacını aynı anda karşılayamaz:
        // hedef tarafı camın altında kalmalı, kaynak tarafı her şeyin üstünde.
        private SpriteRenderer quadTop;      // ağızdan hedef deliğe
        private SpriteRenderer quadBottom;   // hedef delikten sıvı yüzeyine
        private MaterialPropertyBlock properties;

        /// <summary>Kolonun dünya-birim genişliği. Sabit: akış yerçekimiyle
        /// düşer, genişliği tüple/miktarla ölçeklenmez.</summary>
        private const float StreamWidth = 0.14f;

        /// <summary>Kolonun alt ucunun hedef yüzeyin altına dalma payı: yuvarlak
        /// uç yüzey çizgisinin altında kalır, akış yüzeye "girer" okunur.
        /// BoardView bunu boş hedefte kolonun tüp dibinin altına taşmaması için
        /// kullanır (yüzey en az bu kadar dipten yukarıda tutulur).</summary>
        public const float SurfacePlunge = 0.07f;

        /// <summary>Üst kolonun ağzın içine doğru uzama payı: tepe ucu deliğe
        /// gömülür, kaynaktaki sıvıyla bağlantı kopuk görünmez. Küçük tutulur
        /// (0.08'di): kolon dudaktan bir tık AŞAĞIDA başlayınca akış daha
        /// doğal okunuyor (kullanıcı ayarı) — sıvıyla bağlantıyı zaten
        /// _MouthOverflow tırmanması kuruyor.</summary>
        private const float TopOverlap = 0.02f;

        /// <summary>İki parçanın birleşim yerinde birbirinin üstüne binme payı:
        /// köşeli uçlar aynı renk/faz ile örtüşür, dikiş görünmez.</summary>
        private const float JoinOverlap = 0.04f;

        /// <summary>Yumuşak kenar kırpılmasın diye dörtgene bırakılan pay.</summary>
        private const float Padding = 0.06f;

        public void Initialize(Sprite unitSprite, Material streamMaterial)
        {
            properties = new MaterialPropertyBlock();

            // Üst parça: kaynak tüpün TÜM parçalarının önünde. Kaynak dökme
            // sırasında bir offset bandı alır (10/20/…); üst parça o kaynağın ön
            // dudağının (offset+6) hemen üstünde olmalı. Sıra her dökmede
            // BoardView'dan SetSortingOrders ile atanır; buradaki 15 varsayılan.
            quadTop = CreateQuad(unitSprite, streamMaterial, "StreamTop", 15);

            // Alt parça: hedefin CAMININ ARKASINDA (order 1, sıvı 0 ile cam 2
            // arası; hedef offset almaz — dökülmüyor). Kolon deliğe girip camın
            // içinden süzülerek yüzeye iner: cam yarı saydam olduğundan kolon
            // görünür, halkanın opak ön dudağı ve camın parlamaları onu doğal
            // biçimde örter — hedefte hiçbir katman açıp kapamak gerekmez.
            quadBottom = CreateQuad(unitSprite, streamMaterial, "StreamBottom", 1);
        }

        /// <summary>Akışın çizim sırasını kaynağın offset bandına göre ayarlar:
        /// üst parça kaynağın önünde (offset+7), alt parça hedefin camının
        /// arkasında (1).</summary>
        public void SetSortingOrders(int topOrder, int bottomOrder)
        {
            quadTop.sortingOrder = topOrder;
            quadBottom.sortingOrder = bottomOrder;
        }

        private SpriteRenderer CreateQuad(Sprite unitSprite, Material material,
            string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = unitSprite;
            r.sharedMaterial = material;
            r.sortingOrder = sortingOrder;
            r.enabled = false;
            return r;
        }

        /// <summary>
        /// Akışı görünür yapar. Kolon, HEDEF deliğin merkez x'inde dik düşer
        /// (pourPos ağız ucunu tam bu hizaya oturtur): üst parça kaynağın yaka
        /// ağzından hedef deliğe, alt parça delikten sıvı yüzeyine. Noktalar
        /// board-local uzaydadır.
        /// </summary>
        public void Show(Color liquidColor, Vector3 sourceMouthLocal,
            Vector3 destMouthLocal, Vector3 destSurfaceLocal)
        {
            Vector4 color = ToShaderColor(liquidColor);
            float x = destMouthLocal.x;
            float cap = StreamWidth * 0.5f;

            // Birleşim uçları KÖŞELİ ve JoinOverlap kadar üst üste biner:
            // kapsül uçlar dikişte boğum yapıp kolonu "yeniden başlıyor"
            // gösterir. Serbest uçlar yuvarlak kalır.
            PlaceColumn(quadTop, color, x,
                sourceMouthLocal.y + TopOverlap, destMouthLocal.y - JoinOverlap,
                capTop: cap, capBottom: 0f);
            PlaceColumn(quadBottom, color, x,
                destMouthLocal.y + JoinOverlap, destSurfaceLocal.y - SurfacePlunge,
                capTop: 0f, capBottom: cap);
        }

        /// <summary>Verilen quad'ı [bottomY, topY] dikey kolonu olarak yerleştirir.</summary>
        private void PlaceColumn(SpriteRenderer quad, Vector4 color, float x,
            float topY, float bottomY, float capTop, float capBottom)
        {
            // Uçlar çok yaklaşsa bile kolon en az kapsül kadar kalsın.
            float columnHalf = Mathf.Max((topY - bottomY) * 0.5f, StreamWidth * 0.5f);
            float centerY = (topY + bottomY) * 0.5f;

            float quadW = StreamWidth + 2f * Padding;
            float quadH = columnHalf * 2f + 2f * Padding;

            quad.transform.localPosition = new Vector3(x, centerY, 0f);
            quad.transform.localScale = new Vector3(quadW, quadH, 1f);

            quad.GetPropertyBlock(properties);
            properties.SetVector(ColorId, color);
            properties.SetVector(QuadSizeId, new Vector4(quadW, quadH, 0f, 0f));
            properties.SetFloat(StreamHalfId, StreamWidth * 0.5f);
            properties.SetFloat(ColumnHalfId, columnHalf);
            properties.SetFloat(CapTopId, capTop);
            properties.SetFloat(CapBottomId, capBottom);
            // Bant fazı board-uzayında: iki parçada bantlar örtüşür.
            properties.SetFloat(CenterYId, centerY);
            quad.SetPropertyBlock(properties);

            quad.enabled = true;
        }

        public void Hide()
        {
            quadTop.enabled = false;
            quadBottom.enabled = false;
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
