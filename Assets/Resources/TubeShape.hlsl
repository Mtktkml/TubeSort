#ifndef TUBESORT_TUBE_SHAPE_INCLUDED
#define TUBESORT_TUBE_SHAPE_INCLUDED

// Tüpün şekli tek bir yerde tanımlanır; hem cam hem sıvı bu dosyayı kullanır.
// İkisi ayrı ayrı hesaplasaydı en küçük fark bile sıvının camdan taşmasına
// ya da içeride boşluk kalmasına yol açardı.

// İşaretli mesafe fonksiyonu (SDF): bir noktanın şeklin kenarına uzaklığını verir.
// Sonuç negatifse nokta şeklin içinde, pozitifse dışında, sıfırsa tam kenarında.
//
// "Şu piksel içeride mi?" sorusunu evet/hayır yerine bir mesafeyle cevaplamak
// iki şey kazandırır: kenarı yumuşatabiliriz (mesafeye göre alfa) ve şekli
// büyütüp küçültebiliriz (mesafeye sabit eklemek şekli şişirir/daraltır).
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

// Tüp gövdesi: üstte hafif yuvarlatılmış köşeler, altta tam yuvarlak dip.
//
// uv: dörtgenin doku koordinatı (0..1)
// size: tüpün dünya birimindeki boyutu. Gerekli, çünkü dörtgen yatayda ve
//       dikeyde farklı ölçeklendiğinden uv uzayında hesap yapmak köşeleri
//       elips yapardı. Dünya birimine çevirince köşeler gerçekten dairesel olur.
float SdTube(float2 uv, float2 size, float topRadius, float bottomRadius)
{
    float2 p = (uv - 0.5) * size;
    float2 halfSize = size * 0.5;
    float4 radii = float4(topRadius, bottomRadius, topRadius, bottomRadius);

    return SdRoundedBox(p, halfSize, radii);
}

#endif
