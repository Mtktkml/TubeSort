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
        // Dalga ve yüzey ölçüleri dünya birimindedir, tüpün oranı değil:
        // dalga fiziksel bir şeydir, tüp uzadı diye büyümemeli.
        _WaveAmplitude ("Dalga yüksekliği (dünya birimi)", Float) = 0.024
        _WaveFrequency ("Dalga sıklığı", Float) = 16
        _WaveSpeed ("Dalga hızı", Float) = 2.5
        _EdgeSoftness ("Yüzey yumuşaklığı (dünya birimi)", Float) = 0.012
        _SideShading ("Kenar gölgesi", Range(0, 1)) = 0.35
        _Glossiness ("Parlaklık", Range(0, 1)) = 0.25
        _WallThickness ("Cam et kalınlığı", Float) = 0.05
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
                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;
                float _EdgeSoftness;
                float _SideShading;
                float _Glossiness;
                float _WallThickness;
            CBUFFER_END

            // Bu değerler her tüp için farklı; MaterialPropertyBlock ile
            // tüp tüp gönderilir, o yüzden CBUFFER dışında durur.
            float4 _LayerColors[MAX_LAYERS];
            float _LayerTops[MAX_LAYERS];
            float _FillLevel;
            int _LayerCount;
            float4 _QuadSize;
            float4 _BodySize;
            float4 _MouthSize;
            float _TopRadius;
            float _BottomRadius;
            float _MouthRadius;
            float _MouthBlend;

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
                float glassDistance = SdTube(p, _QuadSize.xy, _BodySize.xy, _MouthSize.xy,
                    _TopRadius, _BottomRadius, _MouthRadius, _MouthBlend);
                float innerDistance = glassDistance + _WallThickness;

                float innerEdge = fwidth(innerDistance);
                float insideGlass = 1.0 - smoothstep(-innerEdge, innerEdge, innerDistance);
                if (insideGlass <= 0.001)
                    discard;

                // Bundan sonraki hesaplar gövdenin kendi uzayında: uv.y 0 = dip,
                // 1 = gövdenin tepesi. Dörtgen ağız için fazladan uzun olduğundan
                // dörtgenin uv'siyle çalışsaydık doluluk ve katman sınırları kayardı.
                float2 uv = BodyUV(p, _QuadSize.xy, _BodySize.xy);

                // Dalga ve yumuşaklık dünya biriminde tanımlıdır; burada gövdenin
                // oranına çevriliyorlar. Doğrudan uv'de kullanılsalardı uzun tüpte
                // dalga da uzardı: 12 birimlik tüpte 4 birimliğin üç katı.
                float waveAmplitude = _WaveAmplitude / _BodySize.y;
                float edgeSoftness = _EdgeSoftness / _BodySize.y;

                // Sıvının yüzeyi düz bir çizgi değil, yavaşça salınan bir dalga.
                float wave = sin(uv.x * _WaveFrequency + _Time.y * _WaveSpeed) * waveAmplitude;
                float surface = _FillLevel + wave;

                // Yüzeyin altındaysak 1, üstündeysek 0. Aradaki dar bant
                // kenarın testere gibi görünmesini engeller.
                float inside = smoothstep(surface, surface - edgeSoftness, uv.y);
                if (inside <= 0.001)
                    discard;

                // Bu piksel hangi katmanda? Katman sınırları dipten yukarı sıralı,
                // o yüzden "üstünde kaldığım son sınır" katman indeksini verir.
                int layerIndex = 0;
                for (int k = 0; k < MAX_LAYERS; k++)
                {
                    if (k < _LayerCount && uv.y >= _LayerTops[k])
                        layerIndex = k + 1;
                }
                layerIndex = clamp(layerIndex, 0, _LayerCount - 1);

                half4 color = _LayerColors[layerIndex];

                // Silindir yanılsaması: kenarlara doğru koyulaşma.
                float distanceFromCenter = abs(uv.x - 0.5) * 2.0;
                float shade = 1.0 - distanceFromCenter * distanceFromCenter * _SideShading;
                color.rgb *= shade;

                // Sol tarafta ince bir parlama şeridi; camdan yansıyan ışık hissi.
                // Şerit dar tutulmalı: genişledikçe sıvının rengine beyaz katar ve
                // paletteki renkler birbirine yaklaşıp ayırt edilemez hale gelir.
                const float highlightCenter = 0.22;
                const float highlightWidth = 0.05;

                float highlight = smoothstep(highlightWidth, 0.0, abs(uv.x - highlightCenter));
                color.rgb += highlight * _Glossiness * 0.5;

                color.a *= inside * insideGlass;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
