using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ImmersiveX.Media
{
    /// <summary>
    /// The GPU side of a 4D mesh: the atlas meshes and the repeated fine meshes as two ordinary MeshRenderers (so stereo,
    /// depth and the room's other content work as usual), posed by a buffer of object matrices updated every frame;
    /// two atlas textures painted from the colour keyframes around the current time; and the effect Gaussians through the
    /// splat renderer ImmersiveX already has. Everything lives under <see cref="Root"/>, whose transform takes the content's
    /// coordinates (three.js: right-handed, y up, metres) into the room.
    /// </summary>
    sealed class Mesh4DRenderer : IDisposable
    {
        static readonly int ObjectsId = Shader.PropertyToID("_Objects");
        static readonly int AtlasAId = Shader.PropertyToID("_AtlasA");
        static readonly int AtlasBId = Shader.PropertyToID("_AtlasB");
        static readonly int ProbesAId = Shader.PropertyToID("_ProbesA");
        static readonly int ProbesBId = Shader.PropertyToID("_ProbesB");
        static readonly int KeyMixId = Shader.PropertyToID("_KeyMix");
        static readonly int AtlasHeightId = Shader.PropertyToID("_AtlasHeight");
        static readonly int CutAngleId = Shader.PropertyToID("_CutAngle");
        static readonly int CutDirId = Shader.PropertyToID("_CutDir");
        static readonly int DissolveRId = Shader.PropertyToID("_DissolveR");
        static readonly int DissolveCId = Shader.PropertyToID("_DissolveC");
        static readonly int DoorOpenId = Shader.PropertyToID("_DoorOpen");
        static readonly int DoorYId = Shader.PropertyToID("_DoorY");
        static readonly int DoorAId = Shader.PropertyToID("_DoorA");
        static readonly int CutGlowId = Shader.PropertyToID("_CutGlow");
        static readonly int DissolveGlowId = Shader.PropertyToID("_DissolveGlow");
        static readonly int GroundId = Shader.PropertyToID("_Ground");
        static readonly int MapId = Shader.PropertyToID("_Map");
        static readonly int ProbesId = Shader.PropertyToID("_Probes");
        static readonly int WidthId = Shader.PropertyToID("_Width");
        static readonly int HeightId = Shader.PropertyToID("_Height");

        readonly Mesh4DContainer _c;
        readonly int _objects;
        readonly Vector4[] _table;
        readonly GraphicsBuffer _tableBuffer;
        readonly GraphicsBuffer _map;
        readonly Texture2D[] _probes = new Texture2D[2];
        readonly int[] _probeKey = { -1, -1 };
        readonly RenderTexture[] _atlas = new RenderTexture[2];
        readonly int[] _atlasKey = { -1, -1 };
        readonly byte[] _rgba;
        readonly Material _material; // atlas objects that take part in a rule (cutaway, door, dissolve): they discard
        readonly Material _plainMaterial; // every other atlas object: no discard, so early depth rejection works
        readonly Material _perVertexMaterial;
        readonly Material _paint;
        readonly Mesh _atlasMesh;
        readonly Mesh _plainMesh;
        readonly Mesh _vertexMesh;
        readonly GameObject[] _renderers;
        HologramRenderer _effects;
        readonly uint[] _effectData;
        readonly float[] _effectPositions;
        readonly int[] _keys;
        readonly uint[] _order;
        readonly int[] _buckets = new int[65536];
        int _effectFrame = -1;
        int _effectCount;
        HologramFrameHeader _effectHeader;
        Vector3 _sortedFrom = new Vector3(float.NaN, 0f, 0f);

        /// <summary>The pose of every object at the current time (12 floats each: columns 0–2, then the translation).</summary>
        public readonly float[] Matrices;

        /// <summary>Whether each object is visible at the current time.</summary>
        public readonly bool[] Visible;

        /// <summary>The 44 per-frame floats at the current time (globals, camera, effect parameters, subject).</summary>
        public readonly float[] Floats = new float[Mesh4DContainer.FloatsPerFrame];

        public Transform Root { get; }

        public int VisibleTriangles { get; private set; }
        public int EffectCount => _effectCount;

        public Mesh4DRenderer(Mesh4DContainer container, Transform parent, Material material, Material paint, Material splats)
        {
            _c = container;
            _objects = container.ObjectCount;
            Matrices = new float[_objects * 12];
            Visible = new bool[_objects];
            _table = new Vector4[_objects * 5];
            _tableBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _objects * 5, 16);

            Root = new GameObject("4D Scene").transform;
            Root.SetParent(parent, false);
            Root.gameObject.layer = parent.gameObject.layer;

            _material = new Material(material) { name = "Mesh 4D (atlas, rules)" };
            _material.EnableKeyword("MESH4D_RULES");
            _plainMaterial = new Material(material) { name = "Mesh 4D (atlas)" };
            _perVertexMaterial = new Material(material) { name = "Mesh 4D (per vertex)" };
            _perVertexMaterial.EnableKeyword("MESH4D_PER_VERTEX");
            _paint = new Material(paint) { name = "Mesh 4D Paint" };

            // texel → probe map
            var texels = container.AtlasWidth * container.AtlasHeight;
            _map = new GraphicsBuffer(GraphicsBuffer.Target.Structured, texels, 8);
            var pairs = new uint[texels * 2];
            for (var i = 0; i < texels; i++)
            {
                pairs[i * 2] = container.TexelFront[i];
                pairs[i * 2 + 1] = container.TexelBack[i];
            }

            _map.SetData(pairs);
            container.TexelFront = container.TexelBack = null; // on the GPU now

            var probeRows = (container.Probes + Mesh4DContainer.ProbeTextureWidth - 1) / Mesh4DContainer.ProbeTextureWidth;
            _rgba = new byte[Mesh4DContainer.ProbeTextureWidth * probeRows * 4];
            for (var i = 0; i < 2; i++)
            {
                _probes[i] = new Texture2D(Mesh4DContainer.ProbeTextureWidth, probeRows, TextureFormat.RGBA32, false, true)
                {
                    name = $"Mesh 4D probes {i}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                };
                _atlas[i] = new RenderTexture(container.AtlasWidth, container.AtlasHeight * 2 + 8, 0, GraphicsFormat.R8G8B8A8_UNorm)
                {
                    name = $"Mesh 4D atlas {i}", useMipMap = true, autoGenerateMips = false, anisoLevel = 4,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
                };
                _atlas[i].Create();
            }

            foreach (var m in Materials)
            {
                m.SetBuffer(ObjectsId, _tableBuffer);
                m.SetFloat(AtlasHeightId, container.AtlasHeight);
                m.SetColor(CutGlowId, container.CutGlow);
                m.SetColor(DissolveGlowId, container.DissolveGlow);
                m.SetFloat(GroundId, container.Ground); // like every standing media: nothing shows below the floor
            }

            _paint.SetBuffer(MapId, _map);
            _paint.SetInt(WidthId, container.AtlasWidth);
            _paint.SetInt(HeightId, container.AtlasHeight);

            _atlasMesh = BuildAtlasMesh(container, rules: true);
            _plainMesh = BuildAtlasMesh(container, rules: false);
            ReleaseAtlasArrays(container);
            _vertexMesh = BuildPerVertexMesh(container);
            _renderers = new[]
            {
                _atlasMesh != null ? Renderer("Atlas meshes (cut, door, dissolve)", _atlasMesh, _material) : null,
                _plainMesh != null ? Renderer("Atlas meshes", _plainMesh, _plainMaterial) : null,
                _vertexMesh != null ? Renderer("Repeated meshes", _vertexMesh, _perVertexMaterial) : null,
            };

            if (container.HasEffects)
            {
                var most = Mathf.Max(1, container.MaxEffectGaussians());
                var rows = (most + SplatPacker.TextureWidth - 1) / SplatPacker.TextureWidth;
                _effectData = new uint[HologramFrame.ExpectedBytes(rows, SplatPacker.TextureWidth) / 4];
                _effects = new HologramRenderer(Root, splats, most, _effectData.Length, container.SigmaLogRange);
                _effects.Transform.gameObject.layer = Root.gameObject.layer;
                _effectPositions = new float[most * 3];
                _keys = new int[most];
                _order = new uint[most];
            }
        }

        GameObject Renderer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.layer = Root.gameObject.layer;
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>Objects with a per-pixel rule (cutaway 1, door 2, dissolve 4) need the discarding variant.</summary>
        static bool HasRule(Mesh4DContainer.ObjectInfo o) => (o.Flags & 7) != 0;

        /// <summary>The atlas objects with rules (<paramref name="rules"/>) or without, as one mesh (or null when there are none).</summary>
        static Mesh BuildAtlasMesh(Mesh4DContainer c, bool rules)
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var objects = new System.Collections.Generic.List<Vector2>();
            var indices = new System.Collections.Generic.List<int>();
            for (var o = 0; o < c.ObjectCount; o++)
            {
                var info = c.Objects[o];
                if (info.AtlasRange == null || HasRule(info) != rules)
                    continue;
                int first = info.AtlasRange[0], count = info.AtlasRange[1], start = info.AtlasRange[2], length = info.AtlasRange[3];
                var offset = positions.Count - first;
                for (var v = first; v < first + count; v++)
                {
                    positions.Add(new Vector3(c.AtlasPositions[v * 3], c.AtlasPositions[v * 3 + 1], c.AtlasPositions[v * 3 + 2]));
                    uvs.Add(c.AtlasUVs[v]);
                    objects.Add(new Vector2(o, 0f));
                }

                for (var i = start; i < start + length; i++)
                    indices.Add((int)c.AtlasIndices[i] + offset);
            }

            if (indices.Count == 0)
                return null;
            var mesh = new Mesh { name = rules ? "Mesh 4D atlas (rules)" : "Mesh 4D atlas", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, objects);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400000f); // posed in the shader
            return mesh;
        }

        static void ReleaseAtlasArrays(Mesh4DContainer c)
        {
            c.AtlasPositions = null;
            c.AtlasUVs = null;
            c.AtlasIndices = null;
        }

        Material[] Materials => new[] { _material, _plainMaterial, _perVertexMaterial };

        /// <summary>Every member of every family (engines), merged into one mesh with its object index and probe weights.</summary>
        static Mesh BuildPerVertexMesh(Mesh4DContainer c)
        {
            var vertices = 0;
            var indices = 0;
            foreach (var f in c.Families)
            {
                vertices += f.Vertices * f.Members.Length;
                indices += f.Indices.Length * f.Members.Length;
            }

            if (vertices == 0)
                return null;
            var positions = new Vector3[vertices];
            var objects = new Vector2[vertices];
            var pf = new Vector3[vertices];
            var pb = new Vector3[vertices];
            var wf = new Vector3[vertices];
            var wb = new Vector3[vertices];
            var tris = new int[indices];
            int v = 0, t = 0;
            foreach (var f in c.Families)
            {
                foreach (var member in f.Members)
                {
                    var start = v;
                    for (var i = 0; i < f.Vertices; i++, v++)
                    {
                        positions[v] = new Vector3(f.Positions[i * 3], f.Positions[i * 3 + 1], f.Positions[i * 3 + 2]);
                        objects[v] = new Vector2(member, 0f);
                        pf[v] = new Vector3(f.ProbesFront[i * 3], f.ProbesFront[i * 3 + 1], f.ProbesFront[i * 3 + 2]);
                        pb[v] = new Vector3(f.ProbesBack[i * 3], f.ProbesBack[i * 3 + 1], f.ProbesBack[i * 3 + 2]);
                        wf[v] = new Vector3(f.WeightsFront[i * 3], f.WeightsFront[i * 3 + 1], f.WeightsFront[i * 3 + 2]) / 255f;
                        wb[v] = new Vector3(f.WeightsBack[i * 3], f.WeightsBack[i * 3 + 1], f.WeightsBack[i * 3 + 2]) / 255f;
                    }

                    for (var i = 0; i < f.Indices.Length; i++)
                        tris[t++] = start + (int)f.Indices[i];
                }
            }

            var mesh = new Mesh { name = "Mesh 4D repeated meshes", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetUVs(1, objects);
            mesh.SetUVs(2, pf);
            mesh.SetUVs(3, pb);
            mesh.SetUVs(4, wf);
            mesh.SetUVs(5, wb);
            mesh.SetIndices(tris, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400000f);
            return mesh;
        }

        // ---------------------------------------------------------------- posing

        /// <summary>
        /// Pose everything for time <paramref name="seconds"/>: interpolate the 30 fps poses into <see cref="Matrices"/>,
        /// set the rules' uniforms, and make sure the two colour keyframes around the time are painted.
        /// </summary>
        public void Pose(double seconds)
        {
            var c = _c;
            var x = Mathf.Clamp((float)(seconds * c.Fps), 0f, c.Frames - 1);
            var f0 = (int)x;
            var f1 = Mathf.Min(c.Frames - 1, f0 + 1);
            var a = x - f0;
            var b0 = f0 * Mesh4DContainer.FloatsPerFrame;
            var b1 = f1 * Mesh4DContainer.FloatsPerFrame;
            for (var k = 0; k < Floats.Length; k++)
                Floats[k] = Mathf.Lerp(c.FrameFloats[b0 + k], c.FrameFloats[b1 + k], a);
            if (float.IsNaN(c.FrameFloats[b0 + 16]) || float.IsNaN(c.FrameFloats[b1 + 16]))
                Floats[16] = c.FrameFloats[(a < 0.5f ? b0 : b1) + 16];

            var m = c.Matrices;
            for (var o = 0; o < _objects; o++)
            {
                var vis0 = c.Visible[f0 * _objects + o] != 0;
                var vis1 = c.Visible[f1 * _objects + o] != 0;
                Visible[o] = a < 0.5f ? vis0 : vis1;
                var m0 = ((long)f0 * _objects + o) * 12;
                var m1 = ((long)f1 * _objects + o) * 12;
                var jump = Mathf.Abs(m[m1 + 9] - m[m0 + 9]) + Mathf.Abs(m[m1 + 10] - m[m0 + 10]) + Mathf.Abs(m[m1 + 11] - m[m0 + 11]) > 500f;
                if (jump || !vis0 || !vis1)
                    Array.Copy(m, a < 0.5f ? m0 : m1, Matrices, o * 12, 12);
                else
                    for (var k = 0; k < 12; k++)
                        Matrices[o * 12 + k] = m[m0 + k] + (m[m1 + k] - m[m0 + k]) * a;
                var h = Mathf.Lerp(c.Highlights[f0 * _objects + o], c.Highlights[f1 * _objects + o], a) / 255f;
                _table[o * 5 + 3] = new Vector4(Visible[o] ? 1f : 0f, h, c.Objects[o].Flags, c.Objects[o].Side);
                _table[o * 5 + 4] = new Vector4(c.Objects[o].ProbeStart, c.Objects[o].Flip ? 1f : 0f, c.Objects[o].HullRadius, 0f);
            }

            foreach (var material in Materials)
            {
                material.SetFloat(CutAngleId, Floats[0]);
                material.SetVector(CutDirId, new Vector4(Floats[1], Floats[2]));
                material.SetFloat(DissolveRId, Floats[3]);
                material.SetVector(DissolveCId, new Vector4(Floats[4], Floats[5]));
                material.SetFloat(DoorOpenId, Floats[6]);
            }

            PaintKeys(seconds);
        }

        /// <summary>
        /// Write the object table (after anything that moves objects on top of the recording, such as the user moving a
        /// part) and upload it. <paramref name="offset"/> gives each object's extra transform in content space, or null.
        /// </summary>
        public void Commit(Func<int, Matrix4x4?> offset = null)
        {
            var tris = 0;
            for (var o = 0; o < _objects; o++)
            {
                var i = o * 12;
                var m = new Matrix4x4(
                    new Vector4(Matrices[i], Matrices[i + 1], Matrices[i + 2], 0f),
                    new Vector4(Matrices[i + 3], Matrices[i + 4], Matrices[i + 5], 0f),
                    new Vector4(Matrices[i + 6], Matrices[i + 7], Matrices[i + 8], 0f),
                    new Vector4(Matrices[i + 9], Matrices[i + 10], Matrices[i + 11], 1f));
                var extra = offset?.Invoke(o);
                if (extra.HasValue)
                    m = extra.Value * m;
                _table[o * 5] = new Vector4(m.m00, m.m10, m.m20, m.m03);
                _table[o * 5 + 1] = new Vector4(m.m01, m.m11, m.m21, m.m13);
                _table[o * 5 + 2] = new Vector4(m.m02, m.m12, m.m22, m.m23);
                if (Visible[o])
                    tris += TrianglesOf(o);
            }

            VisibleTriangles = tris;
            _tableBuffer.SetData(_table);
        }

        int TrianglesOf(int o)
        {
            var info = _c.Objects[o];
            if (info.AtlasRange != null)
                return info.AtlasRange[3] / 3;
            return info.Family >= 0 ? _c.Families[info.Family].Triangles : 0;
        }

        /// <summary>The object's current pose in content space.</summary>
        public Matrix4x4 ObjectMatrix(int o)
        {
            var i = o * 12;
            return new Matrix4x4(
                new Vector4(Matrices[i], Matrices[i + 1], Matrices[i + 2], 0f),
                new Vector4(Matrices[i + 3], Matrices[i + 4], Matrices[i + 5], 0f),
                new Vector4(Matrices[i + 6], Matrices[i + 7], Matrices[i + 8], 0f),
                new Vector4(Matrices[i + 9], Matrices[i + 10], Matrices[i + 11], 1f));
        }

        void PaintKeys(double seconds)
        {
            var c = _c;
            var kx = Mathf.Clamp((float)(seconds * c.ColorRate), 0f, c.ColorKeys - 1);
            var k0 = (int)kx;
            var k1 = Mathf.Min(c.ColorKeys - 1, k0 + 1);
            // keep whichever slot already holds a keyframe we need
            int a = _atlasKey[0] == k0 ? 0 : _atlasKey[1] == k0 ? 1 : (_atlasKey[0] == k1 ? 1 : 0);
            var b = 1 - a;
            if (_atlasKey[a] != k0)
                Paint(a, k0);
            if (_atlasKey[b] != k1 && k1 != k0)
                Paint(b, k1);
            var bSlot = k1 == k0 ? a : b;
            foreach (var material in Materials)
            {
                material.SetTexture(AtlasAId, _atlas[a]);
                material.SetTexture(AtlasBId, _atlas[bSlot]);
                material.SetTexture(ProbesAId, _probes[a]);
                material.SetTexture(ProbesBId, _probes[bSlot]);
                material.SetFloat(KeyMixId, kx - k0);
            }
        }

        void Paint(int slot, int key)
        {
            var c = _c;
            var stride = c.Probes * 3;
            var source = key * stride;
            for (int i = 0, o = 0; i < c.Probes; i++, o += 4)
            {
                _rgba[o] = c.Colors[source + i * 3];
                _rgba[o + 1] = c.Colors[source + i * 3 + 1];
                _rgba[o + 2] = c.Colors[source + i * 3 + 2];
                _rgba[o + 3] = 255;
            }

            _probes[slot].SetPixelData(_rgba, 0);
            _probes[slot].Apply(false, false);
            _probeKey[slot] = key;

            _paint.SetTexture(ProbesId, _probes[slot]);
            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(_atlas[slot]);
            _paint.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            RenderTexture.active = previous;
            _atlas[slot].GenerateMips();
            _atlasKey[slot] = key;
        }

        // ---------------------------------------------------------------- effects

        /// <summary>Show the effect Gaussians recorded for time <paramref name="seconds"/>, sorted for <paramref name="viewer"/>.</summary>
        public void PoseEffects(double seconds, Transform viewer)
        {
            if (_effects == null)
                return;
            var frame = Mathf.Clamp(Mathf.RoundToInt((float)(seconds * _c.Fps)), 0, _c.Frames - 1);
            var newFrame = frame != _effectFrame;
            if (newFrame)
            {
                _effectFrame = frame;
                LoadEffects(frame);
            }

            if (_effectCount == 0 || viewer == null)
                return;
            var from = _effects.Transform.InverseTransformPoint(viewer.position);
            if (newFrame || (from - _sortedFrom).sqrMagnitude > 4f)
            {
                HologramFrame.SortBackToFront(_effectPositions, _effectCount, from, _keys, _buckets, _order);
                _effects.UploadOrder(_order, _effectCount);
                _sortedFrom = from;
            }
        }

        void LoadEffects(int frame)
        {
            var count = _c.EffectFrame(frame, out var min, out var max, out var at, out var data);
            count = Mathf.Min(count, _keys.Length);
            _effectCount = count;
            if (count == 0)
            {
                _effects.Visible = false;
                return;
            }

            var width = SplatPacker.TextureWidth;
            var rows = (count + width - 1) / width;
            var alphaStart = HologramFrame.AlphaStart(new HologramFrameHeader { Rows = rows }, width);
            var d = _effectData;
            d[0] = (uint)count;
            d[1] = Bits(min.x); d[2] = Bits(min.y); d[3] = Bits(min.z);
            d[4] = Bits(max.x); d[5] = Bits(max.y); d[6] = Bits(max.z);
            d[7] = (uint)rows;
            Array.Clear(d, alphaStart, d.Length - alphaStart);
            var step = (max - min) / 65535f;
            for (var i = 0; i < count; i++, at += 11)
            {
                uint x = (uint)(data[at] | data[at + 1] << 8), y = (uint)(data[at + 2] | data[at + 3] << 8), z = (uint)(data[at + 4] | data[at + 5] << 8);
                uint s = data[at + 6], r = data[at + 7], g = data[at + 8], bl = data[at + 9], alpha = data[at + 10];
                var t = HologramFrame.HeaderUints + i * 4;
                d[t] = x | (y << 16);
                d[t + 1] = z | (s << 16) | (s << 24);
                d[t + 2] = s | (255u << 8) | (128u << 16) | (128u << 24); // isotropic: identity rotation (w, x, y, z)
                d[t + 3] = 128u | (r << 8) | (g << 16) | (bl << 24);
                d[alphaStart + (i >> 2)] |= alpha << ((i & 3) * 8);
                _effectPositions[i * 3] = min.x + x * step.x;
                _effectPositions[i * 3 + 1] = min.y + y * step.y;
                _effectPositions[i * 3 + 2] = min.z + z * step.z;
            }

            _effectHeader = new HologramFrameHeader { Count = count, Min = min, Max = max, Rows = rows };
            _effects.UploadFrame(d, alphaStart + (count + 3) / 4, _effectHeader, alphaStart);
        }

        static uint Bits(float v) => (uint)BitConverter.SingleToInt32Bits(v);

        public void Dispose()
        {
            foreach (var go in _renderers)
                if (go != null)
                {
                    go.GetComponent<MeshRenderer>().enabled = false;
                    UnityEngine.Object.Destroy(go);
                }

            _effects?.Dispose();
            _tableBuffer?.Release();
            _map?.Release();
            foreach (var t in _probes)
                if (t != null)
                    UnityEngine.Object.Destroy(t);
            foreach (var rt in _atlas)
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }

            if (_atlasMesh != null)
                UnityEngine.Object.Destroy(_atlasMesh);
            if (_plainMesh != null)
                UnityEngine.Object.Destroy(_plainMesh);
            if (_vertexMesh != null)
                UnityEngine.Object.Destroy(_vertexMesh);
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_plainMaterial);
            UnityEngine.Object.Destroy(_perVertexMaterial);
            UnityEngine.Object.Destroy(_paint);
            if (Root != null)
                UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
