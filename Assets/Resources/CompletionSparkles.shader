// Tamamlanma efekti — DUZENSIZ IŞILTILAR: dipten cikip yukari akan altin
// yildizlar. Referans: DUZENSIZ dagilim (izgara belli olmamali) ve seridin
// BASINI TAKIP eder — en yogun basin hemen ALTINDA, seridin x-kolonuna yakin.
// Izgara-hash + buyuk rastgele kayma ile grid kirilir; boylar cesitli. Additive;
// _Progress zarfiyla belirir/soner. _HeadMax (collar orani) TubeView'den.
Shader "TubeSort/CompletionSparkles"
{
    Properties
    {
        _SparkColor ("Isilti rengi (altin)", Color) = (1, 0.86, 0.46, 1)
        _Columns ("Sutun (kaba)", Float) = 5
        _Rows ("Satir (ince)", Float) = 15
        _SparkSize ("Isilti boyutu (taban)", Range(0.02, 0.4)) = 0.13
        _RiseSpeed ("Yukselme hizi", Float) = 0.45
        _TwinkleSpeed ("Parildama hizi", Float) = 7
        _Density ("Yogunluk (0..1)", Range(0, 1)) = 0.5
        _Turns ("Serit sarim (takip icin)", Float) = 1.6
        _Amplitude ("Serit genlik (takip icin)", Range(0.1, 0.5)) = 0.30
        _SpinSpeed ("Serit donme (takip icin)", Float) = 1.5
        _RiseStart ("Tirmanma baslangici (progress)", Range(0, 1)) = 0.12
        _RiseEnd ("Tirmanma bitisi (progress)", Range(0, 1)) = 0.68
        _TrailLen ("Iz uzunlugu (uv)", Range(0.1, 0.9)) = 0.55

        // Script'ten surulur (TubeView). UnityPerMaterial'de OLMALI: yoksa SRP Batcher
        // global sayar, iki tup ayni anda tamamlaninca paylasilir (bkz. CompletionRing).
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
                float _Columns;
                float _Rows;
                float _SparkSize;
                float _RiseSpeed;
                float _TwinkleSpeed;
                float _Density;
                float _Turns;
                float _Amplitude;
                float _SpinSpeed;
                float _RiseStart;
                float _RiseEnd;
                float _TrailLen;
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
                float env = smoothstep(0.0, 0.12, _Progress)
                          * (1.0 - smoothstep(0.78, 0.96, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;
                float headMax = max(_HeadMax, 0.35);
                float head = smoothstep(_RiseStart, _RiseEnd, _Progress) * headMax;

                // Basi takip: yogunluk basin hemen ALTINDA en yuksek (iz), ustunde
                // yok. Az taban ambient birak (dipte de birkac isilti kalsin).
                float trail = smoothstep(head - _TrailLen, head, uv.y);
                float aboveCut = 1.0 - smoothstep(head, head + 0.05, uv.y);
                float dens = (trail * 0.85 + 0.15) * aboveCut;

                // Seridin x-kolonuna yakinlik: isiltilar serit cevresinde toplanir.
                float rx = 0.5 + _Amplitude
                    * sin(uv.y * _Turns * 6.2831853 + _Time.y * _SpinSpeed);
                dens *= 0.45 + 0.55 * (1.0 - smoothstep(0.0, 0.42, abs(uv.x - rx)));

                // Izgara-hash; hucreler asagi kayar -> yukari akis.
                float brise = _Time.y * _RiseSpeed;
                float2 g = float2(uv.x * _Columns, (uv.y - brise) * _Rows);
                float2 cell = floor(g);
                float2 h = Hash22(cell);

                // Duzensizlik: presence combine-hash ile, BUYUK rastgele kayma ile
                // isilti hucresinden tasar (grid deseni kirilir).
                float present = step(1.0 - _Density, frac(h.x * 1.7 + h.y * 0.9));
                float2 q = (frac(g) - 0.5) - (h - 0.5) * 0.9;
                float d = length(q);

                float sz = _SparkSize * (0.4 + 1.3 * h.y);   // cesitli boy
                float core = 1.0 - smoothstep(0.0, sz, d);
                float cross =
                    (1.0 - smoothstep(0.0, sz * 3.0, abs(q.x)))
                        * (1.0 - smoothstep(0.0, sz * 0.25, abs(q.y)))
                  + (1.0 - smoothstep(0.0, sz * 3.0, abs(q.y)))
                        * (1.0 - smoothstep(0.0, sz * 0.25, abs(q.x)));
                float spark = (core + cross * 0.5) * present;

                float tw = saturate(0.4 + 0.6
                    * sin(_Time.y * _TwinkleSpeed + h.y * 30.0 + h.x * 11.0));

                float intensity = spark * tw * dens * env;

                float3 col = _SparkColor.rgb * intensity;
                return half4(col, intensity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
