# TubeSort

> **ARŞİV BRANCH'İ (29 Tem 2026):** Görsel katmanın (cam + bej yaka + mantar
> tıpa) runtime'da **shader ile çizilen** son hâli burada saklanır. Güncel
> geliştirme **master**'da: görsel katman ekip asset'lerine (sprite) geçti,
> mentör tercihi o yön. Bu branch yalnız referans/geri dönüş içindir; üzerine
> geliştirme yapılmaz. Aşağıdaki doküman o günün fotoğrafıdır.

Water Sort (sıvı sıralama) bulmaca oyunu. Unity 6000.3.9f1, 2D URP, mobil hedefli.

Ball Sort'un görsel varyantı **değil**: sıvı grup halinde akar, kısmi dökme vardır
ve dökülen sıvı hedeftekiyle birleşir.

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
`Vector3`, `Color`, `MonoBehaviour` buraya yazılamaz; yazılırsa proje derlenmez.

Kazancı: kurallar sahne kurmadan, EditMode'da saniyeler içinde test edilebiliyor.
Görsel katman baştan yazıldı (sprite → shader), Core'un tek satırı değişmedi.

- `Tube.cs` — bir tüpün içeriği. Sıvı dipten yukarı `int` listesi. Kilit özellik
  `TopSegmentLength`: üstteki bitişik aynı renk sayısı.
- `Board.cs` — kurallar. Oyunun tamamı tek fonksiyonda:
  `PourableAmount = min(kaynağın üst segmenti, hedefteki boş yer)`.
  Water Sort'u Ball Sort'tan ayıran satır budur.
- `PourResult` — hamlenin raporu (renk, miktar, kaynak, hedef). Animasyon ve undo
  bu bilgiyi kullanır.

Renkler `int`; `Color` bir Unity tipi olduğu için Core'a giremez. Çeviri
`ColorPalette`'te yapılır.

### Game — görsel katman

- `ColorPalette.cs` — Core'un `int` renk kimliğini ekran rengine çevirir.
  Tanımsız kimlikte parlak pembe döner (hata gizlenmesin diye).
- `TubeView.cs` — çevirmen. Core'un verisini shader diline çevirir: bitişik aynı
  renkleri tek katmanda birleştirir, sınırları ve doluluğu
  `MaterialPropertyBlock` ile gönderir.
- `BoardView.cs` — tahtayı kurar, dokunuşu `Board`'a hamleye çevirir, yerleşimi
  ekrana göre hesaplar. Tek oyun kuralı içermez; hepsini `Board`'a sorar.

### Shader'lar — `Assets/Resources/`

`Resources` altındalar çünkü `Shader.Find` build'de kullanılmayan shader'ları
bulamaz; `Resources` altındakiler garanti dahil edilir.

- `TubeShape.hlsl` — şekil matematiği (SDF). Cam ve sıvı ikisi de `#include`
  eder; formül tek yerde olmasa sıvı camdan taşardı.
- `Glass.shader` — cam tüp. Tek parça: dibi yarım daire, ağzına doğru yatayda
  genişler (`SdSmoothUnion` ile kaynatılmış iki yuvarlak dikdörtgen).
- `Liquid.shader` — sıvı. Camın şeklini alıp et kalınlığı kadar daraltarak
  kırpar, içine katmanları ve yüzey dalgasını çizer.
- `Stream.shader` — dökme akışı. Kuadratik Bezier eğrisini SDF ile çizer,
  kaynakta geniş hedefte daralır, akış yönünde parlaklık dalgası kayar.

Elle HLSL yazıldı, Shader Graph değil: katman renkleri **dizi** ve döngüyle
işleniyor, Shader Graph'ta dizi/döngü yok.

## Kurallar ve tuzaklar

**İsimlendirme:** kod İngilizce, yorumlar Türkçe.

**Ölçü uzayı — her değer için ayrı karar ver:** "bu şey tüple birlikte büyümeli mi?"

| Tüple ölçeklenir (oran) | Ölçeklenmez (dünya birimi) |
|---|---|
| Doluluk seviyesi | Dalga yüksekliği |
| Katman sınırları | Yüzey yumuşaklığı, `FillHeadroom` |
| | Ağız genişlemesi, cam et kalınlığı |

