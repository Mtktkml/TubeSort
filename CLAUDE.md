# TubeSort

Water Sort (sıvı sıralama) bulmaca oyunu. Unity 6000.3.9f1, 2D URP, mobil hedefli.

Ball Sort'un görsel varyantı **değil**: sıvı grup halinde akar, kısmi dökme vardır
ve dökülen sıvı hedeftekiyle birleşir. Su kuralı tek satır:
`PourableAmount = min(kaynağın üst segmenti, hedefteki boş yer)`.

**İsimlendirme:** kod İngilizce, yorumlar Türkçe.

## Mimari

Bağımlılıklar hep aşağı akar, hiç yukarı çıkmaz:

```
Tests.PlayMode ──> Game ──> Core
Tests.EditMode ──────────> Core
```

| Assembly | Konum | Unity kullanır | Build'e girer |
|---|---|---|---|
| `TubeSort.Core` | `Assets/Scripts/Core/` | **hayır** | evet |
| `TubeSort.Game` | `Assets/Scripts/Game/` | evet | evet |
| `TubeSort.Tests.EditMode` | `Assets/Tests/EditMode/` | evet | hayır |
| `TubeSort.Tests.PlayMode` | `Assets/Tests/PlayMode/` | evet | hayır |

### Core — oyunun kuralları

`noEngineReferences: true` ile Unity kodu derleyici düzeyinde yasaklı. `Transform`,
`Vector3`, `Color`, `MonoBehaviour` buraya yazılamaz. Kazancı: kurallar sahne
kurmadan, EditMode'da saniyeler içinde test edilebilir.

- `Tube.cs` — bir tüpün içeriği: dipten yukarı `int` renk listesi. Kilit özellik
  `TopSegmentLength`: üstteki bitişik aynı renk sayısı.
- `Board.cs` — hamle kuralları ve `PourableAmount`. `PourResult` struct'ı (hamle
  raporu: renk, miktar, kaynak, hedef) da bu dosyada; animasyon ve undo bunu kullanır.
- `Solver.cs` — çözülebilirlik kararı + çözüm/durum sayımı (zorluk metrikleri).
  Ayrıntı: `Docs/SOLVER.md`.
- `MoveHistory.cs` — undo için hamle yığını. Board'a gömülmedi: çözücü gibi yoğun
  kullananlar hamle başına geçmiş maliyeti ödemesin.

Renkler `int`; `Color` bir Unity tipi olduğu için Core'a giremez. Çeviri
`ColorPalette`'te yapılır.

### Game — görsel katman

Her şey koddan kurulur; sahnede yalnız kamera, ışık ve boş bir `Board` kök
nesnesi var. Prefab/asset referansı yok.

- `ColorPalette.cs` — `int` renk kimliğini ekran rengine çevirir. Tanımsız kimlikte
  parlak pembe döner (hata gizlenmesin diye).
- `TubeView.cs` — tek tüpün görünümü: cam/yaka/tıpa sprite katmanları + sıvı shader.
  Core verisini bitişik aynı renkleri birleştirip `MaterialPropertyBlock` ile sıvıya
  gönderir.
- `BoardView.cs` — tahtayı kurar, dokunuşu `Board`'a hamleye çevirir, yerleşimi
  ekrana göre hesaplar. Oyun kuralı içermez; hepsini `Board`/`Solver`'a sorar.
  HUD'u da kurar: level başlığı, çıkmaz banner'ı, butonlar (hepsi TMP/kod çizimi).
- `StreamView.cs` — dökme akışının iki parçalı görseli (aşağıda).
- `LevelLibrary.cs` — `Resources`'taki JSON'dan tahta kurar.
- `ButtonView.cs` / `UndoButtonView.cs` / `PilotNextButtonView.cs` — koddan çizilen
  buton görselleri; tıklama yakalama `BoardView`'da.

**Tahta yükleme kapısı — `BoardView.LoadBoard`:** dışarıdan tahta verir. `Start`
öncesi çağrılırsa kurulum onunla yapılır; oyun sırasında çağrılırsa görünümler
yıkılıp yeniden kurulur. Level geçişi ve testler bu kapıyı kullanır. Hiç kaynak
yoksa son çare pilot merdivenin ilk tahtası yüklenir.

