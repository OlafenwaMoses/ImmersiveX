using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ImmersiveX.Tests
{
    public class SpacePanelTests
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

        static MappedSpace Room()
        {
            var space = MappedSpace.Create("Room 1", "metaquest");
            space.Signature = new SpaceSignature { FloorArea = 14.7f, CeilingHeight = 2.6f, WallLengths = new[] { 4f, 3.6f, 4f, 3.6f } };
            space.UpdatedUtc = "2026-10-22T11:04:00.0000000Z";
            return space;
        }

        [Test]
        public void ASavedRoom_SaysSo_WithItsFloorWallsAndWhen()
        {
            var (saved, headline, details, note, canRecreate) = SpacePanel.Describe(Room(), mapping: false, canRescan: true, status: null);
            Assert.IsTrue(saved);
            Assert.AreEqual("Space saved", headline);
            StringAssert.Contains("Room 1", details);
            StringAssert.Contains("14.7 m² floor", details);
            StringAssert.Contains("4 walls", details);
            StringAssert.Contains("2.6 m ceiling", details);
            StringAssert.Contains("Saved 22 Oct 2026", details);
            StringAssert.StartsWith("Re-create it", note);
            Assert.IsTrue(canRecreate);
        }

        [Test]
        public void NoRoom_SaysItIsNotSaved_AndOffersToCreateIt()
        {
            var (saved, headline, _, note, canRecreate) = SpacePanel.Describe(null, mapping: false, canRescan: true, status: null);
            Assert.IsFalse(saved);
            Assert.AreEqual("No space saved", headline);
            StringAssert.StartsWith("Create it", note);
            Assert.IsTrue(canRecreate);
        }

        [Test]
        public void WhileMapping_ReCreateWaits()
        {
            var (_, headline, details, _, canRecreate) = SpacePanel.Describe(Room(), mapping: true, canRescan: false, status: "Waiting for the room scan…");
            Assert.AreEqual("Mapping your room…", headline);
            Assert.AreEqual("Waiting for the room scan…", details);
            Assert.IsFalse(canRecreate);
        }

        [Test]
        public void TheMenusSpaceButton_OpensThePanelInItsPlace_AndBackReturns()
        {
            // the panels' side bars drop their primitives' colliders with Destroy, which edit mode reports (they go in Play)
            LogAssert.ignoreFailingMessages = true;
            var item = new GameObject("Item 1");
            item.AddComponent<FakeItem>();
            _made.Add(item);
            var menu = new GameObject("Content Menu").AddComponent<ContentMenu>();
            _made.Add(menu.gameObject);
            menu.Open();

            var footer = menu.Panel.GetComponentsInChildren<Button>(true).Where(b => b.name == "Space" || b.name == "Quest menu" || b.name == "Close app").ToList();
            Assert.AreEqual(3, footer.Count, "Space, Quest menu and Close app along the bottom");
            var space = footer.First(b => b.name == "Space");
            Assert.Less(space.transform.localPosition.x, footer.First(b => b.name == "Quest menu").transform.localPosition.x, "Space comes first");

            space.onClick.Invoke();
            Assert.IsTrue(menu.Space.IsShowing);
            Assert.IsFalse(menu.Panel.gameObject.activeSelf, "the menu hides behind the Space panel");
            Assert.IsTrue(menu.IsOpen);
            StringAssert.Contains("No space saved", menu.Space.Says);
            Assert.IsFalse(menu.Space.RecreateButton.interactable, "no session: nothing to map");

            menu.BackFromSpace();
            Assert.IsFalse(menu.Space.IsShowing);
            Assert.IsTrue(menu.Panel.gameObject.activeSelf);
        }
    }
}
