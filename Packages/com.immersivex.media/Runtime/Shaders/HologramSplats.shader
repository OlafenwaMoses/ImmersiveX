// Draws one frame of a streamed Gaussian-splat hologram (see HologramPlayer).
//
// _Frame is the frame file as it arrives from the server, read as uints: an 8-uint header, then one texel of 4
// uints per Gaussian (uint16 position inside the frame's bounding box, 8-bit log scales, 8-bit rotation quaternion,
// RGB), then one opacity byte per Gaussian. _Order lists the Gaussians farthest first. Every Gaussian is a quad
// (4 mesh vertices); the vertex shader projects its 3D covariance to a screen-space ellipse for each eye.
//
// The maths matches GenXR's web player (hologram-player.js), so the hologram looks the same in the headset.
Shader "ImmersiveX/Hologram Splats"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Splats"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One OneMinusSrcAlpha // premultiplied, drawn back to front
            ZWrite Off
            ZTest LEqual
            Cull Off                   // the object's scale flips Y (the source uses a y-down camera frame)

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            StructuredBuffer<uint> _Frame;
            StructuredBuffer<uint> _Order;
            float3 _BoundsMin;
            float3 _BoundsMax;
            float2 _ScaleRange;
            int _AlphaStart;
            float _Opacity;

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 corner : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // q = (w, x, y, z)
            float3x3 QuaternionToMatrix(float4 q)
            {
                float w = q.x, x = q.y, y = q.z, z = q.w;
                return float3x3(
                    1.0 - 2.0 * (y * y + z * z), 2.0 * (x * y - w * z), 2.0 * (x * z + w * y),
                    2.0 * (x * y + w * z), 1.0 - 2.0 * (x * x + z * z), 2.0 * (y * z - w * x),
                    2.0 * (x * z - w * y), 2.0 * (y * z + w * x), 1.0 - 2.0 * (x * x + y * y));
            }

            Varyings Hidden(Varyings output)
            {
                output.positionCS = float4(0.0, 0.0, 2.0, 1.0); // outside the clip volume on every API
                return output;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                uint splat = _Order[input.vertexID >> 2];
                uint cornerIndex = input.vertexID & 3u;
                float2 corner = float2((cornerIndex & 1u) != 0u ? 2.0 : -2.0, (cornerIndex & 2u) != 0u ? 2.0 : -2.0);

                uint texel = 8u + splat * 4u;
                uint4 t = uint4(_Frame[texel], _Frame[texel + 1u], _Frame[texel + 2u], _Frame[texel + 3u]);
                uint opacity = (_Frame[(uint)_AlphaStart + (splat >> 2)] >> ((splat & 3u) * 8u)) & 0xffu;

                float3 unit = float3(t.x & 0xffffu, t.x >> 16, t.y & 0xffffu) / 65535.0;
                float3 positionWS = TransformObjectToWorld(lerp(_BoundsMin, _BoundsMax, unit));
                float3 positionVS = TransformWorldToView(positionWS);
                float4 positionCS = TransformWorldToHClip(positionWS);
                float depth = -positionVS.z;
                float limit = 1.2 * positionCS.w;
                if (depth < 0.05 || abs(positionCS.x) > limit || abs(positionCS.y) > limit)
                    return Hidden(output);

                // 3D covariance in object space, then in view space.
                float3 scale = exp(_ScaleRange.x + float3((t.y >> 16) & 0xffu, t.y >> 24, t.z & 0xffu) / 255.0 * (_ScaleRange.y - _ScaleRange.x));
                float4 rotation = normalize((float4((t.z >> 8) & 0xffu, (t.z >> 16) & 0xffu, t.z >> 24, t.w & 0xffu) - 128.0) / 128.0);
                float3x3 m = mul(QuaternionToMatrix(rotation), float3x3(scale.x, 0.0, 0.0, 0.0, scale.y, 0.0, 0.0, 0.0, scale.z));
                float3x3 toView = mul((float3x3)UNITY_MATRIX_V, (float3x3)UNITY_MATRIX_M);
                float3x3 covariance = mul(toView, mul(mul(m, transpose(m)), transpose(toView)));

                // Project to a 2D covariance in this eye's pixels (the Jacobian of the perspective projection).
                float2 focal = float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]) * _ScreenParams.xy * 0.5;
                float3 ju = float3(focal.x / depth, 0.0, focal.x * positionVS.x / (depth * depth));
                float3 jv = float3(0.0, focal.y / depth, focal.y * positionVS.y / (depth * depth));
                float a = dot(ju, mul(covariance, ju)) + 0.3;
                float b = dot(ju, mul(covariance, jv));
                float d = dot(jv, mul(covariance, jv)) + 0.3;

                float mid = 0.5 * (a + d);
                float radius = length(float2(0.5 * (a - d), b));
                float major = mid + radius;
                float minor = mid - radius;
                if (minor < 0.0)
                    return Hidden(output);
                float2 axis = float2(b, major - a);
                axis = dot(axis, axis) > 1e-12 ? normalize(axis) : float2(1.0, 0.0);
                float2 majorAxis = min(sqrt(2.0 * major), 1024.0) * axis;
                float2 minorAxis = min(sqrt(2.0 * minor), 1024.0) * float2(axis.y, -axis.x);

                positionCS.xy += (corner.x * majorAxis + corner.y * minorAxis) / _ScreenParams.xy * positionCS.w;
                output.positionCS = positionCS;

                half3 color = half3((t.w >> 8) & 0xffu, (t.w >> 16) & 0xffu, t.w >> 24) / 255.0;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color = SRGBToLinear(color);
                #endif
                output.color = half4(color, opacity / 255.0 * _Opacity);
                output.corner = corner;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float power = -dot(input.corner, input.corner);
                if (power < -4.0)
                    discard;
                half alpha = (half)(exp(power) * input.color.a);
                if (alpha < 1.0 / 255.0)
                    discard;
                return half4(input.color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
