using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// A <see cref="ContentApp"/>'s page in the content menu: the menu's own panel at its full size, with the app's view
    /// in it. Each view has the menu's header (a gold eyebrow, a title that shrinks to fit, and <b>×</b> to put the menu
    /// away) and its footer (a back button, <b>Quest menu</b> and <b>Close app</b>). The back button is the app's
    /// (<b>All lessons</b>, say), or <b>Content</b>, back to the menu's list. The app draws the rest between
    /// <see cref="BodyTop"/> and <see cref="BodyBottom"/>, in millimetres from the panel's middle.
    /// </summary>
    public sealed class ContentAppPage
    {
        public const float Width = 460f;
        public const float Height = 584f;
        public const float HeaderHeight = 76f;
        public const float FooterHeight = 88f;
        public const float Top = Height / 2f;
        public const float BodyTop = Top - HeaderHeight;
        public const float BodyBottom = -Height / 2f + FooterHeight;

        /// <summary>What the back button says when it goes back to the menu's list.</summary>
        public const string ContentLabel = "Content";

        const float PanelRadius = 28f;

        readonly ContentMenu _menu;
        readonly RectTransform _canvas;
        RectTransform _view;

        public ContentApp App { get; }

        /// <summary>The panel (it moves by its side bars, as the menu's list does).</summary>
        public GameObject Panel { get; }

        /// <summary>The view showing, or null before the app has drawn one.</summary>
        public RectTransform View => _view;

        /// <summary>The title of the view showing.</summary>
        public string Title { get; private set; }

        /// <summary>What the back button says on the view showing.</summary>
        public string Back { get; private set; }

        public bool IsOpen => Panel != null && Panel.activeSelf;

        internal ContentAppPage(ContentMenu menu, ContentApp app)
        {
            _menu = menu;
            App = app;
            Panel = new GameObject($"Content Menu Page: {app.Title}");
            Panel.SetActive(false);
            Panel.transform.SetParent(menu.transform, false);
            _canvas = WorldUi.CreateCanvas(Panel.transform, new Vector2(Width, Height));
            var background = _canvas.Find("Background").GetComponent<Image>();
            Brand.Round(background, PanelRadius).color = Brand.Panel;
            Brand.Hairline(_canvas, Vector2.zero, new Vector2(Width, Height), PanelRadius, Brand.LineGold);
            PanelMover.Attach(Panel, new Vector2(Width, Height) * 0.001f);
        }

        /// <summary>
        /// A fresh view in place of the one showing, with the menu's header and footer. <paramref name="back"/> labels the
        /// footer's first button and <paramref name="onBack"/> is what it does; without them it's <b>Content</b>, back to
        /// the menu's list. Returns the view to draw on (panel-sized, centred).
        /// </summary>
        public RectTransform NewView(string title, string eyebrow, string back = null, UnityAction onBack = null)
        {
            if (_view != null)
            {
                _view.gameObject.SetActive(false); // gone from this frame's button search, before it's destroyed
                Discard(_view.gameObject);
            }

            Title = title;
            Back = back ?? ContentLabel;
            _view = WorldUi.Box(_canvas, "View", Vector2.zero, new Vector2(Width, Height));
            const float left = -Width / 2f + 20f, right = Width / 2f - 70f; // the title runs up to the ×
            var centre = (left + right) / 2f;
            var brow = WorldUi.Label(_view, "Eyebrow", eyebrow ?? string.Empty, 14, new Vector2(centre, Top - 26f), new Vector2(right - left, 20f), FontStyle.Bold,
                TextAnchor.MiddleLeft);
            brow.color = Brand.Gold;
            var heading = WorldUi.Label(_view, "Title", title ?? string.Empty, 30, new Vector2(centre, Top - 52f), new Vector2(right - left, 40f), FontStyle.Bold,
                TextAnchor.MiddleLeft);
            heading.color = Brand.Ink;
            heading.resizeTextForBestFit = true; // a long title gets smaller, then wraps
            heading.resizeTextMinSize = 16;
            heading.resizeTextMaxSize = 30;
            Brand.Pill(_view, "×", new Vector2(Width / 2f - 38f, Top - 40f), new Vector2(48f, 48f), false, Close, 26);

            var y = -Height / 2f + FooterHeight / 2f;
            Brand.Pill(_view, Back, new Vector2(-146f, y), new Vector2(136f, 54f), false, onBack ?? BackToContent, 18);
            Brand.Pill(_view, "Quest menu", new Vector2(0f, y), new Vector2(136f, 54f), false, _menu.OpenDeviceMenu);
            Brand.Pill(_view, "Close app", new Vector2(146f, y), new Vector2(136f, 54f), true, _menu.QuitApp);
            return _view;
        }

        /// <summary>Put the menu away (while the app's media plays, say). The page stays as it is for next time.</summary>
        public void Close() => _menu.Close();

        /// <summary>Bring the menu back in front of the user on this page.</summary>
        public void Open() => _menu.OpenApp(App, inFront: true);

        /// <summary>Back to the menu's list (the app stays showing).</summary>
        public void BackToContent() => _menu.BackToContent();

        internal void Destroy()
        {
            if (Panel != null)
                Discard(Panel);
        }

        static void Discard(GameObject gameObject)
        {
            if (Application.isPlaying)
                Object.Destroy(gameObject);
            else
                Object.DestroyImmediate(gameObject); // edit-mode tests
        }
    }
}
