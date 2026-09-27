using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class FloorGrabTests
    {
        [Test]
        public void Grab_TurnsOnlyAroundUp()
        {
            Assert.AreEqual(30f, FloorGrabTransformer.Twist(Quaternion.identity, Quaternion.AngleAxis(30f, Vector3.forward)), 1e-3f);
            Assert.AreEqual(0f, FloorGrabTransformer.Twist(Quaternion.identity, Quaternion.AngleAxis(40f, Vector3.up)), 1e-3f,
                "turning the wrist sideways isn't a twist");
            Assert.AreEqual(90f, FloorGrabTransformer.TurnBetweenHands(Vector3.right, Vector3.back), 1e-3f);

            var level = FloorGrabTransformer.Level(Quaternion.Euler(30f, 45f, 10f));
            Assert.AreEqual(45f, level.eulerAngles.y, 0.5f);
            Assert.AreEqual(0f, Vector3.Angle(level * Vector3.up, Vector3.up), 1e-3f, "no tilt or roll");
        }

        [Test]
        public void Grab_StaysOnTheFloorAndSlidesAlongLimits()
        {
            var host = new GameObject("Grab test");
            try
            {
                var grab = host.AddComponent<FloorGrabTransformer>();
                grab.FloorHeight = 0.1f;
                grab.IsAllowed = p => p.x <= 1f; // a wall at x = 1
                grab.ResetConstraint(new Vector3(0.5f, 0.1f, 0f));

                Assert.AreEqual(new Vector3(0.8f, 0.1f, 0.3f), grab.Constrain(new Vector3(0.8f, 2f, 0.3f)), "height is locked to the floor");
                Assert.AreEqual(new Vector3(0.8f, 0.1f, 0.9f), grab.Constrain(new Vector3(1.5f, 0.1f, 0.9f)), "slides along the wall");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
