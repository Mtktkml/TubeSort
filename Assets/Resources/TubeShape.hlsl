#ifndef TUBESORT_TUBE_SHAPE_INCLUDED
#define TUBESORT_TUBE_SHAPE_INCLUDED

// Tüpün şekli tek bir yerde tanımlanır; hem cam hem sıvı bu dosyayı kullanır.
// İkisi ayrı ayrı hesaplasaydı en küçük fark bile sıvının camdan taşmasına
// ya da içeride boşluk kalmasına yol açardı.
//
// Tüp tek parçadır: dibi yarım daire, gövdesi düz, ağzına doğru yatayda
// hafifçe genişler. Genişleme ayrı bir parça değil, camın kendi devamıdır.
//
//     \_______/     <- ağız: gövdeden geniş
//      |     |
//      |     |      <- gövde: sıvı burada durur
//      |     |
//      \_____/      <- dip: tam yarım daire

// İşaretli mesafe fonksiyonu (SDF): bir noktanın şeklin kenarına uzaklığını verir.
// Sonuç negatifse nokta şeklin içinde, pozitifse dışında, sıfırsa tam kenarında.
//
// "Şu piksel içeride mi?" sorusunu evet/hayır yerine bir mesafeyle cevaplamak
// üç şey kazandırır: kenarı yumuşatabiliriz (mesafeye göre alfa), şekli
// büyütüp küçültebiliriz (mesafeye sabit eklemek şekli şişirir/daraltır) ve
// şekilleri birbirine kaynaştırabiliriz (aşağıdaki yumuşak birleşim).
//
// p: şeklin merkezine göre nokta
// b: yarı boyutlar (genişliğin ve yüksekliğin yarısı)
// r: köşe yarıçapları - sırasıyla (sağ üst, sağ alt, sol üst, sol alt)
float SdRoundedBox(float2 p, float2 b, float4 r)
{
    // Noktanın hangi köşeye yakın olduğuna göre yarıçapı seç.
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x = (p.y > 0.0) ? r.x : r.y;

    // Köşe yarıçapı kadar içeri çekilmiş bir dikdörtgene olan mesafe,
    // sonra yarıçap kadar geri şişir: yuvarlak köşe böyle oluşur.
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

// Yumuşak birleşim: iki şekli tek şekilde toplar, ama kesiştikleri yerde
// keskin köşe bırakmak yerine kavis oluşturur.
//
// Düz min(d1, d2) de iki şekli birleştirir, ancak birleşme yeri bir basamak
// gibi görünür. Burada geçiş bölgesinde iki mesafeyi harmanlayıp k kadar
// içeri çekiyoruz: gövdeden ağza geçiş basamak değil huni gibi akıyor.
// k büyüdükçe kavis yayvanlaşır.
float SdSmoothUnion(float d1, float d2, float k)
{
    float h = saturate(0.5 + 0.5 * (d2 - d1) / k);
    return lerp(d2, d1, h) - k * h * (1.0 - h);
}

// Doku koordinatını dünya birimine çevirir; orijin dörtgenin merkezinde olur.
// Dünya birimine geçmek şart: dörtgen yatayda ve dikeyde farklı ölçeklendiği
// için uv uzayında hesaplanan köşeler daire değil elips olurdu.
float2 QuadPoint(float2 uv, float2 quadSize)
{
    return (uv - 0.5) * quadSize;
}

// Gövde dörtgenin dibine hizalanır, yatayda ortalanır.
float2 BodyCenter(float2 quadSize, float2 bodySize)
{
    return float2(0.0, -quadSize.y * 0.5 + bodySize.y * 0.5);
}

// Ağız dörtgenin tepesine hizalanır, yatayda ortalanır.
float2 MouthCenter(float2 quadSize, float2 mouthSize)
{
    return float2(0.0, quadSize.y * 0.5 - mouthSize.y * 0.5);
}

// Tüpün tamamı: gövde ile ağız üst üste biner ve yumuşak birleşimle
// tek bir gövdeye kaynar. Aralarında boşluk yoktur; ağız gövdeden
// yalnızca daha geniştir.
float SdTube(float2 p, float2 quadSize, float2 bodySize, float2 mouthSize,
    float topRadius, float bottomRadius, float mouthRadius, float blend)
{
    float2 bodyLocal = p - BodyCenter(quadSize, bodySize);
    float4 bodyRadii = float4(topRadius, bottomRadius, topRadius, bottomRadius);
    float bodyDistance = SdRoundedBox(bodyLocal, bodySize * 0.5, bodyRadii);

    float2 mouthLocal = p - MouthCenter(quadSize, mouthSize);
    float mouthDistance = SdRoundedBox(mouthLocal, mouthSize * 0.5, mouthRadius.xxxx);

    return SdSmoothUnion(bodyDistance, mouthDistance, blend);
}

// Gövdenin kendi doku koordinatı: dibinde 0, tepesinde 1.
// Sıvı hesabı bu uzayda yapılır, böylece doluluk ve katman sınırları
// dörtgenin ağız için ayrılan fazlalığından etkilenmez.
float2 BodyUV(float2 p, float2 quadSize, float2 bodySize)
{
    float2 local = p - BodyCenter(quadSize, bodySize);

    return local / bodySize + 0.5;
}

#endif
