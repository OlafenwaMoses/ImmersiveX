using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>Which way a hand swiped.</summary>
    public enum SwipeDirection
    {
        None,

        /// <summary>Towards the body's middle: the right hand from right to left, the left hand from left to right.</summary>
        Inward,

        /// <summary>The reverse: the right hand from left to right, the left hand from right to left.</summary>
        Outward,
    }

    /// <summary>
    /// Recognises a fast sideways swipe of one hand in front of the body, either way (<see cref="SwipeDirection"/>). Feed it
    /// where the hand is every frame (<see cref="Update"/>).
    /// <para>A swipe is at least <see cref="MinDistance"/> sideways within <see cref="Window"/> seconds, reaching
    /// <see cref="MinPeakSpeed"/>, mostly sideways (not up, down or forward), with the hand in front of the user between chest
    /// and forehead height. Telling a swipe from the hand's way back after one is <see cref="SwipeGate"/>'s job.</para>
    /// </summary>
    public sealed class SwipeDetector
    {
        /// <summary>The longest a swipe may take, in seconds.</summary>
        public const float Window = 0.3f;

        /// <summary>How far sideways the hand must travel, in metres.</summary>
        public const float MinDistance = 0.22f;

        /// <summary>How fast sideways the hand must go at its fastest, in metres per second.</summary>
        public const float MinPeakSpeed = 1.3f;

        /// <summary>Faster than this between two samples is a tracking glitch, not a hand, in metres per second.</summary>
        const float GlitchSpeed = 8f;

        /// <summary>Tracking may drop out this long during a swipe (fast hands blur) without starting over, in seconds.</summary>
        const float MaxGap = 0.2f;

        /// <summary>A sideways move at least this fast that doesn't count is reported in <see cref="LastMiss"/>, in metres per second.</summary>
        const float NearMissSpeed = 0.9f;

        readonly bool _rightHand;
        readonly List<Vector4> _samples = new List<Vector4>(); // xyz = position, w = time

        public SwipeDetector(bool rightHand) => _rightHand = rightHand;

        public bool IsRightHand => _rightHand;

        /// <summary>
        /// Why the sample just added was a fast sideways move that didn't count as a swipe, or null. For diagnostics: a swipe in
        /// progress reports misses until it completes.
        /// </summary>
        public string LastMiss { get; private set; }

        /// <summary>Which way the move in <see cref="LastMiss"/> went.</summary>
        public SwipeDirection LastMissDirection { get; private set; }

        /// <summary>Forget the hand's path (it was busy, or it's gone).</summary>
        public void Reset() => _samples.Clear();

        /// <summary>
        /// Add where the hand is at <paramref name="time"/> (seconds), with the user's head. <paramref name="tracked"/> false
        /// means the hand isn't seen this frame. Returns the way the hand swiped when this sample completes a swipe, else
        /// <see cref="SwipeDirection.None"/>.
        /// </summary>
        public SwipeDirection Update(float time, Vector3 hand, Vector3 headPosition, Vector3 headForward, bool tracked)
        {
            LastMiss = null; // set again below only when this sample is a fast sideways move that doesn't count
            if (_samples.Count > 0 && time - _samples[_samples.Count - 1].w > MaxGap)
                _samples.Clear(); // lost for too long: start over
            if (!tracked)
                return SwipeDirection.None;

            if (_samples.Count > 0)
            {
                var last = _samples[_samples.Count - 1];
                var gap = time - last.w;
                if (gap <= 0f)
                    return SwipeDirection.None;
                if (Vector3.Distance(hand, last) / gap > GlitchSpeed)
                    _samples.Clear(); // the hand jumped: tracking was wrong for a moment
            }

            _samples.Add(new Vector4(hand.x, hand.y, hand.z, time));
            while (_samples.Count > 0 && time - _samples[0].w > Window)
                _samples.RemoveAt(0);
            if (_samples.Count < 3)
                return SwipeDirection.None;

            // The user's level frame: forward from the head, right across.
            var forward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);

            // Fast: the quickest sideways speed each way, over two-sample spans (steadier than single frames). The faster way is
            // the one the hand is swiping.
            float peakInward = 0f, peakOutward = 0f;
            for (var i = 2; i < _samples.Count; i++)
            {
                var span = _samples[i].w - _samples[i - 2].w;
                if (span <= 1e-4f)
                    continue;
                var speed = Vector3.Dot((Vector3)_samples[i] - (Vector3)_samples[i - 2], right) / span;
                var inward = _rightHand ? -speed : speed;
                peakInward = Mathf.Max(peakInward, inward);
                peakOutward = Mathf.Max(peakOutward, -inward);
            }

            var direction = peakInward >= peakOutward ? SwipeDirection.Inward : SwipeDirection.Outward;
            var peak = Mathf.Max(peakInward, peakOutward);
            if (peak < NearMissSpeed)
                return SwipeDirection.None; // not a fast sideways move at all
            LastMissDirection = direction;

            // In front of the user, between chest and forehead.
            var offset = hand - headPosition;
            var ahead = Vector3.Dot(offset, forward);
            if (ahead < 0f || ahead > 0.9f || offset.y < -0.8f || offset.y > 0.35f)
                return Miss($"the hand was {ahead:0.00} m in front and {offset.y:0.00} m from eye height (needs 0–0.9 m in front, chest to forehead)");

            // Over the window: far enough that way, and mostly sideways.
            var start = (Vector3)_samples[0];
            var move = hand - start;
            var sideways = Vector3.Dot(move, right);
            var towardsMiddle = _rightHand ? -sideways : sideways;
            var wanted = direction == SwipeDirection.Inward ? towardsMiddle : -towardsMiddle;
            var vertical = Mathf.Abs(move.y);
            var along = Mathf.Abs(Vector3.Dot(move, forward));
            if (wanted < MinDistance)
                return Miss($"only {wanted:0.00} m sideways in {Window:0.0} s (needs {MinDistance:0.00} m)");
            if (wanted < 1.8f * vertical)
                return Miss($"mostly up or down ({vertical:0.00} m vertical for {wanted:0.00} m sideways)");
            if (wanted < 1.5f * along)
                return Miss($"mostly forward or back ({along:0.00} m for {wanted:0.00} m sideways)");
            if (peak < MinPeakSpeed)
                return Miss($"{peak:0.0} m/s at its fastest (needs {MinPeakSpeed:0.0} m/s)");

            _samples.Clear();
            LastMiss = null;
            return direction;
        }

        SwipeDirection Miss(string why)
        {
            LastMiss = why;
            return SwipeDirection.None;
        }
    }
}
