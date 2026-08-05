using TMPro;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Alt-orta aksiyon çubuğu: panel + 3 sprite buton (geri al / karıştır / +tüp),
    /// her butonda sağ-altta sayı rozeti (kalan hak). Koddan çizilen eski butonların
    /// (UndoButtonView / ButtonView / PilotNextButtonView) yerine geçer; görselleri
    /// Resources/UI'den asset olarak gelir — "her şey koddan çizilir" kuralının
    /// bilinçli istisnası (kullanıcı kararı, pop-up'lardaki gibi).
    ///
    /// Diğer görünümlerin aksine bu bileşen SAHNE NESNESİNE bağlıdır (kullanıcı
    /// kararı): buton GameObject'leri sahnede kalıcıdır ve BoxCollider2D'leri elle
    /// Inspector'dan eklenir. Kod ise sprite'ları yükler, çubuğu ekrana göre
    /// konumlar/ölçekler, collider'ı sprite'a göre boyutlar, rozet+sayıyı kurar ve
    /// tıklamayı çözer.
    ///
    /// Yerleşim responsive: panel her karede görüş alanının bir oranına ölçeklenir
    /// (tahtanın kendisi gibi). Buton GameObject'leri panelin ÇOCUĞUDUR; rozet ve
    /// sayı da butonun çocuğudur — hepsi tek ölçekle birlikte büyür/küçülür.
    ///
    /// Hak sistemi: her aksiyon level başına <see cref="MaxRights"/> (3) hakla
    /// sınırlı. <see cref="ResetRights"/> yeni level'de 3/3/3'e döndürür (restart
    /// değil — BoardView.StepPilot çağırır). <see cref="ConsumeRight"/> başarılı
    /// aksiyonda azaltır. Hak 0'da buton karartılır ve collider'ı kapatılır
    /// (gerçekten tıklanamaz). Tıklama girişi tek noktadan gelir
    /// (BoardView.HandleClick); BoardView <see cref="TryGetAction"/> ile sorar,
    /// aksiyonu çalıştırıp başarılıysa ConsumeRight'ı kendisi çağırır.
    /// </summary>
    public class ButtonBarView : MonoBehaviour
    {
        /// <summary>Çubuktaki üç aksiyon. Sıra <see cref="buttons"/> dizisiyle
        /// birebir: 0=Undo, 1=Shuffle, 2=AddTube (TryGetAction bu eşlemeyi kullanır).</summary>
        public enum ActionKind { Undo, Shuffle, AddTube }

        [Header("Sahne referansları (Inspector'dan bağla)")]
        [Tooltip("Geri al butonu. BoxCollider2D'yi elle ekle; sprite ve boyut koddan gelir.")]
        [SerializeField] private Transform undoButton;
        [Tooltip("Karıştır butonu. BoxCollider2D'yi elle ekle.")]
        [SerializeField] private Transform shuffleButton;
        [Tooltip("+Tüp butonu. BoxCollider2D'yi elle ekle.")]
        [SerializeField] private Transform addTubeButton;

        // Resources/UI sprite yolları (uzantısız). Panel bu bileşenin OLDUĞU
        // nesneye, butonlar çocuk nesnelerine giydirilir.
        private const string PanelPath = "UI/action_panel";
        private const string UndoPath = "UI/action_undo";
        private const string ShufflePath = "UI/action_shuffle";
        private const string AddTubePath = "UI/action_add_tube";
        private const string BadgePath = "UI/action_badge";

        // Çizim sırası: eski butonlarla aynı band (tüplerin ve akışın üstünde).
        private const int PanelSortingOrder = 100;
        private const int ButtonSortingOrder = 101;
        private const int BadgeSortingOrder = 102;
        private const int CountSortingOrder = 103;

        /// <summary>Her aksiyonun level başına hak sayısı (asset rozetindeki 3).</summary>
        private const int MaxRights = 3;

        /// <summary>Panelin görüş genişliğine oranı (bu kadarını kaplar).</summary>
        private const float WidthFraction = 0.9f;
        /// <summary>Panel alt kenarının ekran altından boşluğu (görüş yüksekliği oranı).</summary>
        private const float BottomMarginFraction = 0.03f;
        /// <summary>Butonların panel YARI-genişliğine göre yatay konumu (PPU'dan
        /// bağımsız: gerçek panel genişliğinden türetilir).</summary>
        private static readonly float[] ButtonXFraction = { -0.6f, 0f, 0.6f };

        // Rozet geometrisi (memory ölçümü): jeton çapı = buton çapının %32'si;
        // buton merkezinden sağ-alta ofset ≈ (+0.338, -0.338)·buton genişliği.
        private const float BadgeScaleOfButton = 0.32f;
        private const float BadgeOffsetFraction = 0.338f;

        // Hak > 0: normal; hak = 0: KOYU GRİ dim buton. Alfa yüksek (0.95): düşük
        // alfada arkadaki açık panel sızıp tint'i açardı (kullanıcı: arka planla
        // aynı renge yaklaşıyor) — koyu görünsün diye neredeyse opak.
        private static readonly Color EnabledTint = Color.white;
        private static readonly Color DisabledTint = new Color(0.28f, 0.28f, 0.30f, 0.95f);

        private SpriteRenderer panelRenderer;
        private Transform[] buttons;
        private SpriteRenderer[] buttonRenderers;
        private Collider2D[] colliders;
        private SpriteRenderer[] badges;
        private TextMeshPro[] counts;
        private int[] rights;
        private bool ready;

        private void Awake()
        {
            ready = BuildVisuals();
        }

        /// <summary>Referansları çözer, sprite'ları giydirir, rozet+sayıyı kurar,
        /// collider'ları bulur ve hakları 3/3/3'e ayarlar. Eksik referans/collider
        /// ölümcül değil: Console'a hata basılır, çubuk sessizce çalışmaz.</summary>
        private bool BuildVisuals()
        {
            if (undoButton == null || shuffleButton == null || addTubeButton == null)
            {
                Debug.LogError("ButtonBarView: buton referansları eksik (Inspector'dan bağla).");
                return false;
            }

            buttons = new[] { undoButton, shuffleButton, addTubeButton };

            panelRenderer = Dress(transform, PanelPath, PanelSortingOrder);
            if (panelRenderer == null) return false;

            var badgeSprite = Resources.Load<Sprite>(BadgePath);
            if (badgeSprite == null)
            {
                Debug.LogError($"ButtonBarView: {BadgePath} sprite bulunamadı " +
                               $"(Assets/Resources/{BadgePath}.png).");
                return false;
            }

            buttonRenderers = new SpriteRenderer[3];
            colliders = new Collider2D[3];
            badges = new SpriteRenderer[3];
            counts = new TextMeshPro[3];
            rights = new int[3];

            string[] paths = { UndoPath, ShufflePath, AddTubePath };
            for (int i = 0; i < 3; i++)
            {
                buttonRenderers[i] = Dress(buttons[i], paths[i], ButtonSortingOrder);
                if (buttonRenderers[i] == null) return false;

                colliders[i] = buttons[i].GetComponent<Collider2D>();
                if (colliders[i] == null)
                    Debug.LogError($"ButtonBarView: '{buttons[i].name}' üzerinde BoxCollider2D yok " +
                                   "(elle eklenmeli; tıklama collider'a bağlı).");
                else
                    colliders[i].offset = Vector2.zero;   // sprite pivot Center; kutu tam üstüne otursun

                CreateBadge(buttons[i], i, buttonRenderers[i].sprite.bounds.size.x, badgeSprite);
            }

            for (int i = 0; i < 3; i++)
            {
                rights[i] = MaxRights;
                RefreshButton(i);
            }

            return true;
        }

        /// <summary>Butonun sağ-altına sayı rozeti (jeton sprite'ı) ve üstüne TMP
        /// sayı kurar; ikisi de butonun çocuğudur (onunla ölçeklenir). Sayı asset'e
        /// gömülü değil — TMP ile yazılır ki 3→2→1→0 azalması kod tarafında olsun.</summary>
        private void CreateBadge(Transform button, int index, float buttonNativeW, Sprite badgeSprite)
        {
            float off = BadgeOffsetFraction * buttonNativeW;
            float badgeLocalW = BadgeScaleOfButton * buttonNativeW;   // rozetin buton-yerel boyu

            var badgeGo = new GameObject("Badge");
            badgeGo.transform.SetParent(button, false);
            badgeGo.transform.localPosition = new Vector3(off, -off, 0f);
            badgeGo.transform.localScale =
                Vector3.one * (badgeLocalW / badgeSprite.bounds.size.x);
            var badgeSr = badgeGo.AddComponent<SpriteRenderer>();
            badgeSr.sprite = badgeSprite;
            badgeSr.sortingOrder = BadgeSortingOrder;
            badges[index] = badgeSr;

            var countGo = new GameObject("Count");
            countGo.transform.SetParent(button, false);
            countGo.transform.localPosition = new Vector3(off, -off, 0f);
            var tmp = countGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableAutoSizing = true;   // rakam rozete sığacak kadar büyür
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 36f;
            tmp.rectTransform.sizeDelta = new Vector2(badgeLocalW, badgeLocalW) * 0.85f;
            tmp.sortingOrder = CountSortingOrder;
            counts[index] = tmp;
        }

        /// <summary>Verilen nesneye SpriteRenderer ekler (yoksa) ve Resources'tan
        /// sprite'ı yükleyip atar. Kullanıcı yalnız boş GameObject + collider
        /// oluşturur; görsel koddan gelir.</summary>
        private static SpriteRenderer Dress(Transform target, string spritePath, int sortingOrder)
        {
            var sprite = Resources.Load<Sprite>(spritePath);
            if (sprite == null)
            {
                Debug.LogError($"ButtonBarView: {spritePath} sprite bulunamadı " +
                               $"(Assets/Resources/{spritePath}.png).");
                return null;
            }

            var renderer = target.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = target.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        /// <summary>
        /// Çubuğu ekranın alt-ortasına, genişliği görüş alanının
        /// <see cref="WidthFraction"/>'ı olacak şekilde konumlar ve ölçekler.
        /// BoardView.ApplyLayout her yerleşim tazelemesinde çağırır (çubuk da
        /// görüş alanına bağlı — tahta gibi). Butonlar panelin çocuğu olduğundan
        /// tek ölçekle birlikte büyür/küçülür; collider'lar sprite'a göre boyutlanır.
        /// </summary>
        public void Layout(Camera cam)
        {
            if (!ready || cam == null) return;

            float viewH = cam.orthographicSize * 2f;
            float viewW = viewH * cam.aspect;
            Vector3 c = cam.transform.position;

            // Ölçeklenmemiş panel boyu (birim); PPU ne olursa olsun sprite.bounds
            // gerçek dünya boyunu verir, oran hesabı buna dayanır.
            Vector2 native = panelRenderer.sprite.bounds.size;
            float scale = WidthFraction * viewW / native.x;
            transform.localScale = Vector3.one * scale;

            float panelH = native.y * scale;
            float bottom = c.y - viewH * 0.5f;
            float centerY = bottom + BottomMarginFraction * viewH + panelH * 0.5f;
            transform.position = new Vector3(c.x, centerY, 0f);

            // Butonları panel-yerel konumlarına diz + collider'ı sprite'a boyutla.
            float halfWidth = native.x * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                buttons[i].localScale = Vector3.one;   // panel ölçeğini miras alsın
                buttons[i].localPosition = new Vector3(ButtonXFraction[i] * halfWidth, 0f, 0f);

                if (colliders[i] is BoxCollider2D box)
                    box.size = buttonRenderers[i].sprite.bounds.size;   // yerel; sprite'ı tam kaplar
            }
        }

        /// <summary>Tüm hakları level başı değerine (3/3/3) döndürür. Yeni level'e
        /// geçişte BoardView çağırır — RESTART değil (sayaçlar anlamlı kalsın).</summary>
        public void ResetRights()
        {
            if (!ready) return;
            for (int i = 0; i < 3; i++)
            {
                rights[i] = MaxRights;
                RefreshButton(i);
            }
        }

        /// <summary>Bir aksiyonun hakkını bir azaltır (BAŞARILI aksiyondan sonra
        /// BoardView çağırır). 0'a inince buton karartılır + tıklanamaz olur.</summary>
        public void ConsumeRight(ActionKind action)
        {
            if (!ready) return;
            int i = (int)action;
            if (rights[i] <= 0) return;
            rights[i]--;
            RefreshButton(i);
        }

        /// <summary>Rozet sayısını, dim tint'i ve collider durumunu hakka göre
        /// tazeler. Hak 0: gri tint + collider kapalı (gerçekten tıklanamaz).</summary>
        private void RefreshButton(int i)
        {
            bool enabled = rights[i] > 0;
            counts[i].text = rights[i].ToString();
            buttonRenderers[i].color = enabled ? EnabledTint : DisabledTint;
            if (colliders[i] != null) colliders[i].enabled = enabled;
        }

        /// <summary>
        /// Dünya noktası HAKKI OLAN bir butonun (aktif collider'lı) üstündeyse
        /// ilgili aksiyonu döndürür. BoardView tek giriş noktasından
        /// (HandleClick) çağırır; buton paneli tüplerin üstünde ve önceliklidir.
        /// </summary>
        public bool TryGetAction(Vector3 worldPoint, out ActionKind action)
        {
            action = default;
            if (!ready) return false;

            for (int i = 0; i < 3; i++)
            {
                if (rights[i] > 0 && colliders[i] != null && colliders[i].enabled
                    && colliders[i].OverlapPoint(worldPoint))
                {
                    action = (ActionKind)i;   // dizi sırası enum sırasıyla birebir
                    return true;
                }
            }

            return false;
        }
    }
}
