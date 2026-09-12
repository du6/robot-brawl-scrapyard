// Scrapyard/Beacon — the shaft of light over a treasure chest and over the
// current objective.
//
// owen, 2026-09-12: "why does each item on the planet have a vertical bar on
// top of it". The beam was a plain emissive CYLINDER, and emissive only reads
// as light when something blooms it — which nothing does since the web build
// dropped URP's post-processing to save 2.8 MB. So it drew as a matte stick
// standing on every chest.
//
// This is light instead of geometry: additive blending (the ground shows
// through it), no depth write, brightest at the base and gone by the top,
// softened at the silhouette edges so the cylinder never shows its outline,
// and a slow pulse so it reads as a signal rather than a post.
//
// Back faces are culled on purpose: with Cull Off the near and far walls of
// the cylinder both add, and standing next to the objective marker that
// doubled brightness blew out to a white wall that hid the chest it marks.
Shader "Scrapyard/Beacon"
{
    Properties
    {
        _Color ("Colour", Color) = (1.0, 0.62, 0.18, 1)
        _Intensity ("Intensity", Float) = 1.0
        _Pulse ("Pulse", Float) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Beacon"
            Tags { "LightMode"="UniversalForward" }
            Blend One One          // additive: it can only add light, never darken
            ZWrite Off
            Cull Back             // one layer of light, not the front and back faces added together
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  h          : TEXCOORD0;   // 0 at the base, 1 at the top
                float3 normalWS   : TEXCOORD1;
                float3 viewWS     : TEXCOORD2;
            };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _Pulse;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = p.positionCS;
                // a Unity cylinder is 2 units tall centred on its origin
                o.h = saturate(IN.positionOS.y * 0.5 + 0.5);
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // fade out with height: full at the base, nothing at the tip
                float up = pow(1.0 - IN.h, 1.8);
                // and soften where the cylinder turns away from the eye, so the
                // silhouette has no hard edge to read as a solid rod
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);
                float rim = saturate(1.0 - abs(dot(n, v)));   // 0 facing us, 1 at the edge
                float body = 1.0 - rim;                       // brightest through the middle
                float pulse = 1.0 + _Pulse * sin(_Time.y * 2.2);
                float a = up * (0.25 + 0.75 * body * body) * _Intensity * pulse;
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
