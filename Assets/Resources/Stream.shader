// Dökme akışı: kaynağın yaka ağzından hedef sıvı yüzeyine DİK inen
// dikdörtgen kolon (yerçekimiyle düz düşer, kavisli değil).
// Uçlar kolon yarı genişliği kadar yuvarlak (kapsül): üst uç ağızdan
// çıkan damla gibi, alt uç yüzeye dalar. Akış hissi aşağı kayan
// parlaklık bantlarından gelir.
Shader "TubeSort/Stream"
{
    Properties
    {
        _FlowFrequency ("Bant sıklığı (dünya birimi başına)", Float) = 14
        _FlowSpeed ("Bant hızı", Float) = 7
        _FlowStrength ("Bant belirginliği", Range(0, 1)) = 0.14
        _EdgeSoftness ("Kenar yumuşaklığı (dünya birimi)", Float) = 0.012
        _SideShading ("Kenar gölgesi", Range(0, 1)) = 0.3
        _GleamStrength ("Işıltı şeridi", Range(0, 1)) = 0.18
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
                float _FlowFrequency;
                float _FlowSpeed;
                float _FlowStrength;
                float _EdgeSoftness;
                float _SideShading;
                float _GleamStrength;
            CBUFFER_END

            // Her dökmede değişen değerler; MaterialPropertyBlock ile gelir.
            float4 _Color;
            float4 _QuadSize;
            float _StreamHalf;   // kolonun yarı genişliği (dünya birimi)
            float _ColumnHalf;   // kolonun yarı boyu (dünya birimi)
            float _CapTop;       // üst uç köşe yarıçapı (0 = köşeli: birleşim ucu)
            float _CapBottom;    // alt uç köşe yarıçapı
            float _CenterY;      // quad merkezinin board y'si — bant fazı parçalar
                                 // arası sürekli kalsın (dikişte kırılmasın)

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

                // Dikey kutu; uç yuvarlaklıkları parça başına: serbest uçlar
                // kapsül gibi yuvarlak, iki parçanın birleştiği uçlar KÖŞELİ —
                // yuvarlak uçlar birleşim yerinde boğum yapıp kolonu "yeniden
                // başlıyor" gösterir. Yarıçap sırası TubeShape.SdRoundedBox:
                // (sağ üst, sağ alt, sol üst, sol alt).
                // fwidth discard'dan ÖNCE (mobil türev kuralı).
                float d = SdRoundedBox(p, float2(_StreamHalf, _ColumnHalf),
                    float4(_CapTop, _CapBottom, _CapTop, _CapBottom));
                float e = max(fwidth(d), _EdgeSoftness);
                float inside = 1.0 - smoothstep(-e, e, d);
                if (inside <= 0.001)
                    discard;

                float4 color = _Color;

                // Silindir yanılsaması: kenara doğru koyulaşma.
                float nx = saturate(abs(p.x) / max(_StreamHalf, 1e-4));
                color.rgb *= 1.0 - nx * nx * _SideShading;

                // Akış hissi: aşağı kayan yumuşak parlaklık bantları. Faz quad
                // yerel değil BOARD uzayında (_CenterY): iki parça üst üste
                // bindiğinde bantlar birebir örtüşür, dikiş görünmez.
                float band = sin((p.y + _CenterY) * _FlowFrequency + _Time.y * _FlowSpeed);
                color.rgb *= 1.0 + band * _FlowStrength * 0.5;

                // Merkezin az solunda ince ışıltı şeridi (sıvı/cam şeritleriyle
                // aynı dil: parlama hep sol-orta tarafta).
                float gleam = 1.0 - smoothstep(0.0, _StreamHalf * 0.5,
                    abs(p.x + _StreamHalf * 0.35));
                color.rgb = lerp(color.rgb, float3(1.0, 1.0, 1.0),
                    gleam * _GleamStrength);

                color.a *= inside;
                return (half4)color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
