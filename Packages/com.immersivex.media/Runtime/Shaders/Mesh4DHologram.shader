// Draws a 4D mesh in the hologram look (SP4M version 4, see Mesh4DContainer): every object is light, added over the room.
// Per pixel: its hue × ((base + rim + contour lines) × body gain + scan band × band gain + glow) × scanlines × flicker,
// plus a white-mixed front where it is materialising; where it hasn't materialised yet (or has dissolved), nothing.
// The same maths as the web player's holoLook (the GenXR hologram pipeline), so the piece looks the same in the room.
//
// The renderer's transform takes the content's coordinates (three.js: right-handed, y up) into the room; object space
// below means each baked object's own frame, where its contour lines and materialise front are defined.
Shader "ImmersiveX/Mesh 4D Hologram"
{
    Properties
    {
        _Ground ("Ground height (content units)", Float) = -100000
        _HoloGain ("Brightness in the room", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mesh4DHologram"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            // light adds up; the alpha it leaves lets the headset's compositor keep it over the passthrough
            Blend One One, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            // 8 per object: 3 matrix columns with the translation in w, (visible, glow, flags, side), (-, -, -, materialised),
            // (hue, contour spacing), (height span low, high, body gain, band gain), -
            StructuredBuffer<float4> _Objects;
            #define OBJ(o, k) _Objects[(o) * 8u + (k)]
            float _HoloTime;
            float _HoloFade;
            float _HoloGain; // the whole look's brightness in the room (light added over a lit room needs a little more)
            float _Ground;
            float4 _Holo0; // base, rim, rim power, line
            float4 _Holo1; // line width, sweep, sweep width, sweep speed
            float4 _Holo2; // sweep start, sweep span, scanlines, flicker
            float4 _Holo3; // glow, front, front width, front noise

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 obj : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local : TEXCOORD0;
                float3 scene : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                nointerpolation uint obj : TEXCOORD4;
                nointerpolation float flicker : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4x4 ObjectMatrix(uint o)
            {
                float4 a = OBJ(o, 0u), b = OBJ(o, 1u), c = OBJ(o, 2u);
                return float4x4(a.x, b.x, c.x, a.w,
                                a.y, b.y, c.y, b.w,
                                a.z, b.z, c.z, c.w,
                                0.0, 0.0, 0.0, 1.0);
            }

            // the scene's 3D value noise (the web player's vnoise3)
            float Hash3(float3 p) { return frac(sin(p.x * 127.1 + p.y * 311.7 + p.z * 74.7) * 43758.5453123); }
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = p - i;
                float3 u = f * f * (3.0 - 2.0 * f);
                float x00 = lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), u.x), x10 = lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), u.x);
                float x01 = lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), u.x), x11 = lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                uint o = (uint)(input.obj.x + 0.5);
                if (OBJ(o, 3u).x < 0.5 || OBJ(o, 4u).w < 0.002)
                {
                    output.positionCS = float4(0.0, 0.0, 2.0, 1.0); // not there this frame
                    return output;
                }

                float4x4 m = ObjectMatrix(o);
                float3 scene = mul(m, float4(input.positionOS, 1.0)).xyz;
                output.positionWS = TransformObjectToWorld(scene);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.local = input.positionOS;
                output.scene = scene;
                output.normalWS = TransformObjectToWorldDir(mul((float3x3)m, input.normalOS), false);
                output.obj = o;
                output.flicker = 1.0 - _Holo2.w * ValueNoise(float3(_HoloTime * 9.0, (float)o * 1.7, 0.5)); // one value per object
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (input.scene.y < _Ground - 0.05)
                    discard; // below the floor
                uint o = input.obj;
                float glow = OBJ(o, 3u).y, rv = OBJ(o, 4u).w;
                float4 r5 = OBJ(o, 5u), r6 = OBJ(o, 6u);
                float3 hue = r5.rgb;
                float spacing = r5.w, ylo = r6.x, yhi = r6.y;
                float2 gain = r6.zw;

                // materialising: what lies above the front isn't there yet
                float edge = 0.0;
                if (rv < 0.999)
                {
                    float span = max(1e-3, yhi - ylo);
                    float n = (input.local.y - ylo) / span + (ValueNoise(input.local * (14.0 / span)) - 0.5) * _Holo3.w;
                    float front = rv * 1.32 - 0.16;
                    if (n > front)
                        discard;
                    edge = 1.0 - smoothstep(0.0, _Holo3.z, front - n);
                }

                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(1.0 - abs(dot(normalize(input.normalWS), v)), _Holo0.z);
                float fl = frac(input.local.y / spacing), dl = min(fl, 1.0 - fl) * spacing, aa = max(fwidth(input.local.y), 1e-5);
                float contour = 1.0 - smoothstep(_Holo1.x * 0.5, _Holo1.x * 0.5 + aa * 1.25, dl);
                float sweepY = _Holo2.x + fmod(_HoloTime * _Holo1.w, _Holo2.y);
                float band = exp(-pow((input.scene.y - sweepY) / _Holo1.z, 2.0));
                float scan = 1.0 - _Holo2.z * (0.5 + 0.5 * sin(input.positionCS.y * 1.5708));
                float k = (_Holo0.x + _Holo0.y * rim + _Holo0.w * contour) * gain.x + _Holo1.y * band * gain.y + _Holo3.x * glow;
                float3 c = hue * k * scan * input.flicker;
                c += lerp(hue, 1.0, 0.55) * edge * _Holo3.y * clamp(gain.x * 1.2, 0.3, 1.0);
                c *= _HoloFade * _HoloGain;
                c = saturate(c);
                half alpha = (half)saturate(max(c.r, max(c.g, c.b)) * 1.2);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c = SRGBToLinear(c); // the look is worked out in display colour
                #endif
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
