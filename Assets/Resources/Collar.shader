// Tüpün ağzındaki KALIN bej yaka. Üstten-önden bakış: üst elips yüz + görünür yan
// yükseklik (ön duvar) + en altta ince alt-dudak. Üst yüzde koyu eliptik delik,
// ince kavisli bevel çizgisi ve gradyan (üst açık → alt kenar koyu). Yaka tüpün
// ÖNÜNDE ama tıpanın ARKASINDA durur (sortingOrder 2). Koyu deliği hep çizer;
// kesik-koni tıpa (önde) deliğin merkezini örter, daraldığı için deliğin koyusu
// tıpanın etrafında görünür kalır.
Shader "TubeSort/Collar"
{
    Properties
    {
        _TopLight ("Üst yüz açık", Color) = (0.96, 0.92, 0.78, 1)
        _TopDark ("Üst yüz koyu (ön kenar)", Color) = (0.84, 0.76, 0.57, 1)
        _SideColor ("Yan duvar", Color) = (0.80, 0.71, 0.53, 1)
        _HoleColor ("Delik (koyu)", Color) = (0.20, 0.11, 0.07, 1)
        _BevelColor ("Bevel çizgisi", Color) = (0.66, 0.56, 0.38, 1)
        _OutlineColor ("Kontur", Color) = (0.12, 0.07, 0.04, 1)
        _OutlineWidth ("Kontur kalınlığı", Float) = 0.02
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
                float4 _TopLight;
                float4 _TopDark;
                float4 _SideColor;
                float4 _HoleColor;
                float4 _BevelColor;
                float4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            // Per-quad (MaterialPropertyBlock):
            float4 _CollarQuad;   // dörtgen dünya boyutu
            float4 _TopRadii;     // üst/alt elips yarıçapları (Rx, Ry)
            float4 _HoleRadii;    // delik yarıçapları (hx, hy)
            float _SideHalf;      // yan duvar yarı-yüksekliği
            float _HoleCenterY;   // delik merkezi y (dörtgen merkezine göre)
            // 1 ise yalnız ÖN yüz (deliğin altı) çizilir; üst/arka/delik atılır. Bu
            // overlay tıpanın ÜSTÜNE çizilir (order 4) → collar'ın ön kenarı tıpanın
            // önüne geçer (tıpa ortada collar'ın arkasına girer).
            float _FrontOnly;

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

            float SdBox(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = QuadPoint(input.uv, _CollarQuad.xy);

                // Ön overlay: yalnız deliğin ALT kenarının altındaki ön yüz (tıpayı
                // önden sarar). Üst/arka/delik atılır (tıpanın üstüne koyu düşmesin).
                if (_FrontOnly > 0.5 && p.y > (_HoleCenterY - _HoleRadii.y))
                    discard;

                float Rx = _TopRadii.x, Ry = _TopRadii.y;
                float topC =  _SideHalf;   // üst elips merkezi
                float botC = -_SideHalf;   // alt elips merkezi

                // Silüet = üst elips ∪ yan dikdörtgen ∪ alt elips (kalın davul).
                float dTop = SdEllipse(p - float2(0.0, topC), float2(Rx, Ry));
                float dBot = SdEllipse(p - float2(0.0, botC), float2(Rx, Ry));
                float dRect = SdBox(p, float2(Rx, _SideHalf));
                float dSil = min(min(dTop, dRect), dBot);

                float e = fwidth(dSil);
                float inside = 1.0 - smoothstep(-e, e, dSil);
                if (inside <= 0.001)
                    discard;

                // Üst yüz mü yan duvar mı?
                float etop = fwidth(dTop);
                float inTop = 1.0 - smoothstep(-etop, etop, dTop);

                // Yan duvar: dikey gradyan (üste yakın açık, dibe koyu).
                float syTop = topC - Ry;            // üst yüzün ön (alt) kenarı
                float syBot = botC - Ry;            // en dip
                float sy = saturate((p.y - syBot) / max(syTop - syBot, 1e-4));
                half3 side = _SideColor.rgb * lerp(0.72, 1.0, sy);

                // Üst yüz: gradyan (arka/üst açık → ön/alt kenar koyu).
                float tny = saturate((p.y - (topC - Ry)) / (2.0 * Ry)); // 0 ön .. 1 arka
                half3 top = lerp(_TopDark.rgb, _TopLight.rgb, tny);

                half3 col = lerp(side, top, inTop);

                // Bevel: üst yüzün ön (alt) tarafında ince kavisli çizgi.
                float dTopInset = SdEllipse(p - float2(0.0, topC), float2(Rx * 0.82, Ry * 0.82));
                float bevel = (1.0 - smoothstep(0.0, 0.012, abs(dTopInset)))
                    * smoothstep(0.04, -0.04, p.y - topC) * inTop;
                col = lerp(col, _BevelColor.rgb, bevel);

                // Delik (üst yüzde).
                float2 pHole = p - float2(0.0, _HoleCenterY);
                float dHole = SdEllipse(pHole, _HoleRadii.xy);
                float eh = fwidth(dHole);

                float alpha = inside;

                // Delik her zaman KOYU dolu; arka (üst) kenarı biraz açık (derinlik).
                // Tıpa (yakanın önünde) varsa deliğin merkezini örter; kesik-koni
                // olduğu için deliğin koyusu tıpanın etrafında görünür kalır.
                float inHole = 1.0 - smoothstep(-eh, eh, dHole);
                half3 holeCol = _HoleColor.rgb * lerp(1.0, 1.5, saturate(pHole.y / max(_HoleRadii.y, 1e-4) * 0.5 + 0.5));
                col = lerp(col, holeCol, inHole);
                float holeRim = 1.0 - smoothstep(_OutlineWidth - eh, _OutlineWidth + eh, abs(dHole));
                col = lerp(col, _OutlineColor.rgb, holeRim * 0.6);

                // Alt-dudak: en altta konturun hemen içinde ince açık bej çizgi.
                float dBotEdge = SdEllipse(p - float2(0.0, botC), float2(Rx, Ry));
                float lip = (1.0 - smoothstep(_OutlineWidth, _OutlineWidth + 0.014, abs(dBotEdge)))
                    * smoothstep(-0.02, -0.08, p.y - botC);
                col = lerp(col, _TopLight.rgb, lip * 0.5 * inside);

                // Tek tip koyu kontur (dış silüet).
                float outline = 1.0 - smoothstep(_OutlineWidth - e, _OutlineWidth + e, abs(dSil));
                col = lerp(col, _OutlineColor.rgb, outline);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
