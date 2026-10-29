using System;
using ImmersiveX;
using UnityEngine;
using UnityEngine.UI;

namespace LearnX
{
    /// <summary>LearnX's panel parts, in ImmersiveX's look (<see cref="Brand"/>), sized in millimetres like every world panel.</summary>
    public static class LearnUi
    {
        public static readonly Color Right = new Color(0.45f, 0.85f, 0.55f);
        public static readonly Color Wrong = new Color(0.98f, 0.55f, 0.42f);
        const float KeyRadius = 14f;

        public enum KeyStyle
        {
            Plain,
            Gold,
            Right,
            Wrong,
            Off,
        }

        public static Text Label(Transform parent, string name, string value, int fontSize, Vector2 position, Vector2 size, Color colour,
            FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = WorldUi.Label(parent, name, value, fontSize, position, size, style, alignment);
            text.color = colour;
            text.supportRichText = false; // lessons are plain text: x < 5 stays x < 5
            text.lineSpacing = 1.1f;
            return text;
        }

        /// <summary>A button with slightly rounded corners and its label in the middle (<see cref="SetStyle"/> colours it).</summary>
        public static Button Key(Transform parent, string label, Vector2 position, Vector2 size, KeyStyle style, Action onClick, int fontSize = 20)
        {
            var box = WorldUi.Box(parent, label, position, size);
            var background = Brand.Round(box.gameObject.AddComponent<Image>(), KeyRadius);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None; // BrandHover shows hover and press
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick?.Invoke());
            var hover = box.gameObject.AddComponent<BrandHover>();
            hover.Background = background;
            hover.Border = Brand.Hairline(box, Vector2.zero, size, KeyRadius, Brand.Line2);
            hover.Text = Label(box, "Label", label, fontSize, Vector2.zero, size - new Vector2(16f, 0f), Brand.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetStyle(button, style);
            return button;
        }

        /// <summary>
        /// A lesson's row, like a row of ImmersiveX's content menu: its title, and its subject in gold below, the whole row
        /// one button. Its title is what the automation's <c>host press</c> finds it by.
        /// </summary>
        public static Button Card(Transform parent, string name, string subject, string title, Vector2 position, Vector2 size, Action onClick)
        {
            var box = WorldUi.Box(parent, name, position, size);
            var background = Brand.Round(box.gameObject.AddComponent<Image>(), KeyRadius);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => onClick?.Invoke());
            var inner = size.x - 32f;
            var heading = Label(box, "Title", title, 20, new Vector2(0f, 11f), new Vector2(inner, 28f), Brand.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            heading.resizeTextForBestFit = true; // a long title gets smaller rather than running onto the subject
            heading.resizeTextMinSize = 14;
            heading.resizeTextMaxSize = 20;
            Label(box, "Subject", subject, 14, new Vector2(0f, -15f), new Vector2(inner, 22f), Brand.Gold, alignment: TextAnchor.MiddleLeft);
            var hover = box.gameObject.AddComponent<BrandHover>();
            hover.Background = background;
            hover.Border = Brand.Hairline(box, Vector2.zero, size, KeyRadius, Brand.Line);
            hover.Text = heading;
            hover.Set(Brand.Card, Brand.Line, Brand.GhostHover, Brand.LineGold, Brand.Ink, Brand.GoldLight);
            return button;
        }

        public static void SetStyle(Button key, KeyStyle style)
        {
            var hover = key.GetComponent<BrandHover>();
            switch (style)
            {
                case KeyStyle.Gold:
                    hover.Set(Brand.GoldButton, Color.clear, Brand.Gold, Color.clear, Brand.OnGold, Brand.OnGold);
                    break;
                case KeyStyle.Right:
                    hover.Set(Brand.GoldTint, Right, Brand.GoldTint, Right, Right, Right);
                    break;
                case KeyStyle.Wrong:
                    hover.Set(Brand.Ghost, Wrong, Brand.Ghost, Wrong, Wrong, Wrong);
                    break;
                case KeyStyle.Off:
                    hover.Set(Brand.Ghost, Brand.Line, Brand.Ghost, Brand.Line, Brand.Ink3, Brand.Ink3);
                    break;
                default:
                    hover.Set(Brand.Ghost, Brand.Line2, Brand.GhostHover, Brand.LineGold, Brand.Ink, Brand.GoldLight);
                    break;
            }
        }

        /// <summary>
        /// The font size for a lesson's text on the menu-sized panel, close to the eyes: large for the youngest, who read
        /// with a grown-up, and smaller as the text gets longer. The content menu's own row titles are 22.
        /// </summary>
        public static int ReadingSize(string stage) => stage switch
        {
            "Early years" => 30,
            "Key stage 1" => 28,
            "Key stage 2" => 24,
            _ => 22,
        };
    }
}