### Görseller ve shader'lar — `Assets/Resources/`

Statik parçalar (cam tüp, bej yaka, mantar tıpa) PNG sprite'lardır; dinamik olanlar
(sıvı, dökme akışı) shader'la çizilir — seviye/renk/halka çalışma anında değiştiği
için asset'le yapılamazlar. `Resources` altındalar çünkü her şey koddan kurulur:
sahnede/prefab'da referans yok, `Resources` dışında build'e girmezlerdi.

Katman sırası (sortingOrder), tüp içi: cam 0 < sıvı 1 < arka yaka 2 < tıpa 3 <
ön yaka parçaları 4. Akışın alt parçası hedefin tıpa katmanına (3), üst parçası
her şeyin önüne (15) çizilir. Dökülen tüp bu değerlere +10 offset alır
(`SetSortingOffset`). Butonlar 100, level başlığı ve banner 101.

- `Sprites/tube.png` — cam tüp (PPU 247.5, pivot Bottom, **9-slice** alt border 88:
  dip kavisi sabit kalır, düz gövde kapasiteyle uzar).
- `Sprites/collar.png` — bej yaka (PPU 244; tam genişliği = `FullWidth` yerleşim
  çapası). Tıpa sandviçinin ön parçaları bu görselden çalışma anında **eğri alfa
  maskesiyle** üretilir (`TubeView.CreateCollarFront*`; bu yüzden Read/Write açık).
  Maske dışı pikselde yalnız alfa sıfırlanır, RGB korunur — yoksa bilinear filtre
  kenarda koyu çizgi bırakır.
- `Sprites/cork.png` — mantar tıpa (PPU 229); yalnız `Tube.IsComplete` tüpte görünür.
- `TubeShape.hlsl` — sıvının şekil matematiği (SDF).
- `Liquid.shader` — sıvı: katmanlar, doluluk, 2.5D yüzey diski + damla halkaları
  (yalnız dökme sırasında; boşta yüzey durgun), eğim.
- `Stream.shader` — dökme akışı: dik dikdörtgen kolonu SDF ile çizer; akış yönünde
  parlaklık dalgası kayar.

PPU'lar tek kaynaktan türetilir: yaka görselinin tam genişliği 1.2 birim kabul edilir,
diğerleri birleşik referans görselindeki (`tube (2).png`) piksel oranlarından hesaplanır.
Yeni görselde oran birleşik referansla doğrulanmalı.

Sıvı elle HLSL yazıldı, Shader Graph değil: katman renkleri **dizi** ve döngüyle
işleniyor, Shader Graph'ta dizi/döngü yok.

## Kurallar ve tuzaklar

**İki yerde tutulan, derleyicinin zorlayamadığı sabitler:**

- **`MaxLayers = 8`** — `TubeView.MaxLayers` ve `Liquid.shader`'daki `MAX_LAYERS`
  aynı olmalı. En kötü durumda katman sayısı kapasiteye eşittir, yani bu sayı aynı
  zamanda **desteklenen en büyük tüp kapasitesi**. Aşılırsa `TubeView.Initialize`
  hata basar (sessizce yanlış çizmek yerine).
- **SDF formülleri** — `TubeShape.hlsl` (GPU, piksel boyama) ve `TubeView.cs` (CPU,
  tıklama doğrulama) aynı şekli çizmeli. GPU/CPU kod paylaşamadığı için tekrar
  kaçınılmaz. Şekil değişirse `SdRoundedBox` ve `SdTube` ikisinde birlikte güncellenir.

**Renk uzayı:** proje Linear. `SetVectorArray` renk dönüşümü yapmaz (`SetColor` yapar).
Shader'a giden renkler `TubeView.ToShaderColor` ile çevrilmeli, yoksa kırmızı pembe çıkar.

**Ölçü uzayı — her değer için ayrı karar ver:** "bu şey tüple birlikte büyümeli mi?"

| Tüple ölçeklenir (oran) | Ölçeklenmez (dünya birimi) |
|---|---|
| Doluluk seviyesi, katman sınırları | Yüzey yumuşaklığı, `FillHeadroom` |
| Damla halkaları (disk-normalize) | `MouthExtension`, 9-slice dip kavisi, akış kolonu genişliği |

