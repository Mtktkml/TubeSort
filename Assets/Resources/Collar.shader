// Tüpün ağzındaki bej yaka: hafif açıyla görülen dairesel pul — simit gibi.
// Silüet: TEK DÜZ ELİPS. Kompozit denemeler (kapsül+bombe, yay şeridi) birleşim
// yerlerinde kıvrım/dalga yaratıyordu; tek elipste eğrilik her yerde pürüzsüz,
// dikey eksene simetrik.
//
// İKİ KATMAN, tıpayı sandviçler (TubeView kurar):
//   _FrontOnly=0 (order 2, ARKA): tam silüet + koyu delik + seam. Boş tüpte
//     görünen budur; tıpalıyken tıpanın arkasında kalır.
//   _FrontOnly=1 (order 4, ÖN): yalnız delik MERKEZİNİN altındaki ön bant;
//     delik içi şeffaf; tıpa gövdesi yalnız PARANTEZ ÇİZGİSİNİN ALTINDAKİ
//     şeffaf pencereden görünür (delik-parantez arası bej tıpayı örter),
//     silüet alt kenarında kontur + çok ince bej şerit tıpanın ÖNÜNDE kalır
//     (referans tube (2)).
// Seam: ön yüz çizgisi — deliğin ALTINDA, ön bandın (delik altı → alt
// kontur) üst 1/3 diliminin ortasından geçen aşağı-bombeli PARANTEZ:
// kontur renginde, gövdesi sabit kalınlıkta, uçları sivrilerek biter
// (referans tube2). Yatay krem gradyan: sağ açık/sarımsı.
Shader "TubeSort/Collar"
{
    Properties
    {
        _CollarLight ("Krem açık (sağ)", Color) = (0.99, 0.96, 0.82, 1)
        _CollarDark ("Krem koyu (sol)", Color) = (0.91, 0.85, 0.70, 1)
        _HoleColor ("Delik (koyu)", Color) = (0.20, 0.11, 0.07, 1)
        _OutlineColor ("Kontur", Color) = (0.11, 0.09, 0.10, 1)
        _OutlineWidth ("Kontur kalınlığı", Float) = 0.025
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TubeShape.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CollarLight;
                float4 _CollarDark;
                float4 _HoleColor;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // Per-quad (MaterialPropertyBlock):
            float4 _CollarQuad;   // dörtgen dünya boyutu
            float4 _TopRadii;     // (x: Rx yarı genişlik, y: yay yükselmesi ARCH)
            float4 _HoleRadii;    // delik yarıçapları (hx, hy)
            float _SideHalf;      // bandın YARI KALINLIĞI (stroke yarıçapı)
            float _HoleCenterY;   // delik merkezi y — ön/arka katman ayrım hattı
            float _FrontOnly;     // 1: ön katman (tıpanın önünde)
            float4 _CorkSilRadii; // tıpa tepe elipsi (rx, ry) — pencere silüeti
            float4 _CorkSilYs;    // (yTop, yStep, yBase, tıpa merkezi − yaka merkezi)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            // Elipse yaklaşık işaretli mesafe (Quilez). Negatif = içeride.
            float SdEllipse(float2 p, float2 ab)
            {
                float k1 = length(p / ab);
                float k2 = length(p / (ab * ab));
                return k1 * (k1 - 1.0) / max(k2, 1e-4);
            }

            // ── Tıpa penceresi silüeti — Cork.shader'daki SdTrapezoid/CorkSil'in
            // KOPYASI (0.89/0.80/0.70 genişlik oranları ve 0.02 köşe payı —
            // Cork _CornerRadius varsayılanı — dahil). Shader'lar arası kod
            // paylaşılamadığından tekrar kaçınılmaz; TIPA ŞEKLİ DEĞİŞİRSE İKİSİ
            // BİRLİKTE GÜNCELLENMELİ. Ön katman bu silüetin içini parantez
            // çizgisinin altında şeffaf bırakır ki tıpa oradan görünsün.
            float SdTrapezoid(float2 p, float r1, float r2, float he)
            {
                float2 k1 = float2(r2, he);
                float2 k2 = float2(r2 - r1, 2.0 * he);
                p.x = abs(p.x);
                float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
                float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot(k2, k2), 0.0, 1.0);
                float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
                return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
            }

            float CorkWinSil(float2 q)
            {
                float yTop = _CorkSilYs.x, yStep = _CorkSilYs.y, yBase = _CorkSilYs.z;
                float W = _CorkSilRadii.x;
                float wStepTop = 0.89 * W;   // büyük koni ALT (kademe üstü)
                float wStepBot = 0.80 * W;   // küçük koni ÜST (içeri basamak)
                float wBase    = 0.70 * W;   // taban

                float dCap = SdEllipse(q - float2(0.0, yTop), _CorkSilRadii.xy);
                float dUpper = SdTrapezoid(q - float2(0.0, (yTop + yStep) * 0.5),
                    wStepTop, W, (yTop - yStep) * 0.5);
                float dLower = SdTrapezoid(q - float2(0.0, (yStep + yBase) * 0.5),
                    wBase, wStepBot, (yStep - yBase) * 0.5);
                float dBase = SdEllipse(q - float2(0.0, yBase), float2(wBase, wBase * 0.35));
                return min(min(dCap, dUpper), min(dLower, dBase)) - 0.02;
            }


            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = QuadPoint(input.uv, _CollarQuad.xy);

                float Rx = _TopRadii.x;
                float arch = _TopRadii.y;   // elips yarı-yüksekliğine ek pay
                float halfT = _SideHalf;    // bandın yarı kalınlığı

                // Silüet: TEK DÜZ ELİPS — simit görünümü. Ön (alt) yarı %20 daha
                // yassı: izleyiciye yakın taraf arka kadar şişkin durmasın diye
                // (kullanıcı isteği). Ekvatorda (y=0) iki yarım elips aynı değeri
                // verir → geçiş kesiksiz; eğrilik her yerde pürüzsüz.
                float ryTop = halfT + arch;
                float ryBot = ryTop * 0.8;
                float ry = (p.y >= 0.0) ? ryTop : ryBot;
                float dSil = SdEllipse(p, float2(Rx, ry));

                float e = fwidth(dSil);

                // Delik mesafesi iki katmanda da lazım. TÜM fwidth türevleri
                // discard'dan ÖNCE hesaplanır: ıraksak discard sonrası türev
                // mobilde tanımsızdır ve kesim hattında piksel dikişi yapabilir.
                float2 pHole = p - float2(0.0, _HoleCenterY);
                float dHole = SdEllipse(pHole, _HoleRadii.xy);
                float eh = fwidth(dHole);

                // Seam omurgası + KENDİ AA'sı da discard öncesi. KONUM kullanıcı
                // tarifinden türetilir: ön bant = delik alt kenarı (yayı 0.012
                // dahil) ile alt kontur iç kenarı arası; çizgi bu bandın üst
                // 1/3 diliminin ORTASINDAN geçer (bant/6 kadar delik altına).
                // Omurga: basık yardımcı elipsin ALT yayı — üst yayı silüet
                // tepesinin (ryTop) dışında kaldığından kendiliğinden görünmez.
                // AA için dSil'in e'si YETMEZ: 3.2:1 basıklıkta Quilez gradyanı
                // uçlara doğru 1'den sapıyor, yanlış ölçekli AA uçları eritip
                // çizgiyi ortada "bıyık" gibi bırakıyordu.
                float frontTop = _HoleCenterY - _HoleRadii.y - 0.012;
                float frontBot = _OutlineWidth - ryBot;
                float seamY = frontTop - (frontTop - frontBot) / 6.0;
                float2 seamR = float2(0.88 * Rx, 0.825 * halfT);
                float2 seamCtr = float2(0.0, seamY + seamR.y);
                float dSeam = SdEllipse(p - seamCtr, seamR);
                float es = fwidth(dSeam);

                // Tıpa penceresi mesafesi de discard öncesi (fwidth kuralı).
                // Tıpa koordinatına geçiş: tıpa merkezi, yaka merkezinin
                // _CorkSilYs.w kadar üstünde.
                float dCork = CorkWinSil(float2(p.x, p.y - _CorkSilYs.w));
                float ew = fwidth(dCork);

                // Pencerenin alt sınır ovali (bkz. ön katman koşul 3): tıpanın
                // dip ovaliyle aynı basıklıkta (0.35), tıpadan geniş (1.25×)
                // elips — yalnız alt yayı sınır olur. fwidth discard öncesi.
                float2 ovalR = float2(1.25, 0.4375) * _CorkSilRadii.x;
                float dOval = SdEllipse(p - float2(0.0, 0.042), ovalR);
                float eo = fwidth(dOval);

                float inside = 1.0 - smoothstep(-e, e, dSil);
                if (inside <= 0.001)
                    discard;

                // Krem taban: yatay gradyan (sol koyu krem → sağ açık/sarımsı)
                // + hafif dikey hacim (üst açık, alt koyu).
                float hx = saturate(p.x / max(Rx, 1e-4) * 0.5 + 0.5);
                half3 col = lerp(_CollarDark.rgb, _CollarLight.rgb, hx);
                float vy = saturate(p.y / max(halfT + arch, 1e-4) * 0.5 + 0.5);
                col *= lerp(0.90, 1.04, vy);

                float alpha = inside;

                if (_FrontOnly > 0.5)
                {
                    // Ön katman yalnız delik merkez hattının ALTINI gösterir; kesim
                    // discard ile değil ALFA ile (türev güvenliği). Kesim çizgisi
                    // görünmez: hattın iki yanında renkler birebir aynı.
                    alpha *= step(p.y, _HoleCenterY);
                    // Delik içi ŞEFFAF pencere: arkadaki tıpa görünür; tıpanın
                    // görünen tabanı deliğin ön kenarına oturur.
                    alpha *= smoothstep(-eh, eh, dHole);
                    // Tıpa gövdesi penceresi (referans tube (2)): tıpa YALNIZ
                    // parantez çizgisinin ALTINDA görünür — delik ile parantez
                    // arasında bej tıpayı örter (deliğin üstünde tıpa zaten
                    // arka katmanın önünde). Pencere üç koşulun kesişimi:
                    //   1) tıpa silüetinin içi — kenardan 0.01 içeride biter:
                    //      bej-tıpa geçişi tıpanın koyu konturuna düşer, arada
                    //      fon sızmaz;
                    //   2) parantezin ALTI — sınır, strok yarı-kalınlığının
                    //      (0.012) hemen İÇİNDE (0.010): tıpa çizgiye DEĞER,
                    //      arada bej sızmaz (kullanıcı isteği; çizginin yenen
                    //      ~0.002'lik alt kenarı fark edilmez);
                    //   3) tıpanın görünen ALT SINIRI aşağı DIŞBÜKEY oval yay
                    //      (dOval, discard öncesi hesap) — düz kesim yerine
                    //      tıpanın kendi dip ovali gibi biter (kullanıcı
                    //      isteği). Merkez y (0.042) öyle ki alt yay kademe
                    //      konturunun üstünden (x≈±0.27..0.34'te y≈-0.10)
                    //      ~0.03 payla geçer — 0.017'lik ilk deneme payı dar
                    //      bırakmıştı, AA + kademe üstü gölgeyle çizgi
                    //      sırıtıyordu. Merkezde yay -0.105'e iner; altta
                    //      kontur + ~0.06 bej şerit yine tıpanın önündedir.
                    // Tıpasız tüpte bu katman zaten kapalı.
                    float corkCut = 1.0 - smoothstep(-ew, ew, dCork + 0.01);
                    corkCut *= smoothstep(0.010 - es, 0.010 + es, dSeam);
                    corkCut *= 1.0 - smoothstep(-eo, eo, dOval);
                    alpha *= 1.0 - corkCut;
                    // Deliğin ön yayı: ince koyu temas çizgisi.
                    float frontRim = 1.0 - smoothstep(0.012 - eh, 0.012 + eh, abs(dHole));
                    col = lerp(col, _OutlineColor.rgb, frontRim * 0.6);
                }
                else
                {
                    // Arka katman: koyu delik dolgusu (boş tüpte görünür); arka
                    // kenarı biraz açık (derinlik), ince kendi kenarı.
                    float inHole = 1.0 - smoothstep(-eh, eh, dHole);
                    half3 holeCol = _HoleColor.rgb * lerp(1.0, 1.5,
                        saturate(pHole.y / max(_HoleRadii.y, 1e-4) * 0.5 + 0.5));
                    col = lerp(col, holeCol, inHole);
                    float holeRim = 1.0 - smoothstep(0.012 - eh, 0.012 + eh, abs(dHole));
                    col = lerp(col, _OutlineColor.rgb, holeRim * 0.5);
                }

                // Seam çizimi — PARANTEZ profili. Renk kontur rengiyle AYNI
                // (kullanıcı isteği: ayrı _SeamColor soluk kalıyordu,
                // kaldırıldı). Yarı-kalınlık orta ~%65 boyunca SABİT 0.012
                // (delik kenar çizgisiyle aynı), son ~%35'te sivri uca incelir:
                // parabolik incelme (önceki deneme) ortayı da inceltip uçları
                // eritiyor, çizgiyi "bıyık" gibi bırakıyordu. Son maske yalnız
                // güvenlik: sıfır kalınlığın AA bandı tipX ötesinde hayalet iz
                // bırakmasın. Görünür yay tamamen ön bantta (y < delik merkezi)
                // kaldığı için ön/arka katman kesim hattı sürekliliği bozulmaz;
                // tıpalıyken çizgi tıpanın önünde kalır (ön yüzey boyası).
                float tipX = 0.76 * Rx;
                float wSeam = 0.012 * smoothstep(tipX, 0.65 * tipX, abs(p.x));
                float seam = (1.0 - smoothstep(wSeam - es, wSeam + es, abs(dSeam)))
                    * smoothstep(tipX, 0.95 * tipX, abs(p.x));
                col = lerp(col, _OutlineColor.rgb, seam);

                // Tek kesintisiz koyu kontur.
                float outline = 1.0 - smoothstep(_OutlineWidth - e, _OutlineWidth + e, abs(dSil));
                col = lerp(col, _OutlineColor.rgb, outline);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
