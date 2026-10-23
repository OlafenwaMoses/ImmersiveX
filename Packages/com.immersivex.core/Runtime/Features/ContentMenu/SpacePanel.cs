using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// The content menu's Space panel: whether ImmersiveX has the user's room saved (its floor, walls and when it was saved),
    /// with <b>Re-create space</b> (map the room again: on Quest, Space Setup) and <b>Back</b> (to the menu). It stands where
    /// the menu was while it shows; the menu hides behind it.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SpacePanel : MonoBehaviour
    {
        const float Width = 460f;
        const float Height = 400f;
        const float Radius = 28f;

        public static readonly Color Saved = new Color(0.36f, 0.86f, 0.55f);
        public static readonly Color NotSaved = new Color(0.98f, 0.72f, 0.3f);

        GameObject _root;
        Image _dot;
        Text _headline, _details, _note;
        Button _recreate;
        Action _back, _recreateRequested;

        public bool IsShowing => _root != null && _root.activeSelf;

        /// <summary>What the panel says now (automation and tests).</summary>
        public string Says => _root != null ? $"{_headline.text} · {_details.text.Replace('\n', ' ')} · {_note.text}" : string.Empty;

        public Button RecreateButton => _recreate;

        public static SpacePanel Create(Transform parent, Action back, Action recreate)
        {
            var panel = new GameObject("Space Panel").AddComponent<SpacePanel>();
            panel.transform.SetParent(parent, false);
            panel._back = back;
            panel._recreateRequested = recreate;
            panel.Build();
            return panel;
        }

        /// <summary>Show it at <paramref name="pose"/> (where the menu is), saying what's known about the room now.</summary>
        public void Show(Pose pose)
        {
            _root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Refresh();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        void Update()
        {
            if (IsShowing)
                Refresh(); // a scan finishing while it shows updates it
        }

        public void Refresh()
        {
            var session = ImmersiveXSession.Instance;
            var (saved, headline, details, note, canRecreate) = Describe(session != null ? session.CurrentSpace : null, session != null && session.IsMappingRoom,
                session != null && session.CanRescanRoom, session != null ? session.RoomStatus : null);
            _dot.color = saved ? Saved : NotSaved;
            _headline.text = headline;
            _details.text = details;
            _note.text = note;
            _recreate.interactable = canRecreate;
            foreach (var graphic in _recreate.GetComponentsInChildren<Graphic>(true))
                graphic.canvasRenderer.SetAlpha(canRecreate ? 1f : 0.35f);
        }

        /// <summary>The panel's words for a room: saved or not, its headline, its details, a note, and whether it can be re-created now.</summary>
        public static (bool saved, string headline, string details, string note, bool canRecreate) Describe(MappedSpace space, bool mapping, bool canRescan, string status)
        {
            if (mapping)
                return (space != null, "Mapping your room…", string.IsNullOrEmpty(status) ? "Follow the steps in the headset." : status,
                    "The menu comes back when the room is saved.", false);
            if (space == null)
                return (false, "No space saved", "ImmersiveX hasn't saved this room yet. Content still plays, but it can't stay where you put it between visits or keep inside your walls.",
                    canRescan ? "Create it: walk round your room and outline its walls, floor and furniture." : "Room mapping isn't available here.", canRescan);

            var signature = space.Signature;
            var walls = signature?.WallLengths?.Length ?? 0;
            var details = $"{(string.IsNullOrEmpty(space.Name) ? "This room" : space.Name)} · {signature?.FloorArea ?? 0f:0.0} m² floor · {walls} wall{(walls == 1 ? string.Empty : "s")}";
            if (signature != null && signature.CeilingHeight > 0.5f)
                details += $" · {signature.CeilingHeight:0.0} m ceiling";
            if (DateTime.TryParse(space.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated))
                details += $"\nSaved {updated.ToLocalTime():d MMM yyyy, HH:mm}";
            return (true, "Space saved", details,
                canRescan ? "Re-create it when the room has changed: you'll outline its walls, floor and furniture again. Content keeps its place where it can." : "Re-creating isn't available here.",
                canRescan);
        }

        void Build()
        {
            _root = new GameObject("Space Panel");
            _root.SetActive(false);
            _root.transform.SetParent(transform, false);
            var canvas = WorldUi.CreateCanvas(_root.transform, new Vector2(Width, Height));
            var background = canvas.Find("Background").GetComponent<Image>();
            Brand.Round(background, Radius).color = Brand.Panel;
            Brand.Hairline(canvas, Vector2.zero, new Vector2(Width, Height), Radius, Brand.LineGold);
            var top = Height * 0.5f;
            var left = -Width * 0.5f + 32f;

            var eyebrow = WorldUi.Label(canvas, "Brand", "IMMERSIVEX", 14, new Vector2(left + 108f, top - 26f), new Vector2(216f, 20f), FontStyle.Bold, TextAnchor.MiddleLeft);
            eyebrow.color = Brand.Gold;
            var title = WorldUi.Label(canvas, "Title", "Your space", 30, new Vector2(left + 108f, top - 52f), new Vector2(216f, 40f), FontStyle.Bold, TextAnchor.MiddleLeft);
            title.color = Brand.Ink;
            Brand.Pill(canvas, "×", new Vector2(Width * 0.5f - 38f, top - 40f), new Vector2(48f, 48f), false, () => _back?.Invoke(), 26).name = "Close";

            var card = WorldUi.Box(canvas, "Status", new Vector2(0f, top - 172f), new Vector2(Width - 32f, 168f));
            Brand.Round(card.gameObject.AddComponent<Image>(), 16f).color = Brand.Card;
            Brand.Hairline(card, Vector2.zero, card.sizeDelta, 16f, Brand.Line);
            var dot = WorldUi.Box(card, "Dot", new Vector2(-card.sizeDelta.x * 0.5f + 28f, 52f), new Vector2(16f, 16f));
            _dot = Brand.Round(dot.gameObject.AddComponent<Image>(), 8f);
            _headline = WorldUi.Label(card, "Headline", string.Empty, 22, new Vector2(18f, 52f), new Vector2(card.sizeDelta.x - 76f, 32f), FontStyle.Bold, TextAnchor.MiddleLeft);
            _headline.color = Brand.Ink;
            _details = WorldUi.Label(card, "Details", string.Empty, 16, new Vector2(0f, -22f), new Vector2(card.sizeDelta.x - 40f, 100f), FontStyle.Normal, TextAnchor.UpperLeft);
            _details.color = Brand.Ink2;
            _details.horizontalOverflow = HorizontalWrapMode.Wrap;

            _note = WorldUi.Label(canvas, "Note", string.Empty, 15, new Vector2(0f, -Height * 0.5f + 118f), new Vector2(Width - 56f, 56f), FontStyle.Normal, TextAnchor.UpperLeft);
            _note.color = Brand.Ink3;
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;

            var footer = -Height * 0.5f + 44f;
            Brand.Pill(canvas, "Back", new Vector2(-104f, footer), new Vector2(196f, 54f), false, () => _back?.Invoke()).name = "Back";
            _recreate = Brand.Pill(canvas, "Re-create space", new Vector2(104f, footer), new Vector2(196f, 54f), true, () =>
            {
                if (_recreate.interactable)
                    _recreateRequested?.Invoke();
            });
            _recreate.name = "Re-create space";
            PanelMover.Attach(_root, new Vector2(Width / 1000f, Height / 1000f));
        }
    }
}
