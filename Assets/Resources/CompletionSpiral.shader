// Tamamlanma efekti — IŞIK ŞERIDI / KUYRUKLU YILDIZ (helis swoosh): tupu saran
// tek parlak altin serit, dipten collar'a TIRMANIR. Referans mantigi: uniform
// helis DEGIL — PARLAK BAS + arkasinda sonen KISA kuyruk (komet). Bas _HeadMax'e
// (collar sag-alti; TubeView gonderir) kadar cikip durur, mouth'un ustune gecmez.
// Dipteki halka glow'unun uzerine binmesin diye en altta (uv.y<0.10) sonumlenir.
// Additive; _Progress zarfiyla belirir, collar'a varinca (yildiz devralir) soner.
Shader "TubeSort/CompletionSpiral"
{
    Properties
    {
        _SpiralColor ("Serit rengi (altin)", Color) = (1, 0.84, 0.4, 1)
        _Turns ("Sarim sayisi (S-kivrim)", Float) = 1.6
        _Amplitude ("Yatay genlik (uv)", Range(0.1, 0.5)) = 0.30
        _Width ("Serit kalinligi (uv)", Range(0.01, 0.2)) = 0.055
        _Softness ("Yumusaklik", Range(0.005, 0.2)) = 0.06
        _RiseStart ("Tirmanma baslangici (progress)", Range(0, 1)) = 0.12
        _RiseEnd ("Tirmanma bitisi (progress)", Range(0, 1)) = 0.68
        _TailLen ("Kuyruk uzunlugu (uv)", Range(0.1, 0.7)) = 0.34
        _BottomAnchor ("Kuyruk dip demiri (uv)", Range(0, 0.2)) = 0.04
        _SideBlend ("Arka (uzak) yariyi gizle - siluet gecis yumusakligi", Range(0.01, 0.4)) = 0.12

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
                // Basin ulasabilecegi en yuksek uv.y (collar sag-usti orani); TubeView
                // = landingY/RingTop gonderir. 0 gelirse (ayarsiz) tam tepe: guvenli
                // varsayilan icin max ile 0.82 tabanla.
                float _HeadMax;
                // SARMA: helis acisinin cos'u derinligi verir. cosT>=0 ON (yakin) yari
                // cizilir; cosT<0 ARKA (uzak) yari GORUNMEZ (cizilmez) -> serit tupun
                // arkasina gecince kaybolur. _SideBlend siluet kenarinda yumusak kaybolus.
                float _SideBlend;
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
                // Belir (0..0.12); collar'a varinca (yildiz devralir) son (0.72..0.90).
                float env = smoothstep(0.0, 0.12, _Progress)
                          * (1.0 - smoothstep(0.72, 0.90, _Progress));
                if (env <= 0.001)
                    discard;

                float2 uv = input.uv;
                float headMax = max(_HeadMax, 0.35);

                // Tirmanan bas: collar'da (headMax) durur.
                float head = smoothstep(_RiseStart, _RiseEnd, _Progress) * headMax;

                // Helis: serit merkezi x. Faz DEMIRLI (zamanla donme YOK): bas
                // (uv.y = _HeadMax) tam sag-tepede (sin = +1) bitecek sekilde sabit
                // faz secilir. Boylece komet YOLUNU degistirmeden dogal olarak
                // sag-uste kivrilip ORADA durur (ani duzeltme/donus yok). _Turns
                // kivrim sikligini (iki kivrim arasi mesafeyi) belirler; faz, tepeyi
                // her _Turns/kapasitede saga sabitler. Yildiz da tam o noktada (TubeView).
                float anchorPhase = 1.5707963 - _HeadMax * _Turns * 6.2831853;
                float helixX = 0.5 + _Amplitude
                    * sin(uv.y * _Turns * 6.2831853 + anchorPhase);
                // Kuyruk tabani: dipte bir noktada demirli (BottomAnchor); bas
                // yeterince yukselince kalkar (komet yukselirken dipten kopar).
                float tailBottom = max(head - _TailLen, _BottomAnchor);
                float tailLen = max(head - tailBottom, 1e-4);

                // Serit boyunca konum: 0 = kuyruk UCU (dip), 1 = bas.
                float s = saturate((uv.y - tailBottom) / tailLen);

                // Genislik ucta 0'a daralir -> KALEM UCU gibi dogal sivri baslangic
                // (duz/dikdortgen yatay kenar YOK). Yumusaklik da daraldigindan uc
                // keskin kalir; ~%25 boyunca acilir, sonra tam genislik.
                float wProfile = smoothstep(0.0, 0.25, s);
                float halfW = _Width * wProfile;
                float edge = _Softness * wProfile + 0.004;
                float dx = abs(uv.x - helixX);
                float ribbon = 1.0 - smoothstep(halfW, halfW + edge, dx);

                // Komet bandi: kuyruk ucundan (yumusak sivri) basa; basin ustunu kes.
                float band = smoothstep(tailBottom, tailBottom + 0.02, uv.y)
                           * (1.0 - smoothstep(head, head + 0.02, uv.y));

                // Uzunluk boyunca parlaklik: kuyrukta sonuk, basa dogru parlak.
                float lengthBright = 0.3 + 0.7 * s;
                float body = ribbon * band * lengthBright;

                // Parlak KOMET BASI: bas hizasinda keskin parlama.
                float hd = (uv.y - head) / 0.045;
                float headGlow = ribbon * exp(-hd * hd);

                // Akis pariltisi.
                float flow = 0.8 + 0.2 * sin(uv.y * 20.0 - _Time.y * 6.0);

                float intensity = (body * flow * 0.85 + headGlow * 1.25) * env;

                // SARMA: bu satirin (uv.y) helis acisinin cos'u derinligi verir.
                // cosT>=0 ON (yakin) yari -> gorunur; cosT<0 ARKA (uzak) yari ->
                // GORUNMEZ (kaybolur). Siluet kenarinda (_SideBlend) yumusak kaybolus.
                // Boylece serit tupun onunden gecer, arkasina donunce kaybolur, sonraki
                // yarim-turda onden yeniden belirir (opak silindir sarma hissi).
                float cosT = cos(uv.y * _Turns * 6.2831853 + anchorPhase);
                float sideMask = smoothstep(-_SideBlend, _SideBlend, cosT);
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
