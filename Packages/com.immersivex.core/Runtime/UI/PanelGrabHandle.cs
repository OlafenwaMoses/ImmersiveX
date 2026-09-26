using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX
{
    /// <summary>
    /// Adds a grab bar under a world-space panel, like the bar under Quest's own windows. Grab the bar with a hand
    /// or controller to move the panel; the panel's buttons stay pressable because only the bar can be grabbed.
    /// While held, the panel stays upright and turns to face the user.
    /// </summary>
    /// <remarks>Add it from code with <see cref="Attach"/>, or put it on a panel root and set its size in metres.</remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/UI/Panel Grab Handle")]
    public sealed class PanelGrabHandle : MonoBehaviour
    {
        static readonly Color Idle = new Color(0.85f, 0.9f, 1f, 0.75f);
        static readonly Color Hover = new Color(0.45f, 0.75f, 1f, 1f);

        [SerializeField, Tooltip("Width and height of the panel in metres. The bar sits just below it.")]
        Vector2 _panelSize = new Vector2(0.5f, 0.6f);

        [SerializeField, Tooltip("Gap between the bottom of the panel and the bar, in metres.")]
        float _gap = 0.035f;

        XRGrabInteractable _grab;
        Renderer _barRenderer;
        bool _built;

        /// <summary>Add a grab bar to <paramref name="panel"/>, whose visible size is <paramref name="panelSizeMetres"/>.</summary>
        public static PanelGrabHandle Attach(GameObject panel, Vector2 panelSizeMetres)
        {
            var handle = panel.GetComponent<PanelGrabHandle>();
            if (handle == null)
                handle = panel.AddComponent<PanelGrabHandle>();
            handle._panelSize = panelSizeMetres;
            handle.Build();
            return handle;
        }

        void Start() => Build();

        void Build()
        {
            if (_built)
                return;
            _built = true;

            // The bar: a slim capsule with a generous invisible box around it, so it's easy to grab.
            var bar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bar.name = "Grab Bar";
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(transform, false);
            bar.transform.localPosition = new Vector3(0f, -(_panelSize.y * 0.5f + _gap), 0f);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            bar.transform.localScale = new Vector3(0.014f, _panelSize.x * 0.22f, 0.014f);
            _barRenderer = bar.GetComponent<Renderer>();
            _barRenderer.material.color = Idle;

            var grabZone = new GameObject("Grab Zone").AddComponent<BoxCollider>();
            grabZone.transform.SetParent(transform, false);
            grabZone.transform.localPosition = bar.transform.localPosition;
            grabZone.size = new Vector3(_panelSize.x * 0.6f, 0.06f, 0.06f);

            // Only the grab zone has a collider, so grabbing moves the whole panel but never blocks its buttons.
            var body = gameObject.GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            _grab = gameObject.GetComponent<XRGrabInteractable>();
            if (_grab == null)
                _grab = gameObject.AddComponent<XRGrabInteractable>();
            _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            _grab.throwOnDetach = false;
            _grab.trackRotation = false; // we keep it upright and facing the user instead
            _grab.useDynamicAttach = true;
            _grab.hoverEntered.AddListener(_ => _barRenderer.material.color = Hover);
            _grab.hoverExited.AddListener(_ => _barRenderer.material.color = _grab.isSelected ? Hover : Idle);
            _grab.selectExited.AddListener(_ => _barRenderer.material.color = Idle);
        }

        void LateUpdate()
        {
            if (_grab == null || !_grab.isSelected)
                return;

            var head = HeadTransform();
            if (head == null)
                return;

            var fromUser = transform.position - head.position;
            fromUser.y = 0f;
            if (fromUser.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(fromUser.normalized, Vector3.up);
        }

        static Transform HeadTransform()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                return session.Origin.Camera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
