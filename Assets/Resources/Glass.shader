// Tüpün cam gövdesi. Sıvının arkasında durur ve şekli belirler.
// Şekli TubeShape.hlsl'den gelir; sıvı da aynı formülle kırpıldığı için
// ikisi kusursuz hizalanır.
Shader "TubeSort/Glass"
{
    Properties
    {
        // Toon stil: soluk açık mavi gövde, kalın siyah outline, outline'ın
        // içinde açık bir kenar çizgisi (cam et-kalınlığı), sol tarafta beyaz şeritler.
        _BodyColor ("Gövde rengi", Color) = (0.73, 0.89, 0.96, 0.9)
        _RimColor ("Outline rengi", Color) = (0.06, 0.06, 0.08, 1.0)
        _RimWidth ("Outline kalınlığı", Float) = 0.03
        _InnerColor ("İç kenar çizgisi", Color) = (0.93, 0.97, 1.0, 0.9)
        _InnerWidth ("İç çizgi kalınlığı", Float) = 0.028
        _GlossStrength ("Şerit opaklığı", Range(0, 1)) = 0.85
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
                float4 _InnerColor;
                float _InnerWidth;
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

                // Kalın siyah outline: kenardan _RimWidth kadar içeride opak bir
                // bant. Şekil tek parça olduğu için outline tüpün etrafından
                // kesintisiz dolaşır. _RimColor opak (a=1), gövde saydam olsa da
                // çizgi tam çıksın.
                float outline = 1.0 - smoothstep(_RimWidth - edge, _RimWidth + edge, abs(distance));

                // Outline'ın hemen içinde açık bir kenar çizgisi (cam et-kalınlığı
                // highlight'ı): abs(distance) [_RimWidth, _RimWidth+_InnerWidth] bandı.
                float innerA = _RimWidth + _InnerWidth;
                float innerBand = saturate((1.0 - smoothstep(innerA - edge, innerA + edge, abs(distance))) - outline);

                half4 color = _BodyColor;
                color = lerp(color, _InnerColor, innerBand);   // önce iç açık çizgi
                color = lerp(color, _RimColor, outline);       // üstüne siyah outline (daha dışta)

                // Solda iki dikey beyaz parlama şeridi (toon cam refleksi):
                // üstte uzun, altında kısa; ikisi de aynı x'te, uçları yuvarlak.
                float2 bodyUV = BodyUV(p, _QuadSize.xy, _BodySize.xy);
                float streakX = smoothstep(0.05, 0.0, abs(bodyUV.x - 0.20));
                float longY  = smoothstep(0.50, 0.53, bodyUV.y) * smoothstep(0.85, 0.82, bodyUV.y);
                float shortY = smoothstep(0.32, 0.35, bodyUV.y) * smoothstep(0.45, 0.42, bodyUV.y);
                float streak = streakX * max(longY, shortY) * (1.0 - outline);

                // Şerit maviye çalan beyaza çeker + opaklığı artırır (saydam camda
                // bile görünsün). Renk Liquid.shader'daki şeritle aynı tutulmalı.
                float streakAmt = streak * _GlossStrength;
                color.rgb = lerp(color.rgb, half3(0.94, 0.97, 1.0), streakAmt);
                color.a = lerp(color.a, 1.0, streakAmt);

                color.a *= inside;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
