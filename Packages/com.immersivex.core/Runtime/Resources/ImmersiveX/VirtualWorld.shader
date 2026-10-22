// The virtual world for VR mode (see VirtualWorld): a quiet, low-lit place that suits any content. One shader, two parts:
//   - the sky (_Part 0, drawn inside a sphere that follows the eyes): deep night blue overhead fading to a dusky haze at
//     the horizon, with sparse, faint, slowly twinkling stars;
//   - the floor (_Part 1): dark slate with a faint metre grid that fades with distance, a soft pool of light where the
//     content stands, and the far floor melting into the horizon haze so the ground seems to go on for ever.
// Everything is a little dithered: dark gradients band on an 8-bit headset display otherwise.
Shader "ImmersiveX/Virtual World"
{
    Properties
    {
        _Part ("Part (0 sky, 1 floor)", Float) = 0
        _Zenith ("Sky overhead", Color) = (0.02, 0.027, 0.055, 1)
        _Horizon ("Horizon haze", Color) = (0.086, 0.098, 0.165, 1)
        _Floor ("Floor", Color) = (0.04, 0.043, 0.063, 1)
        _Pool ("Pool of light", Color) = (0.06, 0.062, 0.088, 1)
        _Grid ("Grid lines", Color) = (0.12, 0.13, 0.19, 1)
        _Centre ("Where the content stands (world)", Vector) = (0, 0, 0, 0)
        _PoolRadius ("Pool radius (m)", Float) = 3.5
        _Stars ("Star brightness", Float) = 0.45
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "VirtualWorld"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Part;
                float4 _Zenith, _Horizon, _Floor, _Pool, _Grid, _Centre;
                float _PoolRadius, _Stars;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // up to half a step of the display's 8 bits, different per pixel, so dark gradients don't band (added in display
            // values: in linear values a step near black is far smaller than 1/255)
            float3 Dither(float3 c, float4 positionCS)
            {
                float n = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
                return SRGBToLinear(saturate(LinearToSRGB(c) + (n - 0.5) / 255.0));
            }

            float3 Sky(float3 dir)
            {
                float e = dir.y;
                float3 c = lerp(_Horizon.rgb, _Zenith.rgb, pow(smoothstep(-0.02, 0.65, e), 0.8));
                c += _Horizon.rgb * 0.35 * exp(-abs(e) * 18.0); // a soft haze band along the horizon
                // stars: one candidate per cell of a fine grid on the sky, few of them lit, faint near the horizon
                float3 cell = floor(dir * 160.0);
                float h = Hash(cell);
                if (h > 0.9965 && e > 0.04)
                {
                    float3 centre = (cell + 0.5 + (float3(Hash(cell + 1.7), Hash(cell + 3.1), Hash(cell + 5.3)) - 0.5) * 0.6) / 160.0;
                    float d = length(normalize(centre) - dir) * 160.0;
                    float twinkle = 0.65 + 0.35 * sin(_Time.y * (0.6 + 2.0 * Hash(cell + 9.1)) + h * 80.0);
                    c += _Stars * twinkle * smoothstep(0.04, 0.3, e) * exp(-d * d * 9.0) * lerp(float3(0.75, 0.82, 1.0), float3(1.0, 0.9, 0.78), Hash(cell + 2.2));
                }
                return c;
            }

            float3 Ground(float3 p)
            {
                float2 fromCentre = p.xz - _Centre.xz;
                float2 fromEyes = p.xz - _WorldSpaceCameraPos.xz;
                float r = length(fromCentre), far = length(fromEyes);
                float3 c = _Floor.rgb + _Pool.rgb * exp(-(r * r) / (_PoolRadius * _PoolRadius));
                // a metre grid, anti-aliased, fading out with distance before it can shimmer
                float2 w = max(fwidth(p.xz), 1e-4);
                float2 l = abs(frac(p.xz - 0.5) - 0.5) / (w * 1.2);
                float onLine = 1.0 - saturate(min(l.x, l.y));
                float fade = exp(-far / 10.0) * saturate(1.0 - max(w.x, w.y) * 4.0);
                c = lerp(c, _Grid.rgb, onLine * fade);
                // far away the floor melts into the horizon haze
                return lerp(c, _Horizon.rgb * 0.92, smoothstep(12.0, 60.0, far));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 c = _Part < 0.5 ? Sky(normalize(input.positionWS - _WorldSpaceCameraPos)) : Ground(input.positionWS);
                return half4(Dither(c, input.positionCS), 1.0);
            }
            ENDHLSL
        }
    }
}
