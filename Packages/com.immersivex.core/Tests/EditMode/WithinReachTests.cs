using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class WithinReachTests
    {
        static readonly Vector3 Eyes = new Vector3(0f, 1.6f, 0f);

        static WithinReach.Follower Placed()
        {
            var follower = new WithinReach.Follower();
            follower.Step(Eyes, Vector3.forward, false, false, 0.016f);
            return follower;
        }

        static void Run(WithinReach.Follower follower, Vector3 eyes, Vector3 forward, float seconds, bool lookedAt = false, bool hold = false)
        {
            for (var t = 0f; t < seconds; t += 1f / 72f)
                follower.Step(eyes, forward, lookedAt, hold, 1f / 72f);
        }

        [Test]
        public void Place_IsLowOnTheRightAtElbowLevel_FacingTheEyes()
        {
            var pose = Placed().Place(WithinReach.DefaultOffset);
            Assert.Greater(pose.position.x, 0.15f, "on the right");
            Assert.Greater(pose.position.z, 0.3f, "in front");
            Assert.AreEqual(1.05f, pose.position.y, 0.02f, "elbow level for 1.6 m eyes: about a metre above the floor");
            Assert.Greater(Vector3.Dot(pose.rotation * Vector3.forward, (pose.position - Eyes).normalized), 0.99f, "turned to the eyes");
        }

        [Test]
        public void LookingAroundAndLeaningIn_KeepItStill()
        {
            var follower = Placed();
            var before = follower.Place(WithinReach.DefaultOffset).position;
            Run(follower, Eyes + new Vector3(0.12f, -0.1f, 0.15f), Quaternion.Euler(0f, 30f, 0f) * Vector3.forward, 1f);
            Run(follower, Eyes, Quaternion.Euler(0f, -35f, 0f) * Vector3.forward, 1f);
            Assert.IsFalse(follower.IsGliding);
            Assert.AreEqual(before, follower.Place(WithinReach.DefaultOffset).position);
        }

        [Test]
        public void Walking_ItGlidesAfterTheUserAndCatchesUp()
        {
            var follower = Placed();
            var walkedTo = Eyes + new Vector3(1.2f, 0f, 0.8f);
            follower.Step(walkedTo, Vector3.forward, false, false, 1f / 72f);
            Assert.IsTrue(follower.IsGliding);
            var partway = follower.Eyes;
            Assert.Less(Vector3.Distance(partway, Eyes), 0.1f, "no jump: it glides");
            Run(follower, walkedTo, Vector3.forward, 2f);
            Assert.IsFalse(follower.IsGliding, "caught up and still again");
            Assert.Less(Vector3.Distance(follower.Eyes, walkedTo), 0.03f);
        }

        [Test]
        public void SittingDown_ItComesDownToo()
        {
            var follower = Placed();
            Run(follower, new Vector3(0f, 1.15f, 0f), Vector3.forward, 2f);
            Assert.AreEqual(0.6f, follower.Place(WithinReach.DefaultOffset).position.y, 0.05f);
        }

        [Test]
        public void TurningAway_ItComesInFront_ButNotWhileLookedAt()
        {
            var follower = Placed();
            var left = Quaternion.Euler(0f, -90f, 0f) * Vector3.forward;
            Run(follower, Eyes, left, 0.5f, lookedAt: true);
            Assert.AreEqual(0f, follower.Yaw, 0.01f, "looking at the panel never chases it away");

            Run(follower, Eyes, left, 2f);
            Assert.AreEqual(-90f, Mathf.DeltaAngle(0f, follower.Yaw), 2f, "in front of the new heading");
            var pose = follower.Place(WithinReach.DefaultOffset);
            Assert.Less(pose.position.x, -0.3f, "in front, facing left");
        }

        [Test]
        public void Held_ItKeepsStill()
        {
            var follower = Placed();
            Run(follower, Eyes + new Vector3(1f, 0f, 0f), Vector3.forward, 1f, hold: true);
            Assert.AreEqual(Eyes, follower.Eyes);
        }

        [Test]
        public void Reset_StartsAgainWithoutAGlide()
        {
            var follower = Placed();
            follower.Reset();
            var there = new Vector3(3f, 1.6f, -2f);
            follower.Step(there, Vector3.right, false, false, 1f / 72f);
            Assert.IsFalse(follower.IsGliding);
            Assert.AreEqual(there, follower.Eyes);
            Assert.AreEqual(90f, follower.Yaw, 0.01f);
        }
    }
}
