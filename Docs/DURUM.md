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

## Aktif iş: tıklama alanına yaka (collar) dahil 🔨

Şu an tüp seçimi yalnız cam gövdeye tıklayınca çalışıyor. Yaka da tıklanabilir
olmalı; ama tıklama alanı görselin dışına taşmamalı (yalnız yaka + cam).
Branch: `feature/collar-click-area`.

## Büyük yol haritası (mentör — 4 iş)

1. **Level içeriği: hep 2 boş + zorluk** ✅ (master'da)
2. **Kazanma pop-up'ı** ("Tebrikler, sonraki level") ← tıklama işinden sonra
3. **Çıkmaz pop-up'ı + hamle kilidi + "reklam izle"** (undo/+tüp/refresh) — pop-up
   altyapısını 2'den yeniden kullanır
4. **(Park) Yeni asset entegrasyonu** — asset gelince görsel katman yenilenir

## Notlar
- Python scriptlerini (`Tools/SolverBenchmark/*.py`) **kullanıcı kendi koşar**; düzenle ama çalıştırma.
- Animasyon süreleri Inspector `[SerializeField]`; kullanıcı test için yavaşlatır — `BoardView.cs` commit'lemeden önce diff'e bak, varsayılan dışı süre commit'leme.
