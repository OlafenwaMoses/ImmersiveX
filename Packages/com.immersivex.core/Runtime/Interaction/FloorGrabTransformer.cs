using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace ImmersiveX
{
    /// <summary>
    /// Grab behaviour for things that stand on the floor, such as a hologram: they slide across the floor (height
    /// locked) and only ever turn around the up axis.
    /// <list type="bullet">
    /// <item>One hand or controller: move by dragging; turn by twisting the wrist.</item>
    /// <item>Two hands: move with both; turn by turning the hands around each other, like a steering wheel.</item>
    /// </list>
    /// An optional <see cref="IsAllowed"/> check (for example the mapped room's walkable area) keeps the object where it may stand.
    /// </summary>
    [AddComponentMenu("ImmersiveX/Interaction/Floor Grab Transformer")]
    public sealed class FloorGrabTransformer : XRBaseGrabTransformer
    {
        [SerializeField, Tooltip("Degrees the object turns per degree of wrist twist when held with one hand.")]
        float _twistGain = 1.5f;

        Pose _startPose;
        Vector3 _startA;
        Vector3 _startB;
        Quaternion _startRotationA;
        Vector3 _lastAllowed;
        int _startHands;

        /// <summary>Handles one-handed and two-handed grabs; registers itself with the object's XR Grab Interactable.</summary>
        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

        /// <summary>Height of the floor the object stands on, in world space.</summary>
        public float FloorHeight { get; set; }

        /// <summary>Optional: returns false for world positions the object may not move to.</summary>
        public Func<Vector3, bool> IsAllowed { get; set; }

        /// <summary>True while at least one hand or controller holds the object.</summary>
        public bool IsHeld { get; private set; }

        public override void OnGrab(XRGrabInteractable grabInteractable) => Begin(grabInteractable);

        public override void OnGrabCountChanged(XRGrabInteractable grabInteractable, Pose targetPose, Vector3 localScale)
        {
            if (grabInteractable.interactorsSelecting.Count > 0)
                Begin(grabInteractable);
            else
                IsHeld = false;
        }

        public override void OnUnlink(XRGrabInteractable grabInteractable)
        {
            base.OnUnlink(grabInteractable);
            IsHeld = false;
        }

        void Begin(XRGrabInteractable grab)
        {
            IsHeld = true;
            _startPose = new Pose(grab.transform.position, grab.transform.rotation);
            _lastAllowed = _startPose.position;
            _startHands = grab.interactorsSelecting.Count;
            var a = grab.interactorsSelecting[0].GetAttachTransform(grab);
            _startA = a.position;
            _startRotationA = a.rotation;
            if (_startHands >= 2)
                _startB = grab.interactorsSelecting[1].GetAttachTransform(grab).position;
        }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase,
            ref Pose targetPose, ref Vector3 localScale)
        {
            var hands = grabInteractable.interactorsSelecting.Count;
            if (hands == 0 || (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic &&
                               updatePhase != XRInteractionUpdateOrder.UpdatePhase.OnBeforeRender))
                return;

            var a = grabInteractable.interactorsSelecting[0].GetAttachTransform(grabInteractable);
            Vector3 move;
            float turn;
            if (hands >= 2 && _startHands >= 2)
            {
                var b = grabInteractable.interactorsSelecting[1].GetAttachTransform(grabInteractable).position;
                move = (a.position + b) * 0.5f - (_startA + _startB) * 0.5f;
                turn = TurnBetweenHands(_startB - _startA, b - a.position);
            }
            else
            {
                move = a.position - _startA;
                turn = -_twistGain * Twist(_startRotationA, a.rotation);
            }

            targetPose = new Pose(Constrain(_startPose.position + move), Quaternion.AngleAxis(turn, Vector3.up) * Level(_startPose.rotation));
        }

        /// <summary>Slide <paramref name="position"/> onto the floor and, if <see cref="IsAllowed"/> refuses it, along the edge.</summary>
        public Vector3 Constrain(Vector3 position)
        {
            position.y = FloorHeight;
            if (IsAllowed == null || IsAllowed(position))
                return _lastAllowed = position;

            var alongX = new Vector3(position.x, FloorHeight, _lastAllowed.z);
            if (IsAllowed(alongX))
                return _lastAllowed = alongX;
            var alongZ = new Vector3(_lastAllowed.x, FloorHeight, position.z);
            if (IsAllowed(alongZ))
                return _lastAllowed = alongZ;
            return _lastAllowed;
        }

        /// <summary>Start constraining from <paramref name="position"/> (call after placing the object from code).</summary>
        public void ResetConstraint(Vector3 position) => _lastAllowed = position;

        /// <summary>
        /// Degrees <paramref name="to"/> is rolled about <paramref name="from"/>'s forward axis (positive = counter-clockwise
        /// as seen by the holder).
        /// </summary>
        public static float Twist(Quaternion from, Quaternion to)
        {
            var relative = Quaternion.Inverse(from) * to;
            var angle = 2f * Mathf.Atan2(relative.z, relative.w) * Mathf.Rad2Deg; // swing-twist about local Z
            return Mathf.DeltaAngle(0f, angle);
        }

        /// <summary>Degrees the line between two hands turned about the up axis (positive = clockwise from above).</summary>
        public static float TurnBetweenHands(Vector3 from, Vector3 to)
        {
            from.y = 0f;
            to.y = 0f;
            if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f)
                return 0f;
            return Vector3.SignedAngle(from, to, Vector3.up);
        }

        /// <summary>The rotation's heading only: no tilt or roll.</summary>
        public static Quaternion Level(Quaternion rotation)
        {
            var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up);
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
