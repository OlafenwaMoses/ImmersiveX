using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImmersiveX.Media
{
    /// <summary>
    /// The GPU side of a hologram: the current frame and draw order in two buffers, and a mesh of one quad per
    /// Gaussian drawn by a normal <see cref="MeshRenderer"/>, so stereo (multiview or single-pass instanced) and
    /// depth testing against other content work like any other object.
    /// </summary>
    sealed class HologramRenderer : IDisposable
    {
        static readonly int FrameId = Shader.PropertyToID("_Frame");
        static readonly int OrderId = Shader.PropertyToID("_Order");
        static readonly int BoundsMinId = Shader.PropertyToID("_BoundsMin");
        static readonly int BoundsMaxId = Shader.PropertyToID("_BoundsMax");
        static readonly int ScaleRangeId = Shader.PropertyToID("_ScaleRange");
        static readonly int AlphaStartId = Shader.PropertyToID("_AlphaStart");

        readonly GraphicsBuffer _frame;
        readonly GraphicsBuffer _order;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MeshRenderer _renderer;
        readonly int _capacity;

        public HologramRenderer(Transform parent, Material template, int capacity, int frameUints, Vector2 scaleRange)
        {
            _capacity = Mathf.Max(1, capacity);
            _frame = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, frameUints), sizeof(uint));
            _order = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _capacity, sizeof(uint));
            _mesh = CreateQuads(_capacity);

            Transform = new GameObject("Splats").transform;
            Transform.SetParent(parent, false);
            Transform.gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = Transform.gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.enabled = false; // until the first frame is uploaded

            _material = new Material(template) { name = "Hologram Splats (instance)" };
            _material.SetBuffer(FrameId, _frame);
            _material.SetBuffer(OrderId, _order);
            _material.SetVector(ScaleRangeId, scaleRange);
            _renderer.sharedMaterial = _material;
        }

        /// <summary>The splats' transform: position, flip and scale that fit the performer to the hologram.</summary>
        public Transform Transform { get; }

        public int Count { get; private set; }

        /// <summary>Upload a frame. Returns false (and draws nothing new) if it has more Gaussians than the mesh holds.</summary>
        public bool UploadFrame(uint[] data, int uints, HologramFrameHeader header, int alphaStart)
        {
            if (header.Count > _capacity || uints > _frame.count)
                return false;

            _frame.SetData(data, 0, 0, uints);
            _material.SetVector(BoundsMinId, header.Min);
            _material.SetVector(BoundsMaxId, header.Max);
            _material.SetInteger(AlphaStartId, alphaStart);
            Count = header.Count;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, Count * 6), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.bounds = new Bounds((header.Min + header.Max) * 0.5f, header.Max - header.Min);
            return true;
        }

        /// <summary>The log-scale range the frame's scales were quantised into (per file for splat files).</summary>
        public void SetScaleRange(Vector2 scaleRange) => _material.SetVector(ScaleRangeId, scaleRange);

        public void UploadOrder(uint[] order, int count)
        {
            _order.SetData(order, 0, 0, Mathf.Min(count, _capacity));
            _renderer.enabled = count > 0;
        }

        public bool Visible
        {
            get => _renderer.enabled;
            set => _renderer.enabled = value && Count > 0;
        }

        static Mesh CreateQuads(int quads)
        {
            var mesh = new Mesh { name = "Hologram Quads", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = new Vector3[quads * 4]; // positions come from the frame buffer, by vertex ID
            var indices = new int[quads * 6];
            for (int q = 0, v = 0, i = 0; q < quads; q++, v += 4, i += 6)
            {
                indices[i] = v;
                indices[i + 1] = v + 2;
                indices[i + 2] = v + 1;
                indices[i + 3] = v + 1;
                indices[i + 4] = v + 2;
                indices[i + 5] = v + 3;
            }

            mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: false);
            mesh.MarkDynamic();
            return mesh;
        }

        public void Dispose()
        {
            _frame.Release();
            _order.Release();
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_material);
            if (Transform != null)
                UnityEngine.Object.Destroy(Transform.gameObject);
        }
    }
}
