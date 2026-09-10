// Scrapyard/SkyBody - the sister planet and the moon on the horizon. Unlit,
// banded, limb-shaded, NO fog (they sit 800 m out, past the fog's end, and
// must read as sky). Listed in Always Included Shaders.
Shader "Scrapyard/SkyBody"
{
    Properties
    {
        _ColorA ("Band A", Color) = (0.85, 0.55, 0.35, 1)
        _ColorB ("Band B", Color) = (0.55, 0.30, 0.45, 1)
        _Bands ("Bands", Float) = 7
        _SunDir ("Sun Dir", Vector) = (0.4, 0.5, -0.7, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-20" }
        Pass
        {
            Name "SkyBody"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                float  localY     : TEXCOORD2;
            };
            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA, _ColorB, _SunDir;
                float  _Bands;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.localY = v.positionOS.y;   // the sphere primitive: -0.5 .. 0.5
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float band = sin(i.localY * _Bands * 6.2832 + sin(i.localY * 23.0) * 0.6) * 0.5 + 0.5;
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, band);
                float day = saturate(dot(n, normalize(_SunDir.xyz)));
                float limb = pow(saturate(dot(n, normalize(i.viewWS))), 0.45);
                float3 c = col * (0.45 + 0.55 * day) * (0.55 + 0.45 * limb);   // seen live: darker read as a blot on the sky
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
