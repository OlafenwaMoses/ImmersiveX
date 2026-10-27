using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// Content that is an app inside ImmersiveX: picked in the content menu's list, the menu turns to the app's own page
    /// (<see cref="ContentAppPage"/>: its heading, what it draws, and a back button that returns to the list), and the
    /// app puts what it likes in the room under its own object. LearnX is one.
    /// <list type="bullet">
    /// <item>Everything under the app's object is the app's: its media and content aren't listed in the menu on their own,
    /// and they hide (media pauses) with the app when the menu shows something else.</item>
    /// <item>What it puts in the room is ordinary ImmersiveX content, so it's placed, anchored, held in the mapped room and
    /// moved as any other content is.</item>
    /// <item>The menu's swipes open the app's page while it's showing; <b>Content</b> on the page goes back to the list.</item>
    /// </list>
    /// </summary>
    public abstract class ContentApp : MonoBehaviour, IContentItem
    {
        [SerializeField, Tooltip("Its name in the content menu.")]
        string _title = "App";

        [SerializeField, Tooltip("What it is, in a few words, under its name in the content menu.")]
        string _kind = "App";

        public string Key => $"app/{gameObject.scene.name}/{name}";

        public string Title
        {
            get => _title;
            set => _title = value;
        }

        public string Kind
        {
            get => _kind;
            set => _kind = value;
        }

        public bool IsShown { get; private set; } = true;

        /// <summary>The app's page in the content menu, once the menu has turned to it.</summary>
        public ContentAppPage Page { get; internal set; }

        /// <summary>Show or hide the app with everything it has put in the room.</summary>
        public void SetShown(bool shown)
        {
            if (IsShown == shown)
                return;
            IsShown = shown;
            foreach (var item in OwnContent())
                item.SetShown(shown);
            OnShown(shown);
        }

        /// <summary>The content under the app's object (its media, its models).</summary>
        public IEnumerable<IContentItem> OwnContent()
        {
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != this && behaviour is IContentItem item)
                    yield return item;
        }

        /// <summary>True when <paramref name="behaviour"/> is content inside an app (the menu leaves it to the app).</summary>
        public static bool BelongsToAnApp(MonoBehaviour behaviour) =>
            behaviour is not ContentApp && behaviour.transform.parent != null && behaviour.transform.parent.GetComponentInParent<ContentApp>(true) != null;

        /// <summary>
        /// The menu has turned to the app for the first time: draw its first view on <paramref name="page"/> with
        /// <see cref="ContentAppPage.NewView"/>. The page keeps what's drawn until the app draws again.
        /// </summary>
        protected internal abstract void OnPageOpened(ContentAppPage page);

        /// <summary>The app has been shown or hidden (its content already has been).</summary>
        protected virtual void OnShown(bool shown)
        {
        }
    }
}
