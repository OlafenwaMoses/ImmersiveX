// 360° and 180° video on the inside of a sphere around the viewer (see VideoPlayable).
// The equirectangular lookup is done per pixel from the view direction, so there's no seam where the video wraps, and
// stereo layouts pick each eye's half: top-bottom (left eye on top) or side-by-side (left eye on the left).
Shader "ImmersiveX/Panorama"
{
    Properties
    {
        [MainTexture] _MainTex ("Video", 2D) = "black" {}
        _Layout ("Layout (0 mono, 1 top-bottom, 2 side-by-side)", Float) = 0
        _Coverage ("Coverage (360 or 180 degrees)", Float) = 360
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }

        Pass
        {
            Name "Panorama"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front   // seen from inside
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Layout;
            float _Coverage;

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.direction = input.positionOS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.direction);
                float longitude = atan2(d.x, d.z);          // 0 = forward (+Z), positive to the right
                float latitude = asin(clamp(d.y, -1.0, 1.0));
                float2 uv = float2(0.0, latitude / PI + 0.5);
                if (_Coverage < 270.0)
                {
                    if (abs(longitude) > PI * 0.5)
                        return half4(0.0, 0.0, 0.0, 1.0);   // behind a 180° video
                    uv.x = longitude / PI + 0.5;
                }
                else
                {
                    uv.x = longitude / (2.0 * PI) + 0.5;
                }

                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                float rightEye = unity_StereoEyeIndex > 0 ? 1.0 : 0.0;
                #else
                float rightEye = 0.0;
                #endif
                if (_Layout > 0.5 && _Layout < 1.5)
                    uv.y = uv.y * 0.5 + (1.0 - rightEye) * 0.5;  // top-bottom: left eye in the top half
                else if (_Layout > 1.5)
                    uv.x = uv.x * 0.5 + rightEye * 0.5;          // side-by-side: left eye in the left half

                // Mip 0: derivatives jump where the longitude wraps, which would pick a blurry mip along the seam.
                half3 color = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
