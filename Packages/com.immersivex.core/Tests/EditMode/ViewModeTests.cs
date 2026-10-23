using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ImmersiveX.Tests
{
    public class ViewModeTests
    {
        sealed class FakeItem : MonoBehaviour, IContentItem
        {
            public string Key => name;
            public string Title => name;
            public string Kind => "Test";
            public bool IsShown { get; private set; }
            public void SetShown(bool shown) => IsShown = shown;
        }

        readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made)
                if (made != null)
                    Object.DestroyImmediate(made);
            _made.Clear();
        }

        ViewModes Modes()
        {
            // runtime objects drop colliders and materials with Destroy, which edit mode reports (they go in Play)
            LogAssert.ignoreFailingMessages = true;
            var modes = ViewModes.Create(null);
            _made.Add(modes.gameObject);
            return modes;
        }

        [Test]
        public void StartsInMixedReality_WithTheVirtualWorldHidden()
        {
            var modes = Modes();
            Assert.AreEqual(ViewMode.Mixed, modes.Mode);
            Assert.IsFalse(modes.World.IsShowing);
            Assert.IsFalse(modes.Guard.gameObject.activeSelf);
        }

        [Test]
        public void VirtualReality_ShowsTheWorldAndTheRoom_AndMixedRealityHidesThemAgain()
        {
            var modes = Modes();
            var changes = new List<ViewMode>();
            modes.Changed += changes.Add;

            modes.Set(ViewMode.Virtual);
            Assert.AreEqual(ViewMode.Virtual, modes.Mode);
            Assert.IsTrue(modes.World.IsShowing);
            Assert.IsTrue(modes.Guard.gameObject.activeSelf);
            Assert.IsNotNull(modes.World.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "Floor")?.sharedMaterial, "the world's shader is in Resources");

            modes.Toggle();
            Assert.AreEqual(ViewMode.Mixed, modes.Mode);
            Assert.IsFalse(modes.World.IsShowing);
            Assert.IsFalse(modes.Guard.gameObject.activeSelf);
            CollectionAssert.AreEqual(new[] { ViewMode.Virtual, ViewMode.Mixed }, changes);
        }

        [Test]
        public void RoomGuard_PutsAWallOnEveryEdgeOfTheFloor_UpToTheCeiling()
        {
            var modes = Modes();
            var square = new[] { new Vector2(-2f, -1.5f), new Vector2(2f, -1.5f), new Vector2(2f, 1.5f), new Vector2(-2f, 1.5f) };
            modes.Guard.Show(square, new Pose(new Vector3(1f, 0f, 2f), Quaternion.identity), 2.5f, null);

            Assert.AreEqual(4, modes.Guard.Walls);
            var mesh = modes.Guard.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(16, mesh.vertexCount, "a quad a wall");
            Assert.AreEqual(2.5f, mesh.bounds.max.y, 1e-4f, "up to the ceiling");
            Assert.AreEqual(0f, mesh.bounds.min.y, 1e-4f, "from the floor");
            Assert.AreEqual(3f, mesh.bounds.max.x, 1e-4f, "where the floor is");
            var metres = new List<Vector2>();
            mesh.GetUVs(0, metres);
            Assert.AreEqual(4f, metres.Max(m => m.x), 1e-4f, "uv along a wall is in metres");
        }

        [Test]
        public void Menu_HasMrAndVrButtons_ThatSwitchTheView()
        {
            var modes = Modes();
            for (var i = 0; i < 3; i++)
            {
                var item = new GameObject($"Item {i + 1}");
                item.AddComponent<FakeItem>();
                _made.Add(item);
            }

            var menu = new GameObject("Content Menu").AddComponent<ContentMenu>();
            _made.Add(menu.gameObject);
            menu.Open();
            var buttons = menu.Panel.GetComponentsInChildren<Button>(true);
            var mr = buttons.FirstOrDefault(b => b.name == "MR");
            var vr = buttons.FirstOrDefault(b => b.name == "VR");
            Assert.IsNotNull(mr);
            Assert.IsNotNull(vr);
            Assert.Less(mr.transform.localPosition.x, vr.transform.localPosition.x, "MR first, then VR");
            Assert.IsNotNull(vr.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Icon")?.sprite, "each has an icon");

            vr.onClick.Invoke();
            Assert.AreEqual(ViewMode.Virtual, modes.Mode);
            StringAssert.Contains("view VR", menu.Describe());
            mr.onClick.Invoke();
            Assert.AreEqual(ViewMode.Mixed, modes.Mode);
        }
    }
}
