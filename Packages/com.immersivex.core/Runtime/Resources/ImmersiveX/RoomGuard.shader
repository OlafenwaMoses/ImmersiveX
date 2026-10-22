// The real room's walls and furniture in VR mode (see RoomGuard), so the user doesn't walk into them while the real world
// is hidden: a soft grid on each surface that shows only near the user and brightens as they come closer, plus a faint
// line where walls meet the floor, always there, so the room's extent can be felt without breaking the virtual world.
// Meshes carry their surface in metres in uv (u along, v up) and the surface's size in uv2.
Shader "ImmersiveX/Room Guard"
{
    Properties
    {
        _Color ("Colour", Color) = (0.55, 0.85, 1.0, 1)
        _Near ("Fully shown within (m)", Float) = 0.45
        _Far ("Starts showing at (m)", Float) = 1.25
        _Spacing ("Grid spacing (m)", Float) = 0.25
        _Base ("Line along the floor, always", Float) = 0.14
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RoomGuard"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Near, _Far, _Spacing, _Base;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 metres : TEXCOORD0;
                float2 size : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 metres : TEXCOORD1;
                float2 size : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.metres = input.metres;
                output.size = input.size;
                return output;
            }

            // 1 on a line of about a pixel and a half, anti-aliased
            float Lines(float2 uv, float spacing)
            {
                float2 w = max(fwidth(uv), 1e-4);
                float2 l = abs(frac(uv / spacing - 0.5) - 0.5) * spacing / (w * 1.5);
                return 1.0 - saturate(min(l.x, l.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // how close the eyes are, across the floor (height doesn't matter: a wall is as near at any height)
                float d = length(input.positionWS.xz - _WorldSpaceCameraPos.xz);
                float near = saturate((_Far - d) / max(_Far - _Near, 1e-3));
                near *= near;

                float2 m = input.metres;
                float2 w = max(fwidth(m), 1e-4);
                float edge = min(min(m.x, input.size.x - m.x), min(m.y, input.size.y - m.y));
                float rim = 1.0 - saturate(edge / (w.x * 2.0) - 0.5);
                float grid = Lines(m, _Spacing);
                float foot = 1.0 - saturate(m.y / (w.y * 2.0) - 0.5); // where the surface meets the floor

                float a = near * max(grid * 0.55, rim) + near * 0.07 + foot * _Base;
                return half4(_Color.rgb, saturate(a) * _Color.a);
            }
            ENDHLSL
        }
    }
}
