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
    /// <item>When there's more content than fits (six rows), up and down buttons appear on the right of the list, with a
    /// track between them showing where the rows are in the list; each click scrolls one row.</item>
    /// <item>MR and VR buttons in its header switch between seeing content in the real room and in a virtual world
    /// (<see cref="ViewModes"/>); MR at every start.</item>
    /// <item>It opens in front of the user when the app starts.</item>
    /// <item>Its <b>Space</b> button (a green dot when the room is saved, amber when not) opens <see cref="SpacePanel"/>: what's
    /// saved about the room, with Re-create space (map it again; the menu comes back when that's done) and Back.</item>
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
        const float ScrollColumn = 60f; // the scroll buttons' column, right of the rows, when the list overflows
        const float ScrollButton = 48f;
        const float OpenDistance = 0.55f;
        const float OpenBelowEyes = 0.12f;
        const float InFrontWithin = 1.2f; // "in front of the user": this close…
        const float InFrontAngle = 40f; // …and this near where they look

        const float PanelRadius = 28f;
        const float RowRadius = 16f;

        sealed class Scroller
        {
            public Button Up, Down;
            public RectTransform Track, Thumb;
        }

        sealed class ModeButton
        {
            public ViewMode Mode;
            public Button Button;
            public Image Background, Icon;
            public Text Label;
            public BrandHover Hover;
        }

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
        readonly List<ModeButton> _modeButtons = new List<ModeButton>();
        ViewModes _modes;
        SpacePanel _space;
        Image _spaceDot;
        bool _awaitingScan;
        XROrigin _origin;
        HandSwipes _swipes;
        GameObject _panel;
        Scroller _scroller;
        int _first; // the item in the top row
        int _builtFor = -1;

        public bool IsOpen => (_panel != null && _panel.activeSelf) || (_space != null && _space.IsShowing);

        /// <summary>The Space panel, once it has been opened.</summary>
        public SpacePanel Space => _space;

        /// <summary>The menu's panel (automation and tests move it about).</summary>
        public Transform Panel => _panel != null ? _panel.transform : null;
        public IReadOnlyList<IContentItem> Items => _items;
        public IContentItem Shown { get; private set; }
        public HandSwipes Swipes => _swipes;

        /// <summary>True when there's more content than rows, so the list scrolls.</summary>
        public bool Scrolls => _items.Count > RowsPerPage;

        /// <summary>The item in the top row (0-based).</summary>
        public int FirstRow => _first;

        /// <summary>The items in the rows, top to bottom.</summary>
        public IEnumerable<IContentItem> VisibleItems => _items.Skip(_first).Take(RowsPerPage);

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
            if (_modes != null)
                _modes.Changed -= OnModeChanged;
            if (Instance == this)
                Instance = null;
        }

        void OnModeChanged(ViewMode mode)
        {
            if (IsOpen)
                Paint();
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
            if (_space != null)
                _space.Hide();
            _first = ClampFirst(_first);
            var shown = Shown != null ? _items.IndexOf(Shown) : -1;
            if (shown >= 0 && (shown < _first || shown >= _first + RowsPerPage))
                _first = ClampFirst(shown - RowsPerPage / 2); // the content that's showing is in view when the menu opens
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
            if (_space != null)
                _space.Hide();
        }

        // ---------------------------------------------------------------- the room's space

        /// <summary>Show the Space panel where the menu is (the menu hides behind it until Back).</summary>
        public void OpenSpace()
        {
            if (_panel == null)
                Open();
            if (_space == null)
                _space = SpacePanel.Create(transform, BackFromSpace, RecreateSpace);
            _space.Show(new Pose(_panel.transform.position, _panel.transform.rotation));
            _panel.SetActive(false);
            ImmersiveXLog.Info($"Content menu: Space panel · {_space.Says}");
        }

        public void BackFromSpace()
        {
            if (_space != null)
                _space.Hide();
            if (_panel != null)
            {
                _panel.SetActive(true);
                Paint();
            }
        }

        /// <summary>Map the room again (Quest: Space Setup); the menu comes back in front of the user when it's done.</summary>
        public void RecreateSpace()
        {
            var session = ImmersiveXSession.Instance;
            if (session == null || !session.CanRescanRoom)
            {
                ImmersiveXLog.Warn("Content menu: the room can't be mapped again right now.");
                return;
            }

            ImmersiveXLog.Info("Content menu: re-creating the room's space.");
            Close();
            _awaitingScan = true;
            session.RescanRoom();
        }

        void Update()
        {
            var session = ImmersiveXSession.Instance;
            if (!_awaitingScan || session == null || session.IsMappingRoom)
                return;
            _awaitingScan = false;
            ImmersiveXLog.Info($"Content menu: the room mapping finished ({session.RoomStatus}); back to the menu.");
            Open();
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
            var scrolls = Scrolls;
            if (_modes == null && ViewModes.Instance != null)
            {
                _modes = ViewModes.Instance;
                _modes.Changed += OnModeChanged;
            }

            var key = rows * 4 + (scrolls ? 1 : 0) + (_modes != null ? 2 : 0);
            if (_panel != null && _builtFor == key)
                return;
            var wasPlaced = _panel != null ? new Pose(_panel.transform.position, _panel.transform.rotation) : (Pose?)null;
            if (_panel != null)
                Destroy(_panel);
            _builtFor = key;
            _rows.Clear();
            _modeButtons.Clear();
            _scroller = null;

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
            Brand.Pill(canvas, "×", new Vector2(Width * 0.5f - 38f, top - 40f), new Vector2(48f, 48f), false, Close, 26);
            if (_modes != null)
                BuildModeSwitch(canvas, new Vector2(66f, top - 40f));

            var rowWidth = Width - 32f - (scrolls ? ScrollColumn : 0f);
            var rowX = scrolls ? -ScrollColumn * 0.5f : 0f;
            for (var i = 0; i < rows; i++)
            {
                var y = top - HeaderHeight - i * (RowHeight + RowGap) - RowHeight * 0.5f;
                _rows.Add(BuildRow(canvas, i, new Vector2(rowX, y), rowWidth));
            }

            if (scrolls)
                _scroller = BuildScroller(canvas, top - HeaderHeight, rows * (RowHeight + RowGap) - RowGap);

            var footer = -height * 0.5f + FooterHeight * 0.5f;
            var space = Brand.Pill(canvas, "Space", new Vector2(-146f, footer), new Vector2(136f, 54f), false, OpenSpace);
            space.GetComponentInChildren<Text>().rectTransform.anchoredPosition = new Vector2(9f, 0f);
            _spaceDot = Brand.Round(WorldUi.Box(space.transform, "Saved", new Vector2(-38f, 0f), new Vector2(12f, 12f)).gameObject.AddComponent<Image>(), 6f);
            _spaceDot.raycastTarget = false;
            Brand.Pill(canvas, "Quest menu", new Vector2(0f, footer), new Vector2(136f, 54f), false, OpenDeviceMenu);
            Brand.Pill(canvas, "Close app", new Vector2(146f, footer), new Vector2(136f, 54f), true, QuitApp);

            // Moved by its side anchors (the ray or a hand) or its frame (a hand); the ray on its face only clicks.
            PanelMover.Attach(_panel, new Vector2(Width / 1000f, height / 1000f));
        }

        Row BuildRow(Transform canvas, int index, Vector2 position, float width)
        {
            var row = new Row();
            var size = new Vector2(width, RowHeight);
            var box = WorldUi.Box(canvas, $"Row {index + 1}", position, size);
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

        /// <summary>The MR and VR buttons, side by side in one pill: an icon and its name each; the mode in use is gold.</summary>
        void BuildModeSwitch(Transform canvas, Vector2 centre)
        {
            const float segment = 84f, height = 48f;
            var frame = WorldUi.Box(canvas, "View mode", centre, new Vector2(segment * 2f + 8f, height + 8f));
            Brand.Round(frame.gameObject.AddComponent<Image>(), (height + 8f) * 0.5f).color = Brand.Card;
            Brand.Hairline(frame, Vector2.zero, frame.sizeDelta, (height + 8f) * 0.5f, Brand.Line);
            foreach (var mode in new[] { ViewMode.Mixed, ViewMode.Virtual })
            {
                var button = new ModeButton { Mode = mode };
                var box = WorldUi.Box(frame, mode == ViewMode.Mixed ? "MR" : "VR", new Vector2((mode == ViewMode.Mixed ? -0.5f : 0.5f) * segment, 0f), new Vector2(segment, height));
                button.Background = Brand.Round(box.gameObject.AddComponent<Image>(), height * 0.5f);
                button.Button = box.gameObject.AddComponent<Button>();
                button.Button.targetGraphic = button.Background;
                button.Button.transition = Selectable.Transition.None;
                button.Button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.Button.onClick.AddListener(() => _modes.Set(mode));
                var icon = WorldUi.Box(box, "Icon", new Vector2(-17f, 0f), new Vector2(34f, 34f));
                button.Icon = icon.gameObject.AddComponent<Image>();
                button.Icon.sprite = ModeIcons.For(mode);
                button.Icon.preserveAspect = true;
                button.Icon.raycastTarget = false;
                button.Label = WorldUi.Label(box, "Label", mode == ViewMode.Mixed ? "MR" : "VR", 17, new Vector2(20f, 0f), new Vector2(40f, height), FontStyle.Bold, TextAnchor.MiddleCenter);
                button.Label.raycastTarget = false;
                button.Hover = box.gameObject.AddComponent<BrandHover>();
                button.Hover.Background = button.Background;
                button.Hover.Text = button.Label;
                _modeButtons.Add(button);
            }
        }

        /// <summary>Switch between MR and VR (automation and tests; the header's buttons do the same).</summary>
        public void SetViewMode(ViewMode mode) => _modes?.Set(mode);

        /// <summary>The scroll column right of the rows: an up button at the top of the list, a down button at the bottom, and
        /// between them a track whose gold thumb shows which part of the list the rows hold.</summary>
        Scroller BuildScroller(Transform canvas, float listTop, float listHeight)
        {
            var x = Width * 0.5f - 16f - ScrollColumn * 0.5f + 6f;
            var scroller = new Scroller
            {
                Up = Brand.Pill(canvas, "▲", new Vector2(x, listTop - ScrollButton * 0.5f), new Vector2(ScrollButton, ScrollButton), false, () => Scroll(-1), 18),
                Down = Brand.Pill(canvas, "▼", new Vector2(x, listTop - listHeight + ScrollButton * 0.5f), new Vector2(ScrollButton, ScrollButton), false, () => Scroll(1), 18),
            };
            scroller.Up.name = "Scroll up";
            scroller.Down.name = "Scroll down";
            var trackHeight = listHeight - 2f * ScrollButton - 24f;
            scroller.Track = WorldUi.Box(canvas, "Scroll track", new Vector2(x, listTop - listHeight * 0.5f), new Vector2(6f, trackHeight));
            Brand.Round(scroller.Track.gameObject.AddComponent<Image>(), 3f).color = Brand.Line;
            scroller.Thumb = WorldUi.Box(scroller.Track, "Thumb", Vector2.zero, new Vector2(6f, trackHeight));
            Brand.Round(scroller.Thumb.gameObject.AddComponent<Image>(), 3f).color = Brand.Gold;
            return scroller;
        }

        /// <summary>Scroll the list by <paramref name="rows"/> rows (negative: up), stopping at its ends.</summary>
        public void Scroll(int rows)
        {
            var first = ClampFirst(_first + rows);
            if (first == _first)
                return;
            _first = first;
            ImmersiveXLog.Info($"Content menu: scrolled {(rows < 0 ? "up" : "down")}; rows {_first + 1}–{Mathf.Min(_items.Count, _first + RowsPerPage)} of {_items.Count}.");
            Paint();
        }

        int ClampFirst(int first) => Mathf.Clamp(first, 0, Mathf.Max(0, _items.Count - RowsPerPage));

        /// <summary>Fill the rows for the current page and highlight the content that's showing.</summary>
        void Paint()
        {
            if (_panel == null)
                return;
            PaintScroller();
            PaintModeSwitch();
            if (_spaceDot != null)
            {
                var session = ImmersiveXSession.Instance;
                _spaceDot.color = session != null && session.CurrentSpace != null ? SpacePanel.Saved : SpacePanel.NotSaved;
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var item = ItemAt(_first + i);
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

        void PaintModeSwitch()
        {
            foreach (var button in _modeButtons)
            {
                var on = _modes != null && _modes.Mode == button.Mode;
                if (on)
                    button.Hover.Set(Brand.GoldButton, null, Brand.Gold, null, Brand.OnGold, Brand.OnGold);
                else
                    button.Hover.Set(new Color(0f, 0f, 0f, 0f), null, Brand.GhostHover, null, Brand.Ink2, Brand.Ink);
                button.Icon.color = on ? Brand.OnGold : Brand.Ink2;
            }
        }

        void PaintScroller()
        {
            if (_scroller == null)
                return;
            var last = Mathf.Max(0, _items.Count - RowsPerPage);
            SetEnabled(_scroller.Up, _first > 0);
            SetEnabled(_scroller.Down, _first < last);
            var track = _scroller.Track.sizeDelta.y;
            var thumb = Mathf.Max(18f, track * RowsPerPage / Mathf.Max(1f, _items.Count));
            _scroller.Thumb.sizeDelta = new Vector2(6f, thumb);
            var along = last > 0 ? _first / (float)last : 0f;
            _scroller.Thumb.anchoredPosition = new Vector2(0f, (track - thumb) * (0.5f - along));
        }

        /// <summary>A scroll button at the end of the list fades and stops taking clicks.</summary>
        static void SetEnabled(Button button, bool enabled)
        {
            button.interactable = enabled;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
                graphic.canvasRenderer.SetAlpha(enabled ? 1f : 0.3f);
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
            (Scrolls ? $"scrolls: rows {_first + 1}–{Mathf.Min(_items.Count, _first + RowsPerPage)} in view · " : string.Empty) +
            (_modes != null ? $"view {(_modes.Mode == ViewMode.Virtual ? "VR" : "MR")} · " : string.Empty) +
            $"space {(ImmersiveXSession.Instance != null && ImmersiveXSession.Instance.CurrentSpace != null ? "saved" : "not saved")}{(_space != null && _space.IsShowing ? " (Space panel open)" : string.Empty)} · " +
            $"swipes recognised: {(_swipes != null ? _swipes.Count : 0)}";

        Transform Viewer() =>
            _origin != null && _origin.Camera != null ? _origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
    }
}
