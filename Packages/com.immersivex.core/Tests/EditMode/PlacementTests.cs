using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class PlacementTests
    {
        const float Tolerance = 1e-4f;

        GameObject _head;

        [SetUp]
        public void CreateHead() => _head = new GameObject("Head");

        [TearDown]
        public void DestroyHead() => Object.DestroyImmediate(_head);

        [Test]
        public void UserPose_KeepsOnlyTheYaw()
        {
            _head.transform.SetPositionAndRotation(new Vector3(1f, 1.6f, 2f), Quaternion.Euler(35f, 90f, 10f));

            var pose = UserRelativePlacement.UserPose(_head.transform);

            AssertVector(new Vector3(1f, 1.6f, 2f), pose.position);
            AssertVector(Vector3.right, pose.rotation * Vector3.forward);
            AssertVector(Vector3.up, pose.rotation * Vector3.up);
        }

        [Test]
        public void UserPose_LookingStraightDown_StillFacesAHorizontalDirection()
        {
            _head.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var pose = UserRelativePlacement.UserPose(_head.transform);

            Assert.AreEqual(0f, (pose.rotation * Vector3.forward).y, Tolerance);
        }

        [Test]
        public void Apply_OffsetsInTheUsersFrame()
        {
            var user = new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f)); // facing +X

            var placed = UserRelativePlacement.Apply(user, new Vector3(0f, -0.15f, 0.8f));

            AssertVector(new Vector3(0.8f, 1.45f, 0f), placed.position);
        }

        [Test]
        public void ColliderFitting_WrapsTheVisibleMesh()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.transform.localScale = new Vector3(2f, 1f, 0.5f);
                cube.transform.position = new Vector3(3f, 1f, 0f);

                var box = ColliderFitting.FitBox(cube);

                AssertVector(Vector3.zero, box.center);
                AssertVector(Vector3.one, box.size);
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance, $"x of {actual}");
            Assert.AreEqual(expected.y, actual.y, Tolerance, $"y of {actual}");
            Assert.AreEqual(expected.z, actual.z, Tolerance, $"z of {actual}");
        }
    }
}
