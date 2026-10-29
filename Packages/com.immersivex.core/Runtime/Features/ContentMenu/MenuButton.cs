using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// The menu button: a small grid of white squares, half transparent, at the user's stomach level and a foot to their
    /// left. The ray clicks it and a finger presses it; either opens the content menu in front of the user, as the opening
    /// swipe does (<see cref="HandSwipes"/>). It stays with the user as the media controls do (<see cref="WithinReach"/>),
    /// but on the other side, below them, and never stacks with them. Under the ray or a finger its squares brighten.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Where it sits from the user's eyes, in metres: a foot to their left, at stomach level, close in front.</summary>
        public static readonly Vector3 Offset = new Vector3(-0.3f, -0.52f, 0.34f);

        /// <summary>The squares across and down.</summary>
        public const int Grid = 3;

        const float Square = 11f;
        const float Gap = 4f;
        const float Margin = 3f;

        /// <summary>The squares at rest: white, half transparent.</summary>
        public static readonly Color Idle = new Color(1f, 1f, 1f, 0.5f);

        static readonly Color Lit = new Color(1f, 1f, 1f, 0.9f);

        readonly List<Image> _squares = new List<Image>();

        /// <summary>The button the ray clicks and a finger presses.</summary>
        public Button Button { get; private set; }

        /// <summary>Its squares, row by row.</summary>
        public IReadOnlyList<Image> Squares => _squares;

        /// <summary>A menu button for <paramref name="viewer"/>'s user (the headset camera when null) that calls <paramref name="onPress"/>.</summary>
        public static MenuButton Create(Func<Transform> viewer, UnityAction onPress)
        {
            var root = new GameObject("Menu Button");
            var follow = root.AddComponent<WithinReach>();
            follow.Offset = Offset;
            follow.Stacks = false;
            follow.Viewer = viewer;

            var side = Grid * Square + (Grid - 1) * Gap + Margin * 2f;
            var canvas = WorldUi.CreateCanvas(root.transform, new Vector2(side, side));
            var area = canvas.Find("Background").GetComponent<Image>();
            area.color = Color.clear; // only the squares show; the whole tile takes the click
            area.raycastTarget = true;

            var button = area.gameObject.AddComponent<MenuButton>();
            button.Button = area.gameObject.AddComponent<Button>();
            button.Button.transition = Selectable.Transition.None; // the squares light up instead (OnPointerEnter)
            button.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.Button.onClick.AddListener(onPress);

            var first = -(Grid - 1) * (Square + Gap) * 0.5f;
            for (var row = 0; row < Grid; row++)
            for (var column = 0; column < Grid; column++)
            {
                var square = WorldUi.Rect(canvas, $"Square {row * Grid + column + 1}",
                    new Vector2(first + column * (Square + Gap), -first - row * (Square + Gap)), new Vector2(Square, Square), Idle);
                square.sprite = UiSprites.Panel;
                square.type = Image.Type.Sliced;
                square.pixelsPerUnitMultiplier = 40f / 2f; // the panel's 40 px corners at 2 mm: squares with softened corners
                button._squares.Add(square);
            }

            return button;
        }

        public void OnPointerEnter(PointerEventData eventData) => Paint(Lit);

        public void OnPointerExit(PointerEventData eventData) => Paint(Idle);

        void OnDisable() => Paint(Idle);

        void Paint(Color color)
        {
            foreach (var square in _squares)
                square.color = color;
        }
    }
}
