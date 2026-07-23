# TubeSort Çözülebilirlik Solver'ı — Kavramsal Özet

Kod: `Assets/Scripts/Core/Solver.cs` · Testler: `Assets/Tests/EditMode/SolverTests.cs` ·
Çapraz doğrulama: `Tools/SolverBenchmark/crosscheck.py`

## Problem

Level üreticinin ürettiği rastgele tahta çözülebilir mi — ve kaç farklı
çıkışı var? Çözülebilirlik kararı NP-tam (Ito et al. 2022); ama tek bir
tahta için pratikte hızla cevaplanabilir. Amaç *en kısa* çözümü bulmak
değil, çözülebilirliğe karar vermek ve **çözüm sayısını** ölçmek (zorluk
metriği; mentör kararı, 23 Tem 2026). En kısa çözüm uzunluğu ayrı ve ucuz
bir soru olarak build-time'da BFS ile ölçülür (aşağıda).

## Arama modeli: tek tahta üzerinde geri izlemeli DFS

Her hamle bir "durum"dan diğerine geçiştir; tüm olası durumlar bir ağaç gibi
düşünülebilir. DFS bu ağacı derinlemesine gezer: bir hamle **gerçekten
yapılır**, oradan derine inilir; o dal çıkmaza çıkarsa hamle **geri alınır**
ve sıradaki hamle denenir (backtracking). Bellekte hiçbir zaman ağacın tamamı
yoktur — tek bir tahta nesnesi, yap/geri-al ritmiyle ağacın üzerinde
gezdirilir. BFS'in elenme sebebi tam bu: BFS her durumu ayrı kopya olarak
saklamak zorunda (üstel bellek); A\* elenmesi de aynı mantık: en kısa çözüm
aranmıyor ki maliyetine katlanılsın.

Arama ilk çözümde **durmaz**: her tahtada erişilebilir durum uzayının
tamamı gezilir ve çözüme düşen her kenar sayılır. İlk bulunan çözümün yolu
örnek olarak saklanır (replay testi, Console logu, ileride ipucu) ama
hiçbir metriğe girmez — DFS'in yolu gezinme sırasının rastlantısıdır, en
kısa değildir. Kararların **kanıt yükü** yine asimetriktir: "çözülebilir"
için tek kenar yeter, "çözülemez" için uzayın tükenmiş olması gerekir;
maliyet ise iki durumda da aynıdır (uzayı tüketmek). Uzay tüketildiği için
karar, durum sayısı ve çözüm sayısı gezinme sırasından bağımsızdır.

## Asıl hız kaynağı: kanonik durum önbelleği

Görülen her durum bir parmak iziyle kaydedilir; aynı duruma ikinci gelişte
dal anında kesilir. Kritik incelik parmak izinin **kanonik** olması: tüpler
içeriklerine göre sabit bir sıraya dizilerek metinleştirilir. Böylece:

- Tüplerin dizilişi farklı ama oyunca özdeş tahtalar (n tüpte n! permütasyon)
  **tek duruma** iner.
- Eşdeğer boş tüpler ayırt edilmez.
- Ters hamleler ve döngüler kendiliğinden elenir — "geri dönülen her durum
  zaten kayıtlıdır." Ayrı bir döngü-engelleme kuralına gerek kalmaz.

Sonlanma garantisi de buradan gelir: klasik özyinelemedeki "küçülen problem"
yerine burada **tükenen kaynak** vardır — durum uzayı sonludur ve ilerleyen
her adım keşfedilmemiş havuzdan bir durumu kalıcı olarak tüketir. Havuz
biter, arama biter; derinlik sınırı olmasa bile.

## Budamalar: hangi hamleler hiç denenmez

1. **Tamamlanmış tüpten dökme yok** — çözülmüşü bozmak hiçbir çözüm için
   gerekli olamaz. (Gerçek arama uzayı kesintisi.)
2. **Aynı kapasitedeki boş tüplerden yalnız biri hedef alınır** — hepsi
   kanonik olarak aynı duruma çıkar.
3. **Tamamı tek renk tüpü aynı kapasiteli boş tüpe dökmek yok** — tahta
   kanonik olarak hiç değişmez, hiçlik hamlesidir.

2 ve 3'ün elediklerini önbellek nasılsa yakalardı; ama önbellek pahalı
yakalar (hamle yapılıp, anahtar üretilip, geri alındıktan sonra). Budama
aynı işi hamle üretilmeden, bedavaya yapar. Yani 1 mantıksal kesinti,
2–3 maliyet optimizasyonudur.

## Hamle önceliklendirmesi: birleştirme önce, boş tüp sonra

