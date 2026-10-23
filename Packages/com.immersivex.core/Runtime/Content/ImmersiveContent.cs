using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX
{
    public enum PlacementRule
    {
        /// <summary>Appear in front of the user when the app starts and whenever it's picked in the content menu (default).</summary>
        InFrontOfUser,
        /// <summary>Start exactly where the object is in the scene.</summary>
        KeepScenePosition,
    }

    /// <summary>
    /// Makes an object part of the immersive experience: it can be grabbed with a hand or controller at it (the ray only
    /// clicks) and floats where it's released. It's placed by its <see cref="PlacementRule"/> when the app starts (in front of
    /// the user by default, and again whenever it's picked in the content menu); then it moves only when the user moves it,
    /// held in place with a spatial anchor (see <see cref="ContentAnchor"/>) through tracking corrections and the headset
    /// coming off.
    /// This is the one component you need. Add it with right-click ▸ ImmersiveX ▸ Make Immersive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    [AddComponentMenu("ImmersiveX/Immersive Content")]
    public sealed class ImmersiveContent : MonoBehaviour, IHandleTarget, IContentItem
    {
        [SerializeField, Tooltip("Stable ID used to remember this object's placement. Generated automatically; don't copy it between objects.")]
        string _contentId;

        [SerializeField, Tooltip("Where the object appears the first time the app starts. After that it stays where the user left it.")]
        PlacementRule _placement = PlacementRule.InFrontOfUser;

        [SerializeField, Tooltip("Metres from the user: X to the right, Y up from eye height, Z forward.")]
        Vector3 _offsetFromUser = new Vector3(0f, -0.15f, 0.8f);

        [SerializeField, Tooltip("Its meshes are parts: a ray click selects one (click again for the assembly it's in, up to the " +
                                 "whole object), and a hand at it picks it up. Off: the whole object is grabbed as one piece.")]
        bool _selectableParts;

        [SerializeField, Tooltip("Show handles around it: a move bar, turn bars and a turn ring (it turns about the up axis), and a " +
                                 "resize corner. The ray from a hand or controller can use them; the object itself is grabbed by hand.")]
        bool _showHandles = true;

        ContentAnchor _anchoring;
        ContentParts _parts;
        ContentHandles _handles;
        Vector3 _originalScale = Vector3.one;
        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly List<Collider> _disabledColliders = new List<Collider>();
        readonly List<Renderer> _measured = new List<Renderer>();
        bool _isHidden;
        bool _hiddenByMenu;
        bool _placed;

        public string ContentId => _contentId;
        public PlacementRule Placement => _placement;
        public Vector3 OffsetFromUser => _offsetFromUser;

        /// <summary>Its selectable parts, or null when <c>Selectable Parts</c> is off or it has fewer than two meshes.</summary>
        public ContentParts Parts => _parts;

        /// <summary>
        /// Place the object by its rule for a user standing at <paramref name="userPose"/> (when the app starts) and anchor it
        /// there. Saved anchors from earlier visits aren't used: the room may be another one.
        /// </summary>
        public void PlaceForUser(Pose userPose)
        {
            if (_placement == PlacementRule.InFrontOfUser)
            {
                var pose = UserRelativePlacement.Apply(userPose, _offsetFromUser);
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            _placed = true;
            SavePlacement(); // anchored here for this visit
        }

        /// <summary>Remember where the object is now, and its size (anchored and saved). Called when the user lets go of it.</summary>
        public void SavePlacement() =>
            _anchoring.Commit(new Pose(transform.position, transform.rotation), transform.localScale.x / Mathf.Max(Mathf.Abs(_originalScale.x), 1e-5f));

        // ---------------------------------------------------------------- handles (IHandleTarget)

        Transform IHandleTarget.Root => transform;

        Transform IHandleTarget.Controls => null;

        /// <summary>The bounds of its meshes in world space.</summary>
        public Bounds WorldBounds
        {
            get
            {
                GetComponentsInChildren(_measured);
                var found = false;
                var bounds = new Bounds(transform.position, Vector3.one * 0.3f);
                foreach (var renderer in _measured)
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                        continue;
                    if (!found)
                        bounds = renderer.bounds;
                    else
                        bounds.Encapsulate(renderer.bounds);
                    found = true;
                }

                return bounds;
            }
        }

        /// <summary>Its height in metres; setting it scales it evenly, between a tenth and ten times its size in the scene.</summary>
        public float Height
        {
            get => WorldBounds.size.y;
            set
            {
                var current = Height;
                if (current < 1e-4f)
                    return;
                var scale = Mathf.Clamp(transform.localScale.x * value / current, Mathf.Abs(_originalScale.x) * 0.1f, Mathf.Abs(_originalScale.x) * 10f);
                transform.localScale = _originalScale * (scale / Mathf.Max(Mathf.Abs(_originalScale.x), 1e-5f));
            }
        }

        public void MoveTo(Vector3 position) => transform.position = position;

        void Reset() => EnsureContentId();

        void OnValidate() => EnsureContentId();

        void Awake()
        {
            _originalScale = transform.localScale;
            if (GetComponentInChildren<Collider>() == null)
                ColliderFitting.FitBody(gameObject); // on the body layer: hands grab it, rays pass through it
            ConfigureGrab();
            if (_selectableParts)
                MakePartsSelectable();

            _anchoring = gameObject.AddComponent<ContentAnchor>();
            _anchoring.Key = "content/" + _contentId;
        }

        void Start()
        {
            if (_showHandles)
                _handles = ContentHandles.Create(this);
        }

        void OnDestroy()
        {
            if (_handles != null)
                Destroy(_handles.gameObject);
        }

        /// <summary>
        /// Hands pick up its parts instead of the whole. The whole object is the top assembly: click a part until the whole
        /// is selected, then grab it to move the object (it's anchored where it's let go, as usual).
        /// </summary>
        void MakePartsSelectable()
        {
            _parts = ContentParts.FromHierarchy(transform, rootIsPart: true);
            if (_parts == null)
                return; // fewer than two meshes: it's one piece
            GetComponent<XRGrabInteractable>().enabled = false;
            _parts.Released += part =>
            {
                if (part.IsRoot)
                    SavePlacement();
            };
        }

        void LateUpdate()
        {
            // Hidden while its anchor is lost (the headset was just put back on), until the device finds it again, and while
            // the content menu shows something else.
            var hidden = _anchoring.IsLost || _hiddenByMenu;
            if (hidden == _isHidden)
                return;
            _isHidden = hidden;
            if (_handles != null)
                _handles.gameObject.SetActive(!hidden);
            if (hidden)
            {
                GetComponentsInChildren(_hidden);
                _hidden.RemoveAll(r => !r.enabled);
                foreach (var renderer in _hidden)
                    renderer.enabled = false;
                GetComponentsInChildren(_disabledColliders);
                _disabledColliders.RemoveAll(c => !c.enabled);
                foreach (var collider in _disabledColliders)
                    collider.enabled = false; // nothing to grab or click while it's hidden
            }
            else
            {
                foreach (var renderer in _hidden)
                    if (renderer != null)
                        renderer.enabled = true;
                _hidden.Clear();
                foreach (var collider in _disabledColliders)
                    if (collider != null)
                        collider.enabled = true;
                _disabledColliders.Clear();
            }
        }

        // ---------------------------------------------------------------- content menu (IContentItem)

        string IContentItem.Key => "content/" + _contentId;

        string IContentItem.Title => name;

        string IContentItem.Kind => _parts != null ? $"Object · {_parts.Parts.Count} parts" : "Object";

        /// <summary>False while the content menu shows something else.</summary>
        public bool IsShown => !_hiddenByMenu;

        /// <summary>Show or hide it (the content menu). Picked again, it comes back in front of the user (its rule allowing).</summary>
        public void SetShown(bool shown)
        {
            var picked = shown && _hiddenByMenu;
            _hiddenByMenu = !shown;
            var head = Camera.main != null ? Camera.main.transform : null;
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                head = session.Origin.Camera.transform;
            if (picked && _placed && head != null && _placement == PlacementRule.InFrontOfUser)
                PlaceForUser(UserRelativePlacement.UserPose(head));
        }

        void EnsureContentId()
        {
            if (string.IsNullOrEmpty(_contentId))
                _contentId = Guid.NewGuid().ToString("N");
        }

        void ConfigureGrab()
        {
            // Floating by default: no gravity, no throwing, and it stays exactly where it's released.
            var body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            var grab = GetComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true; // hold the object where the hand touched it, instead of snapping to its centre
            DirectGrabOnly.Apply(grab); // the ray only clicks; moving it takes a hand or controller at it
            grab.selectExited.AddListener(_ =>
            {
                if (grab.interactorsSelecting.Count == 0)
                    SavePlacement(); // let go: remember where it was put
            });
        }
    }
}
