using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImmersiveX
{
    /// <summary>
    /// The genxr.co hover for a button or card: under the ray or a finger its fill brightens and its hairline turns gold (a
    /// colour tint can't brighten the site's nearly transparent fills); pressed, it dims.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class BrandHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Image Background;
        public Image Border;
        public Text Text;

        Color _fill = Color.clear;
        Color _line = Color.clear;
        Color _hoverFill = Color.clear;
        Color _hoverLine = Color.clear;
        Color _text = Color.white;
        Color _hoverText = Color.white;
        bool _over;
        bool _down;

        /// <summary>The colours at rest and under the pointer (a null border or text colour leaves it as it is).</summary>
        public void Set(Color fill, Color? line, Color hoverFill, Color? hoverLine, Color text, Color hoverText)
        {
            _fill = fill;
            _hoverFill = hoverFill;
            _line = line ?? (Border != null ? Border.color : Color.clear);
            _hoverLine = hoverLine ?? _line;
            _text = text;
            _hoverText = hoverText;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _over = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _over = false;
            _down = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _down = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
            Apply();
        }

        void OnDisable()
        {
            _over = false;
            _down = false;
            Apply();
        }

        void Apply()
        {
            if (Background != null)
            {
                var fill = _over ? _hoverFill : _fill;
                Background.color = _down ? new Color(fill.r * 0.82f, fill.g * 0.82f, fill.b * 0.82f, fill.a) : fill;
            }

            if (Border != null)
                Border.color = _over ? _hoverLine : _line;
            if (Text != null)
                Text.color = _over ? _hoverText : _text;
        }
    }
}
