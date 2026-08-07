// Tamamlanma efekti — DIP HALKASI (buyu cemberi): tupun dibinde/zemininde altin,
// INCE, es-merkezli iki halka. Salinim: halka yaricapi acisal olarak hafif
// modulelenir ve donen bir parlak yay uzerinde gezer -> canli/salinan zemin
// cemberi (referanstaki gibi). Quad genis/yassi oldugu icin dunyada YASSI ELIPS.
// Additive; yogunluk _Progress zarfiyla belirir/soner (TubeView.AnimateCompletion).
Shader "TubeSort/CompletionRing"
{
    Properties
    {
        _RingColor ("Halka rengi (altin)", Color) = (1, 0.8, 0.36, 1)
        _RingRadius ("Ana halka yaricapi (uv)", Range(0.2, 0.9)) = 0.62
        _RingWidth ("Halka kalinligi (INCE)", Range(0.005, 0.12)) = 0.022
        _Softness ("Kenar yumusakligi", Range(0.005, 0.2)) = 0.05
        _InnerGlow ("Ic glow (dusuk: seritle blob yapmasin)", Range(0, 1)) = 0.07
        _SwayAmount ("Salinim genligi (yaricap)", Range(0, 0.3)) = 0.07
        _SwaySpeed ("Salinim/donme hizi", Float) = 1.6
        _PulseSpeed ("Nabiz hizi", Float) = 4
        _PulseAmount ("Nabiz genligi", Range(0, 0.3)) = 0.08

        // Script'ten surulur (TubeView.SetCompletionProgress). UnityPerMaterial'de
        // OLMALI: yoksa SRP Batcher bunu global sayar, iki tup ayni anda tamamlaninca
        // _Progress paylasilir (son yazan kazanir) ve efektler birbirine karisir.
        [HideInInspector] _Progress ("Progress (script)", Float) = 0
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
                float _SwayAmount;
                float _SwaySpeed;
                float _PulseSpeed;
                float _PulseAmount;
                float _Progress;   // TubeView materyale yazar (tup-basina; bkz. Properties notu)
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // Zarf: erken belir, efektin cogunda dur, sonda son.
                float env = smoothstep(0.0, 0.15, _Progress)
                          * (1.0 - smoothstep(0.72, 1.0, _Progress));
                if (env <= 0.001)
                    discard;

                float2 c = input.uv - 0.5;
                float r = length(c) * 2.0;      // ~1 kenar ortasinda
                float ang = atan2(c.y, c.x);

                // Salinim: yaricap acisal + zamanla donen bir dalga; nabiz.
                float pulse = sin(_Time.y * _PulseSpeed);
                float sway = 1.0 + _SwayAmount * sin(ang * 2.0 - _Time.y * _SwaySpeed);
                float ringR = _RingRadius * sway * (1.0 + _PulseAmount * pulse * 0.5);

                float w = _RingWidth + _Softness;
                float ring1 = 1.0 - smoothstep(0.0, w, abs(r - ringR));
                float ring2 = 0.5 * (1.0 - smoothstep(0.0, w, abs(r - ringR * 0.66)));

                // Donen parlak yay: halka boyunca gezen aydinlik (salinim hissi).
                float arc = 0.55 + 0.45 * sin(ang - _Time.y * _SwaySpeed);

                float glow = _InnerGlow * saturate(1.0 - r / max(ringR, 1e-4));

                float intensity = ((ring1 + ring2) * arc + glow) * env
                    * (0.85 + 0.15 * (pulse * 0.5 + 0.5));

                float3 col = _RingColor.rgb * intensity;
                return half4(col, intensity);   // additive
            }
            ENDHLSL
        }
    }

    FallBack Off
}
