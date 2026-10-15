using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveX
{
    /// <summary>
    /// The content menu: a panel listing the scene's content (see <see cref="IContentItem"/>), with Quest menu and Close app
    /// buttons. A fast sideways swipe opens it: the right hand from right to left, or the left hand from left to right. The
    /// reverse swipe closes it: the right hand from left to right, or the left hand from right to left (<see cref="HandSwipes"/>).
    /// <list type="bullet">
    /// <item>One piece of content shows at a time: the one picked in the list, which is highlighted. Picking another one shows
    /// that one instead. The app remembers which one was showing.</item>
    /// <item>The opening swipe puts it in front of the user's eyes, close to the headset, wherever they are; while it's open
    /// elsewhere, it comes to them. Moved by its side bars or frame, it stays where it's put until the next opening swipe.</item>
    /// <item>The ray clicks its buttons and a finger presses them.</item>
    /// </list>
    /// <see cref="ImmersiveXSession"/> adds it when the session is ready (Settings: Content Menu).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ContentMenu : MonoBehaviour
    {
        const string ShownKey = "ImmersiveX.ContentMenu.Shown";
        const float Width = 460f;
        const float HeaderHeight = 76f;
        const float RowHeight = 62f;
        const float RowGap = 8f;
        const float FooterHeight = 88f;
        const int RowsPerPage = 6;
        const float OpenDistance = 0.55f;
        const float OpenBelowEyes = 0.12f;
        const float InFrontWithin = 1.2f; // "in front of the user": this close…
        const float InFrontAngle = 40f; // …and this near where they look

        const float PanelRadius = 28f;
        const float RowRadius = 16f;

        sealed class Row
        {
            public Image Background;
            public Image Border;
            public BrandHover Hover;
            public Text Title;
            public Text Kind;
            public GameObject Showing;
            public IContentItem Item;
        }

        public static ContentMenu Instance { get; private set; }

        readonly List<IContentItem> _items = new List<IContentItem>();
        readonly List<Row> _rows = new List<Row>();
        XROrigin _origin;
        HandSwipes _swipes;
        GameObject _panel;
        Text _pageLabel;
        int _page;
        int _builtFor = -1;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        /// <summary>The menu's panel (automation and tests move it about).</summary>
        public Transform Panel => _panel != null ? _panel.transform : null;
        public IReadOnlyList<IContentItem> Items => _items;
        public IContentItem Shown { get; private set; }
        public HandSwipes Swipes => _swipes;

        /// <summary>Add the menu (and its swipe watcher) for <paramref name="origin"/>'s user, and show one piece of content.</summary>
        public static ContentMenu Create(XROrigin origin)
        {
            var menu = new GameObject("Content Menu").AddComponent<ContentMenu>();
            menu._origin = origin;
            menu._swipes = HandSwipes.Create(origin);
            menu._swipes.transform.SetParent(menu.transform, false);
            menu._swipes.Swiped += (hand, direction) =>
            {
                var action = direction == SwipeDirection.Outward
                    ? menu.IsOpen ? "closing the menu" : "the menu is already closed"
                    : !menu.IsOpen ? "opening the menu in front of the user" : menu.IsInFront() ? "keeping the menu in front of the user" : "bringing the menu in front of the user";
                ImmersiveXLog.Info($"Content menu: {(hand == InteractorHandedness.Right ? "right" : "left")} hand swiped {(direction == SwipeDirection.Outward ? "back" : "across")}; {action}.");
                menu.Swipe(direction);
            };
            menu.FindItems();
            menu.ShowOnly(menu.Remembered() ?? menu._items.FirstOrDefault());
            return menu;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ---------------------------------------------------------------- opening and closing

        /// <summary>What a swipe does: the opening swipe (<see cref="SwipeDirection.Inward"/>) opens the menu in front of the user, and the reverse closes it.</summary>
        public void Swipe(SwipeDirection direction)
        {
            if (direction == SwipeDirection.Outward)
                Close();
            else if (direction == SwipeDirection.Inward)
                Open();
        }

        /// <summary>Close the menu when it's open in front of the user; otherwise open it there (automation).</summary>
        public void Toggle()
        {
            if (IsOpen && IsInFront())
                Close();
            else
                Open();
        }

        /// <summary>Open, close to the user and near where they look.</summary>
        public bool IsInFront()
        {
            var head = Viewer();
            if (!IsOpen || head == null)
                return false;
            var toPanel = _panel.transform.position - head.position;
            return toPanel.magnitude <= InFrontWithin && Vector3.Angle(head.forward, toPanel) <= InFrontAngle;
        }

        public void Open()
        {
            FindItems();
            if (_page * RowsPerPage >= _items.Count)
                _page = 0;
            Build();
            Paint();
            Place();
            _panel.SetActive(true);
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            _panel.SetActive(false);
        }

        /// <summary>In front of the user's eyes, close to the headset, a little below eye level, facing them.</summary>
        void Place()
        {
            var head = Viewer();
            if (head == null)
                return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            _panel.transform.SetPositionAndRotation(head.position + forward * OpenDistance - Vector3.up * OpenBelowEyes, Quaternion.LookRotation(forward, Vector3.up));
        }

        // ---------------------------------------------------------------- content

        /// <summary>Show <paramref name="item"/> and hide every other piece of content.</summary>
        public void ShowOnly(IContentItem item)
        {
            FindItems();
            foreach (var other in _items)
                if (other != item && other.IsShown)
                    other.SetShown(false);
            if (item != null)
            {
                if (!item.IsShown)
                    item.SetShown(true);
                PlayerPrefs.SetString(ShownKey, item.Key);
                PlayerPrefs.Save();
                ImmersiveXLog.Info($"Content menu: showing '{item.Title}' ({item.Kind}); {_items.Count - 1} other piece(s) hidden.");
            }

            Shown = item;
            if (IsOpen)
                Paint();
        }

        /// <summary>The piece of content at <paramref name="index"/> in the list (0-based), or null.</summary>
        public IContentItem ItemAt(int index) => index >= 0 && index < _items.Count ? _items[index] : null;

        void FindItems()
        {
            _items.Clear();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour is IContentItem item)
                    _items.Add(item);
            _items.Sort((a, b) => Compare(HierarchyPath(((MonoBehaviour)a).transform), HierarchyPath(((MonoBehaviour)b).transform)));
            if (Shown != null && !_items.Contains(Shown))
                Shown = null;
        }

        IContentItem Remembered()
        {
            var key = PlayerPrefs.GetString(ShownKey, string.Empty);
            return key.Length > 0 ? _items.FirstOrDefault(i => i.Key == key) : null;
        }

        static List<int> HierarchyPath(Transform transform)
        {
            var path = new List<int>();
            for (var t = transform; t != null; t = t.parent)
                path.Insert(0, t.GetSiblingIndex());
            path.Insert(0, transform.gameObject.scene.buildIndex);
            return path;
        }

        static int Compare(List<int> a, List<int> b)
        {
            for (var i = 0; i < Mathf.Min(a.Count, b.Count); i++)
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            return a.Count.CompareTo(b.Count);
        }

        // ---------------------------------------------------------------- the panel

        void Build()
        {
            var rows = Mathf.Clamp(_items.Count, 1, RowsPerPage);
            if (_panel != null && _builtFor == rows)
                return;
            var wasPlaced = _panel != null ? new Pose(_panel.transform.position, _panel.transform.rotation) : (Pose?)null;
            if (_panel != null)
                Destroy(_panel);
            _builtFor = rows;
            _rows.Clear();

            var height = HeaderHeight + rows * (RowHeight + RowGap) + FooterHeight;
            _panel = new GameObject("Content Menu Panel");
            _panel.SetActive(false);
            _panel.transform.SetParent(transform, false);
            if (wasPlaced.HasValue)
                _panel.transform.SetPositionAndRotation(wasPlaced.Value.position, wasPlaced.Value.rotation);
            var canvas = WorldUi.CreateCanvas(_panel.transform, new Vector2(Width, height));
            var background = canvas.Find("Background").GetComponent<Image>();
            Brand.Round(background, PanelRadius).color = Brand.Panel;
            Brand.Hairline(canvas, Vector2.zero, new Vector2(Width, height), PanelRadius, Brand.LineGold);
            var top = height * 0.5f;

            var eyebrow = WorldUi.Label(canvas, "Brand", "IMMERSIVEX", 14, new Vector2(-Width * 0.5f + 128f, top - 26f), new Vector2(216f, 20f), FontStyle.Bold, TextAnchor.MiddleLeft);
            eyebrow.color = Brand.Gold;
            var title = WorldUi.Label(canvas, "Title", "Content", 30, new Vector2(-Width * 0.5f + 128f, top - 52f), new Vector2(216f, 40f), FontStyle.Bold, TextAnchor.MiddleLeft);
            title.color = Brand.Ink;
            _pageLabel = WorldUi.Label(canvas, "Page", string.Empty, 17, new Vector2(70f, top - 40f), new Vector2(90f, 36f), FontStyle.Normal, TextAnchor.MiddleCenter);
            _pageLabel.color = Brand.Ink2;
            Brand.Pill(canvas, "‹", new Vector2(4f, top - 40f), new Vector2(44f, 44f), false, () => Turn(-1), 24);
            Brand.Pill(canvas, "›", new Vector2(136f, top - 40f), new Vector2(44f, 44f), false, () => Turn(1), 24);
            Brand.Pill(canvas, "×", new Vector2(Width * 0.5f - 38f, top - 40f), new Vector2(48f, 48f), false, Close, 26);

            for (var i = 0; i < rows; i++)
            {
                var y = top - HeaderHeight - i * (RowHeight + RowGap) - RowHeight * 0.5f;
                _rows.Add(BuildRow(canvas, i, y));
            }

            var footer = -height * 0.5f + FooterHeight * 0.5f;
            Brand.Pill(canvas, "Quest menu", new Vector2(-104f, footer), new Vector2(196f, 54f), false, OpenDeviceMenu);
            Brand.Pill(canvas, "Close app", new Vector2(104f, footer), new Vector2(196f, 54f), true, QuitApp);

            // Moved by its side anchors (the ray or a hand) or its frame (a hand); the ray on its face only clicks.
            PanelMover.Attach(_panel, new Vector2(Width / 1000f, height / 1000f));
        }

        Row BuildRow(Transform canvas, int index, float y)
        {
            var row = new Row();
            var size = new Vector2(Width - 32f, RowHeight);
            var box = WorldUi.Box(canvas, $"Row {index + 1}", new Vector2(0f, y), size);
            row.Background = Brand.Round(box.gameObject.AddComponent<Image>(), RowRadius);
            row.Background.color = Brand.Card;
            row.Border = Brand.Hairline(box, Vector2.zero, size, RowRadius, Brand.Line);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = row.Background;
            button.transition = Selectable.Transition.None; // BrandHover lights the card up under the ray or a finger
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            row.Hover = box.gameObject.AddComponent<BrandHover>();
            row.Hover.Background = row.Background;
            row.Hover.Border = row.Border;
            button.onClick.AddListener(() =>
            {
                if (row.Item != null)
                    ShowOnly(row.Item);
            });
            row.Title = WorldUi.Label(box, "Title", string.Empty, 22, new Vector2(-46f, 11f), new Vector2(size.x - 130f, 30f), FontStyle.Bold, TextAnchor.MiddleLeft);
            row.Kind = WorldUi.Label(box, "Kind", string.Empty, 16, new Vector2(-46f, -15f), new Vector2(size.x - 130f, 24f), FontStyle.Normal, TextAnchor.MiddleLeft);

            // "Showing": a small gold pill on the content that's showing.
            var pill = WorldUi.Box(box, "Showing", new Vector2(size.x * 0.5f - 62f, 0f), new Vector2(100f, 30f));
            Brand.Round(pill.gameObject.AddComponent<Image>(), 15f).color = Brand.GoldButton;
            WorldUi.Label(pill, "Label", "SHOWING", 13, Vector2.zero, new Vector2(100f, 30f), FontStyle.Bold, TextAnchor.MiddleCenter).color = Brand.OnGold;
            row.Showing = pill.gameObject;
            return row;
        }

        void Turn(int pages)
        {
            var count = Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)RowsPerPage));
            _page = (_page + pages + count) % count;
            Paint();
        }

        /// <summary>Fill the rows for the current page and highlight the content that's showing.</summary>
        void Paint()
        {
            if (_panel == null)
                return;
            var pages = Mathf.Max(1, Mathf.CeilToInt(_items.Count / (float)RowsPerPage));
            _pageLabel.text = pages > 1 ? $"{_page + 1} / {pages}" : string.Empty;
            foreach (var arrow in _pageLabel.transform.parent.GetComponentsInChildren<Button>(true))
                if (arrow.name == "‹" || arrow.name == "›")
                    arrow.gameObject.SetActive(pages > 1);

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var item = ItemAt(_page * RowsPerPage + i);
                row.Item = item;
                row.Background.gameObject.SetActive(item != null || (i == 0 && _items.Count == 0));
                if (item == null)
                {
                    row.Title.text = i == 0 && _items.Count == 0 ? "No content in this scene" : string.Empty;
                    row.Title.color = Brand.Ink2;
                    row.Kind.text = string.Empty;
                    row.Showing.SetActive(false);
                    row.Hover.Set(Brand.Card, Brand.Line, Brand.Card, Brand.Line, Brand.Ink2, Brand.Ink2);
                    continue;
                }

                var showing = item == Shown;
                row.Title.text = item.Title;
                row.Kind.text = item.Kind;
                row.Showing.SetActive(showing);
                if (showing)
                    row.Hover.Set(Brand.GoldTint, Brand.LineGold, Brand.GoldTint, Brand.Gold, Brand.Ink, Brand.Ink);
                else
                    row.Hover.Set(Brand.Card, Brand.Line, Brand.GhostHover, Brand.LineGold, Brand.Ink, Brand.Ink);
                row.Title.color = showing ? Brand.GoldLight : Brand.Ink;
                row.Kind.color = Brand.Ink2;
            }
        }

        // ---------------------------------------------------------------- device

        public void OpenDeviceMenu()
        {
            if (Services.TryGet<IDeviceMenuProvider>(out var device) && device.Open() != null)
            {
                Close();
                return;
            }

            ImmersiveXLog.Warn("Content menu: this device's own menu can't be opened from here (it can on a Quest).");
        }

        public void QuitApp()
        {
            ImmersiveXLog.Info("Content menu: closing the app.");
            Application.Quit(); // ignored in the editor's Play mode
        }

        /// <summary>One line for logs and automation.</summary>
        public string Describe() =>
            $"menu {(IsOpen ? "open" : "closed")} · showing {(Shown != null ? $"'{Shown.Title}'" : "nothing")} · " +
            $"{_items.Count} item(s): {string.Join(", ", _items.Select((i, n) => $"{n + 1}. {i.Title}{(i.IsShown ? " (shown)" : string.Empty)}"))} · " +
            $"swipes recognised: {(_swipes != null ? _swipes.Count : 0)}";

        Transform Viewer() =>
            _origin != null && _origin.Camera != null ? _origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
    }
}