Dalga bir zamanlar gövde oranındaydı; 12 birimlik tüpte 4 birimliğin üç katı
oluyordu.

**Renk uzayı:** proje Linear. `SetVectorArray` renk dönüşümü yapmaz (`SetColor`
yapar). Shader'a giden renkler `TubeView.ToShaderColor` ile çevrilmeli, yoksa
kırmızı pembe çıkar.

**`MaxLayers = 8`** iki yerde: `TubeView.MaxLayers` ve `Liquid.shader`'daki
`MAX_LAYERS`. Aynı olmak zorunda, derleyici bunu zorlayamıyor. En kötü durumda
katman sayısı kapasiteye eşittir, yani bu sayı aynı zamanda **desteklenen en
büyük tüp kapasitesi**. Aşılırsa `TubeView.Initialize` hata basar (sessizce
yanlış çizmek yerine).

**SDF formülleri iki yerde:** `TubeShape.hlsl` (shader, piksel boyama) ve
`TubeView.cs` (C#, tıklama doğrulama). GPU ile CPU arasında kod paylaşılamadığı
için tekrar kaçınılmaz. Tüp şekli değişirse ikisi birlikte güncellenmelidir:
`SdRoundedBox`, `SdSmoothUnion` ve `SdTube`.

**Sabitleri ölçüden türet, uydurma.** `horizontalSpacing = 1.2f` ve
`maxTubesPerRow = 5` gibi ekranla/ölçüyle ilgisiz sayılar iki kez sorun çıkardı.
Aralık artık `TubeView.FullWidth`'ten, sütun sayısı kameranın görüş alanından
hesaplanıyor.

**Girdi:** `Mouse` değil `Pointer` kullanılır. `Mouse.current` telefonda ve Device
Simulator'da null; `Pointer` ikisinin ortak atası.

**Test:** mobil hedef olduğu için doğrulama **Device Simulator**'da yapılmalı,
Game penceresinde değil. Game penceresi yanıltır.

## Test çalıştırma

Unity Editor **kapalı** olmalı; açıksa proje kilitli olur ve batchmode başlamaz.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe" -runTests -batchmode `
  -projectPath "C:\Users\musta\TubeSort" -testPlatform EditMode `
  -testResults "<yol>\results.xml" -logFile "<yol>\unity.log"
```

`-testPlatform PlayMode` ile de aynısı. Editor'dan: **Window → General → Test Runner**.

Mevcut durum: **EditMode 26/26**, **PlayMode 35/35**.

EditMode'u tercih et: sahne kurmadığı için saniyeler sürer. PlayMode'u yalnızca
gerçek oyun ortamı gerektiğinde kullan.

Batchmode çizim yapmaz: shader'ın derlendiğini doğrular, **doğru göründüğünü**
doğrulamaz. Görsel doğrulama gözle yapılır.

## Çalışma şekli

- Her değişiklik için yeni branch, `--no-ff` ile merge, sonra branch silinir.
- Adım adım ilerlenir; kullanıcı kodu okuyup onaylamadan merge edilmez.
- Testi yeşil görmek yetmez: bozup **kırmızıya döndüğü** doğrulanır.
- `git checkout -- <dosya>` gibi geri döndüren komutlar önce kullanıcıya sorulur
  (bir kez uncommitted çalışma böyle kayboldu).

## Yol haritası

1. ~~Sıvı mantığı (headless)~~
2. ~~Basit görsel~~
3. ~~Sıvı shader'ı~~ (SDF, cam, genişleyen ağız)
4. ~~Ekrana uyarlanan yerleşim~~
5. ~~Dökme animasyonu~~
6. Level üretici
7. Cila + meta (undo, +1 tüp, kapak animasyonu, ses)
8. Build

### Kaldığımız yer (29 Tem 2026 — toon tasarım TAMAM, sırada: feature/asset-tube)

**Toon tasarım entegrasyonu:** ekip çizgi-film stili tüp+tıpa tasarımı gönderdi
(referanslar kullanıcının Masaüstü'nde: `tube2.png`, `tube (2).png`, `mm.png`,
`ağız kısmı.png`). Tasarım asset olarak değil **runtime'da shader ile** üretiliyor.
~7 iterasyon turu (kullanıcı + ekip spec'leriyle) tamamlandı:

- **Glass/Liquid/TubeShape:** düz toon tüp (mouth-expansion kalktı), kalın tek tip
  kontur, iç açık çizgi, iki mavimsi-beyaz şerit (cam+sıvı hizalı, `_StreakScale`),
  `MouthExtension` (cam tepesi yakanın arkasına uzar, sıvı matematiği değişmez).
- **Collar.shader (yeni):** bej yaka — TEK düz elips silüet (simit; alt yarı %20
  basık "yumurta"), üstte içeride kalan koyu delik, deliğin ALTINDA ön bandın üst
  1/3 diliminden geçen aşağı-bombeli PARANTEZ çizgisi (kontur renginde, gövdesi
  sabit kalın, uçlara sivrilir; kendi `fwidth`'i), yatay krem gradyan. **İki
  katman sandviç:** arka (order 2) + ön (order 4) tıpayı (order 3) sarar; ön
  katmanın şeffaf penceresi = delik içi + parantez ALTINDAKİ tıpa gövdesi (alt
  sınır tıpa dip ovali gibi dışbükey yay; en altta kontur + bej şerit tıpanın
  önünde). Tıpa silüeti `CorkWinSil` olarak Collar'a kopya — Cork ile birlikte
  güncellenmeli.
- **Cork.shader (yeni):** mantar tıpa — basık elips üst yüz + büyük koni + kademe +
  küçük koni + oval dışbükey dip; prosedürel gözenekler **eleme** ile (çizgiye/
  kontura değecek gözenek hiç çizilmez); üst yüz koyu dolgulu basık, yan açık
  dolgulu yuvarlak. Yalnız `Tube.IsComplete` tüpte görünür. (Kademe hizasındaki
  "temas gölgesi" bandı kaldırıldı — pencereden görününce çizgi gibi duruyordu.)
- **Mobil sağlamlık:** tüm `fwidth` çağrıları discard'lardan önce; ön katman kesimi
  discard değil alfa ile (OpKill türev bozulması riski).
- Kalınlık/konum sabitleri `TubeView.cs`'de yorumlu; doğrulamalar ultracode
  workflow'larıyla yapıldı (3 tur, derleme+geometri+spec mercekleri).

**Tamam (29 Tem 2026, gözle onaylandı, master'a birleşti):** parantez seam +
açılan renkler (cam 0.82,0.94,0.99; yaka 0.99,0.96,0.82 → 0.91,0.85,0.70) +
tıpa yaka penceresi (katman sırası referans `tube (2).png`: tıpa → delik → bej →
parantez → tıpa → ince bej + kontur → tüpte tıpa). Kalan cila adayları (asset
işi sonrasına): tıpa pop animasyonu, `SdTube`'un no-op mouth parametre zincirinin
sadeleştirilmesi.

**Sırada — feature/asset-tube:** AYNI tasarım ekipten gelecek sprite
asset'leriyle yeniden kurulacak (deney; shader sürümü yedek olarak master'da
kalır, asset sürümü onaylanırsa temizlenir). Butonlar BAŞKA ekibin işi — kapsam
yalnız tüp (cam+yaka+tıpa). Dikkat: tüp gövdesi kapasiteyle uzuyor
(`HeightFor`) — tube.png düz ölçeklenemez, 9-slice (dip sabit, gövde uzar) ya
da kapasite başına varyant gerekir; ekiple konuşulacak. Eski deneme branch'i
`feature/asset-redesign` (buton sprite'ları + deneme tüp asset'i + AssetTest
sahnesi) 29 Tem'de silindi; commit `1ec9f9f` reflog'da.

### Kaldığımız yer (23 Tem 2026)

**Hedef (mentör kararı): 300 önceden üretilmiş-seçilmiş level.** Leveller
runtime'da üretilmeyecek; Python'da çok sayıda aday üretilip metriklerle
en iyileri seçilecek, zorluğu artan sırayla dosyaya yazılacak. Kapasite
4/5/6; renk sayısı (K) ve boş tüp sayısı (2 kolay / 1 zor) bağımsız
parametreler. Eski plan ("5 leveli EMPTIES=1 ile yeniden üret") bu hattın
içine katlandı.

Yol haritası — A, B, C tamam, sıra D'de:

- [x] **A. Solver sayım semantiği:** arama ilk çözümde durmaz, uzayı
  tüketir, çözüme düşen kenarları sayar (`SolutionCount`, `CountIsExact`).
  C# + Python; çapraz doğrulama 8/8 — karar + durum + çözüm sayısı
  üçlüsü birebir kıyaslanıyor. Ayrıntı: `Docs/SOLVER.md`.
- [x] **B. En kısa çözüm uzunluğu:** `crosscheck.py`'de `shortest_solution`
  — kanonik graf üzerinde BFS, garantili en kısa (DFS ilk yolu metrik
  değildir: level 5'te ilk yol 59, en kısa 41). Bilinçli olarak yalnız
  Python'da: tek tüketicisi build-time; C# ileride sayıyı levels.json'dan
  okur, algoritmayı koşmaz. Makbuz satırı `generate_levels.py`'de
  (kapasite/renk/boş + çözümSayısı/enKısa/ilkYol/durum) — şimdilik log,
  şema genişlemesi C'nin kararı.
- [x] **C. Pilot merdiven + zorluk skoru ilk sürüm:**
  `Tools/SolverBenchmark/pilot_ladder.py` (ayrı script; `generate_levels.py`
  D'ye kadar yerinde). Skor **leksikografik** (mentör kararı, 24 Tem):
  birincil enKısa, eşitlik bozucu `1/çözümSayısı`; **ağırlık yok** —
  ham sinyaller (enKısa/çözüm/durum) her level için loglanır, ağırlık
  kararı veriyle mentöre bırakıldı. Slot temsilcisi = 30 adayın **medyanı**,
  dağılım (min/med/maks) raporlanır. Çıktı: `pilot_ladder.md` (levels.json'a
  dokunmaz). 12 slotluk merdiven, seçilen enKısa **monoton** (8→33, düşüş
  yok). Üç bulgu (aşağıda) D'yi şekillendiriyor. Skor doğrulandı; açık
  kalan tek şey mentörün ağırlık/eğri kararı (pilot ona veriyi sunar).

  **Pilot bulguları (24 Tem, D'nin girdisi):**
  1. **boş=1 saf rastgele üretimde kap≥5'te çöküyor:** kabul oranı ~%0
     (kap6 renk7 boş1: 600 denemede 0 kabul). Teoriyle uyumlu (Ito et al.:
     ~3 boş / 4 dolu). **Karar:** boş=1 yalnız kap≤4'te (kabul ~%15-24).
     Garantili boş=1 üretimi (**ters-üretim**) D'ye bırakıldı.
  2. **enKısa ≈ 0.8·(renk×kap) = tahta hacmi**, slot sırasını değil hacmi
     takip eder. **Karar:** merdiven hacme göre monoton dizilir; son 300
     level **slot sırasına değil ölçülen skora göre** sıralanacak.
  3. **`1/çözümSayısı` eşitlik bozucu doğrulandı:** eşit-uzunlukta boş=1
     ve dar tahtalar 3-12× az çözümle doğru şekilde daha zor sıralanıyor.
     Yan bulgu: eşit hacimde sığ+çok-renk, derin+az-renkten daha dar
     (kapasitenin hacim ötesi etkisi — D'de mentöre).
- [x] **D. Level üretimi + Unity tarafı.** *(27 Tem 2026'da mentör + kullanıcı
  kararlarıyla revize edildi; Faz 1A + 1B + formül revizyonu master'a birleşti —
  bkz. commit'ler `ba1e519`, `0cfac29`, `e11e871`, `b17251a`.)*
  Pilotun açtığı işlerin güncel durumu:
  - **Ters-üretim: İPTAL.** Gereksiz görüldü; mevcut rastgele generate-and-test
    onaylandı. Karar: kap≤4'te boş=1 mümkün, **kap≥5'te her zaman 2 boş**
    (piyasa oyunlarıyla uyumlu: kap4 + 2 boş standart). boş=1 çöküşü artık
    "kabul et ve 2 boşa geç" ile yönetiliyor, ters-üretime gerek kalmadı.
  - **Her tier'dan 2 tahta** (mentör): ekranda `level 1.1`, `1.2`, `2.1`…
    `pilot_ladder.py` → `choose_two` slot başına skora göre **orta-üst 2 adayı**
    (ordered[15], ordered[16]) seçer (30 aday, tek medyan yok).
  - **Öğretici + köprü leveller** (kullanıcı, referans oyundan): başa 2 öğretici
    tier (T1: 1 renk / 2 tüp, **elle**; T2: 2 renk / 1 boş / 3 tüp, üretici) —
    SABİT, skora girmez (yoksa boş=1 öğreticisi zor kümeye kayardı). En büyük
    skor uçurumuna (ölçülen 0.277→0.486, boş 2→1 geçişi) köprü `(4,4,1)`.
    Sonuç: **15 tier × 2 = 30 tahta**.
  - **Skora göre sıralama:** ranked tier'lar (12 mevcut + köprü) skora göre
    artan; öğreticiler başa sabit. `pilot_levels.json`'a yazılır.
  - **Ağırlık/eğri: oyuncu testiyle.** Mentör soyut karar vermeyecek; insanlar
    oynayacak, feedback `WEIGHTS`'i ayarlayacak. JSON'a skor DEĞİL **ham metrik**
    (enKısa, çözümSayısı) yazılır — ağırlık değişince yeniden sıralanır.
    **Güncel WEIGHTS (`e11e871`, mentör):** eski 0.55T/0.20A/0.15L/0.10C →
    **L0.45 / C0.25 / A0.15 / T0.15**. Boyut baskın olsun diye enKısa (L)
    öne alındı; tüp/renk artışında monotonluk sağlandı, 12→13'teki büyük düşüş
    gitti. Öğretici 2 `(4,2,1)` → `(4,2,2)`: 2 boş tüple çıkmaz-güvenli yapıldı
    (2.1/2.2 tuzaklıydı). `pilot_levels.json` + `pilot_ladder.md` seed 42 ile
    yeniden üretildi.
  - **Şema + Unity — TAMAM:** `pilot_levels.json` şeması genişledi (`label`,
    `shortest`, `solutionCount`); C# tarafı bitti — `LevelLibrary` DTO'ya
    `label`, `BoardView` "LEVEL x.y" başlığı (TMP), `LevelLibraryTests` 30 tahta.
    **Faz 1A (`ba1e519`):** level akışı — oto-geçiş (çözülünce 0.7s sonra sonraki),
    skip/önceki navigasyonu, restart (yüklemedeki `pristineBoard` kopyasından),
    +tüp (`Board.AddTube` + görünüm yeniden kurma), `ButtonView` (koddan çizili
    placeholder butonlar). **Faz 1B (`0cfac29`):** çıkmaz tespiti — her hamlede
    `Solver.IsSolvable`, çıkmazda "Çıkmaz!" banner + undo/restart/+tüp yanıp söner;
    TMP (LiberationSans SDF) eklendi. Ekran (13+ tüp sığıyor mu) ve `ColorPalette`
    (renkler ayırt edilebilir mi) göz kontrolü hâlâ açık.
  - **Mevcut leveller korunmuyor** (kullanıcı): üretim serbestçe yeniden
    koşuluyor, RNG kayması sorun değil.
  - **300 hedefi:** önce 30 tahta insan testine gidecek; ağırlık kalibre olunca
    kovalar genişletilip ölçeklenecek.
  - **Maliyet notu:** solve() uzayı tükettiği için kap6 slotları pahalı
    (~70 sn/30 aday); üretim dakikalar sürer — build-time, telefonu etkilemez.

Notlar:

- Benchmark Tablo 2 (2 boş çözülemez avı) yeni semantikte fiilen işlevsiz:
  avda elenen her çözülebilir aday tam tüketim maliyeti ödüyor, 45 sn'ye
  3-23 deneme sığıyor. Gerekirse solver'a "yalnız varlık" hızlı modu
  eklenebilir — mentörle konuşulacak.

Durum (29 Tem sonu): master'da her şey birleşik — solver + sayım, undo
özelliği (`feature/undo`), dökme donması düzeltmesi (`TiltedEdgeLevel`,
`fix/pour-freeze`), **D adımının tamamı** (level akışı + navigasyon +
çıkmaz tespiti + formül revizyonu, `b17251a`) ve **toon tasarım
entegrasyonu** (`feature/design-integration` birleşti). `feature/level-metrics`
ve `feature/asset-redesign` (butonlar başka ekibe geçti; son hâli `1ec9f9f`)
silindi. Çalışma **`feature/asset-tube`** branch'inde: toon tasarımın
asset'lerle yeniden yapımı. Cihazda (APK) doğrulandı, telefonda fps gözlemi
"Bilinen eksikler"de.

### Bilinen eksikler

- `BoardView.CreateTestBoard()` elle kurulmuş geçici bir tahta; çözülebilirliği
  garanti değil. Level üreticiyle silinecek. (Dışarıdan tahta verme kapısı
  hazır: `BoardView.LoadBoard` — Start öncesi çağrılırsa kurulum onunla
  yapılır, oyun sırasında çağrılırsa görünümler yıkılıp yeniden kurulur.
  Level üretici ve level geçişi bu kapıyı kullanacak.)
- Sahne hâlâ `SampleScene` adında; içindeki `BoardView` nesnesi `GameObject`.
- Görsel asset'e geçiş `feature/asset-tube`'da deneniyor (ekip tasarımları);
  yazı tipi TMP LiberationSans ile geldi (Faz 1B). Ses ve ikon cila adımında
  (Kenney.nl, freesound.org).
- **Telefonda akıcılık (23 Tem 2026, cihaz testi):** animasyonlar cihazda
  editördekinden az akıcı. Muhtemel sebepler, olasılık sırasıyla:
  (1) `Application.targetFrameRate` ayarlanmadı — Unity mobilde varsayılan
  30 fps'e kilitler; (2) SDF shader'ların piksel maliyeti: alpha-blend'li
  büyük quad'lar mobilde overdraw'a çok duyarlı. Cila adımında ele
  alınacak: önce targetFrameRate=60 denenecek, yetmezse cihazda profiler.

### Bilinen hatalar

- **Deadlock tespiti — ÇÖZÜLDÜ (`0cfac29`, Faz 1B):**
  `Board.HasAnyValidMove` yalnızca "yapılabilecek hamle var mı" sorusunu
  soruyordu; hamle var ama oyun kazanılamaz (gerçek çıkmaz) durumunu
  yakalamıyordu. Artık her hamleden sonra `Solver.IsSolvable` (ilk çözümde
  duran ucuz varlık kontrolü) koşuyor; çözülemez duruma düşülünce ekranda
  "Çıkmaz!" banner'ı çıkıyor ve undo/restart/+tüp butonları yanıp sönerek
  yönlendiriyor. Aşağıdaki kararlar bu çözümün gerekçesi (referans için
  korunuyor):

  **Karar — mimari: generate-and-test.** Deadlock oyun sırasında
  yakalanmaz; level üretiminden **sonra** solver ile "bu tahta çözülebilir
  mi?" doğrulanır, çözülemeyen tahta atılıp yenisi üretilir. Bu, PCG
  literatüründe belgelenmiş standart pratik (De Kegel & Haahr, IEEE ToG
  2020). Alternatif olan yapısal garanti (yeterince boş tüp) kanıtlı ama
  pratik değil: kapasite 4'te her 4 dolu tüpe ~3 boş tüp gerekir
  (Ito et al., FUN 2022).

  **Karar — algoritma: DFS + budama + kanonik durum önbelleği.**
  *Güncelleme (23 Tem 2026, mentör kararı):* arama ilk çözümde durmaz;
  erişilebilir uzayı tüketir ve çözüme düşen kenarları sayar
  (`SolutionCount` — zorluk metriği). İlk bulunan yol örnek olarak
  raporlanır, metrik değildir. Ayrıntı: `Docs/SOLVER.md`. Dayanaklar
  (Ito et al., arXiv:2202.09495):
  - Çözülebilirlik kararı **NP-tam** → budama tercih değil, zorunluluk.
  - **Ball sort ↔ water sort eşdeğer** (Corollary 4): ball-sort solver
    literatürü kısmi-dökme mekaniğine aynen uygulanır. (Teorem başlangıç
    konfigürasyonları için; oyun ortası kısmi tüplü tahtalar formal
    kapsam dışı.)
  - Her çözülebilir tahtanın **polinom uzunlukta çözümü** kanıtlı →
    sınıra kadar arayıp bulamamak doğru bir "çözülemez" kararıdır.

  **Uygulama detayları:**
  - **Kanonikleştirme** — asıl kazanç buradan: durum hash'lenmeden önce
    tüpler kanonik sıraya sokulur; tüp sırası permütasyonları ve eşdeğer
    boş tüpler tek duruma iner.
  - Budama: tamamlanmış tüpten dökme yok, tek renkli tüpü boş tüpe
    taşıma yok, kaynak başına yalnız bir boş hedef. (Ters hamle ve
    döngüler kanonik önbellek tarafından zaten elenir.)
  - **Bütçe loglaması:** düğüm bütçesi aşılırsa sonuç "bilinmiyor"dur,
    "çözülemez" değil — ikisi ayrı raporlanır. Yoksa zayıf doğrulayıcı
    level havuzunu sessizce kolaya yamultur (Murase 1996 dersi).

  **Elenen alternatifler:** BFS (çözülebilirlik/sayım için gereksiz bellek;
  ama **en kısa çözüm uzunluğu** metriği için build-time'da kanonik graf
  üzerinde BFS kullanılacak — uzay zaten tüketiliyor, ek maliyet sınıfı
  yok), naif DFS (durum tekrarı), Bidirectional BFS (geriye hamle üretme
  karmaşıklığı), boş tüp garantisi (oyun tasarımını bozar). **A\*/IDA\***
  ancak tahta boyutları BFS'i aşarsa gündeme gelir; "color break"
  heuristiği (farklı renk üstüne oturan renk geçişi sayısı) hazır fikir
  olarak duruyor. Tüm çözüm *yollarını* saymak/saklamak da elendi:
  sıralama kombinasyonlarıyla katlanarak büyür (#P), önbelleği geçersiz
  kılar — ölçümü ve gerekçesi `Docs/SOLVER.md`'de.

- **Son katman dökme artefaktı — çözüldü:** Shader'da surface-based
  `survivalScore` ile son ~1 birim sıvıda ağız tarafına doğru çekilme
  uygulandı. Sadece eğik tüplerde etkin (`tiltAmount`), dik hedef tüpler
  etkilenmiyor.

### Device Simulator sınırlamaları

- **Hızlı tıklamada donma:** Device Simulator'da art arda hızlı tıklayınca
  Input System dokunuş "bırakma" olayını kaybedebiliyor; `isPressed` true'da
  takılı kalıyor ve yeni basış algılanmıyor. Game penceresinde ve gerçek
  cihazda bu sorun **yok**. Oyunun hatası değil, simülatörün bilinen sınırı.
  **Uyarı (23 Tem 2026):** geçmişte simülatöre atfedilen donmaların bir
  kısmı aslında dökme animasyonu kilidiymiş (kapasite >= 5 tüplerde
  sıvı-ağızda formülünün kelepçe hatası; gerçek geometriyle düzeltildi,
  bkz. `TiltedEdgeLevel` ve `PourFreezeTests`). Simülatörde donma görülürse
  önce Console'a bakılmalı: watchdog LogError'u varsa oyun hatasıdır.

### Kod okuma sırası

Basit görsel (adım 2) sonrasını anlamak için aşağıdan yukarı:

1. `Assets/Scripts/Core/Tube.cs` — tüpün veri modeli (tazeleme)
2. `Assets/Scripts/Core/Board.cs` — hamle kuralları (tazeleme)
3. `Assets/Scripts/Core/PourResult.cs` — hamle raporu (tazeleme)
4. **`Assets/Resources/TubeShape.hlsl`** — şekil matematiği (SDF), cam ve sıvı ortak
5. **`Assets/Resources/Glass.shader`** — cam tüp
6. **`Assets/Resources/Liquid.shader`** — sıvı, katmanlar, dalga
7. **`Assets/Scripts/Game/ColorPalette.cs`** — int renk → ekran rengi
8. **`Assets/Scripts/Game/TubeView.cs`** — Core → shader köprüsü
9. **`Assets/Scripts/Game/StreamView.cs`** — dökme akış görseli
10. **`Assets/Scripts/Game/BoardView.cs`** — tahta, dokunuş, yerleşim
11. `Assets/Tests/EditMode/` — Core testleri
12. `Assets/Tests/PlayMode/` — görsel testler

### Dökme animasyonu — tamamlandı

Üç fazlı coroutine (`AnimatePour`): kayma+eğilme+dökme (eş zamanlı) →
doğrulma+geri dönüş (eş zamanlı).

**Eş zamanlı kayma+eğilme (çakışma çözümü):**
- Ayrı kayma fazı yok. Kayma ve eğilme aynı anda başlar: tüp hedefe
  kaydıkça eğilir, pivot offset tabanı kaldırır → hedef tüple çakışma olmaz.
- `pourPos` her kare güncel `currentAngle`'a göre yeniden hesaplanır
  (`CalculatePourPosition`): ağız her açıda hedefin üstüne düşer.
- Stream kaynağı her zaman lip'ten (`CalculateSourceMouth`), sıvı
  yüzeyinden değil. Pour hızı açıya bağlı: fill düşürülmeden önce açının
  sıvıyı lip'te tutmaya yetip yetmediği kontrol edilir.

**Tek açı sistemi (SmoothDamp):**
- Açı her zaman `CalculatePourAngle`'dan gelir, `SmoothDamp` ile pürüzsüz
  takip edilir. Sıvı ağza ulaşınca (`HasLiquidReachedMouth`) dökme başlar.
- `_TiltAngle` uniform'u Liquid.shader'a geçer; sıvı yüzeyi ve katman
  sınırları dünya uzayında yatay kalır (`sin/cos` oranı, ±0.2 clamp).
- Transform döner, pivot telafisi ile ağızdan dönme illüzyonu sağlanır.
- Eğim açısı sıvı miktarına göre dinamik: dolu tüp 60°, boş tüp 100°
  (`CalculatePourAngle`).
- Fill interpolasyonu lineer (SmoothStep ortada hızlanıyordu).

**Denenen ve reddedilen açı sistemleri:** (1) İki fazlı interpolasyon +
CalculatePourAngle — geçiş anında sıçrama, çok birim döküldüğünde hızlanma.
(2) fillFade ile açıyı sıfıra indirmek — sıvı dibe çöküyor. (3) Üstel
yumuşatma (Lerp dt*8) — geçişi yumuşatıyor ama kök nedeni çözmüyor.

**Katman güncelleme zamanlaması:**

| | Kaynak tüp | Hedef tüp |
|---|---|---|
| Katmanlar | Doğrulma sonrası `Refresh()` | Dökme öncesi `Refresh()` |
| Seviye | Eski yerden kademeli düşer | Eski yerden kademeli yükselir |

**Akış görseli (Stream.shader + StreamView):**

SDF Bezier eğrisi: tek quad üzerinde kuadratik Bezier, 10 doğru parçasıyla
yaklaşık hesaplanır. Kaynakta geniş, hedefte daralır (taper). Akış yönünde
kayan parlaklık dalgası hareket hissi verir. Bitiş noktası her kare hedef
tüpün sıvı seviyesiyle güncellenir (saydam tüpte sıvıya kadar uzanır).

**Son katman drain (Liquid.shader):**
Fill < 0.2 ve tüp eğikken (`tiltAmount`) etkin. `survivalScore = surface /
maxSurface`: ağız tarafı (yüksek score) kalır, kapalı uç önce kaybolur.
Dik tüplerde (hedef) etki yok. Denenen ve reddedilen drain yaklaşımları:
(1) Yatay floor — üçgen artefakt. (2) Orantılı floor — merkez şerit
tabana bağlı kalıyor. (3) Cam SDF wallProximity — çapraz daralma, yanlış
yön. (4) İkisinin karışımı (proportional+constant lerp) — karmaşık, hâlâ
yapay. Surface-based score en doğal sonucu verdi.

**Denenen ve reddedilen:** LineRenderer akış — yapay göründüğü için reddedildi.
