using UnityEngine;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// GenXR's look (genxr.co): near-black panels, warm white text, gold accents, rounded cards with hairline borders and
    /// pill-shaped buttons. Used by ImmersiveX's own panels (the content menu first).
    /// </summary>
    public static class Brand
    {
        public static readonly Color Panel = Hex(0x0b0d13, 0.95f);        // --bg-1
        public static readonly Color Ink = Hex(0xf5f2ea);                  // --ink: titles and body text
        public static readonly Color Ink2 = Hex(0xc4beb1);                 // --ink-2: secondary text
        public static readonly Color Ink3 = Hex(0x8d8779);                 // --ink-3: quiet labels
        public static readonly Color Gold = Hex(0xe8c36a);                 // --gold-300: the accent
        public static readonly Color GoldLight = Hex(0xf0daa2);            // --gold-200: text on dark, when selected
        public static readonly Color GoldButton = Hex(0xdcb24f);           // the middle of the site's gold button gradient
        public static readonly Color OnGold = Hex(0x1a1204);               // text on gold
        // The site layers faint white and gold over the near-black (--surface, --line, --line-gold). Blended here in sRGB over
        // the panel, so they look the same in a linear-colour project, where low alphas would come out far brighter.
        public static readonly Color Card = Hex(0x16181e);       // a card on the panel (4.5% white)
        public static readonly Color Ghost = Hex(0x1a1b21);      // a ghost button (6% white)
        public static readonly Color GhostHover = Hex(0x2a2c31);  // a card or ghost button under the ray or a finger
        public static readonly Color Line = Hex(0x26282d);       // hairline borders (10% white)
        public static readonly Color Line2 = Hex(0x3a3c41);      // stronger hairlines (buttons, 18% white)
        public static readonly Color LineGold = Hex(0x6e5f3a);   // a gold hairline (the panel's edge, the selected card)
        public static readonly Color GoldTint = Hex(0x3a3325);   // a selected card (a fifth gold)

        /// <summary>The sprite's corners are 40 units at scale 1 (see <see cref="UiSprites"/>).</summary>
        const float SpriteRadius = 40f;

        /// <summary>Give <paramref name="image"/> rounded corners of <paramref name="radius"/> panel units (mm).</summary>
        public static Image Round(Image image, float radius)
        {
            image.sprite = UiSprites.Panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(radius, 1f);
            return image;
        }

        /// <summary>A rounded hairline border of <paramref name="color"/> over a rect.</summary>
        public static Image Hairline(Transform parent, Vector2 position, Vector2 size, float radius, Color color)
        {
            var line = WorldUi.Rect(parent, "Hairline", position, size, color);
            line.sprite = UiSprites.PanelRim;
            line.type = Image.Type.Sliced;
            line.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(radius, 1f);
            return line;
        }

        /// <summary>
        /// A pill-shaped button: <paramref name="gold"/> for the main action (gold with dark text), else a ghost button (a faint
        /// fill, a hairline and warm white text), as on genxr.co.
        /// </summary>
        public static Button Pill(Transform parent, string label, Vector2 position, Vector2 size, bool gold, UnityEngine.Events.UnityAction onClick, int fontSize = 20)
        {
            var box = WorldUi.Box(parent, label, position, size);
            var background = Round(box.gameObject.AddComponent<Image>(), size.y * 0.5f);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None; // BrandHover shows hover and press
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null)
                button.onClick.AddListener(onClick);
            var border = gold ? null : Hairline(box, Vector2.zero, size, size.y * 0.5f, Line2);
            var text = WorldUi.Label(box, "Label", label, fontSize, Vector2.zero, size, FontStyle.Bold, TextAnchor.MiddleCenter);
            text.color = gold ? OnGold : Ink;
            var hover = box.gameObject.AddComponent<BrandHover>();
            hover.Background = background;
            hover.Border = border;
            hover.Text = gold ? null : text;
            if (gold)
                hover.Set(GoldButton, null, Gold, null, OnGold, OnGold);
            else
                hover.Set(Ghost, Line2, GhostHover, LineGold, Ink, GoldLight);
            return button;
        }


        static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
    }
}
