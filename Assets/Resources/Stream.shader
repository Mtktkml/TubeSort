// Kaynak tüpün ağzından hedef tüpe akan sıvıyı tek dörtgene çizer.
//
// Kuadratik Bezier eğrisi 10 doğru parçasıyla yaklaşık hesaplanır.
// Her piksel için eğriye en yakın mesafe bulunur; bu mesafe kalınlıktan
// küçükse piksel akışın içindedir. Kalınlık eğri boyunca daralır
// (yerçekimi etkisi), parlaklık dalgası akış yönünde kayar.
Shader "TubeSort/Stream"
{
    Properties
    {
        _WidthStart ("Kaynak kalınlığı", Float) = 0.06
        _WidthEnd ("Hedef kalınlığı", Float) = 0.03
        _FlowSpeed ("Akış hızı", Float) = 8
        _FlowFreq ("Akış dalga sıklığı", Float) = 25
        _FlowStrength ("Akış dalga gücü", Range(0, 0.3)) = 0.1
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

            #define SEGMENTS 10

            CBUFFER_START(UnityPerMaterial)
                float _WidthStart;
                float _WidthEnd;
                float _FlowSpeed;
                float _FlowFreq;
                float _FlowStrength;
            CBUFFER_END

            // Her akış için farklı; MaterialPropertyBlock ile gönderilir.
            float4 _Color;
            float4 _P0;    // Bezier başlangıç (kaynak ağız), xy kullanılır
            float4 _P1;    // Bezier kontrol noktası, xy kullanılır
            float4 _P2;    // Bezier bitiş (hedef ağız), xy kullanılır
            float4 _QuadSize;

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

            // Kuadratik Bezier: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            float2 EvalBezier(float t, float2 p0, float2 p1, float2 p2)
            {
                float u = 1.0 - t;
                return u * u * p0 + 2.0 * u * t * p1 + t * t * p2;
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
                // UV'yi quad merkezinden dünya birimine çevir.
                float2 p = (input.uv - 0.5) * _QuadSize.xy;

                float2 bezP0 = _P0.xy;
                float2 bezP1 = _P1.xy;
                float2 bezP2 = _P2.xy;

                // Eğri üzerindeki en yakın noktayı bul.
                float minDist = 1e9;
                float closestT = 0;

                for (int i = 0; i < SEGMENTS; i++)
                {
                    float t0 = (float)i / SEGMENTS;
                    float t1 = (float)(i + 1) / SEGMENTS;
                    float2 a = EvalBezier(t0, bezP0, bezP1, bezP2);
                    float2 b = EvalBezier(t1, bezP0, bezP1, bezP2);

                    // Doğru parçası a-b üzerindeki en yakın nokta.
                    float2 ab = b - a;
                    float tSeg = saturate(dot(p - a, ab) / dot(ab, ab));
                    float2 closest = a + ab * tSeg;
                    float dist = length(p - closest);
                    float tGlobal = lerp(t0, t1, tSeg);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestT = tGlobal;
                    }
                }

                // Kalınlık eğri boyunca daralır: kaynakta geniş, hedefte ince.
                float radius = lerp(_WidthStart, _WidthEnd, closestT);

                // SDF kenar yumuşatması.
                float edge = fwidth(minDist);
                float alpha = 1.0 - smoothstep(radius - edge, radius + edge, minDist);

                if (alpha <= 0.001)
                    discard;

                // Akış animasyonu: eğri boyunca kayan parlaklık dalgası.
                float flow = sin(closestT * _FlowFreq - _Time.y * _FlowSpeed)
                    * _FlowStrength + 1.0;

                float4 color = _Color;
                color.rgb *= flow;
                color.a *= alpha;
                return (half4)color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
