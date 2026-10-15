using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Rebuilds a web page's three.js scene in Unity from the bridge's messages (see WebBridge.js) and keeps it in step:
    /// <list type="bullet">
    /// <item>Every three.js object becomes a GameObject with the same hierarchy; local matrices, visibility and lights follow
    /// the page frame by frame.</item>
    /// <item>Meshes, skinned meshes (with their bones), instanced meshes (drawn with GPU instancing) and lines are drawn; points
    /// and sprites aren't yet.</item>
    /// <item>Materials map to the glTF materials: standard and physical to PBR, basic to unlit; custom shaders to their
    /// likeliest colour. Shadow catchers aren't drawn.</item>
    /// <item>three.js is right-handed, Unity left-handed: every position, normal, matrix and light flips its z, and triangles
    /// their winding.</item>
    /// <item>The page's camera (where it is and looks) comes along, so ImmersiveX knows what the page shows.</item>
    /// <item>What sinks under the room's floor is hidden (<see cref="HideBelow"/>).</item>
    /// </list>
    /// </summary>
    sealed class WebScene : IDisposable
    {
        sealed class Node
        {
            public int Id;
            public string Kind;
            public GameObject Object;
            public int[] Materials;
            public int Geometry;
            public Renderer Renderer;
            public MeshFilter Filter;
            public int[] Bones;
            public Matrix4x4[] BindPoses;
            public bool BonesBound;
            public Matrix4x4[] Instances;
            public bool Ground;
        }

        sealed class Geometry
        {
            public Mesh Mesh;
            public int Groups;
            public int[] GroupMaterials;
            public Vector3 Size;
        }

        readonly Transform _root;
        readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();
        readonly Dictionary<int, Geometry> _geometries = new Dictionary<int, Geometry>();
        readonly Dictionary<int, Material> _materials = new Dictionary<int, Material>();
        readonly Dictionary<int, Dictionary<string, object>> _materialData = new Dictionary<int, Dictionary<string, object>>();
        readonly Dictionary<int, Texture2D> _textures = new Dictionary<int, Texture2D>();
        readonly List<Node> _instanced = new List<Node>();
        readonly List<Node> _waitingForBones = new List<Node>();
        readonly HashSet<Renderer> _ground = new HashSet<Renderer>(); // hidden for good, whatever the page does to their materials
        readonly HashSet<Renderer> _belowFloor = new HashSet<Renderer>(); // hidden while they're under the room's floor
        float _floor = float.NegativeInfinity; // the room's floor (world height): instances under it aren't drawn
        readonly Material[] _templates = new Material[6];
        int _logged;

        public WebScene(Transform parent)
        {
            _root = new GameObject("Web Scene").transform;
            _root.SetParent(parent, false);
        }

        /// <summary>The scene's root: three.js's world, z already flipped.</summary>
        public Transform Root => _root;

        public int NodeCount => _nodes.Count;
        public int MeshCount { get; private set; }
        public int VertexCount { get; private set; }

        /// <summary>The page said it has finished loading (it set <c>window.__ready</c>, as GenXR pages do).</summary>
        public bool PageReady { get; private set; }

        /// <summary>The page's load event has fired.</summary>
        public bool PageLoaded { get; private set; }

        /// <summary>The page is playing sound or video (its media elements).</summary>
        public bool MediaPlaying { get; private set; }

        /// <summary>The meshes hidden as the ground (see <see cref="HideGround"/>).</summary>
        public IEnumerable<Renderer> Ground => _ground;

        public bool IsGround(Renderer renderer) => _ground.Contains(renderer);

        /// <summary>When the scene last changed (objects added, removed, moved, shown or hidden), in unscaled time.</summary>
        public float LastChange { get; private set; }

        /// <summary>The page's title (from the bridge's hello).</summary>
        public string Title { get; private set; }

        /// <summary>Whether the page's camera is known yet (<see cref="CameraPosition"/>, <see cref="CameraForward"/>).</summary>
        public bool HasCamera { get; private set; }

        /// <summary>Where the page's camera is, in the scene's space (<see cref="Root"/>'s local space: page units, z flipped).</summary>
        public Vector3 CameraPosition { get; private set; }

        /// <summary>The way the page's camera looks, in the scene's space.</summary>
        public Vector3 CameraForward { get; private set; } = Vector3.forward;

        /// <summary>The page's selectable things (actors): id, root node, title, info. Updated when the page changes them.</summary>
        public List<(string id, int root, string title, string info)> Selectables { get; } = new List<(string, int, string, string)>();

        /// <summary>Raised when <see cref="Selectables"/> changes.</summary>
        public event Action SelectablesChanged;

        /// <summary>The GameObject of a three.js object, or null.</summary>
        public GameObject ObjectOf(int id) => _nodes.TryGetValue(id, out var node) ? node.Object : null;

        // ---------------------------------------------------------------- messages

        /// <summary>Apply one message from the bridge.</summary>
        public void Apply(string text)
        {
            Dictionary<string, object> message;
            try
            {
                message = Json.Parse(text) as Dictionary<string, object>;
            }
            catch (FormatException exception)
            {
                Warn("A message from the page couldn't be read: " + exception.Message);
                return;
            }

            if (message == null || !(message.TryGetValue("t", out var type) && type is string kind))
                return;
            switch (kind)
            {
                case "hello":
                    Title = Str(message, "title");
                    break;
                case "ready":
                    PageReady = true;
                    break;
                case "pageload":
                    PageLoaded = true;
                    break;
                case "media":
                    MediaPlaying = message.TryGetValue("playing", out var playing) && playing is bool isPlaying && isPlaying;
                    break;
                case "geo":
                    ReadGeometry(message);
                    break;
                case "tex":
                    ReadTexture(message);
                    break;
                case "mat":
                    ReadMaterial(message);
                    break;
                case "frame":
                    ReadFrame(message);
                    break;
                case "log":
                    if (Str(message, "level") == "info")
                        ImmersiveXLog.Info("Web page: " + Str(message, "text")); // answers to diagnostics: always shown
                    else
                        Warn("Web page: " + Str(message, "text"));
                    break;
            }
        }

        void ReadFrame(Dictionary<string, object> frame)
        {
            foreach (var key in ChangeKeys)
                if (frame.TryGetValue(key, out var value) && value is List<object> changes && changes.Count > 0)
                {
                    LastChange = Time.unscaledTime; // the page moved something (its camera and lights don't count)
                    break;
                }

            if (frame.TryGetValue("add", out var added) && added is List<object> nodes)
                foreach (var item in nodes)
                    if (item is Dictionary<string, object> node)
                        AddNode(node);

            if (frame.TryGetValue("del", out var deleted) && deleted is List<object> removed)
                foreach (var id in removed)
                    RemoveNode(Int(id));

            if (frame.TryGetValue("m", out var matrices) && matrices is List<object> values)
            {
                for (var i = 0; i + 16 < values.Count; i += 17)
                {
                    if (!_nodes.TryGetValue(Int(values[i]), out var node))
                        continue;
                    var m = new Matrix4x4();
                    for (var e = 0; e < 16; e++)
                        m[e % 4, e / 4] = Float(values[i + 1 + e]); // three.js elements are column-major
                    SetLocal(node.Object.transform, Flip(m));
                }
            }

            if (frame.TryGetValue("vis", out var visibility) && visibility is List<object> flags)
                for (var i = 0; i + 1 < flags.Count; i += 2)
                    if (_nodes.TryGetValue(Int(flags[i]), out var node))
                        node.Object.SetActive(Int(flags[i + 1]) != 0);

            if (frame.TryGetValue("inst", out var instances) && instances is List<object> sets)
                foreach (var item in sets)
                    if (item is Dictionary<string, object> set && _nodes.TryGetValue(Int(set["id"]), out var node))
                        ReadInstances(node, Int(set["count"]), Str(set, "m"));

            if (frame.TryGetValue("lights", out var lights) && lights is List<object> directions)
            {
                for (var i = 0; i + 4 < directions.Count; i += 5)
                {
                    if (!_nodes.TryGetValue(Int(directions[i]), out var node) || !node.Object.TryGetComponent(out Light light))
                        continue;
                    var direction = _root.TransformDirection(new Vector3(Float(directions[i + 1]), Float(directions[i + 2]), -Float(directions[i + 3])));
                    if (direction.sqrMagnitude > 1e-8f)
                        light.transform.rotation = Quaternion.LookRotation(direction);
                    light.intensity = LightIntensity(light.type, Float(directions[i + 4]));
                }
            }

            if (frame.TryGetValue("cam", out var cam) && cam is List<object> camera && camera.Count >= 6)
            {
                CameraPosition = new Vector3(Float(camera[0]), Float(camera[1]), -Float(camera[2]));
                var forward = new Vector3(Float(camera[3]), Float(camera[4]), -Float(camera[5]));
                if (forward.sqrMagnitude > 1e-8f)
                    CameraForward = forward.normalized;
                HasCamera = true;
            }

            if (frame.TryGetValue("sel", out var selectables) && selectables is List<object> list)
                ReadSelectables(list);

            BindWaitingBones();
        }

        static readonly string[] ChangeKeys = { "add", "del", "m", "vis", "inst" };

        void AddNode(Dictionary<string, object> data)
        {
            var id = Int(data["id"]);
            var parentId = Int(data["parent"]);
            if (_nodes.TryGetValue(id, out var existing))
            {
                // Described again: it moved to another parent.
                existing.Object.transform.SetParent(parentId != 0 && _nodes.TryGetValue(parentId, out var newParent) ? newParent.Object.transform : _root, false);
                return;
            }

            var kind = Str(data, "kind");
            var name = Str(data, "name");
            var gameObject = new GameObject(string.IsNullOrEmpty(name) ? $"{kind} {id}" : name);
            gameObject.transform.SetParent(parentId != 0 && _nodes.TryGetValue(parentId, out var parent) ? parent.Object.transform : _root, false);
            var node = new Node { Id = id, Kind = kind, Object = gameObject };
            _nodes[id] = node;

            node.Materials = data.TryGetValue("mats", out var mats) && mats is List<object> ids ? ids.ConvertAll(Int).ToArray() : Array.Empty<int>();
            node.Geometry = data.TryGetValue("geo", out var geo) ? Int(geo) : 0;
            switch (kind)
            {
                case "mesh":
                    AddMesh(node, skinned: false);
                    break;
                case "skinned":
                    node.Bones = data.TryGetValue("bones", out var bones) && bones is List<object> boneIds ? boneIds.ConvertAll(Int).ToArray() : null;
                    node.BindPoses = BindPoses(Str(data, "boneInverses"), data.TryGetValue("bindMatrix", out var bind) ? bind as List<object> : null, node.Bones?.Length ?? 0);
                    AddMesh(node, skinned: true);
                    break;
                case "instanced":
                    _instanced.Add(node);
                    break;
                case "line":
                case "segments":
                    AddLines(node, strip: kind == "line");
                    break;
                case "light":
                    if (data.TryGetValue("light", out var light) && light is Dictionary<string, object> settings)
                        AddLight(node, settings);
                    break;
            }
        }

        void RemoveNode(int id)
        {
            if (!_nodes.TryGetValue(id, out var node))
                return;
            _nodes.Remove(id);
            _instanced.Remove(node);
            _waitingForBones.Remove(node);
            DiscardOwnMesh(node);
            if (node.Object != null)
                Discard(node.Object);
        }

        // ---------------------------------------------------------------- meshes

        void AddMesh(Node node, bool skinned)
        {
            if (!_geometries.TryGetValue(node.Geometry, out var geometry))
                return; // geometry that was too big to send
            if (skinned && node.Bones != null && node.Bones.Length > 0)
            {
                var renderer = node.Object.AddComponent<SkinnedMeshRenderer>();
                renderer.updateWhenOffscreen = true;
                node.Renderer = renderer;
                _waitingForBones.Add(node); // bones may arrive later in the same frame
            }
            else
            {
                node.Filter = node.Object.AddComponent<MeshFilter>();
                node.Filter.sharedMesh = geometry.Mesh;
                node.Renderer = node.Object.AddComponent<MeshRenderer>();
            }

            node.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            node.Renderer.sharedMaterials = MaterialsFor(node, geometry);
            node.Renderer.enabled = Shows(node);
            MeshCount++;
        }

        void BindWaitingBones()
        {
            for (var i = _waitingForBones.Count - 1; i >= 0; i--)
            {
                var node = _waitingForBones[i];
                var bones = new Transform[node.Bones.Length];
                var ready = true;
                for (var b = 0; b < bones.Length && ready; b++)
                {
                    ready = _nodes.TryGetValue(node.Bones[b], out var bone);
                    if (ready)
                        bones[b] = bone.Object.transform;
                }

                if (!ready || !_geometries.TryGetValue(node.Geometry, out var geometry))
                    continue;
                var renderer = (SkinnedMeshRenderer)node.Renderer;
                var mesh = UnityEngine.Object.Instantiate(geometry.Mesh); // bind poses belong to this skin
                mesh.bindposes = node.BindPoses;
                renderer.bones = bones;
                renderer.rootBone = bones.Length > 0 ? bones[0] : node.Object.transform;
                renderer.sharedMesh = mesh;
                node.BonesBound = true;
                _waitingForBones.RemoveAt(i);
            }
        }

        static Matrix4x4[] BindPoses(string inverses, List<object> bindMatrix, int count)
        {
            var poses = new Matrix4x4[count];
            var floats = Floats(inverses);
            var bind = Matrix4x4.identity;
            if (bindMatrix != null && bindMatrix.Count == 16)
                for (var e = 0; e < 16; e++)
                    bind[e % 4, e / 4] = Float(bindMatrix[e]);
            for (var i = 0; i < count; i++)
            {
                var inverse = new Matrix4x4();
                for (var e = 0; e < 16 && i * 16 + e < floats.Length; e++)
                    inverse[e % 4, e / 4] = floats[i * 16 + e];
                poses[i] = Flip(inverse * bind); // three.js skins with boneInverse * bindMatrix
            }

            return poses;
        }

        void AddLines(Node node, bool strip)
        {
            if (!_geometries.TryGetValue(node.Geometry, out var geometry))
                return;
            var mesh = UnityEngine.Object.Instantiate(geometry.Mesh);
            var count = mesh.vertexCount;
            var indices = new List<int>();
            if (strip)
                for (var i = 0; i + 1 < count; i++)
                    indices.Add(i, i + 1);
            else
                for (var i = 0; i + 1 < count; i += 2)
                    indices.Add(i, i + 1);
            mesh.subMeshCount = 1;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            node.Filter = node.Object.AddComponent<MeshFilter>();
            node.Filter.sharedMesh = mesh;
            node.Renderer = node.Object.AddComponent<MeshRenderer>();
            node.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            node.Renderer.sharedMaterial = node.Materials.Length > 0 ? MaterialFor(node.Materials[0], unlit: true) : Template(Unlit);
        }

        void ReadInstances(Node node, int count, string base64)
        {
            var floats = Floats(base64);
            count = Mathf.Min(count, floats.Length / 16);
            node.Instances = new Matrix4x4[count];
            for (var i = 0; i < count; i++)
            {
                var m = new Matrix4x4();
                for (var e = 0; e < 16; e++)
                    m[e % 4, e / 4] = floats[i * 16 + e];
                node.Instances[i] = Flip(m);
            }
        }

        readonly Matrix4x4[] _batch = new Matrix4x4[1023];

        /// <summary>Draw the instanced meshes (every frame, after everything has moved).</summary>
        public void DrawInstances()
        {
            foreach (var node in _instanced)
            {
                if (node.Instances == null || node.Instances.Length == 0 || node.Object == null || !node.Object.activeInHierarchy ||
                    !_geometries.TryGetValue(node.Geometry, out var geometry))
                    continue;
                var toWorld = node.Object.transform.localToWorldMatrix;
                var materials = MaterialsFor(node, geometry);
                var centre = geometry.Mesh.bounds.center;
                for (var start = 0; start < node.Instances.Length; start += _batch.Length)
                {
                    var end = Mathf.Min(node.Instances.Length, start + _batch.Length);
                    var count = 0;
                    for (var i = start; i < end; i++)
                    {
                        var instance = toWorld * node.Instances[i];
                        if (instance.MultiplyPoint3x4(centre).y >= _floor)
                            _batch[count++] = instance; // under the room's floor: not drawn
                    }

                    if (count == 0)
                        continue;
                    for (var sub = 0; sub < geometry.Mesh.subMeshCount; sub++)
                    {
                        var material = materials[Mathf.Min(sub, materials.Length - 1)];
                        if (material == null)
                            continue;
                        material.enableInstancing = true;
                        Graphics.RenderMeshInstanced(new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, layer = _root.gameObject.layer },
                            geometry.Mesh, sub, _batch, count);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- geometry

        void ReadGeometry(Dictionary<string, object> data)
        {
            var id = Int(data["id"]);
            if (!(data.TryGetValue("attrs", out var a) && a is Dictionary<string, object> attributes) ||
                !(attributes.TryGetValue("position", out var p) && p is Dictionary<string, object> position))
                return;

            var positions = Floats(Str(position, "f32"));
            var count = positions.Length / 3;
            var vertices = new Vector3[count];
            for (var i = 0; i < count; i++)
                vertices[i] = new Vector3(positions[i * 3], positions[i * 3 + 1], -positions[i * 3 + 2]);

            if (!_geometries.TryGetValue(id, out var geometry))
            {
                geometry = new Geometry { Mesh = new Mesh { name = $"Web geometry {id}", indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 } };
                _geometries[id] = geometry;
            }

            var mesh = geometry.Mesh;
            mesh.Clear();
            mesh.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            if (attributes.TryGetValue("normal", out var n) && n is Dictionary<string, object> normal)
            {
                var floats = Floats(Str(normal, "f32"));
                var normals = new Vector3[count];
                for (var i = 0; i < count && i * 3 + 2 < floats.Length; i++)
                    normals[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], -floats[i * 3 + 2]);
                mesh.SetNormals(normals);
            }

            if (attributes.TryGetValue("uv", out var u) && u is Dictionary<string, object> uv)
            {
                var floats = Floats(Str(uv, "f32"));
                var uvs = new Vector2[count];
                for (var i = 0; i < count && i * 2 + 1 < floats.Length; i++)
                    uvs[i] = new Vector2(floats[i * 2], floats[i * 2 + 1]);
                mesh.SetUVs(0, uvs);
            }

            if (attributes.TryGetValue("color", out var c) && c is Dictionary<string, object> color)
            {
                var size = Int(color["size"]);
                var floats = Floats(Str(color, "f32"));
                var colors = new Color[count];
                for (var i = 0; i < count && i * size + 2 < floats.Length; i++)
                    colors[i] = new Color(floats[i * size], floats[i * size + 1], floats[i * size + 2], size > 3 ? floats[i * size + 3] : 1f).gamma;
                mesh.SetColors(colors);
            }

            if (attributes.TryGetValue("skinIndex", out var si) && si is Dictionary<string, object> skinIndex &&
                attributes.TryGetValue("skinWeight", out var sw) && sw is Dictionary<string, object> skinWeight)
            {
                var indices = Floats(Str(skinIndex, "f32"));
                var weights = Floats(Str(skinWeight, "f32"));
                var bones = new BoneWeight[count];
                for (var i = 0; i < count && i * 4 + 3 < indices.Length && i * 4 + 3 < weights.Length; i++)
                    bones[i] = new BoneWeight
                    {
                        boneIndex0 = (int)indices[i * 4], weight0 = weights[i * 4],
                        boneIndex1 = (int)indices[i * 4 + 1], weight1 = weights[i * 4 + 1],
                        boneIndex2 = (int)indices[i * 4 + 2], weight2 = weights[i * 4 + 2],
                        boneIndex3 = (int)indices[i * 4 + 3], weight3 = weights[i * 4 + 3],
                    };
                mesh.boneWeights = bones;
            }

            // Triangles: the index buffer (or the vertices in order), in groups (one per material), winding flipped.
            var triangles = data.TryGetValue("index", out var index) && index is Dictionary<string, object> indexData
                ? Indices(Str(indexData, "data"), indexData.TryGetValue("wide", out var wide) && wide is bool w && w)
                : Sequence(count);
            var range = data.TryGetValue("range", out var r) && r is List<object> rangeList && rangeList.Count == 2 ? (Int(rangeList[0]), Int(rangeList[1])) : (0, -1);
            var groups = data.TryGetValue("groups", out var g) && g is List<object> groupList && groupList.Count > 0 ? groupList : null;
            if (groups == null)
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(Flipped(triangles, range.Item1, range.Item2 < 0 ? triangles.Length : range.Item2), 0, true);
                geometry.GroupMaterials = new[] { 0 };
            }
            else
            {
                mesh.subMeshCount = groups.Count;
                geometry.GroupMaterials = new int[groups.Count];
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = (List<object>)groups[i];
                    mesh.SetTriangles(Flipped(triangles, Int(group[0]), Int(group[1])), i, true);
                    geometry.GroupMaterials[i] = Int(group[2]);
                }
            }

            geometry.Groups = mesh.subMeshCount;
            if (!mesh.HasVertexAttribute(VertexAttribute.Normal))
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            geometry.Size = mesh.bounds.size;
            VertexCount += count;

            // Objects already using this geometry pick up its new shape.
            foreach (var node in _nodes.Values)
                if (node.Geometry == id && node.Filter != null && node.Kind == "mesh")
                    node.Filter.sharedMesh = mesh;
        }

        static int[] Flipped(int[] indices, int start, int length)
        {
            start = Mathf.Clamp(start, 0, indices.Length);
            length = Mathf.Clamp(length, 0, indices.Length - start) / 3 * 3;
            var result = new int[length];
            for (var i = 0; i < length; i += 3)
            {
                result[i] = indices[start + i];
                result[i + 1] = indices[start + i + 2];
                result[i + 2] = indices[start + i + 1];
            }

            return result;
        }

        static int[] Sequence(int count)
        {
            var result = new int[count];
            for (var i = 0; i < count; i++)
                result[i] = i;
            return result;
        }

        static int[] Indices(string base64, bool wide)
        {
            var bytes = string.IsNullOrEmpty(base64) ? Array.Empty<byte>() : Convert.FromBase64String(base64);
            var result = new int[bytes.Length / (wide ? 4 : 2)];
            for (var i = 0; i < result.Length; i++)
                result[i] = wide ? BitConverter.ToInt32(bytes, i * 4) : BitConverter.ToUInt16(bytes, i * 2);
            return result;
        }

        static float[] Floats(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return Array.Empty<float>();
            var bytes = Convert.FromBase64String(base64);
            var floats = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * 4);
            return floats;
        }

        // ---------------------------------------------------------------- materials and textures

        const int Lit = 0;
        const int LitTransparent = 1;
        const int LitClip = 2;
        const int Unlit = 3;
        const int UnlitTransparent = 4;

        static readonly string[] TemplateNames = { "Metallic", "MetallicTransparent", "MetallicAlphaClip", "Unlit", "UnlitTransparent" };
        static readonly int BaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int BaseTexture = Shader.PropertyToID("baseColorTexture");
        static readonly int Metallic = Shader.PropertyToID("metallicFactor");
        static readonly int Roughness = Shader.PropertyToID("roughnessFactor");
        static readonly int Emissive = Shader.PropertyToID("emissiveFactor");
        static readonly int EmissiveTexture = Shader.PropertyToID("emissiveTexture");
        static readonly int NormalTexture = Shader.PropertyToID("normalTexture");
        static readonly int AlphaCutoff = Shader.PropertyToID("alphaCutoff");
        static readonly int Cull = Shader.PropertyToID("_Cull");

        Material Template(int kind)
        {
            if (_templates[kind] == null)
                _templates[kind] = Resources.Load<Material>("ImmersiveXMedia/glTF/" + TemplateNames[kind]);
            return _templates[kind];
        }

        void ReadMaterial(Dictionary<string, object> data)
        {
            var id = Int(data["id"]);
            _materialData[id] = data;
            if (_materials.TryGetValue(id, out var existing))
            {
                Discard(existing); // rebuilt below with its new settings
                _materials.Remove(id);
            }

            // Objects already using it get the new one.
            foreach (var node in _nodes.Values)
                if (Array.IndexOf(node.Materials, id) >= 0 && node.Renderer != null && _geometries.TryGetValue(node.Geometry, out var geometry))
                {
                    if (node.Kind == "line" || node.Kind == "segments")
                        node.Renderer.sharedMaterial = MaterialFor(id, unlit: true);
                    else
                        node.Renderer.sharedMaterials = MaterialsFor(node, geometry);
                    node.Renderer.enabled = Shows(node);
                }
        }

        Material[] MaterialsFor(Node node, Geometry geometry)
        {
            var result = new Material[Mathf.Max(1, geometry.Groups)];
            for (var i = 0; i < result.Length; i++)
            {
                var slot = geometry.GroupMaterials != null && i < geometry.GroupMaterials.Length ? geometry.GroupMaterials[i] : 0;
                result[i] = node.Materials.Length > 0 ? MaterialFor(node.Materials[Mathf.Clamp(slot, 0, node.Materials.Length - 1)], unlit: false) : Template(Lit);
            }

            return result;
        }

        /// <summary>Whether a mesh is drawn: not a shadow catcher, not the ground, not under the room's floor.</summary>
        bool Shows(Node node) => !IsShadowCatcher(node) && !_ground.Contains(node.Renderer) && !_belowFloor.Contains(node.Renderer);

        bool IsShadowCatcher(Node node)
        {
            foreach (var id in node.Materials)
                if (_materialData.TryGetValue(id, out var data) && (Str(data, "type") == "ShadowMaterial" || data.TryGetValue("visible", out var v) && v is bool visible && !visible))
                    return true;
            return false;
        }

        Material MaterialFor(int id, bool unlit)
        {
            if (_materials.TryGetValue(id, out var cached))
                return cached;
            if (!_materialData.TryGetValue(id, out var data))
                return Template(unlit ? Unlit : Lit);

            var type = Str(data, "type");
            var opacity = data.TryGetValue("opacity", out var o) ? Float(o) : 1f;
            var transparent = data.TryGetValue("transparent", out var t) && t is bool isTransparent && isTransparent && opacity < 0.999f;
            var additive = data.TryGetValue("blending", out var b) && Int(b) == 2; // AdditiveBlending
            var alphaTest = data.TryGetValue("alphaTest", out var a) ? Float(a) : 0f;
            var basic = unlit || type == "MeshBasicMaterial" || type == "LineBasicMaterial" || type == "LineDashedMaterial" || type == "PointsMaterial" || type == "SpriteMaterial";
            if (type == "ShaderMaterial" || type == "RawShaderMaterial")
            {
                // A custom shader can't be drawn as it is: glows and plumes (additive) become soft see-through colour.
                basic = additive;
                transparent |= additive;
                if (additive)
                    opacity = Mathf.Min(opacity, 0.45f);
            }

            var kind = basic ? (transparent ? UnlitTransparent : Unlit) : transparent ? LitTransparent : alphaTest > 0f ? LitClip : Lit;
            var material = new Material(Template(kind)) { name = $"Web {type} {id}" };

            var color = data.TryGetValue("color", out var c) && c is List<object> rgb && rgb.Count >= 3
                ? new Color(Float(rgb[0]), Float(rgb[1]), Float(rgb[2]), 1f)
                : Color.white;
            color.a = transparent ? opacity : 1f;
            material.SetColor(BaseColor, color.gamma); // three.js keeps colours linear; SetColor takes sRGB
            if (!basic)
            {
                if (data.TryGetValue("metalness", out var metalness) && metalness != null)
                    material.SetFloat(Metallic, Mathf.Clamp01(Float(metalness)));
                material.SetFloat(Roughness, data.TryGetValue("roughness", out var roughness) && roughness != null ? Mathf.Clamp01(Float(roughness)) : 0.8f);
                if (data.TryGetValue("emissive", out var e) && e is List<object> emissive && emissive.Count >= 3)
                {
                    var intensity = data.TryGetValue("emissiveIntensity", out var ei) && ei != null ? Float(ei) : 1f;
                    material.SetColor(Emissive, new Color(Float(emissive[0]) * intensity, Float(emissive[1]) * intensity, Float(emissive[2]) * intensity).gamma);
                }
            }

            SetTexture(material, BaseTexture, data, "map");
            if (!basic)
            {
                SetTexture(material, EmissiveTexture, data, "emissiveMap");
                SetTexture(material, NormalTexture, data, "normalMap");
            }

            if (alphaTest > 0f)
                material.SetFloat(AlphaCutoff, alphaTest);
            var side = data.TryGetValue("side", out var s) ? Int(s) : 0;
            if (material.HasProperty(Cull))
                material.SetFloat(Cull, side == 2 ? 0f : side == 1 ? 1f : 2f); // double / back / front side
            if (data.TryGetValue("depthWrite", out var dw) && dw is bool depthWrite && !depthWrite && transparent)
                material.SetFloat("_ZWrite", 0f);
            material.enableInstancing = true;
            _materials[id] = material;
            return material;
        }

        void SetTexture(Material material, int property, Dictionary<string, object> data, string slot)
        {
            if (!data.TryGetValue(slot, out var value) || value == null || !_textures.TryGetValue(Int(value), out var texture) || !material.HasProperty(property))
                return;
            material.SetTexture(property, texture);
            if (_textureTransforms.TryGetValue(Int(value), out var transform))
            {
                material.SetTextureScale(property, new Vector2(transform.x, transform.y));
                material.SetTextureOffset(property, new Vector2(transform.z, transform.w));
            }
        }

        readonly Dictionary<int, Vector4> _textureTransforms = new Dictionary<int, Vector4>();

        void ReadTexture(Dictionary<string, object> data)
        {
            var id = Int(data["id"]);
            var srgb = data.TryGetValue("srgb", out var s) && s is bool isSrgb && isSrgb;
            if (!_textures.TryGetValue(id, out var texture))
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear: !srgb) { name = $"Web texture {id}" };
                _textures[id] = texture;
            }

            texture.LoadImage(Convert.FromBase64String(Str(data, "png")));
            texture.wrapModeU = Wrap(data, "wrapS");
            texture.wrapModeV = Wrap(data, "wrapT");
            texture.anisoLevel = 4;
            var repeat = data.TryGetValue("repeat", out var r) && r is List<object> rr && rr.Count == 2 ? new Vector2(Float(rr[0]), Float(rr[1])) : Vector2.one;
            var offset = data.TryGetValue("offset", out var o) && o is List<object> oo && oo.Count == 2 ? new Vector2(Float(oo[0]), Float(oo[1])) : Vector2.zero;
            _textureTransforms[id] = new Vector4(repeat.x, repeat.y, offset.x, offset.y);

            // Materials made before the texture arrived pick it up.
            foreach (var pair in _materialData)
                foreach (var slot in new[] { "map", "emissiveMap", "normalMap" })
                    if (pair.Value.TryGetValue(slot, out var value) && value != null && Int(value) == id && _materials.TryGetValue(pair.Key, out var material))
                        SetTexture(material, slot == "map" ? BaseTexture : slot == "emissiveMap" ? EmissiveTexture : NormalTexture, pair.Value, slot);
        }

        static TextureWrapMode Wrap(Dictionary<string, object> data, string key)
        {
            switch (data.TryGetValue(key, out var value) ? Int(value) : 1001)
            {
                case 1000: return TextureWrapMode.Repeat;
                case 1002: return TextureWrapMode.Mirror;
                default: return TextureWrapMode.Clamp;
            }
        }

        // ---------------------------------------------------------------- lights

        void AddLight(Node node, Dictionary<string, object> settings)
        {
            var type = Str(settings, "type");
            LightType unityType;
            switch (type)
            {
                case "DirectionalLight": unityType = LightType.Directional; break;
                case "PointLight": unityType = LightType.Point; break;
                case "SpotLight": unityType = LightType.Spot; break;
                default: return; // hemisphere and ambient light come from the room's lighting
            }

            var light = node.Object.AddComponent<Light>();
            light.type = unityType;
            if (settings.TryGetValue("color", out var c) && c is List<object> rgb && rgb.Count >= 3)
                light.color = new Color(Float(rgb[0]), Float(rgb[1]), Float(rgb[2])).gamma;
            light.intensity = LightIntensity(unityType, settings.TryGetValue("intensity", out var i) ? Float(i) : 1f);
            light.shadows = LightShadows.None;
            if (unityType != LightType.Directional)
                light.range = settings.TryGetValue("distance", out var d) && Float(d) > 0f ? Float(d) : 10f;
            if (unityType == LightType.Spot && settings.TryGetValue("angle", out var angle))
                light.spotAngle = Float(angle) * 2f * Mathf.Rad2Deg;
        }

        /// <summary>three.js lights are in physical units; this keeps them about as bright on URP's lit materials.</summary>
        static float LightIntensity(LightType type, float three) => type == LightType.Directional ? three * 0.45f : three * 0.08f;

        // ---------------------------------------------------------------- selectables

        void ReadSelectables(List<object> list)
        {
            var changed = list.Count != Selectables.Count;
            var next = new List<(string, int, string, string)>();
            for (var i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Dictionary<string, object> item))
                    continue;
                var entry = (Str(item, "id"), Int(item["root"]), Str(item, "title"), Str(item, "info"));
                next.Add(entry);
                changed |= i >= Selectables.Count || Selectables[i] != entry;
            }

            if (!changed)
                return;
            Selectables.Clear();
            Selectables.AddRange(next);
            SelectablesChanged?.Invoke();
        }

        // ---------------------------------------------------------------- fitting

        readonly List<(Node node, Bounds local)> _pieces = new List<(Node, Bounds)>();
        readonly List<float> _sizes = new List<float>();
        float _restHeight = -1f; // the scene's height at rest, kept once it's placed (things may fly off later)

        /// <summary>
        /// Hide the ground (MR shows the real floor instead): flat meshes far wider than the scene is tall (a terrain, a
        /// studio floor), and far wider than its typical piece. Flat parts of the scene (a stage, a platform, a road) stay.
        /// Pages show and hide things as they play, so this runs again and again.
        /// </summary>
        public void HideGround()
        {
            _pieces.Clear();
            _sizes.Clear();
            var low = float.MaxValue;
            var high = float.MinValue;
            foreach (var node in _nodes.Values)
            {
                if (node.Renderer == null || node.Object == null || !node.Object.activeInHierarchy || IsShadowCatcher(node))
                    continue;
                var local = SceneBoundsOf(node);
                _pieces.Add((node, local));
                _sizes.Add(Mathf.Max(local.size.x, local.size.z));
                low = Mathf.Min(low, local.min.y);
                high = Mathf.Max(high, local.max.y);
            }

            if (_pieces.Count <= 3)
                return;
            _sizes.Sort();
            var typical = Mathf.Max(_sizes[_sizes.Count / 2], 1e-4f);
            var tall = Mathf.Max(_restHeight > 0f ? _restHeight : high - low, 1e-4f);
            _measuredHeight = high - low;
            foreach (var (node, local) in _pieces)
            {
                var across = Mathf.Max(local.size.x, local.size.z);
                var ground = local.size.y < across * 0.02f && across > typical * 6f && across > tall * 3f;
                if (ground == _ground.Contains(node.Renderer))
                    continue;
                if (ground)
                    _ground.Add(node.Renderer);
                else
                    _ground.Remove(node.Renderer);
                node.Renderer.enabled = Shows(node);
            }
        }

        float _measuredHeight = -1f;

        /// <summary>The scene is placed: from now on the ground is told apart by its height now, whatever flies off later.</summary>
        public void KeepRestHeight()
        {
            if (_restHeight < 0f && _measuredHeight > 0f)
                _restHeight = _measuredHeight;
        }

        /// <summary>A mesh's bounds in the scene's space (page units), from the mesh itself (so hidden ones have them too).</summary>
        Bounds SceneBoundsOf(Node node)
        {
            var mesh = MeshOf(node);
            var toScene = _root.worldToLocalMatrix * node.Object.transform.localToWorldMatrix;
            if (mesh == null)
                return new Bounds(toScene.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            return Transformed(mesh.bounds, toScene);
        }

        /// <summary>A mesh's world bounds, from the mesh itself (so hidden ones have them too).</summary>
        static Bounds WorldBoundsOf(Node node)
        {
            var mesh = MeshOf(node);
            var toWorld = node.Object.transform.localToWorldMatrix;
            if (mesh == null)
                return new Bounds(toWorld.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            return Transformed(mesh.bounds, toWorld);
        }

        static Bounds Transformed(Bounds local, Matrix4x4 matrix)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (var i = 0; i < 8; i++)
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        static Mesh MeshOf(Node node) => node.Filter != null ? node.Filter.sharedMesh : node.Renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : null;

        /// <summary>
        /// Hide what's under the room's floor (<paramref name="floor"/>, a world height): in MR the real floor is there, so
        /// what the page has sunk below it (a platform dropping away under something rising) mustn't show through it.
        /// A mesh goes once its middle is under the floor and comes back when the page brings it up again.
        /// </summary>
        public void HideBelow(float floor)
        {
            _floor = floor;
            foreach (var node in _nodes.Values)
            {
                var renderer = node.Renderer;
                if (renderer == null || node.Object == null || _ground.Contains(renderer) || IsShadowCatcher(node))
                    continue;
                var below = WorldCentreOf(node).y < floor;
                if (below == _belowFloor.Contains(renderer))
                    continue;
                if (below)
                    _belowFloor.Add(renderer);
                else
                    _belowFloor.Remove(renderer);
                renderer.enabled = !below;
            }
        }

        /// <summary>
        /// The height (in <paramref name="space"/>) of the page's ground (the widest of what <see cref="HideGround"/> hid), when
        /// it lies between <paramref name="low"/> and <paramref name="high"/> (a cloud layer far above isn't the ground).
        /// </summary>
        public bool TryGroundHeight(Transform space, float low, float high, out float height)
        {
            height = 0f;
            var widest = -1f;
            foreach (var node in _nodes.Values)
            {
                if (node.Renderer == null || node.Object == null || !node.Object.activeInHierarchy || !_ground.Contains(node.Renderer))
                    continue;
                var world = WorldBoundsOf(node);
                var y = space.InverseTransformPoint(world.center).y;
                var across = Mathf.Max(world.size.x, world.size.z);
                if (y >= low && y <= high && across > widest)
                {
                    widest = across;
                    height = y;
                }
            }

            return widest >= 0f;
        }

        /// <summary>
        /// The level the scene stands on when the page has no ground of its own: the bottom shared by the most floor area among
        /// what's drawn in its lower half (table legs, feet, the base of a tower), in <paramref name="space"/>.
        /// </summary>
        public bool TryContactLevel(Transform space, Bounds scene, out float level)
        {
            level = scene.min.y;
            var step = Mathf.Max(scene.size.y * 0.01f, 1e-4f);
            var areas = new Dictionary<int, float>();
            var sums = new Dictionary<int, float>();
            foreach (var node in _nodes.Values)
            {
                if (node.Renderer == null || !node.Renderer.enabled || node.Object == null || !node.Object.activeInHierarchy)
                    continue;
                var world = WorldBoundsOf(node);
                var bottom = space.InverseTransformPoint(new Vector3(world.center.x, world.min.y, world.center.z)).y;
                if (bottom > scene.center.y)
                    continue;
                var area = Mathf.Max(world.size.x * world.size.z, 1e-8f);
                var bin = Mathf.FloorToInt((bottom - scene.min.y) / step);
                areas[bin] = (areas.TryGetValue(bin, out var a) ? a : 0f) + area;
                sums[bin] = (sums.TryGetValue(bin, out var sum) ? sum : 0f) + area * bottom;
            }

            var most = 0f;
            foreach (var pair in areas)
            {
                // With its neighbours, so a level split across two steps still counts as one.
                var area = pair.Value + (areas.TryGetValue(pair.Key - 1, out var under) ? under : 0f) + (areas.TryGetValue(pair.Key + 1, out var over) ? over : 0f);
                if (area <= most)
                    continue;
                most = area;
                level = sums[pair.Key] / pair.Value;
            }

            return most > 0f;
        }

        /// <summary>Show again what <see cref="HideBelow"/> hid (to measure the whole scene); the next call hides it again.</summary>
        public void ShowBelow()
        {
            if (_belowFloor.Count > 0)
            {
                _belowFloor.Clear();
                foreach (var node in _nodes.Values)
                    if (node.Renderer != null)
                        node.Renderer.enabled = Shows(node);
            }

            _floor = float.NegativeInfinity;
        }

        /// <summary>The world centre of a mesh (from the mesh itself, so hidden ones have one too).</summary>
        static Vector3 WorldCentreOf(Node node)
        {
            var mesh = MeshOf(node);
            return mesh != null ? node.Object.transform.TransformPoint(mesh.bounds.center) : node.Object.transform.position;
        }

        /// <summary>
        /// The bounds of what <paramref name="node"/> (and what's under it) draws, in the scene's space (<see cref="Root"/>'s
        /// local space: page units); false when it draws nothing. Measured from the meshes and where they are now (Unity's
        /// renderer bounds lag behind for objects just shown).
        /// </summary>
        public bool TryBoundsOf(Transform node, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            var toScene = _root.worldToLocalMatrix;
            foreach (var renderer in node.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && TryMeshBounds(renderer, toScene, out var local))
                    Encapsulate(ref bounds, ref found, local);
            return found;
        }

        /// <summary>The centre of what <paramref name="node"/> draws, in the scene's space; its own position when it draws nothing yet.</summary>
        public Vector3 CentreOf(Transform node) => TryBoundsOf(node, out var bounds) ? bounds.center : _root.InverseTransformPoint(node.position);

        /// <summary>
        /// The bounds of what's drawn, in <paramref name="space"/>, leaving out the ground (see <see cref="HideGround"/>);
        /// measured from the meshes and where they are now.
        /// </summary>
        public bool TryBounds(Transform space, out Bounds bounds)
        {
            bounds = default;
            HideGround();
            var found = false;
            var toSpace = space.worldToLocalMatrix;
            foreach (var renderer in _root.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && TryMeshBounds(renderer, toSpace, out var local))
                    Encapsulate(ref bounds, ref found, local);
            return found;
        }

        /// <summary>A renderer's mesh bounds in the space <paramref name="toSpace"/> maps world positions into.</summary>
        static bool TryMeshBounds(Renderer renderer, Matrix4x4 toSpace, out Bounds bounds)
        {
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            bounds = mesh != null ? Transformed(mesh.bounds, toSpace * renderer.transform.localToWorldMatrix) : default;
            return mesh != null;
        }

        static void Encapsulate(ref Bounds bounds, ref bool found, Bounds more)
        {
            if (found)
            {
                bounds.Encapsulate(more);
            }
            else
            {
                bounds = more;
                found = true;
            }
        }

        Node FindNode(GameObject gameObject)
        {
            foreach (var node in _nodes.Values)
                if (node.Object == gameObject)
                    return node;
            return null;
        }

        /// <summary>Where the objects called <paramref name="name"/> are, for diagnostics: local and world position, shown or not, renderers.</summary>
        public string DescribeNode(string name)
        {
            var text = new System.Text.StringBuilder();
            foreach (var node in _nodes.Values)
            {
                if (node.Object == null || !node.Object.name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                var t = node.Object.transform;
                var drawn = 0;
                foreach (var renderer in node.Object.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled)
                        drawn++;
                text.Append($"{node.Object.name} #{node.Id} ({node.Kind}): local {t.localPosition:F1} scale {t.localScale:F2}, world {t.position:F2}, " +
                            $"{(node.Object.activeInHierarchy ? "shown" : "not shown")}, {drawn} renderers drawn; ");
            }

            return text.Length > 0 ? text.ToString() : $"No object called '{name}'.";
        }

        /// <summary>What's in the mirrored scene, for diagnostics: objects by kind, what's drawn or hidden, materials by type.</summary>
        public string Describe()
        {
            var kinds = new Dictionary<string, int>();
            var hidden = 0;
            var inactive = 0;
            var shadowCatchers = 0;
            foreach (var node in _nodes.Values)
            {
                kinds[node.Kind] = kinds.TryGetValue(node.Kind, out var n) ? n + 1 : 1;
                if (node.Object != null && !node.Object.activeInHierarchy)
                    inactive++;
                else if (node.Renderer != null && !node.Renderer.enabled)
                {
                    if (IsShadowCatcher(node))
                        shadowCatchers++;
                    else
                        hidden++;
                }
            }

            var materials = new Dictionary<string, int>();
            foreach (var data in _materialData.Values)
            {
                var type = Str(data, "type");
                materials[type] = materials.TryGetValue(type, out var n) ? n + 1 : 1;
            }

            var instances = 0;
            foreach (var node in _instanced)
                instances += node.Instances?.Length ?? 0;
            var text = new System.Text.StringBuilder();
            text.Append($"'{Title}': {_nodes.Count} objects (");
            foreach (var pair in kinds)
                text.Append($"{pair.Key} {pair.Value}, ");
            text.Append($"), {inactive} not visible in the page, {hidden} hidden as ground, {shadowCatchers} shadow catchers; ");
            text.Append($"{_instanced.Count} instanced meshes drawing {instances} instances; {_waitingForBones.Count} skins waiting for bones; materials: ");
            foreach (var pair in materials)
                text.Append($"{pair.Key} {pair.Value}, ");
            text.Append($"textures {_textures.Count}; top level: ");
            foreach (Transform child in _root)
                text.Append($"{child.name}{(child.gameObject.activeSelf ? string.Empty : " (hidden)")} [{child.GetComponentsInChildren<Renderer>(true).Length}], ");
            text.Append("; ground: ");
            foreach (var node in _nodes.Values)
                if (node.Renderer != null && node.Object != null && _ground.Contains(node.Renderer))
                    text.Append($"{node.Object.name} at {_root.InverseTransformPoint(WorldCentreOf(node)).y:0.#}{(node.Object.activeInHierarchy ? string.Empty : " (not shown)")}, ");
            text.Append("; largest: ");
            var largest = new List<Renderer>(_root.GetComponentsInChildren<Renderer>());
            largest.Sort((a, b) => Mathf.Max(b.bounds.size.x, b.bounds.size.z).CompareTo(Mathf.Max(a.bounds.size.x, a.bounds.size.z)));
            for (var i = 0; i < Mathf.Min(6, largest.Count); i++)
            {
                var node = FindNode(largest[i].gameObject);
                var type = node != null && node.Materials.Length > 0 && _materialData.TryGetValue(node.Materials[0], out var d) ? Str(d, "type") : "?";
                text.Append($"{largest[i].name} {largest[i].bounds.size:F2} {type}{(largest[i].enabled ? string.Empty : " (hidden)")}, ");
            }

            return text.ToString().TrimEnd(',', ' ');
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>three.js (right-handed) to Unity (left-handed): flip z on both sides of the matrix.</summary>
        static Matrix4x4 Flip(Matrix4x4 m)
        {
            m.m02 = -m.m02;
            m.m12 = -m.m12;
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            m.m23 = -m.m23;
            m.m32 = -m.m32;
            return m;
        }

        static void SetLocal(Transform transform, Matrix4x4 m)
        {
            var x = (Vector3)m.GetColumn(0);
            var y = (Vector3)m.GetColumn(1);
            var z = (Vector3)m.GetColumn(2);
            var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f)
                scale.x = -scale.x; // a mirrored object
            var rotation = z.sqrMagnitude > 1e-12f && y.sqrMagnitude > 1e-12f ? Quaternion.LookRotation(z, y) : Quaternion.identity;
            transform.SetLocalPositionAndRotation(m.GetColumn(3), rotation);
            transform.localScale = scale;
        }

        static string Str(Dictionary<string, object> data, string key) => data.TryGetValue(key, out var value) ? value as string ?? string.Empty : string.Empty;

        static int Int(object value) => value is double d ? (int)d : value is bool b ? (b ? 1 : 0) : 0;

        static float Float(object value) => value is double d ? (float)d : 0f;

        /// <summary>Destroy, or at once outside Play mode (tests, the editor).</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(thing);
            else
                UnityEngine.Object.DestroyImmediate(thing);
        }

        void Warn(string text)
        {
            if (_logged++ < 25)
                ImmersiveXLog.Warn(text);
        }

        public void Dispose()
        {
            foreach (var node in _nodes.Values)
                DiscardOwnMesh(node); // while their objects are still there
            if (_root != null)
                Discard(_root.gameObject);
            foreach (var geometry in _geometries.Values)
                Discard(geometry.Mesh);
            foreach (var material in _materials.Values)
                Discard(material);
            foreach (var texture in _textures.Values)
                Discard(texture);
        }

        /// <summary>The mesh an object has to itself (a skin with its bind poses, lines): it goes with the object.</summary>
        static void DiscardOwnMesh(Node node)
        {
            if (node.Object == null)
                return;
            if (node.Kind == "skinned" && node.Renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null && node.BonesBound)
                Discard(skin.sharedMesh);
            else if ((node.Kind == "line" || node.Kind == "segments") && node.Filter != null && node.Filter.sharedMesh != null)
                Discard(node.Filter.sharedMesh);
        }
    }

    static class ListExtensions
    {
        public static void Add(this List<int> list, int a, int b)
        {
            list.Add(a);
            list.Add(b);
        }
    }
}