**Sabitleri ölçüden türet, uydurma.** Ekranla/ölçüyle ilgisiz sihirli sayılar
(`horizontalSpacing = 1.2`, `maxTubesPerRow = 5`) tekrar tekrar sorun çıkardı.
Yatay aralık `TubeView.FullWidth`'ten, sütun sayısı kameranın görüş alanından hesaplanır.

**Girdi:** `Mouse` değil `Pointer` kullanılır. `Mouse.current` telefonda ve Device
Simulator'da null; `Pointer` ikisinin ortak atası.

## Level sistemi

Leveller runtime'da üretilmez: `Tools/SolverBenchmark/` Python scriptleri build-time'da
çok sayıda aday üretip metriklerle seçer, zorluğu artan sırayla JSON'a yazar.

- `pilot_ladder.py` — parametre merdiveni (kapasite, renk, boş) + zorluk skoru;
  `pilot_levels.json` ve analiz raporu `pilot_ladder.md` üretir.
- `crosscheck.py` — Python ↔ C# solver çapraz doğrulaması.
- `Resources/pilot_levels.json` — 30 tahta (öğretici + köprü + ölçülü tier'lar);
  şema `label`, `shortest`, `solutionCount` içerir. `Resources/levels.json` eski
  5 levellik küçük set (elle, genişletilmiş alanlar yok).

**JSON'a skor değil ham metrik yazılır** (`shortest`, `solutionCount`): ağırlık
kalibrasyonu oyuncu testiyle değişince yeniden sıralanabilsin diye. C# tarafı
algoritmayı koşmaz, sayıları JSON'dan okur.

**Tasarım kuralları:** kapasite ≤ 4'te boş tüp sayısı 1 olabilir; kapasite ≥ 5'te
her zaman 2 boş (rastgele üretim tek boşta çöker). Her tier'dan 2 tahta ekranda
`1.1`, `1.2`, `2.1`… diye gösterilir. Başta 2 sabit öğretici tier skora girmez.

## Oynanış akışı

- **Level geçişi:** çözülünce kısa gecikmeyle sonraki; skip/önceki navigasyonu;
  restart (yüklemedeki `pristineBoard` kopyasından); +tüp (`Board.AddTube` + görünüm
  yeniden kurma).
- **Çıkmaz tespiti:** her hamleden sonra `Solver.IsSolvable` (ilk çözümde duran ucuz
  kontrol). Çözülemez duruma düşülünce "Çıkmaz!" banner'ı çıkar ve undo/restart/+tüp
  butonları yanıp sönerek yönlendirir.

**Dökme animasyonu** — `AnimatePour` coroutine'i, kayma + eğilme + dökme eş zamanlı,
sonra doğrulma + geri dönüş eş zamanlı:

- Tek açı sistemi: açı `CalculatePourAngle`'dan gelir, `SmoothDamp` ile pürüzsüz
  takip edilir (`_TiltAngle` uniform'u shader'a geçer; sıvı yüzeyi dünya uzayında
  yatay kalır). Eğim sıvı miktarına bağlı dinamik: dolu tüp ~60°, boş tüp ~100°.
- Ağız her karede hedefin üstüne konumlanır (`CalculatePourPosition`); stream kaynağı
  her zaman lip'ten (`CalculateSourceMouth`), sıvı yüzeyinden değil.
- Görsel yüzey fiziksel modele demirlenir (`AnchorLiquidToLip`, `_SurfaceLift`):
  kolon sıvıya yapışık kalır. Ağza sabit kilit denendi, kötü sonuç verdi — tekrar
  denenmesin.
- Katman güncelleme zamanlaması: **kaynak** tüp doğrulma sonrası `Refresh`, **hedef**
  tüp dökme öncesi `Refresh`; seviyeler kademeli akar (ışınlanma yok, undo dahil).

**Sıvı canlılığı:** boşta yüzey durgun. Dökme sırasında değme noktasından damlacık
sıçraması; dökme bitince damla halkası patlaması (`PlayRippleBurst`). Level başında
ve tüp kalkış/inişinde sönümlü çalkantı (`PlaySlosh`).

## Test çalıştırma

Unity Editor **kapalı** olmalı; açıksa proje kilitli olur ve batchmode başlamaz.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe" -runTests -batchmode `
  -projectPath "C:\Users\musta\TubeSort" -testPlatform EditMode `
  -testResults "<yol>\results.xml" -logFile "<yol>\unity.log"
```

`-testPlatform PlayMode` ile de aynısı. Editor'dan: **Window → General → Test Runner**.

- **EditMode'u tercih et:** sahne kurmadığı için saniyeler sürer (Core testleri).
  PlayMode'u yalnızca gerçek oyun ortamı gerektiğinde kullan (görsel/animasyon testleri).
- Testler tahtayı `BoardView.LoadBoard` ile enjekte eder; `TestBoards.Classic()`
  deterministik standart tahtadır.
- Batchmode çizim yapmaz: shader'ın **derlendiğini** doğrular, **doğru göründüğünü**
  değil. Görsel doğrulama gözle yapılır.
- Testi yeşil görmek yetmez: bozup **kırmızıya döndüğü** doğrulanır.
- Mobil hedef olduğu için görsel doğrulama **Device Simulator**'da yapılmalı, Game
  penceresinde değil.

## Çalışma şekli

- Her değişiklik için yeni branch, `--no-ff` ile merge, sonra branch silinir.
- Adım adım ilerlenir; kullanıcı kodu okuyup onaylamadan merge edilmez.
- `git checkout -- <dosya>` gibi geri döndüren komutlar önce kullanıcıya sorulur.

## Bilinen eksikler / sıradaki işler

- **Telefon performansı:** animasyonlar cihazda editörden az akıcı. İlk denenecek
  `Application.targetFrameRate = 60` (Unity mobilde varsayılan 30'a kilitler); yetmezse
  cihazda profiler (SDF shader'ların piksel maliyeti + overdraw). Çıkmaz tespitinin
  hamle başı `Solver.IsSolvable` maliyeti de burada ölçülür.
- **Ses ve ikon:** cila adımında (Kenney.nl, freesound.org). Yazı tipi TMP LiberationSans.
- **300 level hedefi:** önce 30 tahta oyuncu testine gider; ağırlık kalibre olunca
  kovalar genişletilip ölçeklenir.
- **Build.**

Görsel katmanın önceki tamamen-shader sürümü `runtime-shaders` branch'inde arşivli.

## Device Simulator sınırlaması

Art arda hızlı tıklamada Input System dokunuş "bırakma" olayını kaybedip `isPressed`
true'da takılabilir. Game penceresinde ve gerçek cihazda bu yok — simülatörün bilinen
sınırı. Simülatörde donma görülürse önce Console'a bak: watchdog `LogError`'u varsa
oyun hatasıdır, simülatör değil.

## Kod okuma sırası

Aşağıdan yukarı:

1. `Assets/Scripts/Core/Tube.cs` — tüpün veri modeli
2. `Assets/Scripts/Core/Board.cs` — hamle kuralları + `PourResult`
3. `Assets/Scripts/Core/Solver.cs` — çözülebilirlik + sayım (bkz. `Docs/SOLVER.md`)
4. `Assets/Resources/TubeShape.hlsl` — sıvının şekil matematiği (SDF)
5. `Assets/Resources/Liquid.shader` — sıvı, katmanlar, 2.5D yüzey + halkalar
6. `Assets/Resources/Sprites/` — cam/yaka/tıpa görselleri (import ayarları: PPU, pivot, 9-slice)
7. `Assets/Scripts/Game/ColorPalette.cs` — int renk → ekran rengi
8. `Assets/Scripts/Game/TubeView.cs` — Core → görünüm köprüsü
9. `Assets/Scripts/Game/StreamView.cs` — dökme akış görseli
10. `Assets/Scripts/Game/LevelLibrary.cs` — JSON → tahta
11. `Assets/Scripts/Game/BoardView.cs` — tahta, dokunuş, yerleşim, HUD, animasyon
12. `Assets/Tests/EditMode/` — Core testleri
13. `Assets/Tests/PlayMode/` — görsel testler
