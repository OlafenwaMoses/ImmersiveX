using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class ContentPartsTests
    {
        GameObject _root;
        Transform _body;
        Transform _wheelLeft;
        Transform _wheelRight;
        Transform _lamp;
        Transform _antenna;

        // Root
        //   Body (mesh)          an assembly: its own mesh and two wheels
        //     Wheel L (mesh)
        //     Wheel R (mesh)
        //   Group (no mesh)      only one branch, so it isn't a part
        //     Lamp (mesh)
        //   Antenna (mesh)
        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Rover");
            _body = Mesh("Body", _root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.4f, 0.6f));
            _wheelLeft = Mesh("Wheel_L", _body, new Vector3(-1.1f, -1f, 0f), new Vector3(0.2f, 0.5f, 0.5f));
            _wheelRight = Mesh("Wheel_R.001", _body, new Vector3(1.1f, -1f, 0f), new Vector3(0.2f, 0.5f, 0.5f));
            var group = new GameObject("Group").transform;
            group.SetParent(_root.transform, false);
            _lamp = Mesh("Lamp", group, new Vector3(0f, 1.2f, 0.4f), Vector3.one * 0.1f);
            _antenna = Mesh("Antenna", _root.transform, new Vector3(0.4f, 1.3f, -0.2f), new Vector3(0.02f, 0.6f, 0.02f));
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        static Transform Mesh(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.name = name;
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = position;
            mesh.transform.localScale = scale;
            return mesh.transform;
        }

        [Test]
        public void Hierarchy_MeshesAndTheirAssembliesAreParts()
        {
            var parts = ContentParts.FromHierarchy(_root.transform);
            Assert.IsNotNull(parts);
            Assert.AreEqual(5, parts.Parts.Count, "body, two wheels, lamp, antenna; the one-branch group isn't a part");
            Assert.AreEqual(parts.Find(_body), parts.Find(_wheelLeft).Parent, "the wheels belong to the body");
            Assert.IsNull(parts.Find(_lamp).Parent, "the lamp's group has one branch, so the lamp is top-level");
            Assert.AreEqual("Wheel R", parts.Find(_wheelRight).Title, "names are made readable");
            Assert.AreEqual("Body › Wheel L", parts.Find(_wheelLeft).Trail);
            foreach (var part in parts.Parts)
                Assert.AreEqual(ContentLayer.Body, part.GetComponentInChildren<Collider>().gameObject.layer, $"{part.Title} is grabbed on the body layer");
        }

        [Test]
        public void OneMesh_HasNoParts()
        {
            var single = new GameObject("Single");
            try
            {
                Mesh("Only", single.transform, Vector3.zero, Vector3.one);
                Assert.IsNull(ContentParts.FromHierarchy(single.transform));
            }
            finally
            {
                Object.DestroyImmediate(single);
            }
        }

        [Test]
        public void ClickingAgain_WalksUpTheAssemblies_ThenDeselects()
        {
            var parts = ContentParts.FromHierarchy(_root.transform);
            var wheel = parts.Find(_wheelLeft);
            parts.Click(wheel);
            Assert.AreEqual(wheel, parts.Selected);
            parts.Click(wheel);
            Assert.AreEqual(parts.Find(_body), parts.Selected, "the second click selects the assembly");
            parts.Click(wheel);
            Assert.IsNull(parts.Selected, "past the top, nothing");
            parts.Click(parts.Find(_antenna));
            Assert.AreEqual(parts.Find(_antenna), parts.Selected);
        }

        [Test]
        public void HandAtAPart_PicksTheSmallestPart_OrTheSelectedAssembly()
        {
            var parts = ContentParts.FromHierarchy(_root.transform);
            Physics.SyncTransforms();
            Assert.AreEqual(parts.Find(_wheelLeft), parts.PartAt(_wheelLeft.position, 0.05f));
            parts.Select(parts.Find(_body));
            Assert.AreEqual(parts.Find(_body), parts.PartAt(_wheelLeft.position, 0.05f), "with the body selected, grabbing a wheel moves the body");
            Assert.IsNull(parts.PartAt(new Vector3(0f, 5f, 0f), 0.05f), "nothing within reach");
        }

        [Test]
        public void MovedPart_StaysWhereItsPut_AndGlidesBack()
        {
            var parts = ContentParts.FromHierarchy(_root.transform);
            var antenna = parts.Find(_antenna);
            var home = _antenna.position;
            void Frame()
            {
                parts.RestoreHomes(); // Update
                parts.LateApply(1f / 72f); // LateUpdate, after the content placed its parts
            }

            Frame();
            var target = new Pose(home + new Vector3(0.5f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));
            parts.BeginHold(antenna);
            parts.MoveTo(antenna, target);
            parts.EndHold(antenna);
            Assert.IsTrue(antenna.IsMoved);
            Assert.That(Vector3.Distance(_antenna.position, target.position), Is.LessThan(1e-4f));

            for (var frame = 0; frame < 3; frame++)
                Frame(); // it stays put frame after frame
            Assert.That(Vector3.Distance(_antenna.position, target.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(_antenna.rotation, target.rotation), Is.LessThan(0.01f));
            Assert.IsTrue(parts.AnyMoved);

            parts.PutBack(antenna);
            for (var frame = 0; frame < 400 && antenna.IsMoved; frame++)
                Frame();
            Assert.IsFalse(antenna.IsMoved);
            Assert.That(Vector3.Distance(_antenna.position, home), Is.LessThan(1e-3f));
        }

        // ---------------------------------------------------------------- the label: always within reach

        static readonly Vector3 Eye = new Vector3(0f, 1.6f, 0f);

        [Test]
        public void Label_ForAPartOutOfReach_ComesInFrontOfTheUser()
        {
            var slim = new Bounds(new Vector3(0.6f, 0.8f, 2.4f), new Vector3(0.08f, 1.6f, 0.08f)); // 2.5 m away
            var pose = PartLabel.Placement(slim, Eye, Vector3.forward, held: false, current: null, out var near);
            Assert.IsTrue(near);
            var flat = Vector3.ProjectOnPlane(pose.position - Eye, Vector3.up);
            Assert.AreEqual(0.5f, flat.magnitude, 0.01f, "half a metre in front, within a finger's reach");
            Assert.AreEqual(Eye.y - 0.12f, pose.position.y, 0.01f, "a little below the eyes");
            Assert.Less(Vector3.Angle(flat, Vector3.ProjectOnPlane(slim.center - Eye, Vector3.up)), 1f, "towards the part");
            Assert.Greater(Vector3.Dot(pose.rotation * Vector3.forward, flat.normalized), 0.99f, "facing the user");
        }

        [Test]
        public void Label_ForAPartWithinReach_OrHeld_StaysAtThePart()
        {
            var nearby = new Bounds(new Vector3(0f, 1.2f, 0.4f), new Vector3(0.1f, 0.1f, 0.1f));
            var pose = PartLabel.Placement(nearby, Eye, Vector3.forward, held: false, current: null, out var near);
            Assert.IsFalse(near);
            Assert.AreEqual(nearby.max.y + 0.12f, pose.position.y, 0.01f, "just above the part");

            var far = new Bounds(new Vector3(0f, 1f, 3f), Vector3.one * 0.2f);
            PartLabel.Placement(far, Eye, Vector3.forward, held: true, current: null, out near);
            Assert.IsFalse(near, "a part being carried keeps its label with it");
        }

        [Test]
        public void Label_InFrontOfTheUser_StaysPutWhileTheyReach_AndFollowsWhenTheyWalkAway()
        {
            var far = new Bounds(new Vector3(0f, 1f, 3f), Vector3.one * 0.2f);
            var placed = PartLabel.Placement(far, Eye, Vector3.forward, false, null, out _);
            var leaning = Eye + new Vector3(0.15f, -0.1f, 0.1f); // the head moves as the hand reaches
            var kept = PartLabel.Placement(far, leaning, Vector3.forward, false, placed, out _);
            Assert.That(Vector3.Distance(kept.position, placed.position), Is.LessThan(1e-5f), "it doesn't move under the finger");

            var walkedAway = Eye + new Vector3(-1.5f, 0f, 0f);
            var followed = PartLabel.Placement(far, walkedAway, Vector3.forward, false, placed, out _);
            Assert.Greater(Vector3.Distance(followed.position, placed.position), 0.5f, "it comes back within reach");
        }
    }
}
