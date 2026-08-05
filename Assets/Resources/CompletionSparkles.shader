// Tamamlanma efekti — YUKSELEN KIVILCIMLAR (adim D): tup boyunca dagilmis kucuk
// altin yildizlar dipten tepeye AKAR (zamanla + progress ile yukselir), her biri
// kendi fazinda parildar. Izgara-hash ile prosedurel: her hucrede seyrek bir
// kivilcim; hucreler zamanla asagi kayar -> ekranda yukari akis. Additive glow;
// yogunluk _Progress zarfiyla belirir/soner. Quad tupun onunde (TubeView).
Shader "TubeSort/CompletionSparkles"
{
    Properties
    {
        _SparkColor ("Kivilcim rengi (altin)", Color) = (1, 0.85, 0.45, 1)
        _Columns ("Sutun sayisi", Float) = 6
        _Rows ("Satir sayisi", Float) = 11
        _SparkSize ("Kivilcim boyutu (hucre)", Range(0.02, 0.4)) = 0.16
        _RiseSpeed ("Yukselme hizi", Float) = 0.35
        _TwinkleSpeed ("Parildama hizi", Float) = 6
        _Density ("Yogunluk (0..1)", Range(0, 1)) = 0.55
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
                float4 _SparkColor;
                float _Columns;
                float _Rows;
                float _SparkSize;
                float _RiseSpeed;
                float _TwinkleSpeed;
                float _Density;
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

            // Hucre -> yalancı-rastgele 2B (kivilcim var mi + konum + faz).
            float2 Hash22(float2 p)
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
                // Zarf: erken belir, ortada dur, sonda sön. Dışında hiç çizme.
                float env = smoothstep(0.0, 0.15, _Progress)
                          * (1.0 - smoothstep(0.75, 1.0, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;

                // Yukari akis: hucreleri asagi kaydir -> kivilcimlar yukari akar.
                // (progress payi: efekt boyunca da bir tur yukselme.)
                float rise = _Progress * 1.2 + _Time.y * _RiseSpeed;

                // Izgara: her hucre bir kivilcim adayi.
                float2 g = float2(uv.x * _Columns, (uv.y - rise) * _Rows);
                float2 cell = floor(g);
                float2 fpos = frac(g) - 0.5;   // hucre merkezli (-0.5..0.5)

                float2 h = Hash22(cell);
                float present = step(1.0 - _Density, h.x);   // seyreklestir

                // Kivilcim merkezini hucre icinde biraz kaydir.
                float2 q = fpos - (h - 0.5) * 0.5;
                float d = length(q);

                // Nokta cekirdegi + ince carpi (yildiz parilti).
                float core = 1.0 - smoothstep(0.0, _SparkSize, d);
                float cross =
                    (1.0 - smoothstep(0.0, _SparkSize * 3.0, abs(q.x)))
                        * (1.0 - smoothstep(0.0, _SparkSize * 0.25, abs(q.y)))
                  + (1.0 - smoothstep(0.0, _SparkSize * 3.0, abs(q.y)))
                        * (1.0 - smoothstep(0.0, _SparkSize * 0.25, abs(q.x)));
                float spark = (core + cross * 0.5) * present;

                // Parildama: her hucre kendi fazinda yanip soner.
                float tw = saturate(0.45 + 0.55
                    * sin(_Time.y * _TwinkleSpeed + h.y * 30.0));

                // Yukseklik zarfı: dipte ve tepede sön (tup icinde kalsin hissi).
                float heightFade = smoothstep(0.0, 0.12, uv.y)
                                 * (1.0 - smoothstep(0.82, 1.0, uv.y));

                float intensity = spark * tw * heightFade * env;

                float3 col = _SparkColor.rgb * intensity;
                return half4(col, intensity);   // additive (Blend One One)
            }
            ENDHLSL
        }
    }

    FallBack Off
}
