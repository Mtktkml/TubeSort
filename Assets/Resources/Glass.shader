// Tüpün cam gövdesi. Sıvının arkasında durur ve şekli belirler.
// Şekli TubeShape.hlsl'den gelir; sıvı da aynı formülle kırpıldığı için
// ikisi kusursuz hizalanır.
Shader "TubeSort/Glass"
{
    Properties
    {
        _BodyColor ("Gövde rengi", Color) = (0.16, 0.18, 0.20, 1)
        _RimColor ("Kenar rengi", Color) = (0.30, 0.33, 0.36, 1)
        _RimWidth ("Kenar kalınlığı", Float) = 0.035
        _GlossStrength ("Parlama şiddeti", Range(0, 1)) = 0.18
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor;
                float4 _RimColor;
                float _RimWidth;
                float _GlossStrength;
            CBUFFER_END

            // Tüpe özel ölçüler; MaterialPropertyBlock ile gönderilir.
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
                float2 p = QuadPoint(input.uv, _QuadSize.xy);

                float distance = SdTube(p, _QuadSize.xy, _BodySize.xy, _MouthSize.xy,
                    _TopRadius, _BottomRadius, _MouthRadius, _MouthBlend);

                // fwidth: bu pikselden komşusuna geçerken mesafe ne kadar değişiyor?
                // Kenarı tam o kadarlık bir bantta yumuşatırsak, kaç piksele
                // yayıldığından bağımsız olarak her zaman tek piksellik pürüzsüz
                // bir geçiş elde ederiz. Sabit bir sayı yazsaydık yakınlaşınca
                // bulanık, uzaklaşınca testere gibi görünürdü.
                float edge = fwidth(distance);

                // Mesafe negatifse içerideyiz.
                float inside = 1.0 - smoothstep(-edge, edge, distance);
                if (inside <= 0.001)
                    discard;

                // Kenara yakın piksellerden ince bir çerçeve: camın et kalınlığı.
                // Şekil tek parça olduğu için çerçeve de ağzın etrafından
                // kesintisiz dolaşır.
                float rim = 1.0 - smoothstep(_RimWidth - edge, _RimWidth + edge, abs(distance));
                half4 color = lerp(_BodyColor, _RimColor, rim);

                // Solda dikey bir parlama şeridi; camın kendi yansıması.
                float2 bodyUV = BodyUV(p, _QuadSize.xy, _BodySize.xy);
                float gloss = smoothstep(0.06, 0.0, abs(bodyUV.x - 0.22));
                color.rgb += gloss * _GlossStrength * (1.0 - rim);

                color.a = inside;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
