// Tamamlanma efekti — OTURMA FLASI (adim E): tipa agiza "tak" diye oturdugu an
// patlayan kisa yildiz flasi. Parlak cekirdek + 4-uclu yildiz isini + genisleyen
// sok halkasi; hepsi hizli parlar, genisleyerek soner. _Flash (0..1) TubeView'in
// kisa PlayFlash coroutine'iyle surulur (efekt boyunca DEGIL, tek atislik).
// Additive glow; quad tipin agzinda, her seyin onunde (TubeView).
Shader "TubeSort/CompletionFlash"
{
    Properties
    {
        _FlashColor ("Flas rengi (sicak altin-beyaz)", Color) = (1, 0.92, 0.7, 1)
        _Spread ("Yayilma (genisleme)", Float) = 1.4
        _CoreTight ("Cekirdek sikiligi", Float) = 9
        _RayLength ("Isin uzunlugu (eksende)", Float) = 2.2
        _RayThin ("Isin inceligi (eksene dik)", Float) = 60
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
                float4 _FlashColor;
                float _Spread;
                float _CoreTight;
                float _RayLength;
                float _RayThin;
            CBUFFER_END

            // Flaş ömrü (0..1); TubeView.PlayFlash yazar (tek atışlık).
            float _Flash;

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
                float life = _Flash;

                // Pop: hızlı parla (0..0.12), yavaş sön (0.15..1). Dışında hiç çizme.
                float bright = smoothstep(0.0, 0.12, life)
                             * (1.0 - smoothstep(0.15, 1.0, life));
                if (bright <= 0.001)
                    discard;

                // Merkezli koordinat; patlama büyüdükçe koordinatı küçült (yayılma).
                float2 c = (input.uv - 0.5) * 2.0;   // -1..1
                float scale = 0.35 + life * _Spread;
                float2 p = c / scale;

                float d = length(p);

                // Parlak çekirdek.
                float core = exp(-d * d * _CoreTight);

                // 4-uçlu yıldız: eksenler boyunca ince parlak ışınlar.
                float rayH = exp(-p.y * p.y * _RayThin) * exp(-abs(p.x) * _RayLength);
                float rayV = exp(-p.x * p.x * _RayThin) * exp(-abs(p.y) * _RayLength);
                float star = rayH + rayV;

                // Genişleyen şok halkası.
                float rr = (d - 0.8) * 4.0;
                float ring = exp(-rr * rr) * saturate(life * 2.0);

                float intensity = (core * 1.6 + star * 1.1 + ring * 0.5) * bright;

                float3 col = _FlashColor.rgb * intensity;
                return half4(col, intensity);   // additive (Blend One One)
            }
            ENDHLSL
        }
    }

    FallBack Off
}
