using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Makes the pieces of a piece of content selectable and movable, with ImmersiveX's interaction rule (the ray clicks, the
    /// hand grabs):
    /// <list type="bullet">
    /// <item>A ray click selects a part. Clicking it again selects the assembly it belongs to, then the next one up, then
    /// nothing.</item>
    /// <item>A hand or controller at a part grabs it and moves it freely; it stays where it's let go. With an assembly
    /// selected, grabbing any piece of it moves the whole assembly.</item>
    /// <item>The selected part's label has Put back (glides it home) and Put all back.</item>
    /// </list>
    /// Parts come from a hierarchy (<see cref="FromHierarchy"/>: models and Unity objects) or are added one by one
    /// (<see cref="Add"/>: web scenes). A moved part keeps its offset on top of wherever the content puts it, so animated
    /// parts keep animating in their new place.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)] // puts parts back where the content left them before the content moves them this frame
    public sealed class ContentParts : MonoBehaviour
    {
        const float GlideSeconds = 0.12f;
        const int MaxMeshColliderTriangles = 60000;

        readonly List<ContentPart> _parts = new List<ContentPart>();
        readonly List<Collider> _colliders = new List<Collider>();
        PartsInteractable _grab;
        PartLabel _label;
        ContentPart _selected;

        public IReadOnlyList<ContentPart> Parts => _parts;

        bool _movable = true;

        /// <summary>
        /// Whether hands can pick parts up and move them. Immersive Media turns it off while its content plays, so parts only
        /// move while the content is paused; a hand reaching through playing content never moves it. A part held when it
        /// turns off is let go.
        /// </summary>
        public bool Movable
        {
            get => _movable;
            set
            {
                if (_movable == value)
                    return;
                _movable = value;
                if (!value && _grab != null)
                    _grab.ReleaseAll();
            }
        }
        public ContentPart Selected => _selected;
        public bool AnyMoved => _parts.Exists(p => p.IsMoved);

        /// <summary>Raised when the user lets go of a part they moved (the root part included).</summary>
        public event Action<ContentPart> Released;

        public event Action<ContentPart> SelectionChanged;

        /// <summary>Raised when a part is about to be picked up (a web page pauses its timeline, for example).</summary>
        public event Action<ContentPart> Grabbed;

        // ---------------------------------------------------------------- building

        /// <summary>
        /// Find the parts under <paramref name="root"/>: every node with a mesh (a skinned mesh's part is the node holding
        /// it and its skeleton), and every node that groups two or more of them. Null when there are fewer than two meshes.
        /// With <paramref name="rootIsPart"/>, the root is the top assembly: grabbing it moves the whole object.
        /// </summary>
        public static ContentParts FromHierarchy(Transform root, bool rootIsPart = false)
        {
            var leaves = new List<Transform>();
            var leafRenderers = new Dictionary<Transform, List<Renderer>>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                var node = renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null
                    ? CommonAncestor(skinned.transform, skinned.rootBone, root)
                    : renderer.transform;
                if (node == root)
                    continue; // the root's own mesh moves with the root
                if (!leafRenderers.TryGetValue(node, out var list))
                {
                    leafRenderers[node] = list = new List<Renderer>();
                    leaves.Add(node);
                }

                list.Add(renderer);
            }

            // A mesh inside a skinned part (a prop held in a hand bone) moves with that part.
            leaves.RemoveAll(leaf => HasAncestorIn(leaf, leafRenderers, root, skinnedOnly: true));
            if (leaves.Count < 2)
                return null;

            // Parts: the leaves, and every node below the root that has two or more branches holding parts.
            var holdsLeaf = new HashSet<Transform>();
            foreach (var leaf in leaves)
                for (var node = leaf; node != null && node != root; node = node.parent)
                    holdsLeaf.Add(node);
            var partNodes = new HashSet<Transform>(leaves);
            foreach (var node in holdsLeaf)
            {
                var branches = leafRenderers.ContainsKey(node) ? 1 : 0;
                foreach (Transform child in node)
                    if (holdsLeaf.Contains(child))
                        branches++;
                if (branches >= 2)
                    partNodes.Add(node);
            }

            var parts = root.gameObject.AddComponent<ContentParts>();
            var rootPart = rootIsPart ? parts.Add(root, string.Empty, Pretty(root.name), null, null, isRoot: true) : null;
            foreach (var node in InHierarchyOrder(root, partNodes))
            {
                var parent = rootPart;
                for (var up = node.parent; up != null && up != root; up = up.parent)
                {
                    if (partNodes.Contains(up))
                    {
                        parent = parts.Find(up);
                        break;
                    }
                }

                var part = parts.Add(node, string.Empty, Pretty(node.name), null, parent);
                if (leafRenderers.TryGetValue(node, out var renderers))
                    parts.AddColliders(part, renderers);
            }

            parts.RefreshGrab();
            return parts;
        }

        /// <summary>
        /// Make <paramref name="node"/> a part (web scenes add their actors this way). Give it colliders with
        /// <see cref="AddColliders"/>, then call <see cref="RefreshGrab"/> once the parts are added.
        /// </summary>
        public ContentPart Add(Transform node, string id, string title, string info, ContentPart parent, bool isRoot = false)
        {
            var part = node.GetComponent<ContentPart>();
            if (part == null)
                part = node.gameObject.AddComponent<ContentPart>();
            part.Owner = this;
            part.Id = id ?? string.Empty;
            part.Title = string.IsNullOrWhiteSpace(title) ? "Part" : title;
            part.Info = info;
            part.Parent = parent;
            part.IsRoot = isRoot;
            if (!_parts.Contains(part))
                _parts.Add(part);
            return part;
        }

        /// <summary>Stop treating <paramref name="part"/> as a part (its sub-parts move up to its assembly).</summary>
        public void Remove(ContentPart part)
        {
            if (part == null || !_parts.Remove(part))
                return;
            foreach (var other in _parts)
                if (other.Parent == part)
                    other.Parent = part.Parent;
            if (_selected == part)
                Select(null);
            _colliders.RemoveAll(c => c == null || c.GetComponentInParent<ContentPart>() == part);
            var target = part.GetComponent<ContentClickTarget>();
            if (target != null)
                Destroy(target);
            Destroy(part);
            RefreshGrab();
        }

        /// <summary>
        /// Give <paramref name="part"/> colliders for <paramref name="renderers"/> (meshes are picked exactly; very large or
        /// skinned meshes by their box), so rays can click it and hands can grab it. They're on the content body layer
        /// (<see cref="ContentLayer"/>), on children of their meshes, so the meshes' own objects are left as they are.
        /// </summary>
        public void AddColliders(ContentPart part, IEnumerable<Renderer> renderers)
        {
            var list = renderers as IList<Renderer> ?? new List<Renderer>(renderers);
            var typical = TypicalSize(list);
            foreach (var renderer in list)
                if (!IsBackdrop(renderer, typical, list.Count))
                    _colliders.Add(ColliderFor(renderer, part.transform));

            var target = part.GetComponent<ContentClickTarget>();
            if (target == null)
                part.gameObject.AddComponent<ContentClickTarget>();
            else
                target.Refresh();
        }

        /// <summary>
        /// Let <paramref name="collider"/> (on the content body layer, under <paramref name="part"/>) click and grab
        /// <paramref name="part"/>: for content that draws without one renderer per part and builds its own colliders
        /// (a 4D mesh). Call <see cref="RefreshGrab"/> once they're added.
        /// </summary>
        public void AddCollider(ContentPart part, Collider collider)
        {
            if (part == null || collider == null)
                return;
            _colliders.Add(collider);
            var target = part.GetComponent<ContentClickTarget>();
            if (target == null)
                part.gameObject.AddComponent<ContentClickTarget>();
            else
                target.Refresh();
        }

        /// <summary>Stop parts being grabbed by <paramref name="renderer"/> (it isn't really part of the content: a page's ground).</summary>
        public void RemoveCollidersOf(Renderer renderer)
        {
            var removed = false;
            for (var i = _colliders.Count - 1; i >= 0; i--)
            {
                var collider = _colliders[i];
                if (collider == null || collider.transform.parent != renderer.transform)
                    continue;
                _colliders.RemoveAt(i);
                Destroy(collider.gameObject); // its "Part Body" holds only it
                removed = true;
            }

            if (removed)
                RefreshGrab();
        }

        /// <summary>The middle size (largest side, in world units) of <paramref name="renderers"/>' meshes.</summary>
        static float TypicalSize(IList<Renderer> renderers)
        {
            var sizes = new List<float>(renderers.Count);
            foreach (var renderer in renderers)
                sizes.Add(SizeOf(renderer));
            if (sizes.Count == 0)
                return 0f;
            sizes.Sort();
            return sizes[sizes.Count / 2];
        }

        static float SizeOf(Renderer renderer)
        {
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null)
                return 0f;
            var size = Vector3.Scale(mesh.bounds.size, renderer.transform.lossyScale);
            return Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }

        /// <summary>
        /// A ground or backdrop far bigger than the rest of its part (a page's ground under everything on it): it gets no
        /// collider, or a hand would touch the part everywhere and pick it up by accident.
        /// </summary>
        static bool IsBackdrop(Renderer renderer, float typical, int count) =>
            count >= 3 && typical > 0f && SizeOf(renderer) > typical * BackdropRatio;

        const float BackdropRatio = 20f;

        /// <summary>Give the grab its current colliders. Call after adding or removing parts.</summary>
        public void RefreshGrab()
        {
            _colliders.RemoveAll(c => c == null);
            if (_grab == null)
            {
                var holder = new GameObject("Part Grabs");
                holder.SetActive(false); // so the interactable registers the colliders we give it, not its children's
                holder.transform.SetParent(transform, false);
                _grab = holder.AddComponent<PartsInteractable>();
                _grab.Parts = this;
                _grab.SetColliders(_colliders);
                holder.SetActive(true);
            }
            else
            {
                _grab.SetColliders(_colliders);
            }
        }

        /// <summary>
        /// A solid collider for a part's mesh: its convex hull (a hand inside a part still touches it, which a hollow mesh
        /// collider can't do), or its box when the mesh is flat, unreadable or very large.
        /// </summary>
        static Collider ColliderFor(Renderer renderer, Transform partNode)
        {
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                var mesh = filter.sharedMesh;
                var body = ContentLayer.CreateBody(renderer.transform, "Part Body");
                var size = mesh.bounds.size;
                var flat = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) < Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.01f;
                if (mesh.isReadable && !flat && TriangleCount(mesh) <= MaxMeshColliderTriangles)
                {
                    var hull = body.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMesh = mesh;
                    return hull;
                }

                var box = body.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                var thickness = Mathf.Max(Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.02f, 0.005f);
                box.size = new Vector3(Mathf.Max(size.x, thickness), Mathf.Max(size.y, thickness), Mathf.Max(size.z, thickness));
                return box;
            }

            // Skinned (or anything else): a box around where it is now, on the part.
            var around = ContentLayer.CreateBody(partNode, "Part Body").AddComponent<BoxCollider>();
            var bounds = renderer.bounds;
            around.center = partNode.InverseTransformPoint(bounds.center);
            var scale = partNode.lossyScale;
            around.size = new Vector3(bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 1e-5f), bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 1e-5f),
                bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 1e-5f));
            return around;
        }

        static long TriangleCount(Mesh mesh)
        {
            long indices = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
                indices += mesh.GetIndexCount(i);
            return indices / 3;
        }

        // ---------------------------------------------------------------- selecting

        /// <summary>A ray click on <paramref name="clicked"/>: select it, or, when it (or an assembly holding it) is already
        /// selected, the next assembly up; past the top, nothing.</summary>
        public void Click(ContentPart clicked)
        {
            if (clicked == null || clicked.IsHeld)
                return;
            if (_selected != null && _selected.Contains(clicked))
                Select(_selected.Parent);
            else
                Select(clicked);
        }

        public void Select(ContentPart part)
        {
            if (part == _selected)
                return;
            _selected = part;
            SelectionChanged?.Invoke(part);
        }

        /// <summary>The part with this id or title (case-insensitive), or null.</summary>
        public ContentPart Find(string idOrTitle) =>
            _parts.Find(p => string.Equals(p.Id, idOrTitle, StringComparison.OrdinalIgnoreCase)) ??
            _parts.Find(p => string.Equals(p.Title, idOrTitle, StringComparison.OrdinalIgnoreCase));

        public ContentPart Find(Transform node) => _parts.Find(p => p.transform == node);

        // ---------------------------------------------------------------- moving

        /// <summary>Put <paramref name="part"/> at <paramref name="world"/> (what a grab does).</summary>
        public void MoveTo(ContentPart part, Pose world)
        {
            if (part == null)
                return;
            if (part.IsRoot)
            {
                part.transform.SetPositionAndRotation(world.position, world.rotation);
                return;
            }

            CaptureHome(part);
            var parent = part.transform.parent;
            var localPosition = parent != null ? parent.InverseTransformPoint(world.position) : world.position;
            var localRotation = parent != null ? Quaternion.Inverse(parent.rotation) * world.rotation : world.rotation;
            part.OffsetRotation = localRotation * Quaternion.Inverse(part.HomeRotation);
            part.OffsetPosition = localPosition - part.OffsetRotation * part.HomePosition;
            part.Gliding = false;
            Apply(part);
        }

        /// <summary>Glide <paramref name="part"/> and its sub-parts back to where the content puts them.</summary>
        public void PutBack(ContentPart part)
        {
            foreach (var other in _parts)
                if (part != null && part.Contains(other) && other.IsMoved && !other.IsHeld)
                    other.Gliding = true;
        }

        /// <summary>Glide every part back to where the content puts it.</summary>
        public void PutAllBack()
        {
            foreach (var part in _parts)
                if (part.IsMoved && !part.IsHeld)
                    part.Gliding = true;
        }

        /// <summary>A hand picks <paramref name="part"/> up (what a grab does; automation uses it too).</summary>
        public void BeginHold(ContentPart part)
        {
            Grabbed?.Invoke(part);
            part.IsHeld = true;
            part.Gliding = false;
            Select(part);
        }

        /// <summary>The hand lets go of <paramref name="part"/>: it stays where it is.</summary>
        public void EndHold(ContentPart part)
        {
            part.IsHeld = false;
            Released?.Invoke(part);
        }

        /// <summary>
        /// The part a hand or controller at <paramref name="interactor"/> picks up: see <see cref="PartAt"/>.
        /// </summary>
        internal ContentPart GrabTargetFor(IXRSelectInteractor interactor)
        {
            var origin = DirectGrabOnly.ReachOrigin(interactor, out var reach);
            return PartAt(origin, reach);
        }

        /// <summary>
        /// The part a hand at <paramref name="origin"/> reaching <paramref name="reach"/> metres picks up: the smallest one
        /// it touches, or the selected assembly holding that one. Null when it touches none.
        /// </summary>
        public ContentPart PartAt(Vector3 origin, float reach)
        {
            ContentPart touched = null;
            var best = float.MaxValue;
            foreach (var collider in DirectGrabOnly.Touching(origin, reach))
            {
                if (!_colliders.Contains(collider))
                    continue;
                var part = collider.GetComponentInParent<ContentPart>();
                if (part == null || part.Owner != this)
                    continue;
                var bounds = collider.bounds;
                var size = bounds.size.x * bounds.size.y * bounds.size.z + bounds.SqrDistance(origin); // smallest first, then nearest
                if (size < best)
                {
                    best = size;
                    touched = part;
                }
            }

            return touched != null && _selected != null && _selected.Contains(touched) ? _selected : touched;
        }

        // ---------------------------------------------------------------- every frame

        void Update() => RestoreHomes();

        /// <summary>
        /// Back to where the content put the moved parts last frame: parts the content doesn't animate stay there, the rest
        /// are moved again by the content before <see cref="LateApply()"/>.
        /// </summary>
        internal void RestoreHomes()
        {
            foreach (var part in _parts)
                if (part.HasHome && !part.IsRoot && part.IsMoved)
                    part.transform.SetLocalPositionAndRotation(part.HomePosition, part.HomeRotation);
        }

        /// <summary>After the content has moved its parts: remember where it put them and add the user's offsets.</summary>
        internal void LateApply() => LateApply(Time.unscaledDeltaTime);

        internal void LateApply(float deltaTime)
        {
            if (_label == null && _selected != null && Application.isPlaying)
                _label = PartLabel.Create(this);
            var glide = 1f - Mathf.Exp(-deltaTime / GlideSeconds);
            foreach (var part in _parts)
            {
                if (part == null || part.IsRoot)
                    continue;
                part.transform.GetLocalPositionAndRotation(out part.HomePosition, out part.HomeRotation);
                part.HasHome = true;
                if (part.Gliding)
                {
                    part.OffsetPosition = Vector3.Lerp(part.OffsetPosition, Vector3.zero, glide);
                    part.OffsetRotation = Quaternion.Slerp(part.OffsetRotation, Quaternion.identity, glide);
                    if (part.OffsetPosition.sqrMagnitude < 1e-8f && Quaternion.Angle(part.OffsetRotation, Quaternion.identity) < 0.05f)
                    {
                        part.OffsetPosition = Vector3.zero;
                        part.OffsetRotation = Quaternion.identity;
                        part.Gliding = false;
                    }
                }

                if (part.IsMoved)
                    Apply(part);
            }
        }

        void CaptureHome(ContentPart part)
        {
            if (part.HasHome)
                return;
            part.transform.GetLocalPositionAndRotation(out part.HomePosition, out part.HomeRotation);
            part.HasHome = true;
        }

        static void Apply(ContentPart part) =>
            part.transform.SetLocalPositionAndRotation(part.OffsetRotation * part.HomePosition + part.OffsetPosition, part.OffsetRotation * part.HomeRotation);

        void Awake() => gameObject.AddComponent<ContentPartsLate>().Parts = this;

        void OnDestroy()
        {
            if (_label != null)
                Destroy(_label.gameObject);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A node's name made readable: "Engine_Bell.001" → "Engine Bell".</summary>
        public static string Pretty(string name)
        {
            var text = Regex.Replace(name ?? string.Empty, @"(\(Clone\)|\.\d+$)", string.Empty);
            text = Regex.Replace(text.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
            return text.Length > 0 ? text : "Part";
        }

        static Transform CommonAncestor(Transform a, Transform b, Transform root)
        {
            for (var x = a; x != null; x = x.parent)
                for (var y = b; y != null; y = y.parent)
                    if (x == y)
                        return x == root || !x.IsChildOf(root) ? a : x;
            return a;
        }

        static bool HasAncestorIn(Transform leaf, Dictionary<Transform, List<Renderer>> leaves, Transform root, bool skinnedOnly)
        {
            for (var node = leaf.parent; node != null && node != root; node = node.parent)
                if (leaves.TryGetValue(node, out var renderers) && (!skinnedOnly || renderers.Exists(r => r is SkinnedMeshRenderer)))
                    return true;
            return false;
        }

        static IEnumerable<Transform> InHierarchyOrder(Transform root, HashSet<Transform> nodes)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node != root && nodes.Contains(node))
                    yield return node;
                for (var i = node.childCount - 1; i >= 0; i--)
                    stack.Push(node.GetChild(i));
            }
        }
    }

    /// <summary>Runs <see cref="ContentParts.LateApply"/> after the content (and its animation) has moved its parts.</summary>
    [DefaultExecutionOrder(900)]
    sealed class ContentPartsLate : MonoBehaviour
    {
        internal ContentParts Parts;

        void LateUpdate()
        {
            if (Parts != null)
                Parts.LateApply();
        }
    }
}
