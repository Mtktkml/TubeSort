// Tamamlanma efekti — PARILTI YILDIZLARI: SERITLE AYNI yoldan giden ama seridin
// DISINDA (biraz genis S) ve ARALIKLI dizilmis altin yildizlar. Yukari akar,
// parildar. Sekiller yol boyunca BOZULMAZ (her yildiz ayrik, sabit-yuvarlak merkez;
// warp/kayma yok). Additive; _Progress zarfiyla belirir/soner.
Shader "TubeSort/CompletionSparkles"
{
    Properties
    {
        _SparkColor ("Yildiz rengi (altin)", Color) = (1, 0.86, 0.46, 1)
        _SparkSize ("Yildiz boyutu (uv)", Range(0.005, 0.08)) = 0.022
        _Rows ("Yol boyu nokta sikligi (satir)", Float) = 22
        _Density ("Doluluk (0..1; ARALIKLI)", Range(0, 1)) = 0.55
        _RiseSpeed ("Yukselme hizi", Float) = 0.35
        _TwinkleSpeed ("Parildama hizi", Float) = 7
        _Turns ("Sarim (SERITLE AYNI)", Float) = 1.6
        _Amplitude ("Yatay genlik (SERITLE AYNI, 0.38)", Range(0.1, 0.6)) = 0.38
        _SparkOffset ("Seridin DISINA offset (uv)", Range(0, 0.2)) = 0.07
        _RiseStart ("Baslangic (progress)", Range(0, 1)) = 0.12
        _RiseEnd ("Bitis (progress)", Range(0, 1)) = 0.68

        // Script'ten surulur (TubeView). UnityPerMaterial'de OLMALI (SRP Batcher).
        [HideInInspector] _Progress ("Progress (script)", Float) = 0
        [HideInInspector] _HeadMax ("Head max (script)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend One One
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
                float _SparkSize;
                float _Rows;
                float _Density;
                float _RiseSpeed;
                float _TwinkleSpeed;
                float _Turns;
                float _Amplitude;
                float _SparkOffset;
                float _RiseStart;
                float _RiseEnd;
                float _Progress;
                float _HeadMax;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

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
                float env = smoothstep(_RiseStart, _RiseStart + 0.06, _Progress)
                          * (1.0 - smoothstep(0.80, 0.96, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;
                float headMax = max(_HeadMax, 0.35);
                float head = smoothstep(_RiseStart, _RiseEnd, _Progress) * headMax;

                // SERITLE AYNI faz (ust sag-tepede biter): yildizlar seridin yolunu izler.
                float anchorPhase = 1.5707963 - _HeadMax * _Turns * 6.2831853;

                // Yukari akis: nokta SATIRLARI zamanla yukselir (scroll). Fragment'a en
                // yakin satiri bul, o noktanin merkezini hesapla (yol uzerinde, ayrik).
                float scroll = _Time.y * _RiseSpeed;
                float rowIdx = floor((uv.y - scroll) * _Rows + 0.5);
                float dotY = rowIdx / _Rows + scroll;

                float2 rnd = Hash22(float2(rowIdx * 1.37, 3.1));
                float present = step(1.0 - _Density, rnd.x);   // ARALIKLI: bazi satirlar bos

                // Nokta x'i: seridin yolunda ama merkezden DISA offset -> serit
                // govdesine binmez (seridin disinda kalir).
                float sway = sin(dotY * _Turns * 6.2831853 + anchorPhase);
                float sideSign = (sway >= 0.0) ? 1.0 : -1.0;
                float dotX = 0.5 + _Amplitude * sway + _SparkOffset * sideSign;

                // BOZULMAYAN yildiz: yerel koordinatta sabit yuvarlak cekirdek + hafif
                // 4-uc capraz. warp yok -> hareket boyunca sekil ayni.
                float2 toDot = float2(uv.x - dotX, uv.y - dotY);
                float d = length(toDot);
                float sz = _SparkSize * (0.7 + 0.6 * rnd.y);
                float core = 1.0 - smoothstep(0.0, sz, d);
                float cross =
                      (1.0 - smoothstep(0.0, sz * 3.0, abs(toDot.x))) * (1.0 - smoothstep(0.0, sz * 0.35, abs(toDot.y)))
                    + (1.0 - smoothstep(0.0, sz * 3.0, abs(toDot.y))) * (1.0 - smoothstep(0.0, sz * 0.35, abs(toDot.x)));
                float shape = core + cross * 0.4;

                float tw = saturate(0.4 + 0.6
                    * sin(_Time.y * _TwinkleSpeed + rnd.y * 30.0 + rnd.x * 11.0));

                // Yalniz basin ALTINDA (serit gibi; hat basi takip eder).
                float belowHead = 1.0 - smoothstep(head, head + 0.06, dotY);

                float intensity = shape * present * tw * belowHead * env;
                float3 col = _SparkColor.rgb * intensity;
                return half4(col, intensity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
