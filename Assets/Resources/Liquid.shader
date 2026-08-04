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
        // TubeView.Width artık camın İÇ boşluğu (104 px): sıvı kutusu duvara
        // zaten dayanır, buradaki pay yalnız küçük bir sıkı-oturma kenarı.
        _WallThickness ("Cam et kalınlığı", Float) = 0.02
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

            // Camdaki yumuşak parlama bandının sıvı içindeki taklidi: dikey
            // bant, uçları eğik kesilmiş (paralelkenar), altta soluk yukarı
            // doğru netleşir — camdaki bantların el yazısıyla aynı.
            // cx/halfW: yatay merkez ve yarı genişlik (sıvı uv'si);
            // y0..y1: dikey pencere; slant: uç kesimlerinin eğikliği.
            float GlassBand(float2 uv, float cx, float halfW,
                float y0, float y1, float slant)
            {
                float ndx = clamp((uv.x - cx) / halfW, -1.0, 1.0);
                float xMask = smoothstep(1.0, 0.55, abs(ndx));
                // Paralelkenar: üst/alt kesim çizgileri bant boyunca x ile kayar.
                float shift = slant * 0.5 * (ndx + 1.0);
                float yMask = smoothstep(y0 - shift, y0 - shift + 0.06, uv.y)
                    * smoothstep(y1 - shift, y1 - shift - 0.04, uv.y);
                // Altta ~%45 soluk, tepede tam — camdaki gradyanla aynı yön.
                float fade = lerp(0.45, 1.0,
                    saturate((uv.y - y0) / max(y1 - y0, 1e-3)));
                return xMask * yMask * fade;
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

                // Cam görselindeki (v2 tube.png) parlama BANTLARININ sıvı
                // bölgesindeki devamı — cam arkada kaldığı için dolu bölgede
                // parlamayı sıvı çizmeli. Camda iki yumuşak paralelkenar bant
                // ölçüldü: sol x38-48 / satır ~140-345 (tepe alfa 76→40),
                // sağ x98-112 / satır ~85-245 (61→41) — ikisi de altta soluk,
                // yukarı doğru netleşiyor. Konumlar sıvı uv'sine yaklaşık
                // taşındı (9-slice esnemesi birebir kaydı zaten imkânsız kılar);
                // oran/şiddet görselle gözle hizalanır, görsel değişirse birlikte.
                float streak = GlassBand(uv, 0.183, 0.050, 0.32, 0.80, 0.06);
                streak = max(streak,
                    0.85 * GlassBand(uv, 0.779, 0.067, 0.55, 0.92, 0.06));
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
