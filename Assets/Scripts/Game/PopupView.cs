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

        // Kutlama (festive) modu: kazanma pop-up'ı çıkmazdan daha CANLI olsun
        // (kullanıcı isteği) — yıldız bandı + zıplamalı belirme + sürekli nabız
        // + gösterim anında konfeti patlaması.
        // Bant büyüdükçe mesaj/butonlar ContentTop üzerinden orantılı kayar.
        private const float StarBandHeight = 1.1f;
        private static readonly Color StarTint = new Color(1f, 0.84f, 0.25f, 1f);
        // Kazanılmamış yıldız: soluk silüet (yıldız sayısı skora bağlı olacak).
        private static readonly Color DimStarTint = new Color(0.55f, 0.50f, 0.44f, 0.9f);

        // İstatistik satırı (hamle/süre çipleri): yıldız bandı ile mesaj
        // arasındaki boşluğu doldurur; metinler SetResults ile yazılır.
        private const float StatsRowHeight = 0.7f;
        private const float StatsGapAbove = 0.05f;
        private static readonly Color StatChipTint = new Color(0.86f, 0.72f, 0.55f, 1f);

        // Konfeti: panel üstünden savrulan renkli pullar (koddan üretilir,
        // asset yok). Renkler oyunun parlak toon ailesinden.
        private const int ConfettiCount = 30;
        private const int ConfettiOrder = 206;   // pop-up'taki her şeyin önünde
        private const float ConfettiGravity = 9f;
        private static readonly Color[] ConfettiColors =
        {
            new Color(0.92f, 0.30f, 0.26f), new Color(0.98f, 0.62f, 0.22f),
            new Color(0.99f, 0.85f, 0.30f), new Color(0.42f, 0.80f, 0.40f),
            new Color(0.36f, 0.62f, 0.92f), new Color(0.65f, 0.48f, 0.85f),
        };

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

        // Kutlama yıldızı: taban ölçek/eğim StarDance koreografisinde kullanılır;
        // Renderer, kazanıldı/soluk boyaması için (SetResults).
        private struct StarDeco
        {
            public Transform Star;
            public SpriteRenderer Renderer;
            public float BaseScale;
            public float BaseTilt;
        }

        // Konfeti pulu: hız/dönüş/ömür her patlamada yeniden zarlanır.
        private struct ConfettiPiece
        {
            public Transform T;
            public SpriteRenderer R;
            public Vector2 Vel;
            public float Spin;
            public float Age;
            public float Life;
        }

        private readonly List<ButtonHit> buttons = new List<ButtonHit>();
        private readonly List<StarDeco> stars = new List<StarDeco>();
        private ConfettiPiece[] confetti;
        private TextMeshPro statLeft;    // "Hamle: X" çipi (kutlama)
        private TextMeshPro statRight;   // "Süre: X" çipi (kutlama)
        private bool festive;
        private float panelHalfHeight;
        private Coroutine appearRoutine;
        private Coroutine starRoutine;
        private Coroutine confettiRoutine;

        /// <summary>Pop-up ekranda mı? BoardView dokunuş yönlendirmesi buna bakar.</summary>
        public bool Visible => gameObject.activeSelf;

        /// <summary>
        /// Pop-up'ı verilen içerikle kurar (gizli başlar). Görseller Resources'tan
        /// yüklenir; eksik asset parlak pembe yer tutucuyla belli olur (sessiz
        /// bozulma yerine — ColorPalette ile aynı felsefe).
        /// </summary>
        /// <param name="titleDrop">Başlığın banner merkezine göre DÜŞEY ofseti
        /// (banner yüksekliği oranı, + = aşağı). Kırmızı bandın merkezi görselden
        /// görsele değişir; piksel ölçümüyle bulunur: banner_hanging +0.043
        /// (bant 19-120, merkez 69.5 vs 64), banner_ribbon -0.035 (bant 13-106,
        /// merkez 59.5 vs 64).</param>
        public void Initialize(Camera camera, string title, string message,
            IReadOnlyList<PopupAction> actions,
            string bannerPath = "UI/banner_hanging", bool festive = false,
            float titleDrop = 0.043f)
        {
            viewCamera = camera;
            this.festive = festive;

            Sprite panelSprite = LoadSprite("UI/panel_brown");
            Sprite bannerSprite = LoadSprite(bannerPath);
            Sprite buttonSprite = LoadSprite("UI/button_brown");
            Sprite badgeSprite = LoadSprite("UI/round_brown");
            Sprite videoSprite = LoadSprite("UI/icon_video");

            float panelWidth = Mathf.Min(MaxPanelWidth,
                CameraViewSize().x * PanelWidthFraction);
            float buttonWidth = panelWidth - 2f * SidePadding;
            float panelHeight = ContentTop + MessageHeight + ButtonGap
                + actions.Count * ButtonHeight
                + Mathf.Max(0, actions.Count - 1) * ButtonGap
                + BottomPadding;

            panelHalfHeight = panelHeight * 0.5f;

            BuildOverlay();
            BuildPanel(panelSprite, panelWidth, panelHeight);
            BuildBanner(bannerSprite, title, panelWidth, panelHeight, titleDrop);
            if (festive)
            {
                BuildStars(panelHeight);
                BuildStats(buttonSprite, panelWidth, panelHeight);
                BuildConfetti();   // overlaySprite'ı kullanır: BuildOverlay sonrası
            }
            BuildMessage(message, panelWidth, panelHeight);
            BuildButtons(buttonSprite, badgeSprite, videoSprite,
                actions, buttonWidth, panelHeight);

            gameObject.SetActive(false);   // yalnız Show ile görünür
        }

        /// <summary>Panelin üst kenarından içeriğin (mesajın) başladığı yere
        /// kadarki pay: kutlamada araya yıldız bandı + istatistik satırı girer.</summary>
        private float ContentTop => TopPadding
            + (festive ? StarBandHeight + StatsGapAbove + StatsRowHeight : 0f);

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

            // Kutlama koreografisi her gösterimde baştan oynar (taze kutlama).
            if (festive)
            {
                if (starRoutine != null) StopCoroutine(starRoutine);
                starRoutine = StartCoroutine(StarDance());
                if (confettiRoutine != null) StopCoroutine(confettiRoutine);
                confettiRoutine = StartCoroutine(ConfettiBurst());
            }
        }

        public void Hide()
        {
            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
                appearRoutine = null;
            }
            if (starRoutine != null)
            {
                StopCoroutine(starRoutine);
                starRoutine = null;
            }
            if (confettiRoutine != null)
            {
                StopCoroutine(confettiRoutine);
                confettiRoutine = null;
            }
            if (confetti != null)
                foreach (ConfettiPiece piece in confetti)
                    if (piece.T != null) piece.T.gameObject.SetActive(false);
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

        /// <summary>Belirme. Normal: hafif küçükten tam boya yumuşak büyüme.
        /// Kutlama: daha küçükten TAŞARAK zıplama (ease-out-back) — kazanma
        /// anı çıkmazdan daha coşkulu okunsun.</summary>
        private IEnumerator AppearPunch()
        {
            float duration = festive ? 0.24f : 0.15f;
            float from = festive ? 0.6f : 0.85f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = festive ? EaseOutBack(t) : Mathf.SmoothStep(0f, 1f, t);
                transform.localScale = Vector3.one * Mathf.LerpUnclamped(from, 1f, eased);
                yield return null;
            }

            transform.localScale = Vector3.one;
            appearRoutine = null;
        }

        /// <summary>Sonuna doğru hafif taşıp (tepe ~1.1) geri oturan yay
        /// eğrisi, 0→1. Zıplamalı belirme ve yıldız popları bunu kullanır.</summary>
        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>Yıldız koreografisi: soldan sağa sırayla zıplayarak belirme,
        /// ardından süresiz nazik nabız + salınım (fazlar yıldız başına kayık,
        /// mekanik durmasın). Pop-up kapanınca Hide durdurur.</summary>
        private IEnumerator StarDance()
        {
            const float startDelay = 0.1f;   // panel otursun, sonra yıldızlar
            const float stagger = 0.12f;     // yıldızlar arası gecikme
            const float pop = 0.28f;         // tek yıldızın belirme süresi
            float elapsed = 0f;

            while (true)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < stars.Count; i++)
                {
                    StarDeco deco = stars[i];
                    float local = elapsed - startDelay - i * stagger;

                    float scale;
                    float tiltDeg = deco.BaseTilt;   // taç eğimi hep korunur
                    if (local <= 0f)
                    {
                        scale = 0f;   // sırası gelmedi: görünmez
                    }
                    else if (local < pop)
                    {
                        scale = EaseOutBack(local / pop);
                    }
                    else
                    {
                        scale = 1f + 0.06f * Mathf.Sin(elapsed * 2.8f + i * 2.1f);
                        tiltDeg += 7f * Mathf.Sin(elapsed * 1.9f + i * 1.3f);
                    }

                    deco.Star.localScale = Vector3.one * (deco.BaseScale * scale);
                    deco.Star.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
                }

                yield return null;
            }
        }

        /// <summary>Konfeti patlaması: pullar panelin üst bölgesinden yukarı ve
        /// yanlara fırlar, yerçekimiyle düşerken döner, ömrünün son üçte
        /// birinde solup söner. Her gösterimde yeniden zarlanıp savrulur.</summary>
        private IEnumerator ConfettiBurst()
        {
            for (int i = 0; i < confetti.Length; i++)
            {
                ConfettiPiece piece = confetti[i];
                piece.T.localPosition = new Vector3(
                    UnityEngine.Random.Range(-0.5f, 0.5f),
                    panelHalfHeight + UnityEngine.Random.Range(-0.2f, 0.3f), 0f);
                piece.T.localRotation = Quaternion.Euler(0f, 0f,
                    UnityEngine.Random.Range(0f, 360f));
                // Dikdörtgen pul: dönerken kağıt konfeti gibi parıldar.
                piece.T.localScale = new Vector3(
                    UnityEngine.Random.Range(0.10f, 0.16f),
                    UnityEngine.Random.Range(0.05f, 0.09f), 1f);
                piece.Vel = new Vector2(
                    UnityEngine.Random.Range(-3.4f, 3.4f),
                    UnityEngine.Random.Range(2.5f, 5.5f));
                piece.Spin = UnityEngine.Random.Range(-540f, 540f);
                piece.Age = 0f;
                piece.Life = UnityEngine.Random.Range(0.9f, 1.5f);
                piece.R.color = ConfettiColors[
                    UnityEngine.Random.Range(0, ConfettiColors.Length)];
                piece.T.gameObject.SetActive(true);
                confetti[i] = piece;
            }

            bool anyAlive = true;
            while (anyAlive)
            {
                anyAlive = false;
                float dt = Time.deltaTime;

                for (int i = 0; i < confetti.Length; i++)
                {
                    ConfettiPiece piece = confetti[i];
                    if (!piece.T.gameObject.activeSelf) continue;

                    piece.Age += dt;
                    if (piece.Age >= piece.Life)
                    {
                        piece.T.gameObject.SetActive(false);
                        confetti[i] = piece;
                        continue;
                    }

                    piece.Vel.y -= ConfettiGravity * dt;
                    piece.T.localPosition +=
                        new Vector3(piece.Vel.x, piece.Vel.y, 0f) * dt;
                    piece.T.localRotation = Quaternion.Euler(0f, 0f,
                        piece.T.localRotation.eulerAngles.z + piece.Spin * dt);

                    // Ömrün son ~üçte birinde sol.
                    float fade = Mathf.Clamp01(
                        (piece.Life - piece.Age) / (piece.Life * 0.35f));
                    Color c = piece.R.color;
                    c.a = fade;
                    piece.R.color = c;

                    confetti[i] = piece;
                    anyAlive = true;
                }

                yield return null;
            }

            confettiRoutine = null;
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
            float panelWidth, float panelHeight, float titleDrop)
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
            // bandın merkezi görselden görsele değişir (askı pimi / kıvrım payı).
            // titleDrop banner başına piksel ölçümüyle verilir (bkz. Initialize).
            textGo.transform.localPosition = new Vector3(0f, y - bannerHeight * titleDrop, 0f);

            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = title;
            tmp.fontSize = 3.6f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 0.96f, 0.88f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(bannerWidth, 1f);
            tmp.sortingOrder = TextOrder;
        }

        /// <summary>Kutlama yıldız bandı: banner ile mesaj arasında üç altın
        /// yıldız, SIKI taç dizilimi — yanlar ortadakine neredeyse üst üste
        /// binecek kadar sokulu, hafif aşağıda ve dışa yatık (kullanıcı isteği).
        /// Görünür hâle gelmeleri StarDance'te — burada yalnız kurulur.</summary>
        private void BuildStars(float panelHeight)
        {
            Sprite starSprite = LoadSprite("UI/icon_star");
            // Bant banner'a sokulur (üst payın yarısı): yıldızlar yukarıda dursun.
            float bandY = panelHeight * 0.5f - TopPadding * 0.5f - StarBandHeight * 0.5f;

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.72f;                 // sokulu (binme korunur)
                float baseScale = i == 1 ? 2.8f : 2.05f;   // orta yıldız büyük
                float yOffset = i == 1 ? 0.12f : -0.1f;    // yanlar hafif aşağıda
                float baseTilt = (1 - i) * 14f;            // sol +14°, sağ -14° dışa yatık

                var go = new GameObject($"Star_{i}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(x, bandY + yOffset, 0f);
                go.transform.localScale = Vector3.zero;    // StarDance büyütür

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = starSprite;
                renderer.color = StarTint;
                // Orta yıldız önde: binen kanatlar onun altına girer.
                renderer.sortingOrder = i == 1 ? IconOrder + 1 : IconOrder;

                stars.Add(new StarDeco
                {
                    Star = go.transform, Renderer = renderer,
                    BaseScale = baseScale, BaseTilt = baseTilt,
                });
            }
        }

        /// <summary>İstatistik çipleri: yıldız bandının altında yan yana iki
        /// küçük plaka (hamle / süre) — pop-up'ın ortası boş kalmasın ve skor
        /// bilgisine yer açılsın diye. Metinler SetResults ile yazılır.</summary>
        private void BuildStats(Sprite chipSprite, float panelWidth, float panelHeight)
        {
            float rowY = panelHeight * 0.5f - TopPadding * 0.5f - StarBandHeight
                - StatsGapAbove - StatsRowHeight * 0.5f;
            const float chipGap = 0.25f;
            float chipWidth = (panelWidth - 2f * SidePadding - chipGap) * 0.5f;

            statLeft = BuildStatChip(chipSprite, "Hamle: -",
                new Vector3(-(chipWidth + chipGap) * 0.5f, rowY, 0f), chipWidth);
            statRight = BuildStatChip(chipSprite, "Süre: -",
                new Vector3((chipWidth + chipGap) * 0.5f, rowY, 0f), chipWidth);
        }

        private TextMeshPro BuildStatChip(Sprite chipSprite, string text,
            Vector3 position, float width)
        {
            var go = new GameObject("StatChip");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;

            var bg = go.AddComponent<SpriteRenderer>();
            bg.sprite = chipSprite;
            bg.drawMode = SpriteDrawMode.Sliced;
            bg.size = new Vector2(width, StatsRowHeight);
            bg.color = StatChipTint;   // butonlardan açık: tıklanabilir sanılmasın
            bg.sortingOrder = ButtonOrder;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);

            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 2.3f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.36f, 0.26f, 0.16f, 1f);   // koyu kahve
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(width - 0.2f, StatsRowHeight);
            tmp.sortingOrder = TextOrder;
            return tmp;
        }

        /// <summary>Kutlama sonuçlarını yazar (gösterimden ÖNCE çağrılır):
        /// kazanılan yıldız sayısı (0-3; kalanlar soluk silüet — soldan sağa
        /// dolar) ve istatistik metinleri. Yıldız sayısı ileride hamle/süre
        /// skorundan türeyecek; şimdilik çağıran yer tutucu vererek yerleşimi
        /// doğrular.</summary>
        public void SetResults(int earnedStars, string leftStat, string rightStat)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                StarDeco deco = stars[i];
                if (deco.Renderer != null)
                    deco.Renderer.color = i < earnedStars ? StarTint : DimStarTint;
            }

            if (statLeft != null) statLeft.text = leftStat;
            if (statRight != null) statRight.text = rightStat;
        }

        /// <summary>Konfeti pullarını kurar (gizli): 1×1 beyaz sprite, tint ile
        /// renklenir. Savrulma değerleri her patlamada ConfettiBurst'te zarlanır.</summary>
        private void BuildConfetti()
        {
            confetti = new ConfettiPiece[ConfettiCount];
            for (int i = 0; i < ConfettiCount; i++)
            {
                var go = new GameObject($"Confetti_{i}");
                go.transform.SetParent(transform, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = overlaySprite;
                renderer.sortingOrder = ConfettiOrder;

                go.SetActive(false);
                confetti[i] = new ConfettiPiece { T = go.transform, R = renderer };
            }
        }

        private void BuildMessage(string message, float panelWidth, float panelHeight)
        {
            var go = new GameObject("Message");
            go.transform.SetParent(transform, false);
            float y = panelHeight * 0.5f - ContentTop - MessageHeight * 0.5f;
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
            float firstY = panelHeight * 0.5f - ContentTop - MessageHeight
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
