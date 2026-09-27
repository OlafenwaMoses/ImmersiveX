using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Side handles around a piece of media: a move bar under its controls, turn bars at its left and right, and a resize
    /// corner at its top right. Select one with a controller or hand (ray or pinch) and drag:
    /// <list type="bullet">
    /// <item>Move: slides the media across the floor (inside the mapped room).</item>
    /// <item>Turn: turns it about the up axis, like a turntable.</item>
    /// <item>Resize: drag up to make it bigger, down to make it smaller.</item>
    /// </list>
    /// The handles follow the media and stay turned towards the user. Letting go saves where the media is.
    /// </summary>
    sealed class MediaHandles : MonoBehaviour
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
        const float MinHeight = 0.3f;
        const float MaxHeight = 4f;

        ImmersiveMedia _media;
        Transform _frame;
        Handle _move;
        Handle _turnLeft;
        Handle _turnRight;
        Handle _resize;

        IXRSelectInteractor _interactor;
        Handle _active;
        bool _direct;
        Plane _plane;
        Vector3 _startPoint;
        Vector3 _startOrigin;
        Pose _startPose;
        float _startHeight;
        Vector3 _layoutSize = new Vector3(0.6f, 1.6f, 0.6f);
        Vector3 _layoutCentre = new Vector3(0f, 0.8f, 0f);

        public bool IsDragging => _active != null;

        public static MediaHandles Create(ImmersiveMedia media)
        {
            var handles = new GameObject($"{media.name} Handles").AddComponent<MediaHandles>();
            handles._media = media;
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

            var interactable = root.AddComponent<XRSimpleInteractable>();
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
            var media = _media.transform;
            _startPose = new Pose(media.position, media.rotation);
            _startHeight = _media.Height;
            _startOrigin = interactor.transform.position;
            _direct = Vector3.Distance(_startOrigin, handle.Transform.position) < DirectReach;

            var grabPoint = handle.Transform.position;
            switch (handle.Kind)
            {
                case Kind.Move:
                case Kind.Turn:
                    _plane = new Plane(Vector3.up, grabPoint); // drag across a level plane at the handle's height
                    break;
                case Kind.Resize:
                    var towardUser = Vector3.ProjectOnPlane(_startOrigin - media.position, Vector3.up);
                    _plane = new Plane(towardUser.sqrMagnitude > 1e-4f ? towardUser.normalized : -media.forward, grabPoint);
                    break;
            }

            _startPoint = PointerPoint(out var hit) && hit ? _lastPoint : grabPoint;
        }

        Vector3 _lastPoint;

        /// <summary>Where the interactor points on the drag plane (ray), or where the hand is (direct).</summary>
        bool PointerPoint(out bool hit)
        {
            hit = false;
            if (_interactor == null)
                return false;
            var origin = _interactor.transform.position;
            if (_direct)
            {
                _lastPoint = _plane.ClosestPointOnPlane(origin);
                hit = true;
                return true;
            }

            var ray = new Ray(origin, _interactor.transform.forward);
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
            _media.SavePlacement();
        }

        void Drag()
        {
            if (!PointerPoint(out var hit) || !hit)
                return;
            var point = _lastPoint;
            switch (_active.Kind)
            {
                case Kind.Move:
                {
                    var delta = Vector3.ProjectOnPlane(point - _startPoint, Vector3.up);
                    _media.MoveTo(_startPose.position + delta);
                    break;
                }

                case Kind.Turn:
                {
                    var centre = _startPose.position;
                    var from = Vector3.ProjectOnPlane(_startPoint - centre, Vector3.up);
                    var to = Vector3.ProjectOnPlane(point - centre, Vector3.up);
                    if (from.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f)
                        _media.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, Vector3.up), Vector3.up) * _startPose.rotation;
                    break;
                }

                case Kind.Resize:
                {
                    var floor = _startPose.position.y;
                    var factor = (point.y - floor) / Mathf.Max(_startPoint.y - floor, 0.05f);
                    _media.Height = Mathf.Clamp(_startHeight * factor, MinHeight, MaxHeight);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- layout

        void LateUpdate()
        {
            if (_media == null)
            {
                Destroy(gameObject);
                return;
            }

            if (_active != null)
                Drag();

            // Follow the media; stay turned towards the user (the handles don't spin with the media).
            var viewer = ImmersiveMedia.ViewerTransform();
            var media = _media.transform;
            _frame.position = media.position;
            if (viewer != null && _active == null)
            {
                var towardUser = Vector3.ProjectOnPlane(viewer.position - media.position, Vector3.up);
                if (towardUser.sqrMagnitude > 1e-4f)
                    _frame.rotation = Quaternion.LookRotation(-towardUser.normalized, Vector3.up);
            }

            if (_active == null)
            {
                var bounds = _media.WorldBounds;
                _layoutSize = Vector3.Lerp(_layoutSize, bounds.size, 0.2f);
                _layoutCentre = Vector3.Lerp(_layoutCentre, bounds.center - media.position, 0.2f);
            }

            var halfWidth = Mathf.Clamp(Mathf.Max(_layoutSize.x, _layoutSize.z) * 0.5f, 0.2f, 2.5f);
            var top = _layoutCentre.y + _layoutSize.y * 0.5f;
            var middle = _layoutCentre.y;
            _turnLeft.Transform.localPosition = new Vector3(-(halfWidth + 0.14f), middle, 0f);
            _turnRight.Transform.localPosition = new Vector3(halfWidth + 0.14f, middle, 0f);
            _resize.Transform.localPosition = new Vector3(halfWidth + 0.12f, top + 0.08f, 0f);

            // The move bar sits just under the media's controls (like the bar under a Quest window).
            var controls = _media.Controls;
            if (controls != null)
            {
                _move.Transform.SetPositionAndRotation(controls.transform.position - controls.transform.up * 0.115f, controls.transform.rotation);
            }
            else
            {
                _move.Transform.localPosition = new Vector3(0f, 0.08f, -(halfWidth + 0.25f));
                _move.Transform.localRotation = Quaternion.identity;
            }
        }

        void OnDestroy()
        {
            foreach (var handle in new[] { _move, _turnLeft, _turnRight, _resize })
                if (handle?.Material != null)
                    Destroy(handle.Material);
        }
    }
}
