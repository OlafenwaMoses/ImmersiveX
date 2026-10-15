using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Watches both hands (tracked hands or controllers, whichever the user holds up) for menu swipes, either way: towards the
    /// body's middle (the right hand from right to left, the left hand from left to right) or the reverse (see
    /// <see cref="SwipeDetector"/>). Raises <see cref="Swiped"/> once per swipe.
    /// <list type="bullet">
    /// <item>A hand that's holding something, or holding a press on a panel (dragging a slider), isn't watched.</item>
    /// <item>Both hands swiping at once (a clap) doesn't count, and after a swipe the next one counts only after
    /// <see cref="SwipeGate.Cooldown"/> seconds, so a hand's way back isn't a swipe (<see cref="SwipeGate"/>).</item>
    /// </list>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class HandSwipes : MonoBehaviour
    {
        readonly SwipeDetector _left = new SwipeDetector(rightHand: false);
        readonly SwipeDetector _right = new SwipeDetector(rightHand: true);
        readonly SwipeGate _gate = new SwipeGate();
        readonly List<XRBaseInteractor> _interactors = new List<XRBaseInteractor>();
        XROrigin _origin;
        float _clock;

        /// <summary>A hand swiped: which hand, and which way.</summary>
        public event Action<InteractorHandedness, SwipeDirection> Swiped;

        /// <summary>How many swipes were recognised (for diagnostics).</summary>
        public int Count { get; private set; }

        public static HandSwipes Create(XROrigin origin)
        {
            var swipes = new GameObject("Hand Swipes").AddComponent<HandSwipes>();
            swipes._origin = origin;
            return swipes;
        }

        void Update()
        {
            _clock += Time.unscaledDeltaTime;
            var head = _origin != null && _origin.Camera != null ? _origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return;

            Watch(_right, InteractorHandedness.Right, head);
            Watch(_left, InteractorHandedness.Left, head);

            if (_gate.TryTake(_clock, out var hand, out var direction))
            {
                Count++;
                Swiped?.Invoke(hand, direction);
            }
        }

        void Watch(SwipeDetector detector, InteractorHandedness hand, Transform head)
        {
            var interactor = ActiveInteractor(hand);
            var tracked = interactor != null;
            var busy = tracked && (HasSelection(interactor) || IsPressingUi(interactor));
            if (busy)
            {
                detector.Reset();
                return;
            }

            // The tracked grab point, not the interactor's transform (on hands that sits on a container that doesn't move).
            var direction = detector.Update(_clock, tracked ? HandPose.Position(interactor) : Vector3.zero, head.position, head.forward, tracked);
            ReportMiss(detector, hand, direction != SwipeDirection.None);
            _gate.Offer(_clock, hand, direction);
        }

        // A fast sideways move that didn't count is logged (once it's clearly over), so swipes can be tuned from a device log.
        string _missRight;
        string _missLeft;
        float _missRightAt;
        float _missLeftAt;

        void ReportMiss(SwipeDetector detector, InteractorHandedness hand, bool recognised)
        {
            var right = hand == InteractorHandedness.Right;
            if (recognised)
            {
                if (right)
                    _missRight = null;
                else
                    _missLeft = null;
                return;
            }

            if (detector.LastMiss != null && detector.LastMiss != (right ? _missRight : _missLeft))
            {
                if (right)
                {
                    _missRight = detector.LastMiss;
                    _missRightAt = _clock;
                }
                else
                {
                    _missLeft = detector.LastMiss;
                    _missLeftAt = _clock;
                }
            }

            var miss = right ? _missRight : _missLeft;
            var at = right ? _missRightAt : _missLeftAt;
            if (miss == null || _clock - at < SwipeDetector.Window)
                return;
            var way = detector.LastMissDirection == SwipeDirection.Outward ? "back" : "across";
            ImmersiveXLog.Info($"Content menu: a fast {(right ? "right" : "left")}-hand move {way} didn't count as a swipe: {miss}.");
            if (right)
                _missRight = null;
            else
                _missLeft = null;
        }

        /// <summary>The active near-far interactor (or any active interactor) for <paramref name="hand"/>: the tracked hand's or the controller's.</summary>
        XRBaseInteractor ActiveInteractor(InteractorHandedness hand)
        {
            if (_interactors.Count == 0 && _origin != null)
                _origin.GetComponentsInChildren(true, _interactors);
            XRBaseInteractor fallback = null;
            foreach (var interactor in _interactors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled || interactor.handedness != hand)
                    continue;
                if (interactor is NearFarInteractor)
                    return interactor;
                if (fallback == null && !(interactor is XRPokeInteractor))
                    fallback = interactor;
            }

            return fallback;
        }

        /// <summary>True while the hand holds a panel press (dragging a slider, say): a fast sideways move then isn't a swipe.</summary>
        static bool IsPressingUi(XRBaseInteractor interactor) =>
            interactor is UnityEngine.XR.Interaction.Toolkit.UI.IUIInteractor ui && ui.TryGetUIModel(out var model) &&
            model.select && model.currentRaycast.isValid;

        bool HasSelection(XRBaseInteractor hand)
        {
            foreach (var interactor in _interactors)
                if (interactor != null && interactor.isActiveAndEnabled && interactor.handedness == hand.handedness && interactor.hasSelection)
                    return true;
            return false;
        }

        /// <summary>
        /// Swipe a hand without moving it (automation): plays a real swipe's path <paramref name="way"/> through the same
        /// detector, in front of the user. Returns true when it was recognised (the menu reacts a moment later).
        /// </summary>
        public bool Simulate(InteractorHandedness hand, SwipeDirection way = SwipeDirection.Inward)
        {
            var head = _origin != null && _origin.Camera != null ? _origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return false;
            var detector = hand == InteractorHandedness.Right ? _right : _left;
            detector.Reset();
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var direction = (hand == InteractorHandedness.Right ? -1f : 1f) * (way == SwipeDirection.Outward ? -1f : 1f);
            var start = head.position + forward * 0.4f - Vector3.up * 0.25f - right * direction * 0.2f;
            var recognised = SwipeDirection.None;
            const int frames = 18; // 0.25 s at 72 Hz, 0.45 m: a brisk swipe
            for (var i = 0; i <= frames && recognised == SwipeDirection.None; i++)
            {
                var along = Mathf.SmoothStep(0f, 1f, i / (float)frames);
                recognised = detector.Update(_clock + i / 72f, start + right * direction * 0.45f * along, head.position, head.forward, true);
            }

            if (recognised == SwipeDirection.None)
                return false;
            _gate.Wake();
            _gate.Offer(_clock, hand, recognised);
            return true;
        }
    }
}
