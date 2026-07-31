# Durum / Devam Notu — 31 Tem 2026

## Kapanan iş: dökme animasyonu + kap≥5 drain sorunu ✅

Kap≥5 drain sorunu (sıvı boşalmayıp aniden yok olma) kökten çözüldü. İki dökme
modeli ayrı branch'lerde denendi, kullanıcı ikisini de oynayıp **fiziksel eğim**
modelini seçti (`feature/pour-tilt-to-lip` → master'a merge edildi; ölçülü-eğim
alternatifi `feature/levels-two-empty` silindi).

Seçilen model (ayrıntı CLAUDE.md "Dökme animasyonu"):
- Tüp, sıvıyı dudağın 1.05 payıyla üstüne taşıyacak kadar eğilir; az sıvıda açı
  artar (~50°→88°, `MaxPourAngle` shader kelepçesiyle twin).
- Boşalma zamanlayıcıyla (gating yok) → donma imkânsız, akışla birlikte biter.
- Dökme sırasında açı fill'i birebir izler (`MoveTowards`) → akış kolonu sıvıya
  hep bitişik.
- Shader'da yüzey + katman sınırları hacim-korumalı → alt katmanlar dik açıda
  incelmez, "alttaki renk de dökülüyor" yanılsaması yok.

Level içeriği işi de master'da: hep 2 boş tüp + zorluk (8,7,2)'ye uzatıldı,
`pilot_levels.json` 30 tahta, skor monoton.

## Kapanan iş: tıklama alanına yaka dahil ✅

Yaka tıklaması master'da (`5d8ca82`): kaba collider + `ContainsPoint`'te
gövde ∪ yaka stadyum SDF'i, görselden taşmadan.

## Kapanan iş: çıkmaz pop-up'ı ✅

Görseller Kenney asset'lerinden (KODDAN ÇİZİM DEĞİL — kullanıcı kararı;
`Assets/Resources/UI/`, CC0). `PopupView` GENEL bileşen: karartma + panel +
banner + rozetli butonlar — kazanma pop-up'ı da aynısını kullanacak. Çıkmazda
pop-up açılır, TÜM dokunuşlar yutulur; kurtarma: Geri Al / +1 Tüp / Baştan Al
(reklam rozeti şimdilik süs/stub). Çözülebilirlik HAMLE ANINDA cache'lenir
(`boardUnsolvable`): çıkmaz veride TryPour + tüp seçimi kilitli — Geri Al
garantili kurtarır. Metinlerde ğ/İ/Ş yok (fontta gömülü değil). ÖNEMLİ:
test tahtaları ÇÖZÜLEBİLİR kurulmalı (renk toplamları kapasiteyi doldurmalı),
yoksa kilit testi baştan engeller.

## Sıradaki iş: kazanma pop-up'ı

PopupView hazır: banner_ribbon + icon_star/trophy asset'leri de Resources'ta.
İş büyük ölçüde içerik + akış: çözülünce AutoAdvance yerine/öncesinde pop-up
("Tebrikler" yerine ğ/İ/Ş içermeyen metin!), sonraki level butonu.

## Büyük yol haritası (mentör — 4 iş)

1. **Level içeriği: hep 2 boş + zorluk** ✅ (master'da)
2. **Kazanma pop-up'ı** ("Tebrikler, sonraki level") ← tıklama işinden sonra
3. **Çıkmaz pop-up'ı + hamle kilidi + "reklam izle"** (undo/+tüp/refresh) — pop-up
   altyapısını 2'den yeniden kullanır
4. **(Park) Yeni asset entegrasyonu** — asset gelince görsel katman yenilenir

## Notlar
- Python scriptlerini (`Tools/SolverBenchmark/*.py`) **kullanıcı kendi koşar**; düzenle ama çalıştırma.
- Animasyon süreleri Inspector `[SerializeField]`; kullanıcı test için yavaşlatır — `BoardView.cs` commit'lemeden önce diff'e bak, varsayılan dışı süre commit'leme.
