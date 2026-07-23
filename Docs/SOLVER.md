# TubeSort Çözülebilirlik Solver'ı — Kavramsal Özet

Kod: `Assets/Scripts/Core/Solver.cs` · Testler: `Assets/Tests/EditMode/SolverTests.cs` ·
Çapraz doğrulama: `Tools/SolverBenchmark/crosscheck.py`

## Problem

Level üreticinin ürettiği rastgele tahta çözülebilir mi? Bu soru NP-tam
(Ito et al. 2022); ama tek bir tahta için pratikte hızla cevaplanabilir.
Amaç *en kısa* çözümü bulmak değil, **"herhangi bir çözüm var mı?"**
sorusuna evet/hayır demek. Bu ayrım tüm tasarım kararlarını belirler.

## Arama modeli: tek tahta üzerinde geri izlemeli DFS

Her hamle bir "durum"dan diğerine geçiştir; tüm olası durumlar bir ağaç gibi
düşünülebilir. DFS bu ağacı derinlemesine gezer: bir hamle **gerçekten
yapılır**, oradan derine inilir; o dal çıkmaza çıkarsa hamle **geri alınır**
ve sıradaki hamle denenir (backtracking). Bellekte hiçbir zaman ağacın tamamı
yoktur — tek bir tahta nesnesi, yap/geri-al ritmiyle ağacın üzerinde
gezdirilir. BFS'in elenme sebebi tam bu: BFS her durumu ayrı kopya olarak
saklamak zorunda (üstel bellek); A\* elenmesi de aynı mantık: en kısa çözüm
aranmıyor ki maliyetine katlanılsın.

Sonuç mantığı asimetriktir: **tek bir dalın** çözüme ulaşması "çözülebilir"
demek için yeter (arama anında biter, hamle zinciri çözüm olarak raporlanır);
"çözülemez" diyebilmek içinse **erişilebilir tüm durumların** tüketilmesi
gerekir.

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

## Doğruluğun görünmez sözleşmeleri

- Geri alma mekanizması tahtayı **bire bir** eski hâline döndürmek
  zorundadır; aksi hâlde arama sessizce yanlış tahtalarda devam ederdi.
- Hamle üretimi geçerli hamlelerin *tamamını* değil, **karar açısından
  kayıpsız bir alt kümesini** verir — kaybedilen yalnızca alternatif çözüm
  yollarıdır.
- Bu sözleşmeler, kuralları bağımsızca implemente eden Python solver'ıyla
  çapraz doğrulamada (8/8 birebir aynı karar) sınanmıştır — Python tarafı
  geri alma yerine taze kopya kullanır, yani aynı sonuca **farklı
  mekanizmayla** ulaşır; bu da doğrulamayı güçlendirir.
