using UnityEngine;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// The label for the selected part of a <see cref="ContentParts"/>: its name after the assemblies it belongs to, a line
    /// about it, and Put back, Put all back and close buttons, which the ray clicks and a finger presses. Corner brackets
    /// mark what's selected.
    /// <para>It shows over the part when that's within a finger's reach (or the part is being held). Otherwise it comes to
    /// the user, like the content menu: half a metre in front of them towards the part, a little below the eyes, with a line
    /// to the part. There it stays put while they reach for it.</para>
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(950)] // after the parts are placed for the frame
    sealed class PartLabel : MonoBehaviour
    {
        const float Width = 400f;
        const float Height = 136f;
        const float RefreshSeconds = 0.2f;
        const float LineWidth = 0.004f;
        const float Reach = 0.6f; // a finger can press it this far from the eyes
        const float NearDistance = 0.5f; // out of reach, it shows this far in front of the user…
        const float BelowEyes = 0.12f; // …this far below their eyes…
        const float StayWithin = 0.9f; // …and stays there while they're this close to it…
        const float StayAngle = 70f; // …and it's this near where they look

        static readonly Color BracketColor = new Color(0.45f, 0.82f, 1f);

        ContentParts _parts;
        GameObject _panel;
        Text _title;
        Text _info;
        Button _putBack;
        Button _putAll;
        readonly LineRenderer[] _brackets = new LineRenderer[16];
        LineRenderer _leader;
        Material _bracketMaterial;
        ContentPart _shown;
        ContentPart _placedFor;
        bool _near;
        float _nextRefresh;

        public static PartLabel Create(ContentParts parts)
        {
            var label = new GameObject("Part Label").AddComponent<PartLabel>();
            label._parts = parts;
            label.Build();
            return label;
        }

        void Build()
        {
            var root = WorldUi.CreateCanvas(transform, new Vector2(Width, Height));
            _panel = root.gameObject;
            _title = WorldUi.Label(root, "Title", string.Empty, 24, new Vector2(-22f, 40f), new Vector2(Width - 76f, 40f), FontStyle.Normal, TextAnchor.MiddleLeft);
            _title.supportRichText = true;
            _info = WorldUi.Label(root, "Info", string.Empty, 18, new Vector2(0f, 4f), new Vector2(Width - 32f, 30f), FontStyle.Normal, TextAnchor.MiddleLeft);
            _putBack = WorldUi.Button(root, "Put back", new Vector2(-96f, -42f), new Vector2(176f, 40f), () => _parts.PutBack(_parts.Selected));
            _putAll = WorldUi.Button(root, "Put all back", new Vector2(96f, -42f), new Vector2(176f, 40f), () => _parts.PutAllBack());
            WorldUi.Button(root, "×", new Vector2(Width * 0.5f - 30f, Height * 0.5f - 28f), new Vector2(40f, 40f), () => _parts.Select(null));

            _bracketMaterial = RuntimeMaterials.Create(BracketColor);
            for (var i = 0; i < _brackets.Length; i++)
                _brackets[i] = Line("Bracket");
            _leader = Line("Leader");
            _leader.positionCount = 2;
        }

        LineRenderer Line(string name)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = _bracketMaterial;
            line.widthMultiplier = LineWidth;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            return line;
        }

        void LateUpdate()
        {
            if (_parts == null)
            {
                Destroy(gameObject);
                return;
            }

            var part = _parts.Selected;
            var shown = part != null && part.isActiveAndEnabled && TryBounds(part, out _);
            if (_panel.activeSelf != shown)
                SetShown(shown);
            if (!shown)
            {
                _shown = null;
                _placedFor = null;
                return;
            }

            TryBounds(part, out var bounds);
            Place(part, bounds);
            DrawBrackets(bounds);
            DrawLeader(bounds);
            if (part != _shown || Time.unscaledTime >= _nextRefresh)
            {
                _shown = part;
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh(part);
            }
        }

        void SetShown(bool shown)
        {
            _panel.SetActive(shown);
            foreach (var line in _brackets)
                line.enabled = shown;
            _leader.enabled = shown && _near;
        }

        void Refresh(ContentPart part)
        {
            _title.text = part.Parent != null && !part.Parent.IsRoot
                ? $"{Escape(part.Parent.Trail)} › <b>{Escape(part.Title)}</b>"
                : $"<b>{Escape(part.Title)}</b>"; // the root object (a Unity object's whole) isn't named in trails
            string hint;
            if (!string.IsNullOrWhiteSpace(part.Info))
                hint = part.Info;
            else if (part.IsHeld)
                hint = "Let go to leave it here.";
            else
                hint = "Grab it with your hand to move it" + (part.Parent != null ? $" · click again for {part.Parent.Title}" : string.Empty) + ".";
            _info.text = hint;

            var anyMoved = _parts.AnyMoved;
            var thisMoved = false;
            foreach (var other in _parts.Parts)
                thisMoved |= part.Contains(other) && other.IsMoved;
            SetEnabled(_putBack, thisMoved);
            SetEnabled(_putAll, anyMoved);
        }

        static void SetEnabled(Button button, bool enabled)
        {
            if (button.interactable == enabled)
                return;
            button.interactable = enabled;
            button.GetComponentInChildren<Text>().color = enabled ? WorldUi.TextColor : new Color(WorldUi.TextColor.r, WorldUi.TextColor.g, WorldUi.TextColor.b, 0.35f);
        }

        void Place(ContentPart part, Bounds bounds)
        {
            var viewer = Viewer();
            var eye = viewer != null ? viewer.position : bounds.center + Vector3.back * 2f;
            var forward = viewer != null ? viewer.forward : Vector3.forward;
            var current = _near && part == _placedFor ? new Pose(transform.position, transform.rotation) : (Pose?)null;
            var pose = Placement(bounds, eye, forward, part.IsHeld, current, out _near);
            _placedFor = part;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>
        /// Where the label goes, facing the user: above the part on its side facing them, at most a little above their eyes,
        /// when that's within a finger's reach or the part is <paramref name="held"/>. Otherwise (<paramref name="near"/>) half a
        /// metre in front of them towards the part, a little below their eyes; it stays where it is (<paramref name="current"/>)
        /// while they're near it and looking its way, so it doesn't move while they reach for it.
        /// </summary>
        internal static Pose Placement(Bounds bounds, Vector3 eye, Vector3 forward, bool held, Pose? current, out bool near)
        {
            var toward = Vector3.ProjectOnPlane(eye - bounds.center, Vector3.up);
            toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.back;
            var front = Mathf.Min(Mathf.Max(bounds.extents.x, bounds.extents.z), 1f);
            var atPart = bounds.center + toward * front;
            atPart.y = Mathf.Min(bounds.max.y + 0.12f, eye.y + 0.15f);
            near = !held && Vector3.Distance(atPart, eye) > Reach;
            if (!near)
                return new Pose(atPart, Quaternion.LookRotation(-toward, Vector3.up));

            if (current.HasValue && Vector3.Distance(current.Value.position, eye) <= StayWithin &&
                Vector3.Angle(forward, current.Value.position - eye) <= StayAngle)
                return current.Value;

            var away = Vector3.ProjectOnPlane(atPart - eye, Vector3.up);
            if (away.sqrMagnitude < 1e-4f)
                away = Vector3.ProjectOnPlane(forward, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            var position = eye + away * NearDistance;
            position.y = eye.y - BelowEyes;
            return new Pose(position, Quaternion.LookRotation(away, Vector3.up));
        }

        /// <summary>A line from the label down to the part, while the label is in front of the user rather than at the part.</summary>
        void DrawLeader(Bounds bounds)
        {
            _leader.enabled = _near;
            if (!_near)
                return;
            var from = transform.position - transform.up * (Height * 0.5f * 0.001f); // the label's bottom edge (the canvas is in millimetres)
            _leader.SetPosition(0, from);
            _leader.SetPosition(1, bounds.ClosestPoint(from));
        }

        /// <summary>Corner brackets around <paramref name="bounds"/>: two arms on one line and the third on another, per corner.</summary>
        void DrawBrackets(Bounds bounds)
        {
            var size = bounds.size;
            var arm = Mathf.Clamp(Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.25f, 0.02f, 0.12f);
            var line = 0;
            for (var corner = 0; corner < 8; corner++)
            {
                var sx = (corner & 1) == 0 ? -1f : 1f;
                var sy = (corner & 2) == 0 ? -1f : 1f;
                var sz = (corner & 4) == 0 ? -1f : 1f;
                var at = bounds.center + Vector3.Scale(bounds.extents, new Vector3(sx, sy, sz));
                var first = _brackets[line++];
                first.positionCount = 3;
                first.SetPosition(0, at - new Vector3(sx * arm, 0f, 0f));
                first.SetPosition(1, at);
                first.SetPosition(2, at - new Vector3(0f, sy * arm, 0f));
                var second = _brackets[line++];
                second.positionCount = 2;
                second.SetPosition(0, at);
                second.SetPosition(1, at - new Vector3(0f, 0f, sz * arm));
            }
        }

        /// <summary>The world bounds of the part's meshes (sub-parts included).</summary>
        static bool TryBounds(ContentPart part, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in part.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is LineRenderer)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        static Transform Viewer()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                return session.Origin.Camera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        static string Escape(string text) => (text ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        void OnDestroy()
        {
            if (_bracketMaterial != null)
                Destroy(_bracketMaterial);
        }
    }
}
