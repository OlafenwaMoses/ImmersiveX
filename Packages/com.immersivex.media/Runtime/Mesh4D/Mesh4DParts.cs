using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A 4D mesh's logical parts (the stage, an assembly, a single piece) as <see cref="ContentParts"/>, so they're selected
    /// and moved like a model's. Each part is a transform that follows the recording (its home is where the recording puts
    /// it); ContentParts adds the user's move on top, and the difference is applied to every object in that part.
    /// </summary>
    sealed class Mesh4DParts : IDisposable
    {
        sealed class Node
        {
            public ContentPart Part;
            public string PartName, PartTitle, PartInfo;
            public Transform Transform;
            public Node Parent;
            public int Frame = -1; // the object whose pose is the part's frame
            public readonly List<int> Objects = new List<int>();
            public readonly List<(BoxCollider box, int obj, Vector3 centre, Vector3 size)> Boxes = new List<(BoxCollider, int, Vector3, Vector3)>();
            public Matrix4x4 Delta = Matrix4x4.identity;
            public bool Moved;
        }

        readonly Mesh4DContainer _c;
        readonly Mesh4DRenderer _renderer;
        readonly List<Node> _nodes = new List<Node>();
        readonly Node[] _nodeOf;
        Transform _holder;

        public ContentParts Parts { get; private set; }

        Mesh4DParts(Mesh4DContainer c, Mesh4DRenderer renderer)
        {
            _c = c;
            _renderer = renderer;
            _nodeOf = new Node[c.ObjectCount];
        }

        /// <summary>Parts for <paramref name="c"/>, or null when it has fewer than two. <paramref name="atlasBoxes"/> comes from <see cref="MeasureAtlasBoxes"/>.</summary>
        public static Mesh4DParts Create(Mesh4DContainer c, Mesh4DRenderer renderer, (Vector3, Vector3)[] atlasBoxes)
        {
            if (c.Parts == null || c.Parts.Length == 0)
                return null;
            var parts = new Mesh4DParts(c, renderer) { _localBoxes = atlasBoxes };
            parts.Build();
            return parts._nodes.Count >= 2 ? parts : null;
        }

        void Build()
        {
            _holder = new GameObject("Parts").transform;
            _holder.SetParent(_renderer.Root, false);
            _holder.gameObject.layer = _renderer.Root.gameObject.layer;
            var byName = new Dictionary<string, Node>();
            // Parts with at least one object, shallowest first so parents exist before their children.
            var ordered = new List<Mesh4DContainer.Part>(_c.Parts);
            ordered.Sort((a, b) => Depth(a.Name).CompareTo(Depth(b.Name)));
            foreach (var p in ordered)
            {
                var objects = new List<int>();
                for (var o = 0; o < _c.ObjectCount; o++)
                    if (_c.Objects[o].Part == p.Name || _c.Objects[o].Part.StartsWith(p.Name + ".", StringComparison.Ordinal))
                        objects.Add(o);
                if (objects.Count == 0)
                    continue;
                var node = new Node { Transform = new GameObject(p.Title ?? p.Name).transform };
                node.Transform.gameObject.layer = _holder.gameObject.layer;
                for (var parent = Parent(p.Name); parent != null; parent = Parent(parent))
                {
                    if (byName.TryGetValue(parent, out var up))
                    {
                        node.Parent = up;
                        break;
                    }
                }

                node.Transform.SetParent(node.Parent != null ? node.Parent.Transform : _holder, false);
                // the part's frame: a hull of the part if it has one (it doesn't swivel), else its first object
                node.Frame = objects.Find(o => _c.Objects[o].Kind == "hull");
                if (node.Frame < 0)
                    node.Frame = objects.Find(o => _c.Objects[o].Kind != "bell" && _c.Objects[o].Kind != "engine" && _c.Objects[o].Kind != "liquid");
                if (node.Frame < 0)
                    node.Frame = objects[0];
                byName[p.Name] = node;
                _nodes.Add(node);
                node.PartName = p.Name;
                node.PartTitle = p.Title;
                node.PartInfo = p.Description;
            }

            // each object belongs to its deepest part
            foreach (var node in _nodes)
                foreach (var o in ObjectsUnder(node))
                    if (_nodeOf[o] == null || Depth(Name(_nodeOf[o])) < Depth(Name(node)))
                        _nodeOf[o] = node;
            for (var o = 0; o < _c.ObjectCount; o++)
                _nodeOf[o]?.Objects.Add(o);

            Parts = _holder.gameObject.AddComponent<ContentParts>();
            foreach (var node in _nodes)
            {
                node.Part = Parts.Add(node.Transform, node.PartName, node.PartTitle, node.PartInfo, node.Parent?.Part);
                foreach (var o in node.Objects)
                {
                    if ((_c.Objects[o].Flags & 8) != 0)
                        continue; // liquids sit inside their tanks
                    var (centre, size) = LocalBox(o);
                    var body = ContentLayer.CreateBody(node.Transform, "Part Body");
                    var box = body.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = size;
                    node.Boxes.Add((box, o, centre, size));
                    Parts.AddCollider(node.Part, box);
                }
            }

            Parts.RefreshGrab();
            Pose();
        }

        static int Depth(string name) => name.Split('.').Length;

        static string Parent(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : null;
        }

        static string Name(Node node) => node.PartName;

        IEnumerable<int> ObjectsUnder(Node node)
        {
            for (var o = 0; o < _c.ObjectCount; o++)
                if (_c.Objects[o].Part == node.PartName || _c.Objects[o].Part.StartsWith(node.PartName + ".", StringComparison.Ordinal))
                    yield return o;
        }

        /// <summary>An object's box in its own frame (from its triangles).</summary>
        (Vector3 centre, Vector3 size) LocalBox(int o)
        {
            var info = _c.Objects[o];
            float[] positions = null;
            int start = 0, count = 0;
            if (info.Family >= 0)
            {
                positions = _c.Families[info.Family].Positions;
                count = positions.Length / 3;
            }
            else if (info.AtlasRange != null && _localBoxes != null)
            {
                return _localBoxes[o];
            }

            if (positions == null)
                return (Vector3.zero, Vector3.one);
            var b = new Bounds(new Vector3(positions[start * 3], positions[start * 3 + 1], positions[start * 3 + 2]), Vector3.zero);
            for (var i = start; i < start + count; i++)
                b.Encapsulate(new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]));
            return (b.center, Vector3.Max(b.size, Vector3.one * 0.2f));
        }

        (Vector3, Vector3)[] _localBoxes;

        /// <summary>Boxes for the atlas objects, measured once from the atlas mesh before its arrays are let go.</summary>
        public static (Vector3, Vector3)[] MeasureAtlasBoxes(Mesh4DContainer c)
        {
            var boxes = new (Vector3, Vector3)[c.ObjectCount];
            for (var o = 0; o < c.ObjectCount; o++)
            {
                var r = c.Objects[o].AtlasRange;
                if (r == null || c.AtlasPositions == null)
                    continue;
                var p = c.AtlasPositions;
                var b = new Bounds(new Vector3(p[r[0] * 3], p[r[0] * 3 + 1], p[r[0] * 3 + 2]), Vector3.zero);
                for (var v = r[0]; v < r[0] + r[1]; v++)
                    b.Encapsulate(new Vector3(p[v * 3], p[v * 3 + 1], p[v * 3 + 2]));
                boxes[o] = (b.center, Vector3.Max(b.size, Vector3.one * 0.2f));
            }

            return boxes;
        }

        // ---------------------------------------------------------------- every frame

        /// <summary>Put every part where the recording has it (before ContentParts adds the user's moves).</summary>
        public void Pose()
        {
            foreach (var node in _nodes)
            {
                var frame = _renderer.ObjectMatrix(node.Frame);
                var parentFrame = node.Parent != null ? _renderer.ObjectMatrix(node.Parent.Frame) : Matrix4x4.identity;
                var local = parentFrame.inverse * frame;
                node.Transform.SetLocalPositionAndRotation(local.GetColumn(3), local.rotation);
                var visible = false;
                foreach (var (box, o, centre, size) in node.Boxes)
                {
                    var objectInPart = frame.inverse * _renderer.ObjectMatrix(o);
                    box.transform.SetLocalPositionAndRotation(objectInPart.MultiplyPoint3x4(centre), objectInPart.rotation);
                    box.enabled = _renderer.Visible[o];
                    visible |= _renderer.Visible[o];
                }

                node.Transform.gameObject.SetActive(visible || node.Boxes.Count == 0);
            }
        }

        /// <summary>The user's move of the part holding object <paramref name="o"/>, in content space (null when it isn't moved).</summary>
        public Matrix4x4? OffsetOf(int o)
        {
            var node = _nodeOf[o];
            return node != null && node.Moved ? node.Delta : (Matrix4x4?)null;
        }

        /// <summary>Measure the user's moves (after ContentParts has applied them this frame).</summary>
        public void Refresh()
        {
            var toContent = _renderer.Root.worldToLocalMatrix;
            foreach (var node in _nodes)
            {
                var placed = toContent * node.Transform.localToWorldMatrix; // where the user has it
                var home = _renderer.ObjectMatrix(node.Frame); // where the recording has it
                node.Delta = placed * home.inverse;
                node.Moved = !IsIdentity(node.Delta);
            }
        }

        static bool IsIdentity(Matrix4x4 m)
        {
            for (var i = 0; i < 16; i++)
                if (Mathf.Abs(m[i] - (i % 5 == 0 ? 1f : 0f)) > 1e-4f)
                    return false;
            return true;
        }

        /// <summary>Object <paramref name="o"/>'s pose with the user's move, in content space.</summary>
        public Matrix4x4 CurrentMatrix(int o)
        {
            var node = _nodeOf[o];
            return node != null && node.Moved ? node.Delta * _renderer.ObjectMatrix(o) : _renderer.ObjectMatrix(o);
        }

        public void Dispose()
        {
            if (_holder != null)
                UnityEngine.Object.Destroy(_holder.gameObject);
        }
    }
}
