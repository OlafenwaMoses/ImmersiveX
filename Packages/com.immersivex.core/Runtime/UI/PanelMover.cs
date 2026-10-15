using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX
{
    /// <summary>
    /// Moves a world-space panel without letting the ray drag it by its face:
    /// <list type="bullet">
    /// <item>Anchors on its left and right sides: the ray or a hand drags them.</item>
    /// <item>Its frame, just outside its edges: a hand grabs it. The ray passes through the frame.</item>
    /// <item>Its face has nothing to grab: the ray only clicks its buttons, a finger presses them.</item>
    /// </list>
    /// While held it stays upright and turns to face the user; it stays where it's let go.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class PanelMover : MonoBehaviour
    {
        static readonly Color Idle = new Color(0.55f, 0.47f, 0.3f);   // a muted gold at rest
        static readonly Color Hover = new Color(0.91f, 0.765f, 0.416f); // --gold-300 under the ray or a hand

        /// <summary>How far outside the panel's edges the anchors and frame sit, in metres (outside its <see cref="PanelZones"/> zone).</summary>
        const float Outside = 0.045f;

        Vector2 _size;
        XRGrabInteractable _grab;
        Material _material;

        /// <summary>Whether a hand or the ray is moving the panel now.</summary>
        public bool IsHeld => _grab != null && _grab.isSelected;

        /// <summary>Make <paramref name="panel"/> movable; <paramref name="sizeMetres"/> is its visible size.</summary>
        public static PanelMover Attach(GameObject panel, Vector2 sizeMetres)
        {
            var mover = panel.GetComponent<PanelMover>();
            if (mover != null)
                return mover;
            mover = panel.AddComponent<PanelMover>();
            mover._size = sizeMetres;
            mover.Build();
            return mover;
        }

        void Build()
        {
            _material = RuntimeMaterials.Create(Idle);
            var halfWidth = _size.x * 0.5f;
            var halfHeight = _size.y * 0.5f;
            var anchorLength = Mathf.Clamp(_size.y * 0.45f, 0.08f, 0.24f);

            // The anchors: visible bars on both sides, on the default layer so the ray can take them.
            foreach (var side in new[] { -1f, 1f })
            {
                var anchor = new GameObject(side < 0f ? "Anchor (left)" : "Anchor (right)");
                anchor.transform.SetParent(transform, false);
                anchor.transform.localPosition = new Vector3(side * (halfWidth + Outside), 0f, 0f);
                var bar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(bar.GetComponent<Collider>());
                bar.name = "Bar";
                bar.transform.SetParent(anchor.transform, false);
                bar.transform.localScale = new Vector3(0.012f, anchorLength * 0.5f, 0.012f);
                var renderer = bar.GetComponent<Renderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var zone = anchor.AddComponent<BoxCollider>();
                zone.size = new Vector3(0.07f, anchorLength + 0.06f, 0.07f);
            }

            // The frame: top and bottom edges on the body layer, so hands reach them and the ray passes through.
            foreach (var side in new[] { -1f, 1f })
            {
                var edge = ContentLayer.CreateBody(transform, side < 0f ? "Frame (bottom)" : "Frame (top)");
                edge.transform.localPosition = new Vector3(0f, side * (halfHeight + Outside), 0f);
                edge.AddComponent<BoxCollider>().size = new Vector3(_size.x + 2f * Outside, 0.04f, 0.05f);
            }

            var body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            _grab = gameObject.AddComponent<XRGrabInteractable>(); // takes the anchors' and the frame's colliders
            _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            _grab.throwOnDetach = false;
            _grab.trackRotation = false; // kept upright and facing the user instead
            _grab.useDynamicAttach = true;
            PanelZoneFilter.Apply(_grab); // a hand at the panel's buttons presses them; it doesn't grab the frame beside them
            _grab.hoverEntered.AddListener(_ => _material.color = Hover);
            _grab.hoverExited.AddListener(_ => _material.color = _grab.isSelected ? Hover : Idle);
            _grab.selectExited.AddListener(_ => _material.color = Idle);
        }

        void LateUpdate()
        {
            if (_grab == null || !_grab.isSelected)
                return;
            var session = ImmersiveXSession.Instance;
            var head = session != null && session.Origin != null && session.Origin.Camera != null
                ? session.Origin.Camera.transform
                : Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return;
            var fromUser = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
            if (fromUser.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(fromUser.normalized, Vector3.up);
        }

        void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
