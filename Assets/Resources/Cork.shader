// Tamamlanmış tüpü kapatan mantar tıpa (sortingOrder 3: arka yakanın (2) önünde,
// yaka ön-overlay'inin (4) arkasında — tıpa yaka bandının içinden geçiyormuş gibi
// okunur). Silüet: basık eliptik ÜST YÜZ + büyük kesik koni + içeri kademe +
// küçük kesik koni + aşağı dışbükey oval DİP (sıvının önünde çizilir, kendi
// rengini korur). Gözenekler prosedürel; hiçbir gözenek üst yüz/yan ayrım
// çizgisine ya da silüet konturuna DEĞMEZ — değecek olan hücre hiç çizilmez.
// Üst yüz gözenekleri koyu dolgulu basık elips, yan gözenekler açık dolgulu
// daire; hepsi net koyu kenarlı ve tam opak.
Shader "TubeSort/Cork"
{
    Properties
    {
        _CapColor ("Üst yüz (açık, doygun tan)", Color) = (0.90, 0.67, 0.40, 1)
        _SideLight ("Yan açık (üst)", Color) = (0.74, 0.51, 0.28, 1)
        _SideDark ("Yan koyu (alt/kenar)", Color) = (0.44, 0.26, 0.11, 1)
        _OutlineColor ("Kontur", Color) = (0.11, 0.09, 0.10, 1)
        _OutlineWidth ("Kontur kalınlığı", Float) = 0.025
        _EdgeShade ("Kenar koyulaşması", Float) = 0.09
        _CornerRadius ("Köşe yuvarlaklığı", Float) = 0.02
        // Gözenekler:
        _PoreFill ("Yan gözenek içi (açık turuncu-tan)", Color) = (0.86, 0.60, 0.34, 1)
        _PoreTopFill ("Üst yüz gözenek içi (koyu kahve)", Color) = (0.38, 0.20, 0.08, 1)
        _PoreEdge ("Gözenek kenarı (koyu kahve)", Color) = (0.30, 0.16, 0.06, 1)
        _PoreDensity ("Gözenek sıklığı", Float) = 7
        _PoreThreshold ("Gözenek seyrekliği", Range(0,1)) = 0.12
        _PoreMin ("Gözenek min yarıçap", Float) = 0.20
        _PoreMax ("Gözenek maks yarıçap", Float) = 0.38
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
                float4 _PoreFill;
                float4 _PoreTopFill;
                float4 _PoreEdge;
                float _PoreDensity;
                float _PoreThreshold;
                float _PoreMin;
                float _PoreMax;
            CBUFFER_END

            // Per-quad (MaterialPropertyBlock):
            float4 _CorkQuad;    // dörtgen dünya boyutu
            float4 _CapRadii;    // tepe elipsi (rx=W en geniş, ry≈W/5)
            float4 _CorkYs;      // (yTop, yStep=kademe, yBase, kullanılmıyor)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            // Kesik koni (trapez) SDF — Quilez. r1 alt yarı-genişlik, r2 üst.
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

            float SdEllipse(float2 p, float2 ab)
            {
                float k1 = length(p / ab);
                float k2 = length(p / (ab * ab));
                return k1 * (k1 - 1.0) / max(k2, 1e-4);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            // Silüet SDF — hem piksel boyama hem gözenek eleme aynı fonksiyonu
            // kullanır; ikisi ayrı hesaplansaydı eleme payları kayardı.
            float CorkSil(float2 q)
            {
                float yTop = _CorkYs.x, yStep = _CorkYs.y, yBase = _CorkYs.z;
                float W = _CapRadii.x;
                float wStepTop = 0.89 * W;   // büyük koni ALT (kademe üstü)
                float wStepBot = 0.80 * W;   // küçük koni ÜST (içeri basamak)
                float wBase    = 0.70 * W;   // taban

                float dCap = SdEllipse(q - float2(0.0, yTop), _CapRadii.xy);
                float dUpper = SdTrapezoid(q - float2(0.0, (yTop + yStep) * 0.5),
                    wStepTop, W, (yTop - yStep) * 0.5);
                float dLower = SdTrapezoid(q - float2(0.0, (yStep + yBase) * 0.5),
                    wBase, wStepBot, (yStep - yBase) * 0.5);
                float dBase = SdEllipse(q - float2(0.0, yBase), float2(wBase, wBase * 0.35));
                return min(min(dCap, dUpper), min(dLower, dBase)) - _CornerRadius;
            }

            // Üst yüz elipsine mesafe (ayrım çizgisi bu elipsin kenarı).
            float CapDist(float2 q)
            {
                return SdEllipse(q - float2(0.0, _CorkYs.x), _CapRadii.xy);
            }

            // Gözenekler: 3x3 hücre taraması, hücre başına jitterlı bir gözenek.
            // flatten>1 üst yüz perspektifi (basık elips), rScale üst yüz
            // gözeneklerini büyütür. ELEME: ayrım çizgisine ya da silüete değecek
            // gözenek hiç çizilmez — kesik/yarım/hayalet gözenek kalmaz.
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

                    // Gözeneğin dünya-uzayı merkezi ve yarıçapları (eleme testleri).
                    float2 center = (cid + 0.5 + off * 0.5) / _PoreDensity;
                    float rw = r / _PoreDensity;          // x yarıçapı
                    float rwY = rw / max(flatten, 1.0);   // y yarıçapı (yüzde basık)

                    float dCapC = CapDist(center);
                    bool onFace = dCapC < -(rwY + 0.02);  // tamamen üst yüz içinde
                    bool onSide = dCapC >  (rw + 0.05);   // ayrım bandının belirgin altında
                    if (!onFace && !onSide) continue;
                    if (CorkSil(center) > -(rwY + 0.02)) continue;
                    if (onFace && abs(center.x) > _CapRadii.x - rw - 0.02) continue;

                    float2 q = (gv - (float2(ox, oy) + off * 0.5)) * float2(1.0, flatten);
                    float d = length(q) - r;
                    fill = max(fill, 1.0 - smoothstep(-0.02, 0.02, d));
                    edge = max(edge, 1.0 - smoothstep(0.03, 0.07, abs(d)));
                }
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = QuadPoint(input.uv, _CorkQuad.xy);
                float yTop = _CorkYs.x, yBase = _CorkYs.z;

                float dSil = CorkSil(p);
                float e = fwidth(dSil);
                float inside = 1.0 - smoothstep(-e, e, dSil);
                if (inside <= 0.001)
                    discard;

                float dCap = CapDist(p);

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

                // Gözenekler: üst yüzde koyu dolgulu basık elips, yanda açık
                // dolgulu daire; ikisi de net koyu kenarlı.
                float flatten = lerp(1.0, _CapRadii.x / max(_CapRadii.y, 1e-4), inCap);
                float rScale = lerp(1.0, 1.7, inCap);
                float pf, pe;
                Pores(p, flatten, rScale, pf, pe);
                half3 poreFillCol = lerp(_PoreFill.rgb, _PoreTopFill.rgb, inCap);
                col = lerp(col, poreFillCol, pf * 0.95);
                col = lerp(col, _PoreEdge.rgb, pe * 0.95);

                // Üst yüz / yan ayrım çizgisi — kontur ile aynı koyulukta, kalın.
                float rim = (1.0 - smoothstep(0.0, 0.040, abs(dCap)))
                    * smoothstep(0.05, -0.06, p.y - yTop) * (1.0 - inCap);
                col = lerp(col, _OutlineColor.rgb, rim);

                // Tek tip koyu kontur. (Kademe hizasındaki "temas gölgesi" bandı
                // kaldırıldı: tıpa yaka penceresinden görünür olunca ortasında
                // yatay çizgi gibi duruyordu — kullanıcı isteği.)
                float outline = 1.0 - smoothstep(_OutlineWidth - e, _OutlineWidth + e, abs(dSil));
                col = lerp(col, _OutlineColor.rgb, outline);

                return half4(col, inside);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
