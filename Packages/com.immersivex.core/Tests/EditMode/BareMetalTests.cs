using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class BareMetalTests
    {
        sealed class App : MonoBehaviour, IBareMetalApp
        {
            public int Calls;
            public void OnHostReady() => Calls++;
        }

        static BareMetalContent NewContent(out GameObject root)
        {
            root = new GameObject("App");
            return root.AddComponent<BareMetalContent>();
        }

        [Test]
        public void WithoutBareMetalTheSessionSetsUpEverything()
        {
            var plan = SessionPlan.For(null, null);

            Assert.IsFalse(plan.IsBareMetal);
            Assert.IsTrue(plan.RoomAccess && plan.MapRoomAtStart && plan.HideBoundary && plan.ContentMenu && plan.ViewModes && plan.PlaceContent);
            Assert.IsFalse(plan.CameraAccess);
        }

        [Test]
        public void BareMetalContentGetsOnlyTheGroundworkByDefault()
        {
            var content = NewContent(out var root);
            try
            {
                var plan = SessionPlan.For(content, null);

                Assert.IsTrue(plan.IsBareMetal);
                Assert.IsTrue(plan.RoomAccess, "room data is asked for, so depth ray casts work");
                Assert.IsTrue(plan.HideBoundary, "the user can walk anywhere");
                Assert.IsFalse(plan.MapRoomAtStart, "no room-mapping prompt");
                Assert.IsFalse(plan.ContentMenu, "the app brings its own menu");
                Assert.IsFalse(plan.ViewModes);
                Assert.IsFalse(plan.CameraAccess);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MappingTheRoomAtStartNeedsRoomAccess()
        {
            var content = NewContent(out var root);
            try
            {
                content.MapRoomAtStart = true;
                content.PassthroughCamera = true;
                Assert.IsTrue(SessionPlan.For(content, null).MapRoomAtStart);
                Assert.IsTrue(SessionPlan.For(content, null).CameraAccess);

                content.RoomAccess = false;
                Assert.IsFalse(SessionPlan.For(content, null).MapRoomAtStart);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EveryAppUnderTheContentHearsOnceThatTheHostIsReady()
        {
            var content = NewContent(out var root);
            try
            {
                var first = root.AddComponent<App>();
                var child = new GameObject("Part");
                child.transform.SetParent(root.transform);
                var second = child.AddComponent<App>();
                var events = 0;
                content.HostReady += () => events++;

                content.NotifyHostReady();
                content.NotifyHostReady();

                Assert.AreEqual(1, first.Calls);
                Assert.AreEqual(1, second.Calls);
                Assert.AreEqual(1, events);
                Assert.IsTrue(content.IsHostReady);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TheMiddleOfThePictureLooksStraightAhead()
        {
            var intrinsics = CameraIntrinsics.FromFieldOfView(new Vector2Int(1280, 960), 70f);

            Assert.AreEqual(Vector3.forward, intrinsics.Direction(new Vector2(0.5f, 0.5f)));
            var top = intrinsics.Direction(new Vector2(0.5f, 1f));
            Assert.AreEqual(Mathf.Tan(35f * Mathf.Deg2Rad), top.y, 1e-4f, "the top edge is half the field of view up");
            Assert.AreEqual(70f, intrinsics.FieldOfView.y, 1e-3f);
            Assert.Greater(intrinsics.FieldOfView.x, intrinsics.FieldOfView.y);
        }

        [Test]
        public void APointFromTheTopLeftOfThePictureLooksUpAndLeft()
        {
            var image = new Texture2D(8, 8);
            var frame = new CameraFrame(image, new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.identity),
                CameraIntrinsics.FromFieldOfView(new Vector2Int(1280, 960), 70f), 0f);
            try
            {
                var ray = frame.ImagePointToRay(new Vector2(0.1f, 0.1f));

                Assert.AreEqual(new Vector3(0f, 1.6f, 0f), ray.origin);
                Assert.Less(ray.direction.x, 0f);
                Assert.Greater(ray.direction.y, 0f);
                Assert.Greater(ray.direction.z, 0f);
                Assert.AreEqual(0.5f, frame.WidthAt(0.25f, 2f) / frame.WidthAt(0.25f, 4f), 1e-4f, "width grows with distance");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void ALensFacingForwardInAndroidsAxesLooksForwardInTheRoom()
        {
            // Android's lens frame has +z out of the screen, towards the user: a lens looking ahead is turned 180° about x.
            var headFromCamera = LensPose.HeadFromCamera(new[] { 0.03f, -0.02f, -0.07f }, new[] { 1f, 0f, 0f, 0f });
            Assert.AreEqual(new Vector3(0.03f, -0.02f, 0.07f), headFromCamera.position, "z flips: 7 cm in front of the head");

            var head = new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f)); // facing +x
            var world = LensPose.World(head, headFromCamera);
            var ahead = world.rotation * Vector3.forward;

            Assert.AreEqual(1f, ahead.x, 1e-4f);
            Assert.AreEqual(1f, (world.rotation * Vector3.up).y, 1e-4f, "and stays upright");
            Assert.AreEqual(1.6f - 0.02f, world.position.y, 1e-4f);
            Assert.AreEqual(0.07f, world.position.x, 1e-4f);
        }

        [Test]
        public void ARayHitsTheNearSideOfABox()
        {
            var ray = new Ray(new Vector3(0f, 0f, -2f), Vector3.forward);

            Assert.IsTrue(ImmersiveHost.RaycastBox(ray, Pose.identity, Vector3.one, 10f, out var hit));
            Assert.AreEqual(1.5f, hit.Distance, 1e-4f);
            Assert.AreEqual(new Vector3(0f, 0f, -1f), hit.Normal);
            Assert.AreEqual(WorldSurfaces.Room, hit.Surface);

            Assert.IsFalse(ImmersiveHost.RaycastBox(ray, Pose.identity, Vector3.one, 1f, out _), "beyond the distance");
            Assert.IsFalse(ImmersiveHost.RaycastBox(new Ray(new Vector3(2f, 0f, -2f), Vector3.forward), Pose.identity, Vector3.one, 10f, out _));
            Assert.IsTrue(ImmersiveHost.RaycastBox(ray, new Pose(Vector3.zero, Quaternion.Euler(0f, 45f, 0f)), Vector3.one, 10f, out var turned));
            Assert.AreEqual(2f - Mathf.Sqrt(0.5f), turned.Distance, 1e-4f, "a box turned 45° shows its edge");
        }
    }
}