Hamle listesi bilinçli sıralanır: **rengi rengin üstüne döken hamleler önce,
boş tüpe dökmeler en sona.** Gerekçe: birleştirme tahtayı ayrıştırmaya
(çözüme) doğru götürür; boş tüp ise kıt manevra alanıdır, erken
harcanmamalıdır. DFS listeyi sırayla denediği için "muhtemelen iyi"
hamleleri öne koymak çözümün daha az düğüm gezilerek bulunmasını sağlar.
Bu bir **sezgidir, doğruluk koşulu değildir**: sıra tersine çevrilse karar
aynı çıkar, sadece daha yavaş.

## Çözüm sayısı metriği: çözüme düşen kenarlar

Sayılan şey: genişletilen kanonik durumlardan yapılan ve tahtayı bitiren
(budanmış) hamleler — durum grafında **çözüme düşen kenarların sayısı**.
Her durum en fazla bir kez genişletildiği için:

- Aynı öz çözümün hamle-sıralaması varyantları tekrar sayılmaz (bağımsız
  hamle çiftleri yol sayısını katlar; kenar sayısını değiştirmez).
- Sayı gezinme sırasından bağımsız ve deterministiktir — çapraz
  doğrulamada birebir kıyaslanır.
- Maliyet tavanı, "çözülemez" kararının zaten ödediği bedeldir (uzayı
  tüketmek); çıktı boyutuyla patlamaz.

Bilinçli olarak YAPILMAYAN: tüm çözüm *yollarını* saymak ya da saklamak.
Yol sayısı bağımsız hamlelerin sıralama kombinasyonlarıyla katlanarak
büyür (ölçüldü: 13 hamlelik çözümü olan tahtada 30 sn'de 2.5M+ yol,
bitmedi) ve tam sayma #P sınıfındadır; üstelik yol biriktirmek kanonik
önbelleği geçersiz kılar. "Ortalama çözüm uzunluğu" da aynı nedenle
tanımsız/gürültülüdür. **En kısa çözüm uzunluğu** ise grafın özelliğidir
ve yol saymadan ölçülür: kanonik graf üzerinde BFS (build-time, Python —
planlanan metrik katmanı). DFS'in örnek yolu ile BFS'in en kısası
çelişmez; iki ayrı soruya iki ayrı cevaptır.

## Neden 3 sonuç durumu (Solvable / Unsolvable / OutOfBudget)

Arama düğüm ve derinlik bütçesiyle sınırlıdır. Bütçe aşıldığında dürüst
cevap "çözülemez" değil, **"bilinmiyor"**dur — çünkü tüm durumlar taranmadan
çözümsüzlük kanıtlanmış olmaz. Bu ayrım kozmetik değil: generate-and-test
akışında "bilinmiyor" da atılır ama **ayrı raporlanır**. İkisi tek kefeye
konsa, solver'ın zorlandığı (muhtemelen zor ve ilginç) tahtalar sessizce
elenir ve level havuzu fark edilmeden kolaya yamulurdu (Murase 1996 dersi).
Derinlik bütçesinin cömert bir tavan olabilmesinin dayanağı da teorik: her
çözülebilir tahtanın polinom uzunlukta çözümü kanıtlı — sınıra dek arayıp
bulamamak anlamlı bir sinyaldir.

Sayım semantiğinin getirdiği köşe: bütçe dolduğunda elde çözüm *varsa*
karar Solvable kalır; yalnız sayım kesinliğini yitirir
(`CountIsExact=false` — "en az N"). OutOfBudget yalnız hiç çözüm
bulunamadan bütçe aşılınca döner. Eksik sayım hiçbir raporda kesin sayı
gibi sunulmaz; tablolar "(alt sınır)" işareti basar.

## Doğruluğun görünmez sözleşmeleri

- Geri alma mekanizması tahtayı **bire bir** eski hâline döndürmek
  zorundadır; aksi hâlde arama sessizce yanlış tahtalarda devam ederdi.
- Hamle üretimi geçerli hamlelerin *tamamını* değil, **karar açısından
  kayıpsız bir alt kümesini** verir — kaybedilen yalnızca alternatif çözüm
  yollarıdır.
- Bu sözleşmeler, kuralları bağımsızca implemente eden Python solver'ıyla
  çapraz doğrulamada sınanmıştır: 8 tahtanın 8'inde **karar + gezilen durum
  sayısı + çözüm sayısı** birebir aynıdır (yeni semantikte üç değer de
  gezinme sırasından bağımsız olduğundan kıyas eski "yalnız karar"
  kıyasından sıkıdır). Python tarafı geri alma yerine taze kopya kullanır,
  yani aynı sonuca **farklı mekanizmayla** ulaşır; bu da doğrulamayı
  güçlendirir.
