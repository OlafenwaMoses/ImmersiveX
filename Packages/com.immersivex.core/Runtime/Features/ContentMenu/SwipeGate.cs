using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// Decides which recognised swipes count (<see cref="HandSwipes"/> feeds it from both hands' <see cref="SwipeDetector"/>s).
    /// <list type="bullet">
    /// <item>A swipe counts <see cref="ClapWindow"/> seconds after it's recognised, unless the other hand swipes in that time:
    /// both hands at once (a clap, or the whole body turning) doesn't count.</item>
    /// <item>After a swipe counts, nothing counts for <see cref="Cooldown"/> seconds. That covers the hand's way back, so the
    /// reverse of an opening swipe never closes the menu straight away.</item>
    /// </list>
    /// </summary>
    public sealed class SwipeGate
    {
        /// <summary>Seconds after a swipe counts before the next one can.</summary>
        public const float Cooldown = 0.8f;

        /// <summary>Seconds to wait for the other hand before a swipe counts.</summary>
        public const float ClapWindow = 0.15f;

        float _quietUntil;
        float _pendingAt = -1f;
        InteractorHandedness _pendingHand;
        SwipeDirection _pendingDirection;

        /// <summary><paramref name="hand"/>'s detector recognised a swipe <paramref name="direction"/> at <paramref name="time"/> (seconds).</summary>
        public void Offer(float time, InteractorHandedness hand, SwipeDirection direction)
        {
            if (direction == SwipeDirection.None || time < _quietUntil)
                return;
            if (_pendingAt >= 0f)
            {
                if (_pendingHand != hand)
                {
                    // The other hand swiped too, at the same moment: a clap, not a swipe.
                    _pendingAt = -1f;
                    _quietUntil = time + Cooldown;
                }

                return; // the same hand again: its first swipe stands
            }

            _pendingAt = time;
            _pendingHand = hand;
            _pendingDirection = direction;
        }

        /// <summary>The swipe that counts at <paramref name="time"/>, once, when one does.</summary>
        public bool TryTake(float time, out InteractorHandedness hand, out SwipeDirection direction)
        {
            hand = _pendingHand;
            direction = _pendingDirection;
            if (_pendingAt < 0f || time - _pendingAt < ClapWindow)
                return false;
            _pendingAt = -1f;
            _quietUntil = time + Cooldown;
            return true;
        }

        /// <summary>Let the next swipe count straight away (automation).</summary>
        public void Wake() => _quietUntil = 0f;
    }
}
