// Tamamlanma efekti — COLLAR YILDIZI: yukselen seridin ucu collar sag-koseye
// varinca ORADA patlayan tek 4-uclu yildiz. Referans: buyuyup dikdortgen olan
// bir flas DEGIL — sabit boyutlu, gaussian isinli, yerel yildiz; hizli parlar,
// yavas soner (kenarlar yumusak sonup dikdortgen sinir birakmaz). _Progress ile
// surulur (serit collar'a varinca ~0.68-0.76 tepe, 0.95'te biter). Additive.
// Not: shader adi tarihsel olarak "CompletionFlash".
Shader "TubeSort/CompletionFlash"
{
    Properties
    {
        _StarColor ("Yildiz rengi (sicak altin-beyaz)", Color) = (1, 0.93, 0.72, 1)
        _CoreTight ("Cekirdek sikiligi", Float) = 13
        _RayLen ("Isin uzunlugu (eksende)", Float) = 2.0
        _RayThin ("Isin inceligi (eksene dik)", Float) = 55
        // _StarStart, seridin basi collar sag-uste VARDIGI ana (spiral _RiseEnd=0.68)
        // demirlenir: bas orada dururken yildiz tam o noktada patlar. Inspector'dan
        // ince ayar yapilabilir (spiral _RiseEnd ile birlikte).
        _StarStart ("Belirme (progress)", Range(0, 1)) = 0.68
        _StarPeak ("Tepe (progress)", Range(0, 1)) = 0.78
        _StarEnd ("Bitis (progress)", Range(0, 1)) = 0.95

        // Script'ten surulur (TubeView). UnityPerMaterial'de OLMALI: yoksa SRP Batcher
        // global sayar, iki tup ayni anda tamamlaninca paylasilir (bkz. CompletionRing).
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
                float4 _StarColor;
                float _CoreTight;
                float _RayLen;
                float _RayThin;
                float _StarStart;
                float _StarPeak;
                float _StarEnd;
                float _Progress;
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
                // Hizli parla, yavas son. Genisleme YOK -> dikdortgen olusmaz.
                float rise = smoothstep(_StarStart, _StarPeak, _Progress);
                float fall = 1.0 - smoothstep(_StarPeak, _StarEnd, _Progress);
                float env = rise * fall;
                if (env <= 0.001)
                    discard;

                // Hafif boy parildamasi (twinkle).
                float pulse = 1.0 + 0.15 * sin(_Time.y * 8.0);
                float2 c = (input.uv - 0.5) * 2.0 / pulse;   // -1..1

                float d = length(c);
                float core = exp(-d * d * _CoreTight);
                float rayH = exp(-c.y * c.y * _RayThin) * exp(-abs(c.x) * _RayLen);
                float rayV = exp(-c.x * c.x * _RayThin) * exp(-abs(c.y) * _RayLen);

                float intensity = (core * 1.5 + rayH + rayV) * env;

                float3 col = _StarColor.rgb * intensity;
                return half4(col, intensity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
