using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImmersiveX
{
    /// <summary>
    /// Builds simple world-space panels from code (1 UI unit = 1 mm), usable with controller rays, hand pinch and poke.
    /// Shared by the device-check panel and ImmersiveX prompts so they look and behave the same.
    /// </summary>
    public static class WorldUi
    {
        public static readonly Color PanelColor = new Color(0.05f, 0.1f, 0.18f, 0.92f);
        public static readonly Color TextColor = new Color(0.92f, 0.95f, 1f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.42f, 0.8f);
        public static readonly Color OnColor = new Color(0.35f, 0.8f, 1f);
        public static readonly Color OffColor = new Color(1f, 1f, 1f, 0.15f);

        static Font _font;

        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>A world-space canvas of <paramref name="sizeMillimetres"/> with a background, under <paramref name="parent"/>.</summary>
        public static RectTransform CreateCanvas(Transform parent, Vector2 sizeMillimetres)
        {
            EnsureEventSystem();
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvasObject.transform;
            root.sizeDelta = sizeMillimetres;
            root.localScale = Vector3.one * 0.001f;
            Rect(root, "Background", Vector2.zero, sizeMillimetres, PanelColor);
            return root;
        }

        public static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Rect(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = Box(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, string value, int fontSize, Vector2 position, Vector2 size,
            FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var text = Box(parent, name, position, size).gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = TextColor;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string label, Vector2 position, Vector2 size, UnityAction onClick)
        {
            var rect = Box(parent, label, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
                button.onClick.AddListener(onClick);
            Label(rect, "Label", label, 20, Vector2.zero, size, FontStyle.Bold, TextAnchor.MiddleCenter);
            return button;
        }

        public static void SetLabel(Button button, string label) => button.GetComponentInChildren<Text>().text = label;

        /// <summary>World-space UI needs an EventSystem with the XR UI input module.</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }
    }
}
