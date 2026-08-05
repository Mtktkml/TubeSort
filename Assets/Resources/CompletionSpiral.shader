// Tamamlanma efekti — IŞIK SPİRALİ (helis): tüpü saran altın ışık şeridi,
// dipten tepeye TIRMANIR. Her y yüksekliğinde şerit merkezi x sinüsle kayar
// (sarılma). Şerit yalnız "tırmanan başın" altında çizilir → aşağıdan yukarı
// büyüyerek çizilmiş gibi. Additive glow; yoğunluk _Progress zarfıyla belirir/
// söner. Quad tüpün önünde (TubeView).
Shader "TubeSort/CompletionSpiral"
{
    Properties
    {
        _SpiralColor ("Spiral rengi (altın)", Color) = (1, 0.82, 0.35, 1)
        _Turns ("Sarım sayısı", Float) = 2.5
        _Amplitude ("Yatay genlik (uv)", Range(0.1, 0.5)) = 0.34
        _RibbonWidth ("Şerit kalınlığı (uv)", Range(0.01, 0.2)) = 0.05
        _Softness ("Yumuşaklık", Range(0.005, 0.2)) = 0.05
        _SpinSpeed ("Dönme hızı", Float) = 2
        _RiseStart ("Tırmanma başlangıcı (progress)", Range(0, 1)) = 0.15
        _RiseEnd ("Tırmanma bitişi (progress)", Range(0, 1)) = 0.60
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
                float4 _SpiralColor;
                float _Turns;
                float _Amplitude;
                float _RibbonWidth;
                float _Softness;
                float _SpinSpeed;
                float _RiseStart;
                float _RiseEnd;
            CBUFFER_END

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
                float env = smoothstep(0.0, 0.15, _Progress)
                          * (1.0 - smoothstep(0.70, 1.0, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;

                // Tırmanan baş: spiral bu yüksekliğe (0..1) kadar çizildi.
                float head = smoothstep(_RiseStart, _RiseEnd, _Progress);

                // Helis: her y'de şerit merkezi x (sarılma) — zamanla döner.
                // 6.2831853 = 2π (TWO_PI adı Unity include'larında zaten makro,
                // çakışmasın diye literal).
                float helixX = 0.5 + _Amplitude
                    * sin(uv.y * _Turns * 6.2831853 + _Time.y * _SpinSpeed);
                float dx = abs(uv.x - helixX);
                float ribbon = 1.0 - smoothstep(0.0, _RibbonWidth + _Softness, dx);

                // Yalnız başın altında görünür (aşağıdan yukarı büyür).
                float below = 1.0 - smoothstep(head, head + 0.05, uv.y);

                // Baş parlaması: baş hizasında daha parlak tepe.
                float headGlow = (1.0 - smoothstep(0.0, 0.08, abs(uv.y - head))) * ribbon;

                // Akış hissi: şerit boyunca kayan parlaklık dalgası.
                float flow = 0.75 + 0.25 * sin(uv.y * 24.0 - _Time.y * 7.0);

                float intensity = (ribbon * below * flow + headGlow) * env;

                float3 col = _SpiralColor.rgb * intensity;
                return half4(col, intensity);   // additive
            }
            ENDHLSL
        }
    }

    FallBack Off
}
