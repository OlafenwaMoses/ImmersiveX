using NUnit.Framework;

namespace ImmersiveX.Tests
{
    public class CoverageTrackerTests
    {
        const float Frame = 1f / 72f;

        static void Turn(CoverageTracker tracker, ref float yaw, float degreesPerSecond, float seconds)
        {
            for (var t = 0f; t < seconds; t += Frame)
            {
                yaw += degreesPerSecond * Frame;
                tracker.Update(yaw, Frame);
            }
        }

        [Test]
        public void SlowFullTurn_CoversEveryDirection()
        {
            var tracker = new CoverageTracker(maxDegreesPerSecond: 60f);
            var yaw = 0f;

            Turn(tracker, ref yaw, degreesPerSecond: 30f, seconds: 13f);

            Assert.AreEqual(CoverageTracker.Sectors, tracker.CoveredCount);
            Assert.AreEqual(1f, tracker.Coverage, 1e-4f);
            Assert.AreEqual(ScanCoaching.Good, tracker.Coaching);
        }

        [Test]
        public void TurningTooFast_AsksToSlowDownAndDoesNotCount()
        {
            var tracker = new CoverageTracker(maxDegreesPerSecond: 60f);
            var yaw = 0f;

            Turn(tracker, ref yaw, degreesPerSecond: 180f, seconds: 2f);

            Assert.AreEqual(ScanCoaching.SlowDown, tracker.Coaching);
            Assert.LessOrEqual(tracker.CoveredCount, 1);
        }

        [Test]
        public void StandingStill_AsksToKeepTurning()
        {
            var tracker = new CoverageTracker(maxDegreesPerSecond: 60f, idleSeconds: 3f);
            var yaw = 0f;
            Turn(tracker, ref yaw, degreesPerSecond: 30f, seconds: 2f);

            Turn(tracker, ref yaw, degreesPerSecond: 0f, seconds: 3.5f);

            Assert.AreEqual(ScanCoaching.KeepTurning, tracker.Coaching);
        }
    }
}
