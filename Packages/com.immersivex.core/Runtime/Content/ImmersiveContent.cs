using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX
{
    public enum PlacementRule
    {
        /// <summary>Appear in front of the user the first time the app starts (default).</summary>
        InFrontOfUser,
        /// <summary>Start exactly where the object is in the scene.</summary>
        KeepScenePosition,
    }

    /// <summary>
    /// Makes an object part of the immersive experience: it can be grabbed with hands and controllers and floats where
    /// it's released. It moves only when the user moves it: where it's left is kept with a spatial anchor (see
    /// <see cref="ContentAnchor"/>), so it's in the same real spot after the headset comes off or the app restarts. The
    /// first time, it's placed by its <see cref="PlacementRule"/>.
    /// This is the one component you need. Add it with right-click ▸ ImmersiveX ▸ Make Immersive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    [AddComponentMenu("ImmersiveX/Immersive Content")]
    public sealed class ImmersiveContent : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable ID used to remember this object's placement. Generated automatically; don't copy it between objects.")]
        string _contentId;

        [SerializeField, Tooltip("Where the object appears the first time the app starts. After that it stays where the user left it.")]
        PlacementRule _placement = PlacementRule.InFrontOfUser;

        [SerializeField, Tooltip("Metres from the user: X to the right, Y up from eye height, Z forward.")]
        Vector3 _offsetFromUser = new Vector3(0f, -0.15f, 0.8f);

        ContentAnchor _anchoring;
        readonly List<Renderer> _hidden = new List<Renderer>();
        bool _lost;

        public string ContentId => _contentId;
        public PlacementRule Placement => _placement;
        public Vector3 OffsetFromUser => _offsetFromUser;

        /// <summary>
        /// Put the object back where the user left it (its spatial anchor, else where it was saved in the room). The first
        /// time, place it by its rule for a user standing at <paramref name="userPose"/> and anchor it there.
        /// </summary>
        public void PlaceForUser(Pose userPose)
        {
            if (isActiveAndEnabled)
                StartCoroutine(Place(userPose));
        }

        IEnumerator Place(Pose userPose)
        {
            RestoredPlacement restored = null;
            var restoring = true;
            _anchoring.Restore(result =>
            {
                restored = result;
                restoring = false;
            });
            while (restoring)
                yield return null;

            if (restored != null)
            {
                transform.SetPositionAndRotation(restored.Pose.position, restored.Pose.rotation);
                ImmersiveXLog.Info($"'{name}' is back {(restored.FromAnchor ? "at its spatial anchor" : "where it was left in the room")}.");
                if (restored.FromAnchor)
                    yield break;
            }
            else if (_placement == PlacementRule.InFrontOfUser)
            {
                var pose = UserRelativePlacement.Apply(userPose, _offsetFromUser);
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            SavePlacement(); // anchor it here, so it's in this spot next time too
        }

        /// <summary>Remember where the object is now (anchored and saved). Called when the user lets go of it.</summary>
        public void SavePlacement() => _anchoring.Commit(new Pose(transform.position, transform.rotation), 0f);

        void Reset() => EnsureContentId();

        void OnValidate() => EnsureContentId();

        void Awake()
        {
            if (GetComponentInChildren<Collider>() == null)
                ColliderFitting.FitBox(gameObject);
            ConfigureGrab();

            _anchoring = gameObject.AddComponent<ContentAnchor>();
            _anchoring.Key = "content/" + _contentId;
        }

        void LateUpdate()
        {
            // Hidden while its anchor is lost (the headset was just put back on), until the device finds it again.
            var lost = _anchoring.IsLost;
            if (lost == _lost)
                return;
            _lost = lost;
            if (lost)
            {
                GetComponentsInChildren(_hidden);
                _hidden.RemoveAll(r => !r.enabled);
                foreach (var renderer in _hidden)
                    renderer.enabled = false;
            }
            else
            {
                foreach (var renderer in _hidden)
                    if (renderer != null)
                        renderer.enabled = true;
                _hidden.Clear();
            }
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
            grab.selectExited.AddListener(_ =>
            {
                if (grab.interactorsSelecting.Count == 0)
                    SavePlacement(); // let go: remember where it was put
            });
        }
    }
}
