// Bir tüpün içindeki sıvının tamamını tek dörtgene çizer.
//
// Çekirdek "3 birim sarı, 1 birim mavi" der; buraya gelene kadar bu bilgi
// normalize edilmiş sınırlara dönüşür: _LayerTops = [0.75, 1.0].
// Her piksel iki soruya cevap arar:
//   1. Sıvı yüzeyinin altında mıyım?  -> alfa
//   2. Öyleysem hangi katmandayım?    -> renk
Shader "TubeSort/Liquid"
{
    Properties
    {
        _EdgeSoftness ("Yüzey yumuşaklığı (dünya birimi)", Float) = 0.012
        _SideShading ("Kenar gölgesi", Range(0, 1)) = 0.35
        // Sıvı kutusu (TubeView.Width) iç kontur çizgisine zaten dayanır ve
        // CAM SIVININ ÖNÜNDE çizilir: kenar bindirmeleri kontur örter. Pay bu
        // yüzden kozmetik düzeyde — büyütmek sıvıyla çanak arasında koyu bir
        // "oturmamış" şerit bırakıyordu (0.02'de yaşandı).
        _WallThickness ("Cam et kalınlığı", Float) = 0.005
        // 2.5D: hafif üstten bakış. Varsayılan, yaka perspektifiyle uyumlu:
        // sıvı yarı genişliği (0.375) × görsellerin elips oranı (0.2) ≈ 0.075.
        _SurfaceEllipse ("2.5D yüzey derinliği (dünya birimi)", Float) = 0.075
        _SurfaceLight ("Yüzey diski açık ton karışımı", Range(0, 1)) = 0.3
        // Damla halkaları: dökme BİTİNCE değme noktasından dışa yayılan eş
        // merkezli elips halkalar (su birikintisine damla düşmüş gibi) — patlama
        // zarfını TubeView.PlayRippleBurst sürer. Dökme SIRASINDA ise değme
        // noktasından iki yana damlacık sıçraması oynar (_SplashStrength).
        // Boşta yüzey DURGUN: sürekli dalga yok. Düşük sıklık = tek geniş halka +
        // kenarda sönen kuyruk; yüksek sıklık kıpır kıpır/testere görünür.
        _RippleFrequency ("Halka sıklığı (yarıçap başına)", Float) = 1.2
        _RippleSpeed ("Halka yayılma hızı", Float) = 2.2
        // Renk payı düşük tutulur, geçişler yumuşak — asıl iş nazik yüzey
        // kabarmasında (_RippleHeight).
        _RippleAmplitude ("Halka belirginliği", Range(0, 1)) = 0.22
        // Halkalar renkten ibaret değil: yüzey halka fazıyla inip çıkar
        // (çukur/tümsek). Dünya birimi — tüple ölçeklenmez.
        _RippleHeight ("Halka yüksekliği (dünya birimi)", Float) = 0.008
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

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

            // Bir tüpte en fazla bu kadar katman olabilir; TubeView.MaxLayers ile
            // aynı olmak zorunda. En kötü durumda katman sayısı tüp kapasitesine
            // eşittir (hiçbir bitişik renk aynı değilse), o yüzden bu sınır aynı
            // zamanda desteklenen en büyük kapasitedir. Döngü her piksel için
            // bu kadar tur döndüğünden gereğinden büyük tutulmaz.
            #define MAX_LAYERS 12

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
                float _SideShading;
                float _WallThickness;
                float _SurfaceEllipse;
                float _SurfaceLight;
                float _RippleFrequency;
                float _RippleSpeed;
                float _RippleAmplitude;
                float _RippleHeight;
            CBUFFER_END

            // Bu değerler her tüp için farklı; MaterialPropertyBlock ile
            // tüp tüp gönderilir, o yüzden CBUFFER dışında durur.
            float4 _LayerColors[MAX_LAYERS];
            float _LayerTops[MAX_LAYERS];
            float _FillLevel;
            int _LayerCount;
            float4 _QuadSize;
            float4 _BodySize;
            float _TopRadius;
            float _BottomRadius;
            // Sıvının gövde tepesinin ÜSTÜNE (halka arkasında ağza doğru)
            // tırmanabildiği pay (dünya birimi): kırpma kutusu yalnız üstten
            // bu kadar uzar, fill/katman matematiği (_BodySize) değişmez.
            // Dökme eğiminde dudağa bastırılan sıvı (kenar 1.05) artık gövde
            // tepesinde kırpılmaz — akış kolonuyla ağızda buluşur. Dinlenmede
            // yüzey en fazla FillSpan'e çıktığından bu bölge hiç boyanmaz.
            float _MouthOverflow;
            float _TiltAngle;
            // Eğik yüzeyin dudak demirlemesi (normalize, gövde oranı): düzlem
            // kaydırması dik açılarda dudaktaki sıvıyı gerçek (hacim korunumlu)
            // modelden alçak gösterir; BoardView farkı her kare buraya yazar,
            // yüzey o kadar kaldırılır — akış kolonu sıvıdan kopmaz.
            float _SurfaceLift;
            // Damla halkalarının anlık gücü (0-1): dökme bitince TubeView'ın
            // patlama zarfı (PlayRippleBurst) sürer — boşta durgun.
            float _RippleStrength;
            // Sıçrama gücü (0-1): akış bu tüpün yüzeyine aktığı sürece
            // BoardView 1'e sürer, akış kesilince 0'a.
            float _SplashStrength;
            // Level başı çalkantı eğimi (normalize): TubeView sönümleyerek
            // sürer; yüzey ve katmanlar birlikte sallanır.
            float _SwaySlope;
            // Tamamlanma efekti ilerlemesi (0-1): tüp tamamlanınca TubeView sürer.
            // >0 iken sıvı içinde yükselen kabarcıklar çizilir (tortu YOK). Boşta 0.
            float _CompletionProgress;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Kabarcık ızgarası için yalancı-rastgele 2B (hücre → konum + boy + faz).
            float2 BubbleHash(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453123);
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }


            half4 Fragment(Varyings input) : SV_Target
            {
                // Tüp tamamen boşsa hiçbir şey çizme.
                if (_FillLevel <= 0.0001)
                    discard;

                float2 p = QuadPoint(input.uv, _QuadSize.xy);

                // Sıvı camın içinde kalmalı. Camla aynı şekli hesaplayıp mesafeye
                // et kalınlığı ekliyoruz: SDF'de mesafeye sabit eklemek şekli
                // içeri doğru daraltır. Böylece sıvı camın bir tık içinden başlar
                // ve yuvarlak dibe kusursuz oturur - ayrı bir maske dokusu ve
                // piksel hizalama derdi olmadan.
                // Kırpma kutusu gövdeden _MouthOverflow kadar uzun (dip hizası
                // aynı: SdTube gövdeyi dörtgenin dibine yaslar) — sıvı dökme
                // eğiminde ağza doğru tırmanabilir.
                float2 clipSize = float2(_BodySize.x, _BodySize.y + _MouthOverflow);
                float glassDistance = SdTube(p, _QuadSize.xy, clipSize,
                    _TopRadius, _BottomRadius);
                float innerDistance = glassDistance + _WallThickness;

                float innerEdge = fwidth(innerDistance);
                float insideGlass = 1.0 - smoothstep(-innerEdge, innerEdge, innerDistance);
                if (insideGlass <= 0.001)
                    discard;

                // Bundan sonraki hesaplar gövdenin kendi uzayında: uv.y 0 = dip,
                // 1 = gövdenin tepesi. Dörtgen ağız için fazladan uzun olduğundan
                // dörtgenin uv'siyle çalışsaydık doluluk ve katman sınırları kayardı.
                float2 uv = BodyUV(p, _QuadSize.xy, _BodySize.xy);

                // Yumuşaklık dünya biriminde tanımlıdır; burada gövdenin oranına
                // çevrilir. Doğrudan uv'de kullanılsaydı uzun tüpte kenar da
                // orantılı yumuşar, kısa tüpten farklı görünürdü.
                float edgeSoftness = _EdgeSoftness / _BodySize.y;

                // Tüp döndüğünde sıvı yüzeyi dünya uzayında yatay kalmalı.
                // UV uzayında bunu sağlamak için yüzeyi eğim açısına göre
                // ters yöne eğiyoruz. sin/cos oranı (tan) geometrik olarak doğru
                // eğimi verir. Kelepçe 0.03 (~88.3°): fiziksel eğim yaklaşımında
                // tüp sıvıyı ağza ulaştırmak için ~88°'ye kadar eğilir; kelepçe
                // 0.2 (~78.7°) olsaydı yüzey orada fazla SIĞ kalıp sıvı dudağa
                // ulaşamazdı. Aşağı sınır yalnız 90°'de bölme patlamasını önler
                // (BoardView.MaxPourAngle ile eş; twin sabit). sin'in işareti her
                // açıda doğru yönü korur.
                float tiltSlope = sin(_TiltAngle) / max(abs(cos(_TiltAngle)), 0.03);
                float tiltOffset = (0.5 - uv.x) * tiltSlope
                    * (_BodySize.x / _BodySize.y);

                // Level başı çalkantısı: eğimle aynı biçimde yüzeyi (ve tiltOffset
                // üzerinden katman sınırlarını) sallar; TubeView sönümleyerek
                // sıfıra indirir.
                tiltOffset += (0.5 - uv.x) * _SwaySlope * (_BodySize.x / _BodySize.y);

                // Yüzey boşta DURGUN: sürekli dalga yok, hareket yalnız dökme
                // sırasındaki damla halkalarından gelir.
                //
                // HACİM KORUMALI TABAN. Eğik tüpte düz "shear" yüzeyi
                // (_FillLevel + eğim) yalnız yüzey iki duvarı da kesiyorken
                // (bol sıvı) doğru hacmi verir. Sıvı azken kapalı uçtaki yüzey
                // tüp dibinin ALTINA taşar; alttan kırpılınca çizilen hacim
                // _FillLevel'i AŞAR (sıvı boşalmıyormuş gibi görünür — kap>=5'te
                // son 1-2 birimde belirgindi). Onun yerine az sıvıda tabanı,
                // döken kenarda hacmi tam _FillLevel olan bir ÜÇGEN verecek
                // şekilde alçaltıyoruz: edge = sqrt(2·|k|·fill). Böylece son
                // birim ağızda düzgün toplanıp biter. Dik/boş tüpte k=0 →
                // taban = _FillLevel (değişmez; hedef tüp etkilenmez).
                float slopeUV = tiltSlope * (_BodySize.x / _BodySize.y);
                float halfRise = 0.5 * abs(slopeUV);
                float surfaceBase = (_FillLevel >= halfRise)
                    ? _FillLevel
                    : sqrt(2.0 * abs(slopeUV) * _FillLevel) - halfRise;

                // _SurfaceLift: eski dudak demirlemesi. Hacim-korumalı taban +
                // gerçek eğim (kelepçe 0.03) sıvıyı zaten doğru kenara/dudağa
                // getirdiği için pratikte ≈0 (bkz. AnchorLiquidToLip); anchor
                // mantığı bozulmasın diye yine de eklenir.
                float surface = surfaceBase + tiltOffset + _SurfaceLift;

                // ── 2.5D: hafif üstten bakış. Yüzey, üstten görünen ELİPS bir
                // disk; katman sınırları da diskin ÖN yayı gibi aşağı kavisli.
                // nx: sıvı genişliğinde -1..1; arc: elips yay profili (kenarda
                // 0, ortada 1). Efekt eğik (döken) tüpte de sürer: bant eğimli
                // yüzey çizgisini izlediği için disk yana yatmış mercek olarak
                // okunur.
                float halfWidthUV = (_BodySize.x * 0.5 - _WallThickness) / _BodySize.x;
                float nx = clamp((uv.x - 0.5) / halfWidthUV, -1.0, 1.0);
                float arc = sqrt(saturate(1.0 - nx * nx));
                float ellipseDepth = _SurfaceEllipse / _BodySize.y;

                // Halka patlaması yüzeyi fiziksel olarak da dalgalandırır:
                // radyal fazla inip çıkan nazik çukur/tümsekler. Radyal koordinat
                // yumuşatılmış (sqrt(nx²+ε)): çıplak |nx| merkezde köşe yapıp
                // zikzak görünür. surface'a eklendiği için disk, katmanlar ve
                // kenar birlikte dalgalanır.
                float rippleR = sqrt(nx * nx + 0.02);
                surface += sin((rippleR * _RippleFrequency
                    - _Time.y * _RippleSpeed) * 6.2832)
                    * _RippleStrength * (1.0 - 0.6 * rippleR)
                    * (_RippleHeight / _BodySize.y);

                // Sıvının üst kenarı diskin ARKA yayı (yüzey çizgisinin
                // ellipseDepth·arc kadar üstü); disk bölgesi aşağıda açık
                // tonla boyanır. Yüzeyin altındaysak 1, üstündeysek 0; dar
                // bant kenarın testere gibi görünmesini engeller.
                float surfaceTop = surface + ellipseDepth * arc;
                float inside = smoothstep(surfaceTop, surfaceTop - edgeSoftness, uv.y);

                // Sıçrama: dökme sırasında değme noktasından (disk merkezi) iki
                // yana fırlayıp geri düşen damlacıklar — yüzeyin ÜSTÜNE çizilir,
                // discard bu yüzden sıçramayı da bekler. Koordinatlar dünya
                // biriminde: damlacık boyu/yayı tüple ölçeklenmez.
                float splash = 0.0;
                if (_SplashStrength > 0.001)
                {
                    float liquidHalfW = _BodySize.x * 0.5 - _WallThickness;
                    float wx = nx * liquidHalfW;
                    float wy = (uv.y - surface) * _BodySize.y;
                    for (int s = 0; s < 8; s++)
                    {
                        float dir = (s < 4) ? -1.0 : 1.0;
                        float k = (float)(s % 4);
                        // Faz, menzil ve boy damlacık başına değişir: tek tip
                        // 4 damla mekanik durur; çeşitlilik gerçek sıçrama gibi
                        // okunur.
                        float phase = k * 0.27 + dir * 0.13;
                        float reach = 0.20 + 0.05 * k;
                        float rise = 0.42 + 0.07 * ((k + 1.0) % 3.0);
                        // d: 0..1 uçuş döngüsü; yanlara açılırken parabolik
                        // yükselip düşer, uçuş sonunda söner.
                        float d = frac(_Time.y * (1.5 + 0.25 * k) + phase);
                        float2 c = float2(dir * d * reach,
                            0.03 + rise * d * (1.0 - d));
                        float r = (0.036 - 0.004 * k) * (1.0 - 0.45 * d);
                        float drop = 1.0 - smoothstep(r * 0.55, r,
                            length(float2(wx, wy) - c));
                        splash = max(splash, drop * (1.0 - d * d));
                    }
                    splash *= _SplashStrength;
                }

                if (inside <= 0.001 && splash <= 0.001)
                    discard;

                // (Eskiden burada "son ~1 birim ağıza çekilme" için heuristik
                // bir drain-clip vardı: _FillLevel<0.2 iken kapalı uçtaki düşük
                // score'lu pikselleri siliyordu. KALDIRILDI — yüzey artık gerçek
                // eğimle çizildiği için (yukarıda _SurfaceLift * mouthDir) sıvı
                // doğru hacimde küçülüp döken kenarda kendiliğinden üçgen olarak
                // toplanıyor. O clip artık gereksiz ve kap>=5'te (birim < 0.2)
                // doğru üçgeni fazladan siliyordu.)

                // Bu piksel hangi katmanda? Katman sınırları dipten yukarı sıralı,
                // o yüzden "üstünde kaldığım son sınır" katman indeksini verir.
                // Sınırlar yüzeyle AYNI hacim-korumalı tabanla çizilir: sınır_j,
                // altında kalan toplam hacmi (_LayerTops[j]) koruyan yükseklikte.
                // Düz kayma (sabit tiltOffset) dik açılarda sınır düzlemlerini
                // tüp dibinin altına taşırıyor, alt katmanlar iplik gibi incelip
                // hacimlerini kaybediyordu; üst katman dökülürken inen yüzey de
                // alttakilerin bölgesini kesip onları "dökülüyor" gösteriyordu.
                // Hacim-korumalı sınırla her katman eğik tüpte de gerçek payını
                // kaplar (az hacimde ağız köşesinde iç içe kamalar) ve üst katman
                // dökülürken yüzey hiçbir zaman alt sınırın altına inmez.
                int layerIndex = 0;
                for (int i = 0; i < MAX_LAYERS; i++)
                {
                    float top = _LayerTops[i];
                    float topBase = (top >= halfRise)
                        ? top
                        : sqrt(2.0 * abs(slopeUV) * top) - halfRise;
                    // Sınır, 2.5D diskin ön yayı gibi ortada ellipseDepth kadar
                    // aşağı kavisli (üstten bakışta kesitin ön kenarı alçak görünür).
                    if (i < _LayerCount && uv.y >= topBase + tiltOffset
                        - ellipseDepth * arc)
                        layerIndex = i + 1;
                }
                layerIndex = clamp(layerIndex, 0, _LayerCount - 1);

                float4 color = _LayerColors[layerIndex];

                // Silindir yanılsaması: kenarlara doğru koyulaşma.
                float distanceFromCenter = abs(uv.x - 0.5) * 2.0;
                float shade = 1.0 - distanceFromCenter * distanceFromCenter * _SideShading;
                color.rgb *= shade;

                // Parlama BURADA ÇİZİLMEZ: sıvı camın ARKASINDA durur (sıvı
                // order 0 < cam gövde 2) ve camın gömülü parlamaları — duvar
                // yansıma çizgileri, yumuşak bantlar, dip parlaması, iç tint —
                // sıvının üstüne kendiliğinden düşer. Boş ve dolu tüpün
                // parlaması böylece tanımı gereği birebir aynıdır. (Önce sıvı
                // içinde bant taklidi denendi; 9-slice esnemesi ve algı
                // farkları yüzünden hiza hiç birebir tutmadı — mimari değişti.)

                // 2.5D yüzey diski: yüzey çizgisinin ±ellipseDepth·arc bandı,
                // üstten görünen elips — en üst katmanın açık tonu. Gölge ve
                // şeritten SONRA basılır ki disk temiz, düz bir yüzey okunsun.
                // Kenarlarda arc sıfıra indiği için disk cama sivrilerek kapanır.
                float discEdge = ellipseDepth * arc - abs(uv.y - surface);
                float inDisc = smoothstep(0.0, edgeSoftness, discEdge);
                float3 surfaceTone = lerp(_LayerColors[_LayerCount - 1].rgb,
                    float3(1.0, 1.0, 1.0), _SurfaceLight);
                float3 discColor = surfaceTone;

                // Damla halkaları: akış kolonu tüpün merkezine indiği için
                // değme noktası disk merkezi. rho: diskin elips-normalize
                // yarıçapı (merkez 0, kenar 1). Halkalar dışa doğru kayar.
                float dy = (uv.y - surface) / max(ellipseDepth, 1e-4);
                float rho = sqrt(saturate(nx * nx + dy * dy));
                float ringWave = 0.5 + 0.5 * sin((rho * _RippleFrequency
                    - _Time.y * _RippleSpeed) * 6.2832);
                // Geniş smoothstep aralığı (0.15-0.85) nazik bir ışık oyunu
                // bırakır; dar aralık keskin bantlar verir, göze batar.
                float rings = smoothstep(0.15, 0.85, ringWave);
                float ringMask = _RippleStrength * _RippleAmplitude
                    * (1.0 - 0.6 * rho);
                discColor = lerp(discColor, float3(1.0, 1.0, 1.0),
                    rings * ringMask);
                discColor = lerp(discColor, _LayerColors[_LayerCount - 1].rgb,
                    (1.0 - rings) * ringMask * 0.5);

                color.rgb = lerp(color.rgb, discColor, inDisc);

                // ── Kabarcıklar (tamamlanma efekti): sıvı içinde dipten yüzeye
                // yükselen küçük kabarcıklar. Yalnız _CompletionProgress>0 iken ve
                // yüzeyin ALTINDA (inside). Yüzeye yaklaşınca söner (patlar). TORTU
                // YOK — sadece yukarı akan kabarcıklar (kullanıcı isteği).
                if (_CompletionProgress > 0.001 && inside > 0.001)
                {
                    // Zarf: efektin ortasında yoğun, başta/sonda sön.
                    float bubEnv = smoothstep(0.0, 0.15, _CompletionProgress)
                                 * (1.0 - smoothstep(0.75, 1.0, _CompletionProgress));

                    // Izgara-hash kabarcıklar; hücreler aşağı kayar → yukarı akış.
                    const float bcols = 4.0;
                    const float brows = 7.0;
                    float brise = _Time.y * 0.5 + _CompletionProgress * 0.7;
                    float2 bg = float2(uv.x * bcols, (uv.y - brise) * brows);
                    float2 bcell = floor(bg);
                    float2 bf = frac(bg) - 0.5;

                    float2 bh = BubbleHash(bcell);
                    float bpresent = step(0.5, bh.x);   // seyreklik

                    // Hücre-yerel konumu dünya oranına çevir → yuvarlak kabarcık.
                    float2 bq = (bf - (bh - 0.5) * 0.5)
                        * float2(_BodySize.x / bcols, _BodySize.y / brows);
                    float bd = length(bq);

                    float bubR = 0.020 + 0.012 * bh.y;   // kabarcık boyu değişir
                    float bcore = 1.0 - smoothstep(0.0, bubR, bd);
                    float brim = 1.0 - smoothstep(0.0, 0.008, abs(bd - bubR));
                    float bub = (bcore * 0.3 + brim) * bpresent;

                    // Yüzeye yaklaşınca sön (üst %10'da patlar).
                    float bubFade = 1.0 - smoothstep(surface - 0.10, surface, uv.y);

                    float bubAmount = saturate(bub * bubEnv * bubFade) * 0.5;
                    color.rgb = lerp(color.rgb, float3(1.0, 1.0, 1.0), bubAmount);
                }

                // Damlacıklar yüzey tonunda: sıvının üstünde kaldıkları yerde
                // (inside≈0) renk katman döngüsünden gelir, açık tona çekilir.
                color.rgb = lerp(color.rgb, surfaceTone, saturate(splash - inside));

                color.a *= max(inside, splash) * insideGlass;
                return (half4)color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
