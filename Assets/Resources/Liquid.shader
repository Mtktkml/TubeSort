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
        _Glossiness ("Şerit parlaklığı", Range(0, 1)) = 0.5
        _WallThickness ("Cam et kalınlığı", Float) = 0.05
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
            #define MAX_LAYERS 8

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
                float _SideShading;
                float _Glossiness;
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
                float glassDistance = SdTube(p, _QuadSize.xy, _BodySize.xy,
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
                // eğimi verir. cos küçüldükçe oran büyür; 0.2'nin altına inmemesi
                // yüzeyin tüp dışına taşmasını önler. sin'in işareti her açıda
                // doğru yönü korur (tan 90°'de işaret değiştirir, bu formül değiştirmez).
                float tiltSlope = sin(_TiltAngle) / max(abs(cos(_TiltAngle)), 0.2);
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
                float k = tiltSlope * (_BodySize.x / _BodySize.y);
                float halfRise = 0.5 * abs(k);
                float surfaceBase = (_FillLevel >= halfRise)
                    ? _FillLevel
                    : sqrt(2.0 * abs(k) * _FillLevel) - halfRise;

                // _SurfaceLift: dökme sırasında dudak demirlemesi. Eğim tavanı
                // ~65°'de tutulduğu için (bkz. CalculatePourAngle) pratikte 0;
                // anchor mantığı bozulmasın diye yine de eklenir.
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
                // Tüp eğildiğinde katman sınırları da yüzeyle aynı açıda eğilir:
                // yerçekimi tüm sıvılara eşit etki eder.
                int layerIndex = 0;
                for (int k = 0; k < MAX_LAYERS; k++)
                {
                    // Sınır, 2.5D diskin ön yayı gibi ortada ellipseDepth kadar
                    // aşağı kavisli (üstten bakışta kesitin ön kenarı alçak görünür).
                    if (k < _LayerCount && uv.y >= _LayerTops[k] + tiltOffset
                        - ellipseDepth * arc)
                        layerIndex = k + 1;
                }
                layerIndex = clamp(layerIndex, 0, _LayerCount - 1);

                float4 color = _LayerColors[layerIndex];

                // Silindir yanılsaması: kenarlara doğru koyulaşma.
                float distanceFromCenter = abs(uv.x - 0.5) * 2.0;
                float shade = 1.0 - distanceFromCenter * distanceFromCenter * _SideShading;
                color.rgb *= shade;

                // Cam görselindeki (tube.png) beyaz şeritlerin sıvı bölgesindeki
                // devamı: cam arkada kaldığı için dolu bölgede şeridi sıvı
                // çizmeli. Konum/aralıklar görselden türetilmez, şeritlere elle
                // hizalanır; görsel değişirse birlikte ayarlanmalı.
                float streakX = smoothstep(0.05, 0.0, abs(uv.x - 0.20));
                float longY  = smoothstep(0.50, 0.53, uv.y) * smoothstep(0.85, 0.82, uv.y);
                float shortY = smoothstep(0.32, 0.35, uv.y) * smoothstep(0.45, 0.42, uv.y);
                float streak = streakX * max(longY, shortY);
                // Mavimsi beyaz — görseldeki şerit tonuyla aynı aile.
                color.rgb = lerp(color.rgb, float3(0.94, 0.97, 1.0), streak * _Glossiness);

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
