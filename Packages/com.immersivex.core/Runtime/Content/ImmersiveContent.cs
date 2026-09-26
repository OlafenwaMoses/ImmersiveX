using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX
{
    public enum PlacementRule
    {
        /// <summary>Appear in front of the user when the app starts (default).</summary>
        InFrontOfUser,
        /// <summary>Stay exactly where the object is in the scene.</summary>
        KeepScenePosition,
    }

    /// <summary>
    /// Makes an object part of the immersive experience: it can be grabbed with hands and controllers,
    /// floats where it's released, and is placed for the user when the app starts.
    /// This is the one component you need. Add it with right-click ▸ ImmersiveX ▸ Make Immersive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    [AddComponentMenu("ImmersiveX/Immersive Content")]
    public sealed class ImmersiveContent : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable ID used to remember this object's placement. Generated automatically; don't copy it between objects.")]
        string _contentId;

        [SerializeField, Tooltip("Where the object appears when the app starts.")]
        PlacementRule _placement = PlacementRule.InFrontOfUser;

        [SerializeField, Tooltip("Metres from the user: X to the right, Y up from eye height, Z forward.")]
        Vector3 _offsetFromUser = new Vector3(0f, -0.15f, 0.8f);

        public string ContentId => _contentId;
        public PlacementRule Placement => _placement;
        public Vector3 OffsetFromUser => _offsetFromUser;

        /// <summary>Position the object for a user standing at <paramref name="userPose"/>. Does nothing for <see cref="PlacementRule.KeepScenePosition"/>.</summary>
        public void PlaceForUser(Pose userPose)
        {
            if (_placement == PlacementRule.KeepScenePosition)
                return;

            var pose = UserRelativePlacement.Apply(userPose, _offsetFromUser);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void Reset() => EnsureContentId();

        void OnValidate() => EnsureContentId();

        void Awake()
        {
            if (GetComponentInChildren<Collider>() == null)
                ColliderFitting.FitBox(gameObject);
            ConfigureGrab();
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
        }
    }
}
