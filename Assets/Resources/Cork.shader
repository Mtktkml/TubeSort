// Tamamlanmış tüpü kapatan mantar tıpa. Yakanın ARKASINDA çizilir (sortingOrder 2);
// yalnız delikten ve yakanın üstünden görünür (yaka önde, tıpa beji örtmez).
// Yapı: eliptik ÜST YÜZ (açık) + yan yüz (gradyan, izleyiciye dönük gözenekler) +
// eliptik oval DİP (camın arkasında, cam tonuyla hafif soluk). Üst yüz gözenekleri
// perspektifle basık elips; yan gözenekler yuvarlak. Hepsi koyu iç + kalın kenar.
Shader "TubeSort/Cork"
{
    Properties
    {
        _CapColor ("Üst yüz (açık)", Color) = (0.85, 0.62, 0.38, 1)
        _SideLight ("Yan açık (üst)", Color) = (0.74, 0.51, 0.28, 1)
        _SideDark ("Yan koyu (alt/kenar)", Color) = (0.50, 0.31, 0.15, 1)
        _OutlineColor ("Kontur", Color) = (0.12, 0.07, 0.04, 1)
        _OutlineWidth ("Kontur kalınlığı", Float) = 0.03
        _EdgeShade ("Kenar koyulaşması", Float) = 0.09
        _CornerRadius ("Köşe yuvarlaklığı", Float) = 0.02
        _RimShade ("Üst yüz/yan ayrım", Range(0,1)) = 0.22
        _ContactShade ("Temas gölgesi", Range(0,1)) = 0.30
        // Gözenekler:
        _PoreFill ("Gözenek iç (koyu)", Color) = (0.55, 0.35, 0.17, 1)
        _PoreEdge ("Gözenek kenar (kalın)", Color) = (0.24, 0.13, 0.05, 1)
        _PoreDensity ("Gözenek sıklığı", Float) = 9
        _PoreThreshold ("Gözenek seyrekliği", Range(0,1)) = 0.12
        _PoreMin ("Gözenek min yarıçap", Float) = 0.13
        _PoreMax ("Gözenek maks yarıçap", Float) = 0.28
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
                float4 _CapColor;
                float4 _SideLight;
                float4 _SideDark;
                float4 _OutlineColor;
                float _OutlineWidth;
                float _EdgeShade;
                float _CornerRadius;
                float _RimShade;
                float _ContactShade;
                float4 _PoreFill;
                float4 _PoreEdge;
                float _PoreDensity;
                float _PoreThreshold;
                float _PoreMin;
                float _PoreMax;
            CBUFFER_END

            // Per-quad (MaterialPropertyBlock):
            float4 _CorkQuad;    // dörtgen dünya boyutu
            float4 _CapRadii;    // tepe elipsi (rx=W en geniş, ry≈W/5)
            float4 _CorkYs;      // (yTop, yStep=kademe, yBase, temas y)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float SdEllipse(float2 p, float2 ab)
            {
                float k1 = length(p / ab);
                float k2 = length(p / (ab * ab));
                return k1 * (k1 - 1.0) / max(k2, 1e-4);
            }

            float SdTrapezoid(float2 p, float r1, float r2, float he)
            {
                float2 k1 = float2(r2, he);
                float2 k2 = float2(r2 - r1, 2.0 * he);
                p.x = abs(p.x);
                float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
                float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / dot(k2, k2), 0.0, 1.0);
                float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
                return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            // Gözenek alanı: 3x3 hücre. flatten>1 ise dikey basıklık (üst yüz perspektifi).
            // rScale ile üst yüz gözenekleri büyütülür.
            void Pores(float2 sp, float flatten, float rScale, out float fill, out float edge)
            {
                float2 pu = sp * _PoreDensity;
                float2 id = floor(pu);
                float2 gv = frac(pu) - 0.5;
                fill = 0.0; edge = 0.0;
                for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    float2 cid = id + float2(ox, oy);
                    if (Hash21(cid) < _PoreThreshold) continue;
                    float2 off = float2(Hash21(cid + 11.3), Hash21(cid + 27.7)) - 0.5;
                    float r = lerp(_PoreMin, _PoreMax, Hash21(cid + 5.1)) * rScale;
                    float2 q = (gv - (float2(ox, oy) + off * 0.5)) * float2(1.0, flatten);
                    float d = length(q) - r;
                    fill = max(fill, 1.0 - smoothstep(-0.02, 0.02, d));
                    edge = max(edge, 1.0 - smoothstep(0.03, 0.07, abs(d)));
                }
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = QuadPoint(input.uv, _CorkQuad.xy);
                float yTop = _CorkYs.x, yStep = _CorkYs.y, yBase = _CorkYs.z;
                float W = _CapRadii.x;
                float wStepTop = 0.89 * W;   // büyük koni ALT (kademe üstü)
                float wStepBot = 0.80 * W;   // küçük koni ÜST (kademe: içeri basamak)
                float wBase    = 0.70 * W;   // taban

                // Tepe elipsi — en geniş yer, basık (ry≈W/5); hem ön hem arka yay çizili.
                float dCap = SdEllipse(p - float2(0.0, yTop), _CapRadii.xy);
                // Büyük kesik koni (üst gövde, ~2/3): yStep..yTop, alt wStepTop → üst W.
                float dUpper = SdTrapezoid(p - float2(0.0, (yTop + yStep) * 0.5),
                    wStepTop, W, (yTop - yStep) * 0.5);
                // Küçük kesik koni (alt gövde, ~1/3): yBase..yStep, alt wBase → üst
                // wStepBot. wStepBot < wStepTop olduğu için yStep'te içeri KADEME oluşur.
                float dLower = SdTrapezoid(p - float2(0.0, (yStep + yBase) * 0.5),
                    wBase, wStepBot, (yStep - yBase) * 0.5);
                // Taban — aşağı dışbükey ön yay (alçak elipsin alt yayı; arka yay
                // koninin içinde kaldığı için silüette çizilmez).
                float dBase = SdEllipse(p - float2(0.0, yBase), float2(wBase, wBase * 0.30));

                float dSil = min(min(dCap, dUpper), min(dLower, dBase)) - _CornerRadius;

                float e = fwidth(dSil);
                float inside = 1.0 - smoothstep(-e, e, dSil);
                if (inside <= 0.001)
                    discard;

                // Yan yüz dikey gradyan (üst açık → alt koyu).
                float sny = saturate((p.y - yBase) / max(yTop - yBase, 1e-4));
                half3 col = lerp(_SideDark.rgb, _SideLight.rgb, sny);

                // Kenara yakın koyulaşma (silindirik hacim).
                float edgeDark = smoothstep(-_EdgeShade, 0.0, dSil);
                col *= lerp(1.0, 0.82, edgeDark);

                // Üst yüz: daha açık, ayrı ton.
                float ec = fwidth(dCap);
                float inCap = 1.0 - smoothstep(-ec, ec, dCap);
                col = lerp(col, _CapColor.rgb, inCap);

                // Gözenekler: üst yüzde basık (perspektif), yanda yuvarlak.
                float flatten = lerp(1.0, _CapRadii.x / max(_CapRadii.y, 1e-4), inCap);
                float rScale = lerp(1.0, 1.7, inCap);   // üst yüz gözenekleri daha büyük
                float pf, pe;
                Pores(p, flatten, rScale, pf, pe);
                // kenara/uca taşmasın
                float poreMask = smoothstep(0.0, 0.05, -dSil);
                pf *= poreMask; pe *= poreMask;
                col = lerp(col, _PoreFill.rgb, pf * 0.7);
                col = lerp(col, _PoreEdge.rgb, pe * 0.8);

                // Üst yüz / yan ayrım çizgisi (kapak ön kenarı) — KALIN, net koyu.
                float rim = (1.0 - smoothstep(0.0, 0.040, abs(dCap)))
                    * smoothstep(0.05, -0.06, p.y - yTop) * (1.0 - inCap);
                col = lerp(col, _OutlineColor.rgb, rim);

                // Temas gölgesi: deliğe giriş (kademe) hizasında yumuşak koyu bant.
                float contact = smoothstep(0.06, 0.0, abs(p.y - _CorkYs.w)) * (1.0 - inCap);
                col *= lerp(1.0, 1.0 - _ContactShade, contact);

                // Tek tip koyu kontur.
                float outline = 1.0 - smoothstep(_OutlineWidth - e, _OutlineWidth + e, abs(dSil));
                col = lerp(col, _OutlineColor.rgb, outline);

                return half4(col, inside);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
