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

        /// <summary>An app with one piece of its own content under it; its first view is called Home.</summary>
        sealed class FakeApp : ContentApp
        {
            public int Opened;

            protected internal override void OnPageOpened(ContentAppPage page) // the tests see the core's internals
            {
                Opened++;
                page.NewView("Home", "FAKE APP");
            }
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

        (ContentMenu menu, FakeApp app, FakeItem own) MenuWithApp()
        {
            var appObject = new GameObject("Item 0 App");
            var app = appObject.AddComponent<FakeApp>();
            app.Title = "Fake App";
            var own = new GameObject("Its media").AddComponent<FakeItem>();
            own.transform.SetParent(appObject.transform, false);
            _made.Add(appObject);
            return (MenuWith(2), app, own);
        }

        static Button ButtonLabelled(Transform panel, string label) =>
            panel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentsInChildren<Text>().Any(t => t.text == label));

        [Test]
        public void App_IsListed_ButWhatItPutsInTheRoomIsnt()
        {
            var (menu, app, own) = MenuWithApp();
            Assert.Contains(app, menu.Items.ToList());
            Assert.IsFalse(menu.Items.Contains(own), "the app's own content is the app's");
            Assert.AreEqual(3, menu.Items.Count);
        }

        [Test]
        public void PickingAnApp_ShowsIt_AndTurnsTheMenuToItsPage_ContentComesBack()
        {
            var (menu, app, _) = MenuWithApp();
            var list = menu.Panel;
            var listPose = new Pose(list.position, list.rotation);

            menu.Pick(app);
            Assert.AreSame(app, menu.Shown);
            Assert.IsNotNull(menu.AppPage);
            Assert.AreEqual(1, app.Opened, "the app draws its first view");
            Assert.AreEqual("Home", menu.AppPage.Title);
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(list.gameObject.activeSelf, "the list makes way for the page");
            Assert.AreNotSame(list, menu.Panel, "the panel showing is the app's page");
            Assert.AreEqual(listPose.position, menu.Panel.position, "the page opens where the list was");

            var content = ButtonLabelled(menu.Panel, ContentAppPage.ContentLabel);
            Assert.IsNotNull(content, "a Content button goes back to the list");
            content.onClick.Invoke();
            Assert.IsNull(menu.AppPage);
            Assert.IsTrue(menu.IsOpen);
            Assert.AreSame(list, menu.Panel);
            Assert.AreSame(app, menu.Shown, "the app keeps showing");
        }

        [Test]
        public void WhileAnAppShows_OpeningTheMenu_OpensItsPage_WithoutDrawingItAgain()
        {
            var (menu, app, _) = MenuWithApp();
            menu.Pick(app);
            menu.Close();
            Assert.IsFalse(menu.IsOpen);

            menu.Open();
            Assert.IsNotNull(menu.AppPage);
            Assert.IsTrue(menu.AppPage.IsOpen);
            Assert.AreEqual(1, app.Opened, "the page keeps the app's view");
        }

        [Test]
        public void ShowingOtherContent_HidesTheAppAndWhatItPutInTheRoom()
        {
            var (menu, app, own) = MenuWithApp();
            menu.Pick(app);
            own.SetShown(true);
            menu.BackToContent();

            menu.Pick(menu.Items.First(i => i.Title == "Item 1"));
            Assert.IsFalse(app.IsShown);
            Assert.IsFalse(own.IsShown, "its content hides with it");
            Assert.IsNull(menu.AppPage);
        }

        [Test]
        public void AnAppsViewCanHaveItsOwnBackButton()
        {
            var (menu, app, _) = MenuWithApp();
            menu.Pick(app);
            var backed = false;
            menu.AppPage.NewView("A lesson", "FAKE APP", "All lessons", () => backed = true);
            Assert.AreEqual("All lessons", menu.AppPage.Back);
            Assert.IsNull(ButtonLabelled(menu.Panel, ContentAppPage.ContentLabel), "the old view is gone");
            ButtonLabelled(menu.Panel, "All lessons").onClick.Invoke();
            Assert.IsTrue(backed);
        }
    }
}
