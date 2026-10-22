using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ImmersiveX.Tests
{
    public class ContentMenuTests
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
                Object.DestroyImmediate(made);
            _made.Clear();
        }

        ContentMenu MenuWith(int items)
        {
            // The panel's side bars drop their primitives' colliders with Destroy, which edit mode reports (they go in Play).
            LogAssert.ignoreFailingMessages = true;
            for (var i = 0; i < items; i++)
            {
                var item = new GameObject($"Item {i + 1}");
                item.AddComponent<FakeItem>();
                _made.Add(item);
            }

            var menu = new GameObject("Content Menu").AddComponent<ContentMenu>();
            _made.Add(menu.gameObject);
            menu.Open();
            return menu;
        }

        static Button ScrollButton(ContentMenu menu, string name) =>
            menu.Panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);

        [Test]
        public void ListThatFits_HasNoScrollButtons()
        {
            var menu = MenuWith(6);
            Assert.IsFalse(menu.Scrolls);
            Assert.IsNull(ScrollButton(menu, "Scroll up"));
            Assert.IsNull(ScrollButton(menu, "Scroll down"));
            Assert.AreEqual(6, menu.VisibleItems.Count());
        }

        [Test]
        public void ListThatOverflows_ScrollsOneRowAClick_AndStopsAtItsEnds()
        {
            var menu = MenuWith(9);
            Assert.IsTrue(menu.Scrolls);
            var up = ScrollButton(menu, "Scroll up");
            var down = ScrollButton(menu, "Scroll down");
            Assert.IsNotNull(up);
            Assert.IsNotNull(down);
            Assert.Greater(down.transform.localPosition.x, 150f, "on the right side of the list");
            Assert.Greater(up.transform.localPosition.y, down.transform.localPosition.y, "up above down");
            Assert.AreEqual(0, menu.FirstRow);
            Assert.IsFalse(up.interactable, "at the top: nothing above");
            Assert.IsTrue(down.interactable);

            down.onClick.Invoke();
            Assert.AreEqual(1, menu.FirstRow);
            Assert.AreEqual("Item 2", menu.VisibleItems.First().Title);
            Assert.IsTrue(up.interactable);

            menu.Scroll(10);
            Assert.AreEqual(3, menu.FirstRow, "the last row shows the last item");
            Assert.AreEqual("Item 9", menu.VisibleItems.Last().Title);
            Assert.IsFalse(down.interactable, "at the bottom: nothing below");

            up.onClick.Invoke();
            Assert.AreEqual(2, menu.FirstRow);
            menu.Scroll(-10);
            Assert.AreEqual(0, menu.FirstRow);
        }

        [Test]
        public void Opening_BringsTheContentThatsShowingIntoView()
        {
            var menu = MenuWith(9);
            menu.ShowOnly(menu.ItemAt(8));
            menu.Close();
            menu.Open();
            Assert.Contains(menu.ItemAt(8), menu.VisibleItems.ToList());
        }
    }
}
