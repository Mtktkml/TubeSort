# TubeSort

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

Mevcut durum: **EditMode 12/12**, **PlayMode 8/8**.

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
5. **Dökme animasyonu** ← sıradaki
6. Level üretici
7. Cila + meta (undo, +1 tüp, kapak animasyonu, ses)
8. Build

### Bilinen eksikler

- `BoardView.CreateTestBoard()` elle kurulmuş geçici bir tahta; çözülebilirliği
  garanti değil. Level üreticiyle silinecek.
- `BoardView` tahtasını kendi kuruyor, dışarıdan alamıyor. Bu yüzden testler tüp
  sayısını kesin iddia edemiyor. Level üreticiyle birlikte enjekte edilmeli.
- Sahne hâlâ `SampleScene` adında; içindeki `BoardView` nesnesi `GameObject`.
- Asset yok ve neredeyse gerekmiyor. Ses, ikon ve yazı tipi cila adımında
  (Kenney.nl, freesound.org, Google Fonts).

### Bilinen hatalar

- **Deadlock tespiti yetersiz:** `Board.HasAnyValidMove` yalnızca "yapılabilecek
  hamle var mı" sorusunu soruyor. Hamle var ama oyun kazanılamaz (gerçek çıkmaz)
  durumunu yakalamıyor. Tam çözülebilirlik analizi BFS/DFS ile durum uzayını
  aramayı gerektirir; `Board.Clone()` buna hazır. Level üreticiyle birlikte
  ele alınmalı.

### Device Simulator sınırlamaları

- **Hızlı tıklamada donma:** Device Simulator'da art arda hızlı tıklayınca
  Input System dokunuş "bırakma" olayını kaybedebiliyor; `isPressed` true'da
  takılı kalıyor ve yeni basış algılanmıyor. Game penceresinde ve gerçek
  cihazda bu sorun **yok**. Oyunun hatası değil, simülatörün bilinen sınırı.

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
9. **`Assets/Scripts/Game/BoardView.cs`** — tahta, dokunuş, yerleşim
10. `Assets/Tests/EditMode/` — Core testleri
11. `Assets/Tests/PlayMode/` — görsel testler

### Sonraki adım için hazır olanlar

Dökme animasyonu için gereken parçalar yol boyunca yerleştirildi:
`PourResult` (ne aktı), `_FillLevel`'in float olması (seviye pürüzsüz düşebilsin),
`IEnumerator`/`yield` (PlayMode testlerinde kullanıldı).

Çözülecek mimari soru: animasyon sürerken `Board` hamleyi çoktan yapmış olacak
ama ekran henüz göstermemiş olacak. Bu arada gelen dokunuşlar yönetilmeli.
