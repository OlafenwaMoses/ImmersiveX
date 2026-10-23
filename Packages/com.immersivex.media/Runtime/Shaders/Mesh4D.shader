// Draws a 4D mesh (see Mesh4DPlayable): the baked scene's own triangles, posed by a table of object matrices that
// changes every frame, coloured from colour probes (through a texture atlas painted twice a second, or per vertex for
// repeated fine meshes such as engines), with the scene's per-pixel rules: a cutaway wedge with a glowing edge, a door,
// a set dissolving outward with a glowing front, front/back sides, and highlights; in version 3 also each object's own
// cutaway (a plane or a wedge in its frame), a sphere dissolve with the scene's own noise, and the set dissolving at the
// edge of the room while the stage follows its subject away. Colours are already lit: nothing is relit here.
//
// The renderer's transform takes the content's coordinates (three.js: right-handed, y up) into the room; object space
// below means each baked object's own frame, where the cutaway and door are defined.
Shader "ImmersiveX/Mesh 4D"
{
    Properties
    {
        _FrontSign ("Front sign", Float) = 1
        _Ground ("Ground height (content units)", Float) = -100000
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mesh4D"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ MESH4D_PER_VERTEX
            #pragma multi_compile_local _ MESH4D_RULES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            // 8 per object: 3 matrix columns with the translation in w, (visible, highlight, flags, side),
            // (first probe, flip, hull radius, own cut level), then its own cut: (type, a), (ref, p0), (p1, glow width, -, -)
            StructuredBuffer<float4> _Objects;
            #define OBJ(o, k) _Objects[(o) * 8u + (k)]
            TEXTURE2D(_AtlasA);
            TEXTURE2D(_AtlasB);
            SAMPLER(sampler_AtlasA);
            TEXTURE2D(_ProbesA);
            TEXTURE2D(_ProbesB);
            float _KeyMix;
            float _AtlasHeight;
            float _FrontSign;
            float _CutAngle;
            float2 _CutDir;
            float _DissolveR;
            float2 _DissolveC;
            float _DoorOpen;
            float2 _DoorY;
            float2 _DoorA;
            float4 _CutGlow;
            float4 _DissolveGlow;
            float _Ground; // the content's ground: what's below it is under the room's floor and isn't drawn
            float _DissolveSphere; // version 3: the set dissolves inside a sphere around (C.x, centre y, C.y)
            float4 _DissolveNoise; // scale, amplitude, glow width, centre y
            float4 _Focus; // while the stage follows its subject: subject (x, z), radius, glow width (radius 0: off)

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 obj : TEXCOORD1;
                #if defined(MESH4D_PER_VERTEX)
                float3 probesFront : TEXCOORD2;
                float3 probesBack : TEXCOORD3;
                float3 weightsFront : TEXCOORD4;
                float3 weightsBack : TEXCOORD5;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 local : TEXCOORD1;
                float3 scene : TEXCOORD2;
                nointerpolation uint obj : TEXCOORD3;
                nointerpolation float det : TEXCOORD4;
                #if defined(MESH4D_PER_VERTEX)
                float3 colorFront : TEXCOORD5;
                float3 colorBack : TEXCOORD6;
                #endif
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

            float3 Probe(Texture2D tex, float index)
            {
                uint k = (uint)(index + 0.5);
                return tex.Load(int3(k % 2048u, k / 2048u, 0)).rgb;
            }

            #if defined(MESH4D_PER_VERTEX)
            float3 Blend3(Texture2D tex, float first, float3 index, float3 w)
            {
                return Probe(tex, first + index.x) * w.x + Probe(tex, first + index.y) * w.y + Probe(tex, first + index.z) * w.z;
            }
            #endif

            Varyings Hidden(Varyings output)
            {
                output.positionCS = float4(0.0, 0.0, 2.0, 1.0);
                return output;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                uint o = (uint)(input.obj.x + 0.5);
                float4 info = OBJ(o, 3u);
                if (info.x < 0.5)
                    return Hidden(output); // not visible this frame
                float4x4 m = ObjectMatrix(o);
                float3 scene = mul(m, float4(input.positionOS, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(scene));
                output.uv = input.uv;
                output.local = input.positionOS;
                output.scene = scene;
                output.obj = o;
                output.det = determinant((float3x3)m);
                #if defined(MESH4D_PER_VERTEX)
                float first = OBJ(o, 4u).x;
                float3 wf = input.weightsFront / max(dot(input.weightsFront, 1.0), 1e-4);
                float3 wb = input.weightsBack / max(dot(input.weightsBack, 1.0), 1e-4);
                output.colorFront = lerp(Blend3(_ProbesA, first, input.probesFront, wf), Blend3(_ProbesB, first, input.probesFront, wf), _KeyMix);
                output.colorBack = lerp(Blend3(_ProbesA, first, input.probesBack, wb), Blend3(_ProbesB, first, input.probesBack, wb), _KeyMix);
                #endif
                return output;
            }

            // The scene's 3D value noise (matches the web player's sxDissolveNoise).
            float LatticeHash(float3 p) { return frac(sin(p.x * 127.1 + p.y * 311.7 + p.z * 74.7) * 43758.5453123); }
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = p - i;
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = LatticeHash(i), n100 = LatticeHash(i + float3(1, 0, 0)), n010 = LatticeHash(i + float3(0, 1, 0)), n110 = LatticeHash(i + float3(1, 1, 0));
                float n001 = LatticeHash(i + float3(0, 0, 1)), n101 = LatticeHash(i + float3(1, 0, 1)), n011 = LatticeHash(i + float3(0, 1, 1)), n111 = LatticeHash(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y), lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
            }
            float DissolveNoise(float3 p) { return 0.65 * ValueNoise(p * 0.11) + 0.35 * ValueNoise(p * 0.37 + float3(17.0, 0.0, 0.0)); }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 info = OBJ(input.obj, 3u);
                float4 info2 = OBJ(input.obj, 4u);
                uint flags = (uint)(info.z + 0.5);
                float cutGlow = 0.0, dissolveGlow = 0.0;
                #if defined(MESH4D_RULES)
                // Only objects that take part in a rule draw with this variant: discarding turns off early depth
                // rejection, so everything else draws without it.
                if (input.scene.y < _Ground - 0.05)
                    discard;

                // cutaway: a wedge around the object's up axis
                if ((flags & 1u) != 0u && _CutAngle > 0.001)
                {
                    float r = length(input.local.xz);
                    if (r > 0.05)
                    {
                        float angle = acos(clamp(dot(input.local.xz / r, _CutDir), -1.0, 1.0));
                        if (angle < _CutAngle)
                            discard;
                        cutGlow = 1.0 - smoothstep(0.0, 0.32, (angle - _CutAngle) * r);
                    }
                }

                // door: a hole growing upward, deeper than 0.3 m inside the hull
                if ((flags & 2u) != 0u && _DoorOpen > 0.001)
                {
                    float a = atan2(input.local.x, input.local.z);
                    float da = abs(fmod(a - _DoorA.x + 3.14159265 + 6.2831853 * 4.0, 6.2831853) - 3.14159265);
                    if (input.local.y > _DoorY.x && input.local.y < lerp(_DoorY.x, _DoorY.y, _DoorOpen) && da < _DoorA.y && length(input.local.xz) > info2.z - 0.3)
                        discard;
                }

                // its own cutaway (version 3): a plane sweeping in, or a wedge opening about an axis
                if ((flags & 64u) != 0u && info2.w > 0.001)
                {
                    float4 c5 = OBJ(input.obj, 5u), c6 = OBJ(input.obj, 6u), c7 = OBJ(input.obj, 7u);
                    if (c5.x < 1.5)
                    {
                        float limit = lerp(c6.w, c7.x, info2.w), along = dot(input.local, c5.yzw);
                        if (along > limit)
                            discard;
                        cutGlow = max(cutGlow, 1.0 - smoothstep(0.0, c7.y, limit - along));
                    }
                    else
                    {
                        float3 w = cross(c5.yzw, c6.xyz);
                        float pu = dot(input.local, c6.xyz), pw = dot(input.local, w);
                        float az = abs(atan2(pw, pu)), open = c7.x * info2.w;
                        if (az < open)
                            discard;
                        cutGlow = max(cutGlow, 1.0 - smoothstep(0.0, c7.y, (az - open) * length(float2(pu, pw))));
                    }
                }

                // the set dissolving outward from the subject
                if ((flags & 4u) != 0u && _DissolveR > -50.0)
                {
                    if (_DissolveSphere > 0.5)
                    {
                        float d = length(input.scene - float3(_DissolveC.x, _DissolveNoise.w, _DissolveC.y));
                        float front = _DissolveR - (ValueNoise(input.scene * _DissolveNoise.x) - 0.5) * _DissolveNoise.y;
                        if (d < front)
                            discard;
                        dissolveGlow = 1.0 - smoothstep(0.0, _DissolveNoise.z, d - front);
                    }
                    else
                    {
                        float d = length(input.scene.xz - _DissolveC);
                        float front = _DissolveR + (DissolveNoise(input.scene) - 0.5) * 14.0;
                        if (d < front)
                            discard;
                        dissolveGlow = 1.0 - smoothstep(0.0, 2.5, d - front);
                    }
                }

                // while the stage follows its subject away, what's left behind dissolves at the edge of the room
                if ((flags & 16u) != 0u && _Focus.z > 0.0)
                {
                    float d = length(input.scene.xz - _Focus.xy) + (ValueNoise(input.scene * 0.7) - 0.5) * _Focus.w * 2.0;
                    if (d > _Focus.z)
                        discard;
                    dissolveGlow = max(dissolveGlow, 1.0 - smoothstep(0.0, _Focus.w, _Focus.z - d));
                }

                #endif
                bool front = IS_FRONT_VFACE(frontFace, true, false);
                if ((input.det < 0.0) != (_FrontSign < 0.0))
                    front = !front;
                #if defined(MESH4D_RULES)
                if (info.w < 0.5 && !front)
                    discard;
                if (info.w > 0.5 && info.w < 1.5 && front)
                    discard;
                #endif
                bool useFront = front != (info2.y > 0.5);

                #if defined(MESH4D_PER_VERTEX)
                float3 c = useFront ? input.colorFront : input.colorBack;
                #else
                float height = 2.0 * _AtlasHeight + 8.0;
                float2 uv = useFront ? float2(input.uv.x, input.uv.y * _AtlasHeight / height) : float2(input.uv.x, (_AtlasHeight + 8.0 + input.uv.y * _AtlasHeight) / height);
                float3 c = lerp(SAMPLE_TEXTURE2D(_AtlasA, sampler_AtlasA, uv).rgb, SAMPLE_TEXTURE2D(_AtlasB, sampler_AtlasA, uv).rgb, _KeyMix);
                #endif

                float h = info.y;
                if ((flags & 8u) != 0u)
                    c *= 1.0 + 0.55 * h; // liquids brighten
                else
                    c += float3(0.30, 0.21, 0.10) * h;
                c = lerp(c, _CutGlow.rgb, cutGlow * 0.85);
                c = lerp(c, _DissolveGlow.rgb, dissolveGlow * 0.9);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                c = SRGBToLinear(saturate(c)); // the baked colours are display colours
                #endif
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
