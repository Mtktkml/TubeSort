using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TubeSort.Game
{
    /// <summary>
    /// Genel amaçlı pop-up: karartma + panel + başlık şeridi + mesaj + rozetli
    /// aksiyon butonları. Görseller Resources/UI'daki Kenney asset'lerinden
    /// kurulur (koddan çizim İSTİSNASI: kullanıcı kararı, bkz. CLAUDE.md).
    /// Çıkmaz pop-up'ı bunu kullanır; kazanma pop-up'ı da aynı bileşeni
    /// farklı içerikle kuracak.
    ///
    /// Tıklama yakalama BoardView'dadır (buton desenleriyle aynı): pop-up
    /// görünürken BoardView TÜM dokunuşları HandleClick'imize yönlendirir —
    /// butona denk gelmeyen dokunuş yutulur (hamle kilidinin kendisi).
    /// Collider kullanılmaz; butonlar yerel dikdörtgen testiyle bulunur.
    /// </summary>
    public class PopupView : MonoBehaviour
    {
        /// <summary>Bir pop-up butonu: etiket + ikon + reklam rozeti + tıklama işi.</summary>
        public struct PopupAction
        {
            public string Label;
            public string IconPath;   // Resources yolu (ör. "UI/icon_undo")
            public bool AdBadge;      // sağda "reklam izle" video rozeti
            public Action OnClick;
        }

        // Çizim sırası: HUD butonları 100, level başlığı 101 — pop-up hepsinin
        // üstünde kendi bandında (200+).
        private const int OverlayOrder = 200;
        private const int PanelOrder = 201;
        private const int BannerOrder = 202;
        private const int ButtonOrder = 202;
        private const int IconOrder = 203;
        private const int BadgeIconOrder = 204;
        private const int TextOrder = 205;

        // Buton zemini krem sprite'a sıcak kahve tint: panel de krem olduğundan
        // tintsiz buton panelde kayboluyordu (krem üstüne krem etiket okunmuyordu).
        private static readonly Color ButtonTint = new Color(0.63f, 0.475f, 0.345f, 1f);
        // Basış geri bildirimi: tabanın koyulaştırılmışı (basılı görsel asset'te yok).
        private const float PressDarken = 0.75f;
        // Video glifi koyu kahve: rozetin krem merkezinde beyaz glif okunmuyordu.
        private static readonly Color VideoIconTint = new Color(0.42f, 0.30f, 0.19f, 1f);

        // Yerleşim ölçüleri (dünya birimi). Panel genişliği kameradan hesaplanır;
        // buton/pay ölçüleri sabit — telefon dikey ekranda okunaklı boylar.
        private const float MaxPanelWidth = 6f;
        private const float PanelWidthFraction = 0.85f;
        private const float ButtonHeight = 0.95f;
        private const float ButtonGap = 0.28f;
        private const float SidePadding = 0.55f;
        private const float TopPadding = 0.85f;    // banner'ın panele binen payı + nefes
        private const float MessageHeight = 0.55f;
        private const float BottomPadding = 0.5f;

        private Camera viewCamera;
        private SpriteRenderer overlay;
        private Texture2D overlayTexture;
        private Sprite overlaySprite;

        // Buton vuruş testi: yerel dikdörtgen + iş + basış geri bildirimi için
        // görsel. BaseColor kurulumda saklanır: basış rengi HEP bu tabandan
        // türetilip HEP buna döner — basış anındaki renk taban alınsaydı hızlı
        // basışlar kararmayı üst üste biriktirirdi (yaşandı).
        private struct ButtonHit
        {
            public Rect LocalRect;
            public Action OnClick;
            public SpriteRenderer Background;
            public Color BaseColor;
        }

        private readonly List<ButtonHit> buttons = new List<ButtonHit>();
        private Coroutine appearRoutine;

        /// <summary>Pop-up ekranda mı? BoardView dokunuş yönlendirmesi buna bakar.</summary>
        public bool Visible => gameObject.activeSelf;

        /// <summary>
        /// Pop-up'ı verilen içerikle kurar (gizli başlar). Görseller Resources'tan
        /// yüklenir; eksik asset parlak pembe yer tutucuyla belli olur (sessiz
        /// bozulma yerine — ColorPalette ile aynı felsefe).
        /// </summary>
        public void Initialize(Camera camera, string title, string message,
            IReadOnlyList<PopupAction> actions)
        {
            viewCamera = camera;

            Sprite panelSprite = LoadSprite("UI/panel_brown");
            Sprite bannerSprite = LoadSprite("UI/banner_hanging");
            Sprite buttonSprite = LoadSprite("UI/button_brown");
            Sprite badgeSprite = LoadSprite("UI/round_brown");
            Sprite videoSprite = LoadSprite("UI/icon_video");

            float panelWidth = Mathf.Min(MaxPanelWidth,
                CameraViewSize().x * PanelWidthFraction);
            float buttonWidth = panelWidth - 2f * SidePadding;
            float panelHeight = TopPadding + MessageHeight + ButtonGap
                + actions.Count * ButtonHeight
                + Mathf.Max(0, actions.Count - 1) * ButtonGap
                + BottomPadding;

            BuildOverlay();
            BuildPanel(panelSprite, panelWidth, panelHeight);
            BuildBanner(bannerSprite, title, panelWidth, panelHeight);
            BuildMessage(message, panelWidth, panelHeight);
            BuildButtons(buttonSprite, badgeSprite, videoSprite,
                actions, buttonWidth, panelHeight);

            gameObject.SetActive(false);   // yalnız Show ile görünür
        }

        /// <summary>Pop-up'ı kameranın ortasında gösterir (küçük büyüme animasyonuyla).</summary>
        public void Show()
        {
            Vector3 cam = viewCamera.transform.position;
            transform.position = new Vector3(cam.x, cam.y, 0f);

            // Karartma her gösterimde güncel görüş alanını kaplar (aspect
            // değişmiş olabilir — editörde pencere, cihazda döndürme).
            Vector2 view = CameraViewSize();
            overlay.transform.localScale = new Vector3(view.x + 2f, view.y + 2f, 1f);

            // Her gösterim temiz sayfa: basış karartması kalmış olabilir
            // (basış coroutine'i pop-up kapanınca yarıda ölür).
            foreach (ButtonHit hit in buttons)
                if (hit.Background != null) hit.Background.color = hit.BaseColor;

            gameObject.SetActive(true);
            if (appearRoutine != null) StopCoroutine(appearRoutine);
            appearRoutine = StartCoroutine(AppearPunch());
        }

        public void Hide()
        {
            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
                appearRoutine = null;
            }
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Pop-up görünürken BoardView her dokunuşu buraya verir. Butona denk
        /// gelen dokunuş işini çalıştırır; gerisi yutulur (hamle kilidi).
        /// </summary>
        public void HandleClick(Vector3 worldPoint)
        {
            // Kök ölçeklenebilir (belirme animasyonu) ama dönmez; yerel nokta
            // için çıkarma yeterli, ölçek farkı vuruş payı içinde kalır.
            Vector2 local = worldPoint - transform.position;

            foreach (ButtonHit hit in buttons)
            {
                if (!hit.LocalRect.Contains(local)) continue;

                StartCoroutine(PressFlash(hit));
                hit.OnClick?.Invoke();
                return;
            }
        }

        /// <summary>Belirme: hafif küçükten tam boya yumuşak büyüme (toon dokunuşu).</summary>
        private IEnumerator AppearPunch()
        {
            const float duration = 0.15f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, t);
                yield return null;
            }

            transform.localScale = Vector3.one;
            appearRoutine = null;
        }

        /// <summary>Basış geri bildirimi: buton kısaca koyulaşır (basılı görsel
        /// varyantı asset'te yok; karartma onun yerini tutar). Renkler hep
        /// SAKLI tabandan türetilir/tabana döner: anlık renkten türetmek, üst
        /// üste basışlarda kararmayı biriktiriyordu.</summary>
        private IEnumerator PressFlash(ButtonHit hit)
        {
            if (hit.Background == null) yield break;

            Color baseColor = hit.BaseColor;
            hit.Background.color = new Color(baseColor.r * PressDarken,
                baseColor.g * PressDarken, baseColor.b * PressDarken, baseColor.a);
            yield return new WaitForSeconds(0.08f);
            if (hit.Background != null) hit.Background.color = baseColor;
        }

        // ── Kurulum parçaları ──────────────────────────────────────────────

        private void BuildOverlay()
        {
            // 1×1 beyaz doku, PPU 1 → 1 birimlik sprite; ölçekle ekranı kaplar.
            overlayTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            overlayTexture.SetPixel(0, 0, Color.white);
            overlayTexture.Apply();
            overlaySprite = Sprite.Create(overlayTexture,
                new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

            var go = new GameObject("Overlay");
            go.transform.SetParent(transform, false);
            overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = overlaySprite;
            overlay.color = new Color(0f, 0f, 0f, 0.6f);
            overlay.sortingOrder = OverlayOrder;
        }

        private void BuildPanel(Sprite sprite, float width, float height)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;   // 9-slice: border meta'da
            renderer.size = new Vector2(width, height);
            renderer.sortingOrder = PanelOrder;
        }

        private void BuildBanner(Sprite bannerSprite, string title,
            float panelWidth, float panelHeight)
        {
            // Banner panelin üst kenarına biner: merkez üst kenarın hafif üstünde.
            float bannerWidth = panelWidth * 0.82f;
            float scale = bannerSprite != null
                ? bannerWidth / bannerSprite.bounds.size.x : 1f;
            float bannerHeight = bannerSprite != null
                ? bannerSprite.bounds.size.y * scale : 1f;
            float y = panelHeight * 0.5f + 0.12f;

            var go = new GameObject("Banner");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = bannerSprite;
            renderer.sortingOrder = BannerOrder;

            var textGo = new GameObject("Title");
            textGo.transform.SetParent(transform, false);
            // Başlık KIRMIZI BANDIN ortasına oturur, sprite merkezine değil:
            // görselin üstünde askı pimleri var. Ofset görselden ÖLÇÜLDÜ:
            // bant orta sütunda 19-120. satırlar, merkezi 69.5; sprite merkezi
            // 64 → fark 5.5/128 ≈ 0.043 yükseklik. (0.1 denendi: çok aşağı.)
            textGo.transform.localPosition = new Vector3(0f, y - bannerHeight * 0.043f, 0f);

            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = title;
            tmp.fontSize = 3.6f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 0.96f, 0.88f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(bannerWidth, 1f);
            tmp.sortingOrder = TextOrder;
        }

        private void BuildMessage(string message, float panelWidth, float panelHeight)
        {
            var go = new GameObject("Message");
            go.transform.SetParent(transform, false);
            float y = panelHeight * 0.5f - TopPadding - MessageHeight * 0.5f;
            go.transform.localPosition = new Vector3(0f, y, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = message;
            tmp.fontSize = 3f;
            tmp.fontStyle = FontStyles.Bold;   // krem panelde silik kalmasın
            tmp.color = new Color(0.36f, 0.26f, 0.16f, 1f);   // koyu kahve
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(panelWidth - 2f * SidePadding, MessageHeight);
            tmp.sortingOrder = TextOrder;
        }

        private void BuildButtons(Sprite buttonSprite, Sprite badgeSprite,
            Sprite videoSprite, IReadOnlyList<PopupAction> actions,
            float buttonWidth, float panelHeight)
        {
            float firstY = panelHeight * 0.5f - TopPadding - MessageHeight
                - ButtonGap - ButtonHeight * 0.5f;

            for (int i = 0; i < actions.Count; i++)
            {
                PopupAction action = actions[i];
                float y = firstY - i * (ButtonHeight + ButtonGap);

                var go = new GameObject($"Button_{action.Label}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, y, 0f);

                var bg = go.AddComponent<SpriteRenderer>();
                bg.sprite = buttonSprite;
                bg.drawMode = SpriteDrawMode.Sliced;
                bg.size = new Vector2(buttonWidth, ButtonHeight);
                bg.color = ButtonTint;   // krem panelden ayrışsın, etiket okunsun
                bg.sortingOrder = ButtonOrder;

                // Sol: aksiyon ikonu (beyaz Kenney glifi).
                if (!string.IsNullOrEmpty(action.IconPath))
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(go.transform, false);
                    iconGo.transform.localPosition = new Vector3(
                        -buttonWidth * 0.5f + 0.55f, 0f, 0f);
                    iconGo.transform.localScale = Vector3.one * 1.15f;

                    var icon = iconGo.AddComponent<SpriteRenderer>();
                    icon.sprite = LoadSprite(action.IconPath);
                    icon.sortingOrder = IconOrder;
                }

                // Orta: etiket (ikona yer bırakmak için hafif sağda).
                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(go.transform, false);
                labelGo.transform.localPosition = new Vector3(0.1f, 0f, 0f);

                var label = labelGo.AddComponent<TextMeshPro>();
                label.text = action.Label;
                label.fontSize = 2.7f;
                label.fontStyle = FontStyles.Bold;
                label.color = new Color(1f, 0.95f, 0.85f, 1f);
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta = new Vector2(buttonWidth - 1.6f, ButtonHeight);
                label.sortingOrder = TextOrder;

                // Sağ: "reklam izle" rozeti (yuvarlak zemin + video glifi).
                // Şimdilik süs — tıklama butonun tamamıdır, reklam akışı stub.
                if (action.AdBadge)
                {
                    var badgeGo = new GameObject("AdBadge");
                    badgeGo.transform.SetParent(go.transform, false);
                    badgeGo.transform.localPosition = new Vector3(
                        buttonWidth * 0.5f - 0.5f, 0f, 0f);
                    badgeGo.transform.localScale = Vector3.one * 0.55f;

                    var badge = badgeGo.AddComponent<SpriteRenderer>();
                    badge.sprite = badgeSprite;
                    badge.sortingOrder = IconOrder;

                    var videoGo = new GameObject("VideoIcon");
                    videoGo.transform.SetParent(badgeGo.transform, false);
                    videoGo.transform.localScale = Vector3.one * 1.3f;

                    var video = videoGo.AddComponent<SpriteRenderer>();
                    video.sprite = videoSprite;
                    video.color = VideoIconTint;   // krem rozet merkezinde okunsun
                    video.sortingOrder = BadgeIconOrder;
                }

                buttons.Add(new ButtonHit
                {
                    LocalRect = new Rect(-buttonWidth * 0.5f, y - ButtonHeight * 0.5f,
                        buttonWidth, ButtonHeight),
                    OnClick = action.OnClick,
                    Background = bg,
                    BaseColor = ButtonTint,
                });
            }
        }

        private static Sprite LoadSprite(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
                Debug.LogError($"Pop-up görseli bulunamadı: Resources/{path}");
            return sprite;
        }

        private Vector2 CameraViewSize()
        {
            float height = viewCamera.orthographicSize * 2f;
            return new Vector2(height * viewCamera.aspect, height);
        }

        /// <summary>Koddan üretilen doku/sprite'lar sahneyle birlikte temizlenir.</summary>
        private void OnDestroy()
        {
            if (overlaySprite != null) Destroy(overlaySprite);
            if (overlayTexture != null) Destroy(overlayTexture);
        }
    }
}
