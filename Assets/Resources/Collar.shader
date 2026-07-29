// Tüpün ağzındaki bej yaka: hafif açıyla görülen dairesel pul — simit gibi.
// Silüet: TEK DÜZ ELİPS. Kompozit denemeler (kapsül+bombe, yay şeridi) birleşim
// yerlerinde kıvrım/dalga yaratıyordu; tek elipste eğrilik her yerde pürüzsüz,
// dikey eksene simetrik.
//
// İKİ KATMAN, tıpayı sandviçler (TubeView kurar):
//   _FrontOnly=0 (order 2, ARKA): tam silüet + koyu delik + seam. Boş tüpte
//     görünen budur; tıpalıyken tıpanın arkasında kalır.
//   _FrontOnly=1 (order 4, ÖN): yalnız delik MERKEZİNİN altındaki ön bant;
//     delik içi ŞEFFAF pencere (arkadaki tıpa görünür, tabanı deliğin ön
//     kenarına oturur), delik ön yayı ince koyu çizgi.
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
