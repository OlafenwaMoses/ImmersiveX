using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Media controls on an acrylic-glass panel: play/pause (one button whose icon shows what it will do), a seek bar
    /// you can drag to any point, the time, a speaker button that mutes, and a volume slider. The panel is mostly
    /// transparent; only the controls are solid.
    /// <para>It follows an anchor (the media it controls) by position only, so moving the media moves the panel with
    /// it, while turning the media leaves the panel where it is.</para>
    /// </summary>
    [AddComponentMenu("ImmersiveX/Media/Media Controls")]
    public sealed class MediaControls : MonoBehaviour
    {
        const float Width = 600f;
        const float Height = 150f;
        const float RefreshSeconds = 0.05f;

        static readonly Color Glass = new Color(0.07f, 0.09f, 0.13f, 0.32f);
        static readonly Color Rim = new Color(1f, 1f, 1f, 0.38f);
        static readonly Color Solid = Color.white;
        static readonly Color Ink = new Color(0.07f, 0.09f, 0.13f);
        static readonly Color Track = new Color(1f, 1f, 1f, 0.28f);

        IMediaTransport _media;
        Transform _anchor;
        Vector3 _offset;
        Image _playIcon;
        Image _speakerIcon;
        Button _playButton;
        Button _speakerButton;
        Slider _seek;
        Slider _volume;
        Text _time;
        bool _seeking;
        float _nextRefresh;

        /// <summary>Create controls for <paramref name="media"/> that follow <paramref name="anchor"/>.</summary>
        public static MediaControls Create(IMediaTransport media, Transform anchor)
        {
            var controls = new GameObject("Media Controls").AddComponent<MediaControls>();
            controls._media = media;
            controls._anchor = anchor;
            controls.Build();
            return controls;
        }

        /// <summary>Follow <paramref name="anchor"/> by position (null: stay where placed).</summary>
        public void Follow(Transform anchor) => _anchor = anchor;

        /// <summary>
        /// Put the panel at <paramref name="worldOffset"/> from the anchor (or at that world position with no anchor), facing
        /// <paramref name="viewer"/>. The offset is kept in world space, so it doesn't turn when the anchor turns.
        /// </summary>
        public void Place(Vector3 worldOffset, Vector3 viewer)
        {
            _offset = worldOffset;
            var position = _anchor != null ? _anchor.position + worldOffset : worldOffset;
            var facing = position - viewer;
            transform.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing, Vector3.up) : transform.rotation);
        }

        void LateUpdate()
        {
            if (_anchor != null)
                transform.position = _anchor.position + _offset;

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }
        }

        // ---------------------------------------------------------------- actions (also used by automation)

        public void TogglePlay()
        {
            _media?.TogglePlay();
            Refresh(); // show the new icon straight away
        }

        public void ToggleMute()
        {
            if (_media == null)
                return;
            var mute = !_media.Muted && _media.Volume > 0f;
            _media.SetMuted(mute);
            if (!mute && _media.Volume <= 0f)
                _media.SetVolume(0.8f);
            Refresh();
        }

        void OnVolumeChanged(float value)
        {
            if (_media == null)
                return;
            _media.SetVolume(value);
            if (value > 0f && _media.Muted)
                _media.SetMuted(false);
            Refresh();
        }

        void OnSeekStart() => _seeking = true;

        void OnSeekEnd()
        {
            if (_media != null && _media.Duration > 0d)
                _media.Seek(_seek.value * _media.Duration);
            _seeking = false;
        }

        // ---------------------------------------------------------------- display

        void Refresh()
        {
            if (_media == null || _playIcon == null)
                return;

            _playIcon.sprite = _media.IsPlaying ? UiSprites.Pause : UiSprites.Play;
            _speakerIcon.sprite = _media.Muted || _media.Volume <= 0f ? UiSprites.Muted : UiSprites.Speaker;
            SetInteractable(_playButton, _playIcon, _media.HasTimeline);
            SetInteractable(_seek, null, _media.HasTimeline);
            SetInteractable(_speakerButton, _speakerIcon, _media.HasAudio);
            SetInteractable(_volume, null, _media.HasAudio);

            var duration = _media.Duration;
            var position = _seeking ? _seek.value * duration : _media.Position;
            if (!_seeking)
                _seek.SetValueWithoutNotify(duration > 0d ? (float)(position / duration) : 0f);
            _volume.SetValueWithoutNotify(_media.Muted ? 0f : _media.Volume);

            if (!string.IsNullOrEmpty(_media.Status))
                _time.text = _media.Status;
            else if (_media.IsBuffering && !_seeking)
                _time.text = $"Buffering {Mathf.RoundToInt(_media.BufferProgress * 100f)}%";
            else
                _time.text = $"{FormatTime(position)} / {FormatTime(duration)}";
        }

        static void SetInteractable(Selectable control, Image icon, bool on)
        {
            if (control.interactable == on)
                return;
            control.interactable = on;
            if (icon != null)
                icon.color = on ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.35f);
        }

        /// <summary>True when the play/pause button shows the pause icon (the media is playing).</summary>
        public bool ShowsPause => _playIcon != null && _playIcon.sprite == UiSprites.Pause;

        /// <summary>True when the speaker button shows the muted icon.</summary>
        public bool ShowsMuted => _speakerIcon != null && _speakerIcon.sprite == UiSprites.Muted;

        /// <summary>Seconds as m:ss.</summary>
        public static string FormatTime(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }

        // ---------------------------------------------------------------- building

        void Build()
        {
            var root = WorldUi.CreateCanvas(transform, new Vector2(Width, Height));

            var glass = root.Find("Background").GetComponent<Image>();
            glass.sprite = UiSprites.Panel;
            glass.type = Image.Type.Sliced;
            glass.color = Glass;
            glass.raycastTarget = true; // a ray on the panel stops here instead of grabbing what's behind it
            var rim = WorldUi.Rect(root, "Rim", Vector2.zero, new Vector2(Width, Height), Rim);
            rim.sprite = UiSprites.PanelRim;
            rim.type = Image.Type.Sliced;

            _playIcon = IconButton(root, "Play", new Vector2(-240f, 0f), 76f, UiSprites.Play, 0.62f, TogglePlay);
            _playButton = _playIcon.GetComponentInParent<Button>();

            _seek = CreateSlider(root, "Seek", new Vector2(48f, 26f), new Vector2(468f, 34f), 26f);
            var events = _seek.gameObject.AddComponent<PressEvents>();
            events.Pressed = OnSeekStart;
            events.Released = OnSeekEnd;

            _time = WorldUi.Label(root, "Time", "Loading…", 22, new Vector2(-72f, -30f), new Vector2(232f, 34f),
                FontStyle.Normal, TextAnchor.MiddleLeft);
            _time.horizontalOverflow = HorizontalWrapMode.Overflow;

            _speakerIcon = IconButton(root, "Speaker", new Vector2(146f, -30f), 50f, UiSprites.Speaker, 0.66f, ToggleMute);
            _speakerButton = _speakerIcon.GetComponentInParent<Button>();
            _volume = CreateSlider(root, "Volume", new Vector2(229f, -30f), new Vector2(104f, 30f), 22f);
            _volume.onValueChanged.AddListener(OnVolumeChanged);
        }

        /// <summary>A solid white circle with a dark icon; returns the icon so it can change.</summary>
        static Image IconButton(Transform parent, string name, Vector2 position, float diameter, Sprite icon, float iconScale, UnityAction onClick)
        {
            var circle = WorldUi.Rect(parent, name, position, new Vector2(diameter, diameter), Solid);
            circle.sprite = UiSprites.Circle;
            circle.raycastTarget = true;
            var button = circle.gameObject.AddComponent<Button>();
            button.targetGraphic = circle;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Solid;
            colors.selectedColor = Solid;
            colors.highlightedColor = new Color(0.82f, 0.9f, 1f);
            colors.pressedColor = new Color(0.62f, 0.74f, 0.9f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            var image = WorldUi.Rect(circle.transform, "Icon", Vector2.zero, Vector2.one * diameter * iconScale, Ink);
            image.sprite = icon;
            return image;
        }

        /// <summary>A thin rounded track, a solid fill and a solid round handle.</summary>
        static Slider CreateSlider(Transform parent, string name, Vector2 position, Vector2 size, float handle)
        {
            var rect = WorldUi.Box(parent, name, position, size);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            var track = WorldUi.Rect(rect, "Track", Vector2.zero, Vector2.zero, Track);
            Stretch(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 8f));
            track.sprite = UiSprites.Panel;
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 11f;
            track.raycastTarget = true;

            var fillArea = WorldUi.Box(rect, "Fill Area", Vector2.zero, Vector2.zero);
            Stretch(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-handle, 8f));
            var fill = WorldUi.Rect(fillArea, "Fill", Vector2.zero, Vector2.zero, Solid);
            Stretch(fill.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(handle, 0f));
            fill.sprite = UiSprites.Panel;
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 11f;

            var handleArea = WorldUi.Box(rect, "Handle Slide Area", Vector2.zero, Vector2.zero);
            Stretch(handleArea, Vector2.zero, Vector2.one, new Vector2(-handle, 0f));
            var knob = WorldUi.Rect(handleArea, "Handle", Vector2.zero, Vector2.zero, Solid);
            Stretch(knob.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(handle, handle - size.y));
            knob.sprite = UiSprites.Circle;
            knob.raycastTarget = true;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = knob.rectTransform;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            var colors = slider.colors;
            colors.normalColor = Solid;
            colors.selectedColor = Solid;
            colors.highlightedColor = new Color(0.82f, 0.9f, 1f);
            colors.pressedColor = new Color(0.62f, 0.74f, 0.9f);
            slider.colors = colors;
            return slider;
        }

        static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = sizeDelta;
        }

        /// <summary>Reports press and release on a control (the seek bar seeks on release, like a video player).</summary>
        sealed class PressEvents : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public UnityAction Pressed;
            public UnityAction Released;

            public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();
            public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
        }
    }
}
