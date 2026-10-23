using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>What <see cref="ContentHandles"/> move, turn and resize: a whole piece of content (never one of its parts).</summary>
    public interface IHandleTarget
    {
        /// <summary>The object that moves and turns.</summary>
        Transform Root { get; }

        /// <summary>The visible content's bounds in world space.</summary>
        Bounds WorldBounds { get; }

        /// <summary>The content's height in metres. Setting it resizes the content (the target keeps it within its limits).</summary>
        float Height { get; set; }

        /// <summary>The controls panel the move bar sits under, or null. It may be away from the content, by the user.</summary>
        Transform Controls { get; }

        /// <summary>Move the content to <paramref name="position"/>, where it's allowed to be.</summary>
        void MoveTo(Vector3 position);

        /// <summary>Remember where the content is now (called when a handle is let go).</summary>
        void SavePlacement();
    }

    /// <summary>
    /// Handles around a piece of content: a move bar (under its controls when it has them), turn bars at its left and right,
    /// a turn ring around its base and a resize corner at its top right. They're controls, so the ray from a hand or
    /// controller can use them as well as a hand at them (the content's body is only grabbed by hand, see
    /// <see cref="DirectGrabOnly"/>). Select one and drag:
    /// <list type="bullet">
    /// <item>Move: slides the content across a level plane (the target keeps standing content inside the mapped room). The
    /// bar sits under the content's controls, which stay by the user, so the content moves from within reach; a hand on
    /// the bar carries it along while it drags.</item>
    /// <item>Turn (bars or ring): turns it about the up axis through its middle, like a turntable.</item>
    /// <item>Resize: drag up to make it bigger, down to make it smaller.</item>
    /// </list>
    /// The handles follow the content and stay turned towards the user. Letting go saves where the content is.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ContentHandles : MonoBehaviour
    {
        enum Kind
        {
            Move,
            Turn,
            Resize,
        }

        sealed class Handle
        {
            public Kind Kind;
            public Transform Transform;
            public Material Material;
        }

        static readonly Color Idle = new Color(0.86f, 0.9f, 0.98f);
        static readonly Color Hover = new Color(0.45f, 0.75f, 1f);
        const float DirectReach = 0.3f;
        const float MoveBarBelowControls = 0.17f;
        const float MoveBarClearance = 0.075f; // under the panel's zone (its edge + PanelZones.Margin)
        const int RingSegments = 48;
        const float RingWidth = 0.022f;

        IHandleTarget _target;
        Transform _frame;
        Handle _move;
        Handle _turnLeft;
        Handle _turnRight;
        Handle _ring;
        Handle _resize;
        Mesh _ringMesh;

        IXRSelectInteractor _interactor;
        Handle _active;
        bool _direct;
        Plane _plane;
        Vector3 _startPoint;
        Vector3 _startOrigin;
        Pose _startPose;
        Vector3 _startCentre;
        float _startBottom;
        float _startHeight;
        Vector3 _lastPoint;
        Vector3 _moveBarStart;
        RectTransform _controlsZone;
        Transform _controlsSeen;
        Vector3 _layoutSize = new Vector3(0.6f, 1.6f, 0.6f);
        Vector3 _layoutCentre = new Vector3(0f, 0.8f, 0f);

        public bool IsDragging => _active != null;

        public static ContentHandles Create(IHandleTarget target)
        {
            var handles = new GameObject($"{target.Root.name} Handles").AddComponent<ContentHandles>();
            handles._target = target;
            handles.Build();
            return handles;
        }

        void Build()
        {
            _frame = transform;
            _move = CreateHandle(Kind.Move, "Move Bar", new Vector3(0.36f, 0.018f, 0.018f), horizontal: true);
            _turnLeft = CreateHandle(Kind.Turn, "Turn Bar (left)", new Vector3(0.022f, 0.34f, 0.022f), horizontal: false);
            _turnRight = CreateHandle(Kind.Turn, "Turn Bar (right)", new Vector3(0.022f, 0.34f, 0.022f), horizontal: false);
            _resize = CreateHandle(Kind.Resize, "Resize Corner", new Vector3(0.05f, 0.05f, 0.05f), horizontal: false, corner: true);
            _ring = CreateRing();
        }

        Handle CreateHandle(Kind kind, string name, Vector3 size, bool horizontal, bool corner = false)
        {
            var root = new GameObject(name);
            root.transform.SetParent(_frame, false);
            var material = RuntimeMaterials.Create(Idle);

            if (corner)
            {
                // An L of two short bars: the universal "resize" corner.
                AddBar(root.transform, new Vector3(0f, -0.045f, 0f), new Vector3(0.018f, 0.1f, 0.018f), material, vertical: true);
                AddBar(root.transform, new Vector3(-0.045f, 0f, 0f), new Vector3(0.018f, 0.1f, 0.018f), material, vertical: false);
            }
            else
            {
                AddBar(root.transform, Vector3.zero, size, material, vertical: !horizontal);
            }

            // A generous invisible grab zone around the visible bar.
            var zone = root.AddComponent<BoxCollider>();
            zone.size = corner ? new Vector3(0.16f, 0.16f, 0.1f) : horizontal ? new Vector3(size.x + 0.08f, 0.08f, 0.08f) : new Vector3(0.08f, size.y + 0.08f, 0.08f);
            zone.center = corner ? new Vector3(-0.03f, -0.03f, 0f) : Vector3.zero;
            return Interactive(root, kind, material);
        }

        /// <summary>A flat ring with four arrowheads, lying around the content's base; its grab zone is a ring of boxes.</summary>
        Handle CreateRing()
        {
            var root = new GameObject("Turn Ring");
            root.transform.SetParent(_frame, false);
            var material = RuntimeMaterials.Create(Idle);
            _ringMesh = RingMesh();
            root.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (var i = 0; i < 24; i++)
            {
                var zone = new GameObject("Grab Zone").AddComponent<BoxCollider>();
                zone.transform.SetParent(root.transform, false);
                zone.transform.localRotation = Quaternion.AngleAxis(i * 15f, Vector3.up);
                zone.center = new Vector3(0f, 0f, 1f);
                zone.size = new Vector3(0.28f, 0.06f, 0.1f); // scaled with the ring's radius in x and z
            }

            return Interactive(root, Kind.Turn, material);
        }

        Handle Interactive(GameObject root, Kind kind, Material material)
        {
            var interactable = root.AddComponent<XRSimpleInteractable>();
            PanelZoneFilter.Apply(interactable); // a hand at a panel presses the panel, not the handle beside it
            var handle = new Handle { Kind = kind, Transform = root.transform, Material = material };
            interactable.hoverEntered.AddListener(_ => material.color = Hover);
            interactable.hoverExited.AddListener(_ =>
            {
                if (_active != handle)
                    material.color = Idle;
            });
            interactable.selectEntered.AddListener(args => BeginDrag(handle, args.interactorObject));
            interactable.selectExited.AddListener(_ => EndDrag(handle));
            return handle;
        }

        /// <summary>A unit-radius flat ring (scaled to the content's size) with four arrowheads pointing the way it turns.</summary>
        static Mesh RingMesh()
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            for (var i = 0; i <= RingSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / RingSegments;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices.Add(direction * (1f - RingWidth));
                vertices.Add(direction * (1f + RingWidth));
                if (i == RingSegments)
                    continue;
                var at = i * 2;
                triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
            }

            for (var arrow = 0; arrow < 4; arrow++)
            {
                var angle = (arrow * 90f + 45f) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var along = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle)); // clockwise from above
                var at = vertices.Count;
                vertices.Add(radial * (1f - RingWidth * 3.5f));
                vertices.Add(radial * (1f + RingWidth * 3.5f));
                vertices.Add(radial + along * RingWidth * 4f);
                triangles.AddRange(new[] { at, at + 2, at + 1 });
            }

            var mesh = new Mesh { name = "Turn Ring" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddBar(Transform parent, Vector3 position, Vector3 size, Material material, bool vertical)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(bar.GetComponent<Collider>());
            bar.name = "Bar";
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = position;
            bar.transform.localRotation = vertical ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
            var length = vertical ? size.y : size.x;
            var thickness = vertical ? size.x : size.y;
            bar.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);
            var renderer = bar.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- dragging

        void BeginDrag(Handle handle, IXRSelectInteractor interactor)
        {
            _active = handle;
            _interactor = interactor;
            handle.Material.color = Hover;
            var root = _target.Root;
            _startPose = new Pose(root.position, root.rotation);
            var bounds = _target.WorldBounds;
            _startCentre = bounds.center;
            _startBottom = bounds.min.y;
            _startHeight = _target.Height;
            _startOrigin = HandPose.Position(interactor);
            _direct = Vector3.Distance(_startOrigin, ClosestPoint(handle)) < DirectReach;

            var grabPoint = ClosestPoint(handle);
            switch (handle.Kind)
            {
                case Kind.Move:
                case Kind.Turn:
                    _plane = new Plane(Vector3.up, grabPoint); // drag across a level plane at the handle's height
                    break;
                case Kind.Resize:
                    var towardUser = Vector3.ProjectOnPlane(_startOrigin - root.position, Vector3.up);
                    _plane = new Plane(towardUser.sqrMagnitude > 1e-4f ? towardUser.normalized : -root.forward, grabPoint);
                    break;
            }

            _startPoint = PointerPoint(out var hit) && hit ? _lastPoint : grabPoint;
            _moveBarStart = _move.Transform.position;
        }

        /// <summary>The point of the handle nearest the interactor (the ring is big; the bars are small).</summary>
        Vector3 ClosestPoint(Handle handle)
        {
            if (handle != _ring || _interactor == null)
                return handle.Transform.position;
            var centre = handle.Transform.position;
            var flat = Vector3.ProjectOnPlane(HandPose.Position(_interactor) - centre, Vector3.up);
            var radius = handle.Transform.lossyScale.x;
            return centre + (flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward) * radius;
        }

        /// <summary>
        /// Where the interactor points on the drag plane (ray), or where the hand is (direct). The tracked poses, not the
        /// interactor's transform: on hands that sits on a container that doesn't move (see <see cref="HandPose"/>).
        /// </summary>
        bool PointerPoint(out bool hit)
        {
            hit = false;
            if (_interactor == null)
                return false;
            if (_direct)
            {
                _lastPoint = _plane.ClosestPointOnPlane(HandPose.Position(_interactor));
                hit = true;
                return true;
            }

            var ray = HandPose.Pointer(_interactor);
            if (_plane.Raycast(ray, out var distance) && distance < 30f)
            {
                _lastPoint = ray.GetPoint(distance);
                hit = true;
            }

            return true;
        }

        void EndDrag(Handle handle)
        {
            if (_active != handle)
                return;
            _active = null;
            _interactor = null;
            handle.Material.color = Idle;
            _target.SavePlacement();
        }

        void Drag()
        {
            if (!PointerPoint(out var hit) || !hit)
                return;
            var point = _lastPoint;
            var root = _target.Root;
            switch (_active.Kind)
            {
                case Kind.Move:
                {
                    var delta = Vector3.ProjectOnPlane(point - _startPoint, Vector3.up);
                    _target.MoveTo(_startPose.position + delta);
                    break;
                }

                case Kind.Turn:
                {
                    // About the vertical axis through the content's middle, so it turns in place.
                    var from = Vector3.ProjectOnPlane(_startPoint - _startCentre, Vector3.up);
                    var to = Vector3.ProjectOnPlane(point - _startCentre, Vector3.up);
                    if (from.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f)
                    {
                        var turn = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, Vector3.up), Vector3.up);
                        var pivot = new Vector3(_startCentre.x, _startPose.position.y, _startCentre.z);
                        root.SetPositionAndRotation(pivot + turn * (_startPose.position - pivot), turn * _startPose.rotation);
                    }

                    break;
                }

                case Kind.Resize:
                {
                    var factor = (point.y - _startBottom) / Mathf.Max(_startPoint.y - _startBottom, 0.05f);
                    _target.Height = _startHeight * Mathf.Max(factor, 0.05f);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- layout

        void LateUpdate()
        {
            if (_target == null || _target.Root == null)
            {
                Destroy(gameObject);
                return;
            }

            if (_active != null)
                Drag();

            // Follow the content; stay turned towards the user (the handles don't spin with the content).
            var viewer = Viewer();
            var root = _target.Root;
            if (_active == null)
            {
                var bounds = _target.WorldBounds;
                _layoutSize = Vector3.Lerp(_layoutSize, bounds.size, 0.2f);
                _layoutCentre = Vector3.Lerp(_layoutCentre, bounds.center - root.position, 0.2f);
            }

            _frame.position = root.position + _layoutCentre;
            if (viewer != null && _active == null)
            {
                var towardUser = Vector3.ProjectOnPlane(viewer.position - _frame.position, Vector3.up);
                if (towardUser.sqrMagnitude > 1e-4f)
                    _frame.rotation = Quaternion.LookRotation(-towardUser.normalized, Vector3.up);
            }

            var halfWidth = Mathf.Clamp(Mathf.Max(_layoutSize.x, _layoutSize.z) * 0.5f, 0.2f, 2.5f);
            var halfHeight = _layoutSize.y * 0.5f;
            _turnLeft.Transform.localPosition = new Vector3(-(halfWidth + 0.14f), 0f, 0f);
            _turnRight.Transform.localPosition = new Vector3(halfWidth + 0.14f, 0f, 0f);
            _resize.Transform.localPosition = new Vector3(halfWidth + 0.12f, halfHeight + 0.08f, 0f);
            var radius = halfWidth + 0.12f;
            _ring.Transform.localPosition = new Vector3(0f, -halfHeight + 0.015f, 0f);
            _ring.Transform.localScale = new Vector3(radius, 1f, radius);

            // The move bar sits under the content's controls (like the bar under a Quest window), just outside the panel's
            // zone, so a hand at the panel's buttons never reaches it. Dragged by hand, it goes with the hand.
            var controls = _target.Controls;
            if (_active == _move && _direct)
            {
                _move.Transform.position = _moveBarStart + Vector3.ProjectOnPlane(_lastPoint - _startPoint, Vector3.up);
            }
            else if (controls != null && controls.gameObject.activeInHierarchy)
            {
                _move.Transform.SetPositionAndRotation(controls.position - controls.up * BelowControls(controls), controls.rotation);
            }
            else
            {
                _move.Transform.localPosition = new Vector3(0f, -halfHeight + 0.08f, -(halfWidth + 0.25f));
                _move.Transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>How far under the middle of <paramref name="controls"/> the move bar goes: clear of its zone, whatever its size.</summary>
        float BelowControls(Transform controls)
        {
            if (_controlsSeen != controls)
            {
                _controlsSeen = controls;
                var canvas = controls.GetComponentInChildren<Canvas>();
                _controlsZone = canvas != null ? canvas.transform as RectTransform : null;
            }

            return _controlsZone != null
                ? _controlsZone.rect.height * 0.5f * _controlsZone.lossyScale.y + PanelZones.Margin + MoveBarClearance
                : MoveBarBelowControls;
        }

        static Transform Viewer()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                return session.Origin.Camera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        void OnDestroy()
        {
            foreach (var handle in new[] { _move, _turnLeft, _turnRight, _ring, _resize })
                if (handle?.Material != null)
                    Destroy(handle.Material);
            if (_ringMesh != null)
                Destroy(_ringMesh);
        }
    }
}
