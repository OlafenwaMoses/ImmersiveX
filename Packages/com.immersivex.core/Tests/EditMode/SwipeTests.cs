using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX.Tests
{
    public class SwipeTests
    {
        static readonly Vector3 Head = new Vector3(0f, 1.6f, 0f);

        /// <summary>
        /// Moves a hand from <paramref name="from"/> to <paramref name="to"/> (relative to the head, in the user's frame: x right,
        /// y up, z forward) over <paramref name="seconds"/>, easing in and out like a real swipe, at 72 frames a second.
        /// Tracking can drop out between <paramref name="lostFrom"/> and <paramref name="lostTo"/> seconds.
        /// </summary>
        static SwipeDirection Swipe(bool rightHand, Vector3 from, Vector3 to, float seconds, Vector3? facing = null,
            float lostFrom = -1f, float lostTo = -1f, float startAt = 0f)
        {
            var forward = facing ?? Vector3.forward;
            var turn = Quaternion.LookRotation(forward, Vector3.up);
            var detector = new SwipeDetector(rightHand);
            var frames = Mathf.CeilToInt(seconds * 72f);
            var recognised = SwipeDirection.None;
            void Add(float time, Vector3 hand, bool tracked)
            {
                var direction = detector.Update(time, hand, Head, forward, tracked);
                if (recognised == SwipeDirection.None)
                    recognised = direction;
            }

            // Hold still a moment first, as a hand does before it moves.
            for (var i = 0; i < 10; i++)
                Add(startAt + i / 72f, Head + turn * from, true);
            var t0 = startAt + 10 / 72f;
            for (var i = 0; i <= frames; i++)
            {
                var t = i / 72f;
                var along = Mathf.SmoothStep(0f, 1f, i / (float)frames);
                Add(t0 + t, Head + turn * Vector3.Lerp(from, to, along), !(t >= lostFrom && t <= lostTo));
            }

            return recognised;
        }

        const SwipeDirection None = SwipeDirection.None, Inward = SwipeDirection.Inward, Outward = SwipeDirection.Outward;

        // In front of the chest: 40 cm ahead, 25 cm below the eyes.
        static readonly Vector3 RightSide = new Vector3(0.25f, -0.25f, 0.4f);
        static readonly Vector3 LeftSide = new Vector3(-0.25f, -0.25f, 0.4f);

        [Test]
        public void RightHand_FastRightToLeft_SwipesInward() => Assert.AreEqual(Inward, Swipe(true, RightSide, LeftSide, 0.25f));

        [Test]
        public void LeftHand_FastLeftToRight_SwipesInward() => Assert.AreEqual(Inward, Swipe(false, LeftSide, RightSide, 0.25f));

        [Test]
        public void TheReverse_SwipesOutward()
        {
            Assert.AreEqual(Outward, Swipe(true, LeftSide, RightSide, 0.25f), "the right hand from left to right");
            Assert.AreEqual(Outward, Swipe(false, RightSide, LeftSide, 0.25f), "the left hand from right to left");
        }

        [Test]
        public void Slow_DoesNotSwipe()
        {
            Assert.AreEqual(None, Swipe(true, RightSide, LeftSide, 1.2f), "half a metre in over a second is reaching, not swiping");
            Assert.AreEqual(None, Swipe(true, LeftSide, RightSide, 1.2f), "nor is it the other way");
        }

        [Test]
        public void Short_DoesNotSwipe()
        {
            Assert.AreEqual(None, Swipe(true, RightSide, RightSide + Vector3.left * 0.15f, 0.12f), "15 cm is a flick");
            Assert.AreEqual(None, Swipe(true, LeftSide, LeftSide + Vector3.right * 0.15f, 0.12f), "either way");
        }

        [Test]
        public void UpDownOrForward_DoesNotSwipe()
        {
            Assert.AreEqual(None, Swipe(true, RightSide + Vector3.down * 0.3f, RightSide + Vector3.up * 0.25f, 0.25f), "raising the hand");
            Assert.AreEqual(None, Swipe(true, RightSide + Vector3.back * 0.2f, RightSide + Vector3.forward * 0.3f, 0.2f), "pushing forward");
            Assert.AreEqual(None, Swipe(true, RightSide + Vector3.up * 0.25f, LeftSide + Vector3.down * 0.35f, 0.25f), "a diagonal chop that's mostly down");
            Assert.AreEqual(None, Swipe(true, LeftSide + Vector3.up * 0.25f, RightSide + Vector3.down * 0.35f, 0.25f), "the same chop the other way");
        }

        [Test]
        public void HandsDownByTheHips_DoNotSwipe()
        {
            Assert.AreEqual(None, Swipe(true, new Vector3(0.25f, -0.95f, 0.2f), new Vector3(-0.25f, -0.95f, 0.2f), 0.25f), "arms swinging while walking");
            Assert.AreEqual(None, Swipe(true, new Vector3(-0.25f, -0.95f, 0.2f), new Vector3(0.25f, -0.95f, 0.2f), 0.25f), "and swinging back");
        }

        [Test]
        public void TrackingDropOutMidSwipe_StillSwipes() =>
            Assert.AreEqual(Inward, Swipe(true, RightSide, LeftSide, 0.25f, lostFrom: 0.08f, lostTo: 0.14f), "fast hands blur; a short dropout is fine");

        [Test]
        public void FacingAnotherWay_SwipesInTheUsersFrame()
        {
            Assert.AreEqual(Inward, Swipe(true, RightSide, LeftSide, 0.25f, facing: Vector3.right), "facing +x, the user's left is +z");
            Assert.AreEqual(Inward, Swipe(false, LeftSide, RightSide, 0.25f, facing: new Vector3(-1f, 0f, -1f).normalized));
            Assert.AreEqual(Outward, Swipe(true, LeftSide, RightSide, 0.25f, facing: Vector3.right));
        }

        // ---------------------------------------------------------------- which swipes count

        const InteractorHandedness RightHand = InteractorHandedness.Right, LeftHand = InteractorHandedness.Left;

        [Test]
        public void Gate_CountsASwipeOnceTheOtherHandHadItsChance()
        {
            var gate = new SwipeGate();
            gate.Offer(1f, RightHand, Inward);
            Assert.IsFalse(gate.TryTake(1.1f, out _, out _), "still waiting for the other hand");
            Assert.IsTrue(gate.TryTake(1.2f, out var hand, out var direction));
            Assert.AreEqual(RightHand, hand);
            Assert.AreEqual(Inward, direction);
            Assert.IsFalse(gate.TryTake(1.5f, out _, out _), "once");
        }

        [Test]
        public void Gate_TheWayBackAfterASwipe_DoesNotCount()
        {
            var gate = new SwipeGate();
            gate.Offer(1f, RightHand, Inward);
            Assert.IsTrue(gate.TryTake(1.2f, out _, out _));
            gate.Offer(1.5f, RightHand, Outward); // the hand swinging back, 0.35 s after it opened the menu
            Assert.IsFalse(gate.TryTake(2f, out _, out _), "the reverse right after the opening swipe is the hand's way back");
        }

        [Test]
        public void Gate_TheReverseLater_Counts()
        {
            var gate = new SwipeGate();
            gate.Offer(1f, RightHand, Inward);
            Assert.IsTrue(gate.TryTake(1.2f, out _, out _));
            var later = 1.2f + SwipeGate.Cooldown + 0.5f;
            gate.Offer(later, RightHand, Outward);
            Assert.IsTrue(gate.TryTake(later + 0.2f, out var hand, out var direction));
            Assert.AreEqual(RightHand, hand);
            Assert.AreEqual(Outward, direction);
        }

        [Test]
        public void Gate_BothHandsAtOnce_DoNotCount()
        {
            var gate = new SwipeGate();
            gate.Offer(1f, RightHand, Inward);
            gate.Offer(1.05f, LeftHand, Inward); // a clap
            Assert.IsFalse(gate.TryTake(1.5f, out _, out _));
            gate.Offer(3f, RightHand, Inward);
            gate.Offer(3.05f, LeftHand, Outward); // both hands moving left together: the body turning
            Assert.IsFalse(gate.TryTake(3.5f, out _, out _));
        }

        [Test]
        public void Gate_TheSameHandAgainWhileWaiting_KeepsItsFirstSwipe()
        {
            var gate = new SwipeGate();
            gate.Offer(1f, LeftHand, Inward);
            gate.Offer(1.1f, LeftHand, Outward);
            Assert.IsTrue(gate.TryTake(1.2f, out _, out var direction));
            Assert.AreEqual(Inward, direction);
        }

        [Test]
        public void TrackingJump_DoesNotSwipe()
        {
            var detector = new SwipeDetector(rightHand: true);
            var hand = Head + RightSide;
            for (var i = 0; i < 10; i++)
                Assert.AreEqual(None, detector.Update(i / 72f, hand, Head, Vector3.forward, true));
            // Tracking snaps the hand 60 cm to the left in one frame (a glitch, 43 m/s), then it stays.
            for (var i = 10; i < 30; i++)
                Assert.AreEqual(None, detector.Update(i / 72f, Head + LeftSide + Vector3.left * 0.1f, Head, Vector3.forward, true));
        }
    }
}
