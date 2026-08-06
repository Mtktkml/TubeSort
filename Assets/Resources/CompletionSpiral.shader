// Tamamlanma efekti — IŞIK ŞERIDI (helis swoosh): tupu SARAN altin komet-serit,
// dipten collar'in en TEPESINE kadar TIRMANIR ve tupe yapisik durur. Iki parca
// (on/arka) cizilir; arka yari sivinin altinda (sivi orter, cam gosterir) -> sarma
// illuzyonu. Additive; _Progress zarfiyla belirir/soner. Arkasinda iz/parilti YOK.
Shader "TubeSort/CompletionSpiral"
{
    Properties
    {
        _SpiralColor ("Serit rengi (altin)", Color) = (1, 0.84, 0.4, 1)
        _Turns ("Sarim sayisi (S-kivrim)", Float) = 1.6
        _Amplitude ("Yatay genlik (uv) - TubeView twin (tupe yapisik)", Range(0.1, 0.6)) = 0.38
        _Width ("Serit kalinligi (uv)", Range(0.01, 0.2)) = 0.055
        _Softness ("Yumusaklik", Range(0.005, 0.2)) = 0.06
        _RiseStart ("Tirmanma baslangici (progress)", Range(0, 1)) = 0.12
        _RiseEnd ("Tirmanma bitisi (progress)", Range(0, 1)) = 0.68
        _TailLen ("Kuyruk uzunlugu (uv)", Range(0.1, 0.7)) = 0.34
        _BottomAnchor ("Kuyruk dip demiri (uv)", Range(0, 0.2)) = 0.04

        // Script'ten surulur (TubeView). UnityPerMaterial'de OLMALI (SRP Batcher).
        [HideInInspector] _Progress ("Progress (script)", Float) = 0
        [HideInInspector] _HeadMax ("Head max (script)", Float) = 0
        [HideInInspector] _BackSide ("Arka yari mi (0 on / 1 arka)", Float) = 0
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
                float4 _SpiralColor;
                float _Turns;
                float _Amplitude;
                float _Width;
                float _Softness;
                float _RiseStart;
                float _RiseEnd;
                float _TailLen;
                float _BottomAnchor;
                float _Progress;
                // Basin ulasacagi en yuksek uv.y (collar EN TEPESI orani); TubeView
                // landingY/RingTop gonderir (yildizla ayni nokta).
                float _HeadMax;
                // Sarma: bu satirin helis acisinin cos'u On/Arka belirler; bu renderer
                // yalniz kendi yarisini cizer (0 on order 7 / 1 arka order -3).
                float _BackSide;
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
                // Serit ANCAK kendi tirmanma zamani (_RiseStart) gelince gorunur -> tup
                // tamamlanir tamamlanmaz erken FLAS/gorunme YOK. Sonra soner.
                float env = smoothstep(_RiseStart, _RiseStart + 0.06, _Progress)
                          * (1.0 - smoothstep(0.72, 0.90, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;
                float headMax = max(_HeadMax, 0.35);

                // Tirmanan bas: en tepeye (headMax) kadar cikip durur.
                float head = smoothstep(_RiseStart, _RiseEnd, _Progress) * headMax;

                // Faz DEMIRLI helis: ust (uv.y=_HeadMax) sag-tepede biter (yildizla ayni nokta).
                float anchorPhase = 1.5707963 - _HeadMax * _Turns * 6.2831853;
                float helixX = 0.5 + _Amplitude
                    * sin(uv.y * _Turns * 6.2831853 + anchorPhase);

                // Kuyruk tabani: dipte demirli (BottomAnchor); bas yukselince kalkar.
                float tailBottom = max(head - _TailLen, _BottomAnchor);
                float tailLen = max(head - tailBottom, 1e-4);

                // Serit boyunca konum: 0 = kuyruk UCU (dip), 1 = bas.
                float s = saturate((uv.y - tailBottom) / tailLen);

                // Genislik ucta 0'a daralir -> KALEM UCU gibi sivri baslangic.
                float wProfile = smoothstep(0.0, 0.25, s);
                float halfW = _Width * wProfile;
                float edge = _Softness * wProfile + 0.004;
                float dx = abs(uv.x - helixX);
                float ribbon = 1.0 - smoothstep(halfW, halfW + edge, dx);

                // Komet bandi: kuyruk ucundan basa; basin ustunu kes.
                float band = smoothstep(tailBottom, tailBottom + 0.02, uv.y)
                           * (1.0 - smoothstep(head, head + 0.02, uv.y));

                // Uzunluk boyunca parlaklik: kuyrukta sonuk, basa dogru parlak.
                float lengthBright = 0.3 + 0.7 * s;
                float body = ribbon * band * lengthBright;

                // Parlak KOMET BASI: bas hizasinda keskin parlama.
                float hd = (uv.y - head) / 0.045;
                float headGlow = ribbon * exp(-hd * hd);

                // Akis parlamasi.
                float flow = 0.8 + 0.2 * sin(uv.y * 20.0 - _Time.y * 6.0);

                float intensity = (body * flow * 0.85 + headGlow * 1.25) * env;

                // SARMA (occlusion): on/arka yari — cos isareti (bkz. TubeView). Arka yari
                // order -3'te sivinin altinda kalir (gizli), cam gosterir.
                float cosT = cos(uv.y * _Turns * 6.2831853 + anchorPhase);
                const float sblend = 0.12;
                float sideMask = (_BackSide < 0.5)
                    ? smoothstep(-sblend, sblend, cosT)
                    : smoothstep(sblend, -sblend, cosT);
                intensity *= sideMask;
                if (intensity <= 0.0005)
                    discard;

                float3 col = _SpiralColor.rgb * intensity;
                return half4(col, intensity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
