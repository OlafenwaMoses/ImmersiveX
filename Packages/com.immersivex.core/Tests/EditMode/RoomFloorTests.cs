using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class RoomFloorTests
    {
        GameObject _head;

        [SetUp]
        public void SetUp() => _head = new GameObject("Head");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_head);

        Transform EyesAt(float y)
        {
            _head.transform.position = new Vector3(0.5f, y, -1f);
            return _head.transform;
        }

        [Test]
        public void TheFloorUnderAStandingOrSittingUser_IsUnderThem()
        {
            Assert.IsTrue(RoomFloor.IsUnder(0f, EyesAt(1.6f)), "standing");
            Assert.IsTrue(RoomFloor.IsUnder(0f, EyesAt(1.15f)), "sitting");
            Assert.IsTrue(RoomFloor.IsUnder(0f, EyesAt(0.75f)), "the headset on a table");
        }

        [Test]
        public void AFloorOnAnotherStorey_IsNotUnderThem()
        {
            Assert.IsFalse(RoomFloor.IsUnder(0f, EyesAt(4.8f)), "a room mapped downstairs, with the user upstairs");
            Assert.IsFalse(RoomFloor.IsUnder(3.2f, EyesAt(1.6f)), "a room mapped upstairs, with the user downstairs");
        }

        [Test]
        public void WithNoSession_TheFloorIsAStandingEyeHeightBelowTheHead()
        {
            Assert.AreEqual(3.3f, RoomFloor.Height(EyesAt(4.8f)), 1e-4f);
            Assert.IsFalse(RoomFloor.RoomIsHere(_head.transform), "no mapped room");
        }
    }
}
