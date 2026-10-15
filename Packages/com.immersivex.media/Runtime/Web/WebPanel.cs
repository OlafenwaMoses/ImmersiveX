using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImmersiveX.Media
{
    /// <summary>
    /// A web page's own controls on a panel: the page's pixels (its 3D view hidden, so only its HTML shows: controls,
    /// captions, cards). It starts beside the content, on the user's left and within reach, so it hides none of the
    /// content. The ray clicks it and drags on it; a finger presses it. It moves like the content menu (the ray by its side
    /// anchors, a hand by its frame) and stays where it's put; otherwise it follows the content.
    /// </summary>
    [AddComponentMenu("")]
    sealed class WebPanel : MonoBehaviour
    {
        const float WidthMetres = 0.6f;

        IWebView _view;
        Transform _anchor;
        Vector3 _offset;
        RawImage _image;
        RectTransform _surface;
        Vector2 _pixels;
        int _shownFrame = -1;
        bool _down;
        PanelMover _mover;
        bool _userPlaced; // the user moved it: it keeps that place by the content

        /// <summary>A panel for <paramref name="view"/> (<paramref name="pixels"/> wide and high) that follows <paramref name="anchor"/>.</summary>
        public static WebPanel Create(IWebView view, Vector2Int pixels, Transform anchor)
        {
            var panel = new GameObject("Web Page Panel").AddComponent<WebPanel>();
            panel._view = view;
            panel._anchor = anchor;
            panel._pixels = pixels;
            panel.Build();
            return panel;
        }

        void Build()
        {
            var size = new Vector2(WidthMetres * 1000f, WidthMetres * 1000f * _pixels.y / _pixels.x);
            var canvas = WorldUi.CreateCanvas(transform, size);
            var glass = canvas.Find("Background").GetComponent<Image>();
            Brand.Round(glass, 24f).color = new Color(0.04f, 0.05f, 0.07f, 0.35f); // faint glass, so the page's text reads over the room
            var image = WorldUi.Box(canvas, "Page", Vector2.zero, size).gameObject.AddComponent<RawImage>();
            image.raycastTarget = true;
            image.color = new Color(1f, 1f, 1f, 0f); // shown once the first frame arrives
            _image = image;
            _surface = image.rectTransform;
            // Events land on the page image; this component turns them into the page's pointer.
            image.gameObject.AddComponent<WebPanelEvents>().Panel = this;
            _mover = PanelMover.Attach(gameObject, size / 1000f);
        }

        /// <summary>Who the panel is placed for: beside the content, on their left, whenever the content is placed or moved.</summary>
        public System.Func<Transform> Viewer;

        Vector3 _placedAt = new Vector3(float.NaN, 0f, 0f);

        /// <summary>
        /// Beside the content on the user's left, within reach, a little below their eyes and turned to them, so it hides
        /// none of the content.
        /// </summary>
        void PlaceBeside()
        {
            var viewer = Viewer?.Invoke();
            if (viewer == null || _anchor == null)
                return;
            var toContent = Vector3.ProjectOnPlane(_anchor.position - viewer.position, Vector3.up);
            var distance = toContent.magnitude;
            var forward = distance > 1e-3f ? toContent / distance : Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f)
                forward = Vector3.forward;
            var left = Vector3.Cross(forward, Vector3.up);
            var position = viewer.position + forward * Mathf.Min(0.45f, Mathf.Max(distance, 0.3f) * 0.6f) + left * 0.45f;
            position.y = Mathf.Max(_anchor.position.y + 0.9f, viewer.position.y - 0.4f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - viewer.position, Vector3.up));
            _offset = position - _anchor.position;
            _placedAt = _anchor.position;
        }

        void LateUpdate()
        {
            if (_mover != null && _mover.IsHeld)
            {
                // The user is moving it: that's its place by the content from now on.
                if (_anchor != null)
                    _offset = transform.position - _anchor.position;
                _userPlaced = true;
            }
            else if (_anchor != null)
            {
                // The content was placed in the room (or moved): beside it again, unless the user has put the panel somewhere.
                if (!_userPlaced && !_down && (float.IsNaN(_placedAt.x) || (_anchor.position - _placedAt).sqrMagnitude > 0.0001f))
                    PlaceBeside();
                transform.position = _anchor.position + _offset;
            }

            if (_view == null || _view.Frame == null || _view.FrameCount == _shownFrame)
                return;
            _shownFrame = _view.FrameCount;
            _image.texture = _view.Frame;
            _image.color = Color.white;
        }

        // ---------------------------------------------------------------- pointer

        public void OnPointerDown(PointerEventData eventData) => Send(WebPointerPhase.Down, eventData);

        public void OnPointerUp(PointerEventData eventData) => Send(WebPointerPhase.Up, eventData);

        public void OnDrag(PointerEventData eventData) => Send(WebPointerPhase.Move, eventData);

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!_down)
                Send(WebPointerPhase.Move, eventData); // hover: the page highlights what's under the ray
        }

        void Send(WebPointerPhase phase, PointerEventData eventData)
        {
            if (_view == null || !TryPixel(eventData, out var pixel))
                return;
            if (phase == WebPointerPhase.Down)
                _down = true;
            else if (phase == WebPointerPhase.Up)
                _down = false;
            _view.Pointer(phase, pixel);
        }

        /// <summary>Where the pointer is on the page, in page pixels from the top left.</summary>
        bool TryPixel(PointerEventData eventData, out Vector2 pixel)
        {
            pixel = default;
            var hit = eventData.pointerCurrentRaycast;
            var world = hit.isValid ? hit.worldPosition : eventData.pointerPressRaycast.worldPosition;
            if (world == Vector3.zero)
                return false;
            var local = _surface.InverseTransformPoint(world);
            var rect = _surface.rect;
            var u = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
            var v = Mathf.Clamp01((rect.yMax - local.y) / rect.height);
            pixel = new Vector2(u * _pixels.x, v * _pixels.y);
            return true;
        }

        public void Dispose() => Destroy(gameObject);

        /// <summary>Forwards the page image's pointer events to its panel.</summary>
        [AddComponentMenu("")]
        sealed class WebPanelEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerMoveHandler, IBeginDragHandler
        {
            public WebPanel Panel;

            public void OnPointerDown(PointerEventData eventData) => Panel.OnPointerDown(eventData);
            public void OnPointerUp(PointerEventData eventData) => Panel.OnPointerUp(eventData);
            public void OnDrag(PointerEventData eventData) => Panel.OnDrag(eventData);
            public void OnPointerMove(PointerEventData eventData) => Panel.OnPointerMove(eventData);
            public void OnBeginDrag(PointerEventData eventData) { } // claims the drag, so the page gets every move
        }
    }
}
