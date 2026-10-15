// Paints one colour keyframe of a 4D mesh into its atlas (see Mesh4DPlayable): every texel takes its colour probe's
// colour (front layer in the bottom half, back layer above an 8-texel gap), smoothed over its 3×3 neighbours with
// weights 1-2-1, skipping empty texels. The texels next to each chart are copies of its edge, so charts never mix.
Shader "Hidden/ImmersiveX/Mesh 4D Paint"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            StructuredBuffer<uint2> _Map; // per atlas texel: front probe, back probe (0xffffffff: empty)
            Texture2D<float4> _Probes;
            int _Width;
            int _Height;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                int2 p = int2(floor(input.uv * float2(_Width, 2 * _Height + 8)));
                int layer, ty;
                if (p.y < _Height) { layer = 0; ty = p.y; }
                else if (p.y >= _Height + 8) { layer = 1; ty = p.y - _Height - 8; }
                else return float4(0.0, 0.0, 0.0, 1.0);
                float3 sum = 0.0;
                float weights = 0.0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 q = int2(p.x + dx, ty + dy);
                        if (q.x < 0 || q.y < 0 || q.x >= _Width || q.y >= _Height)
                            continue;
                        uint2 m = _Map[q.y * _Width + q.x];
                        uint id = layer == 0 ? m.x : m.y;
                        if (id == 0xffffffffu)
                            continue;
                        float w = (dx == 0 ? 2.0 : 1.0) * (dy == 0 ? 2.0 : 1.0);
                        sum += w * _Probes.Load(int3(id % 2048u, id / 2048u, 0)).rgb;
                        weights += w;
                    }
                }

                return float4(weights > 0.0 ? sum / weights : 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}
