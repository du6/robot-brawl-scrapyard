// Scrapyard/PlanetGround - the world's ground (BuilderManager.WorldLook.cs).
// Vertex colour (biome, height band, slope) x main light + ambient, a faint
// grid etched into the flats in world space, and fog. URP 17, WebGL-safe:
// one forward pass, no textures. Listed in Always Included Shaders so a
// material made at runtime finds it in a player build.
Shader "Scrapyard/PlanetGround"
{
    Properties
    {
        _GridColor ("Grid", Color) = (0.25, 0.9, 1.0, 1)
        _GridStrength ("Grid Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fog        : TEXCOORD2;
                float4 color      : COLOR;
            };
            CBUFFER_START(UnityPerMaterial)
                float4 _GridColor;
                float  _GridStrength;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float nl = saturate(dot(n, L.direction));
                float3 amb = SampleSH(n);
                float3 lit = i.color.rgb * (L.color * (nl * L.shadowAttenuation) + amb);
                // the etch: a 6 m grid and a fainter 24 m one, on the flats only
                float2 g1 = abs(frac(i.positionWS.xz / 6.0) - 0.5);
                float l1 = 1.0 - smoothstep(0.0, 0.025, min(g1.x, g1.y));
                float2 g2 = abs(frac(i.positionWS.xz / 24.0) - 0.5);
                float l2 = 1.0 - smoothstep(0.0, 0.008, min(g2.x, g2.y));
                float flatness = saturate((n.y - 0.86) / 0.14);
                float grid = max(l1 * 0.55, l2) * flatness * _GridStrength;
                float3 c = lit + _GridColor.rgb * grid;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
