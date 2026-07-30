# Durum / Devam Notu — 30 Tem 2026

Aktif branch: **`feature/levels-two-empty`** (master'a MERGE EDİLMEDİ).
Son commit: `4f56989`.

## Bu branch'te yapılanlar (commit'li)

1. **Leveller: hep 2 boş tüp + zorluk uzatıldı** (`6f8edd8`)
   - `pilot_ladder.py` LADDER'i 13 boş=2 slota geçti (boş=1 varyantları + köprü
     `(4,4,1)` kaldırıldı); tavan `(8,7,2)`. Öğreticiler değişmedi (T1 özel/0 boş,
     T2 `(4,2,2)`).
   - `pilot_levels.json` yeniden üretildi (kullanıcı koştu): 15 tier × 2 = 30 tahta,
     hepsi 2 boş, skor **monoton** (0.191→0.669, düşüş yok), en zor enKısa 45 / 9 tüp.
   - `LevelLibraryTests` geçerli (yalnız bayat "köprü" yorumu düzeltildi).
2. **Uzun tüplerde (kap 8) dökme başlamama düzeltmesi** (`4f56989`)
   - `CalculatePourAngle`'a **yükseklik-farkında geometrik alt sınır** eklendi
     (`needed = atan(2·(1.05−fill)·h/W)`); kısa tüpte etkisiz, uzun tüpte dökmenin
     başlamasını garantiler. watchdog 4→6. `PourFreezeTests`'e kap-8 mid-fill testi.

## AÇIK SORUN (yarın bakılacak) 🔴

**Kap ≥ 5 tüplerde, ikinci-sondan birim dökülürken sıvı tüpte kalıyor (bitmiyor)
sonra birden yok oluyor.** Kap 4'te sorun yok.

- Denenen ve **ÇALIŞMAYAN** yaklaşım (geri alındı, commit değil):
  `Liquid.shader`'daki son-birim drain artefaktının sabit `0.2` doluluk eşiğini
  kapasiteye göre ölçeklemek (`_DrainThreshold = min(0.2, FillSpan/kapasite)`).
  Teşhis mantıklıydı (0.2 eşiği kap≥5'te 1 birimden fazlasını siliyor) ama
  **sorunu çözmedi** → asıl neden başka (ya da ek bir katman).
- Sonraki adayları incele:
  - Dökme **gating** takılması + sondaki sıçrama (`AnimatePour` içinde
    `liquidAtLip || nextT>=0.98` koşulu; SmoothDamp açı gecikmesiyle son bit
    takılıp sonra `nextT` kaçışıyla aniden boşalıyor olabilir).
  - Shader'daki başka bir düşük-doluluk/yüzey mekanizması.
  - Bu bug'ın açı düzeltmesinden ÖNCE de var olup olmadığını kontrol et (regresyon mu?).

## Büyük yol haritası (mentör — 4 iş)

1. **Level içeriği: hep 2 boş + zorluk** ← *bu branch, yukarıdaki açık sorun hariç bitti*
2. **Kazanma pop-up'ı** ("Tebrikler, sonraki level")
3. **Çıkmaz pop-up'ı + hamle kilidi + "reklam izle"** (undo/+tüp/refresh) — pop-up
   altyapısını 2'den yeniden kullanır
4. **(Park) Yeni asset entegrasyonu** — asset gelince görsel katman yenilenir

## Yarın ilk iş

1. Açık sorunu (kap≥5 drain) yukarıdaki adaylarla ayrıntılı incele + düzelt.
2. Düzelince: PlayMode/EditMode testleri yeşil + Unity göz kontrolü → mentör onayıyla
   master'a `--no-ff` merge.
3. Sonra 2. işe (kazanma pop-up'ı) geç.

## Notlar
- Python scriptlerini (`Tools/SolverBenchmark/*.py`) **kullanıcı kendi koşar**; düzenle ama çalıştırma.
- Animasyon süreleri Inspector `[SerializeField]`; kullanıcı test için yavaşlatır — `BoardView.cs` commit'lemeden önce diff'e bak, varsayılan dışı süre commit'leme.
