#ifndef TUBESORT_TUBE_SHAPE_INCLUDED
#define TUBESORT_TUBE_SHAPE_INCLUDED

// Sıvının şekli tek bir yerde tanımlanır; sıvı shader'ı bu dosyayı kullanır,
// CPU tıklama doğrulaması (TubeView.SdTube) aynı formülleri C#'ta uygular.
// İki taraf ayrı hesaplasaydı en küçük fark bile sıvının tıklama alanından
// ya da cam görselinin içinden sapmasına yol açardı.
//
// Tüp tek parçadır: dibi yarım daire, gövdesi düz. (Görseldeki ağız
// genişlemesi bej yakanın işi; eski ağız genişletme parametre zinciri
// etkisiz kaldığı için kaldırıldı.)
//
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

// Sıvının şekli: yalnızca gövde kutusu — dibi yarım daire, tepesi hafif
// yuvarlak köşe. (Görseldeki ağız genişlemesi bej yakanın işi; eski ağız
// parametre zinciri etkisiz kaldığı için imzadan da temizlendi.)
float SdTube(float2 p, float2 quadSize, float2 bodySize,
    float topRadius, float bottomRadius)
{
    float2 bodyLocal = p - BodyCenter(quadSize, bodySize);
    float4 bodyRadii = float4(topRadius, bottomRadius, topRadius, bottomRadius);
    return SdRoundedBox(bodyLocal, bodySize * 0.5, bodyRadii);
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
