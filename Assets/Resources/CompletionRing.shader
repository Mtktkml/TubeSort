// Tamamlanma efekti — DİP HALKASI (büyü çemberi): tüpün dibinde/zemininde
// altın parlayan, nabız atan eş-merkezli halkalar. Quad genişçe/yassı olduğu
// için uv'deki daire dünyada YASSI ELİPS olur (zemin perspektifi). Additive
// blend: koyu arka planda ışıldar. Yoğunluk _Progress (0..1 tamamlanma zarfı)
// ile belirir ve söner (TubeView.AnimateCompletion sürer).
Shader "TubeSort/CompletionRing"
{
    Properties
    {
        _RingColor ("Halka rengi (altın)", Color) = (1, 0.78, 0.32, 1)
        _RingRadius ("Ana halka yarıçapı (uv)", Range(0.2, 0.9)) = 0.62
        _RingWidth ("Halka kalınlığı", Range(0.01, 0.2)) = 0.05
        _Softness ("Kenar yumuşaklığı", Range(0.005, 0.3)) = 0.10
        _InnerGlow ("İç glow", Range(0, 1)) = 0.22
        _PulseSpeed ("Nabız hızı", Float) = 5
        _PulseAmount ("Nabız genliği", Range(0, 0.3)) = 0.10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend One One   // additive glow
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RingColor;
                float _RingRadius;
                float _RingWidth;
                float _Softness;
                float _InnerGlow;
                float _PulseSpeed;
                float _PulseAmount;
            CBUFFER_END

            // Tamamlanma zarfı; TubeView materyale yazar.
            float _Progress;

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
                // Zarf: erken belir (build 0..0.18), efektin çoğunda dur, sonda
                // sön (fade 0.65..1). Dışında hiç çizme.
                float env = smoothstep(0.0, 0.18, _Progress)
                          * (1.0 - smoothstep(0.65, 1.0, _Progress));
                if (env <= 0.001)
                    discard;

                // uv merkezli; quad geniş/yassı olduğu için dünyada elips.
                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;   // ~1 kenar ortasında

                // Hafif nabız: yarıçap + parlaklık dalgalanır.
                float pulse = sin(_Time.y * _PulseSpeed);
                float ringR = _RingRadius * (1.0 + _PulseAmount * pulse * 0.5);

                float w = _RingWidth + _Softness;
                float ring1 = 1.0 - smoothstep(0.0, w, abs(r - ringR));
                float ring2 = 0.45 * (1.0 - smoothstep(0.0, w, abs(r - ringR * 0.62)));

                // Merkeze doğru dolgun iç glow.
                float glow = _InnerGlow * saturate(1.0 - r / max(ringR, 1e-4));

                float intensity = (ring1 + ring2 + glow) * env
                    * (0.85 + 0.15 * (pulse * 0.5 + 0.5));

                float3 col = _RingColor.rgb * intensity;
                return half4(col, intensity);   // additive (Blend One One)
            }
            ENDHLSL
        }
    }

    FallBack Off
}
