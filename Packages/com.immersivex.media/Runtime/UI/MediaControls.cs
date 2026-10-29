using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Media controls on an acrylic-glass panel: play/pause (one button whose icon shows what it will do), a seek bar
    /// you can drag to any point, the time, a Replay switch (on: it starts again at the end), a speaker button that mutes,
    /// and a volume slider. The panel is mostly transparent; only the controls are solid. A tab above it names the media.
    /// <para>The panel stays within reach of the user, low on their right halfway up the arm, and glides after them when they
    /// walk or turn (see <see cref="WithinReach"/>). Narrated media's caption stays with the media instead, where the user
    /// is looking (<see cref="PlaceCaption"/>).</para>
    /// </summary>
    [AddComponentMenu("ImmersiveX/Media/Media Controls")]
    public sealed class MediaControls : MonoBehaviour
    {
        const float Width = 600f;
        const float Height = 150f;
        const float RefreshSeconds = 0.05f;

        /// <summary>The panel's size beside the user, against its 600 × 150 mm layout: 36 cm wide.</summary>
        public const float PanelScale = 0.6f;

        static readonly Color Glass = new Color(0.07f, 0.09f, 0.13f, 0.32f);
        static readonly Color Rim = new Color(1f, 1f, 1f, 0.38f);
        static readonly Color Solid = Color.white;
        static readonly Color Ink = new Color(0.07f, 0.09f, 0.13f);
        static readonly Color Track = new Color(1f, 1f, 1f, 0.28f);
        const float SwitchTravel = 12f; // the switch's knob, either side of the middle of its track

        IMediaTransport _media;
        WithinReach _panel;
        Transform _captionRoot;
        Transform _captionAnchor;
        Vector3 _captionOffset;
        bool _captionTowardViewer;
        Vector3 _captionDirection;
        Text _title;
        Image _playIcon;
        Image _speakerIcon;
        Button _playButton;
        Button _speakerButton;
        Button _replayButton;
        Image _replayTrack;
        RectTransform _replayKnob;
        Slider _seek;
        Slider _volume;
        Text _time;
        Text _caption;
        Image _captionGlass;
        bool _seeking;
        bool _adjusting;
        float _nextRefresh;

        /// <summary>Create controls for <paramref name="media"/>, titled <paramref name="title"/>.</summary>
        public static MediaControls Create(IMediaTransport media, string title, Func<Transform> viewer)
        {
            var controls = new GameObject("Media Controls").AddComponent<MediaControls>();
            controls._media = media;
            controls.Viewer = viewer;
            controls.Build(title);
            return controls;
        }

        /// <summary>The panel (it moves with the user); the move bar sits under it.</summary>
        public Transform Panel => _panel.transform;

        /// <summary>Who the panel stays with and the caption faces.</summary>
        public Func<Transform> Viewer { get; private set; }

        /// <summary>While this returns true the panel keeps still (as it does while its own sliders are dragged).</summary>
        public Func<bool> Hold;

        /// <summary>
        /// Put the caption at <paramref name="worldOffset"/> from <paramref name="anchor"/> (the media), or at that world
        /// position with no anchor. The offset is kept in world space, so it doesn't turn when the media turns. With
        /// <paramref name="towardViewer"/> it comes round to the user's side of the media as they walk around it (never
        /// closer than 0.4 m short of them).
        /// </summary>
        public void PlaceCaption(Transform anchor, Vector3 worldOffset, bool towardViewer = false)
        {
            _captionAnchor = anchor;
            _captionOffset = worldOffset;
            _captionTowardViewer = towardViewer;
            _captionDirection = new Vector3(worldOffset.x, 0f, worldOffset.z).normalized;
            LateUpdate();
        }

        Vector3 CaptionOffset(Transform viewer)
        {
            if (!_captionTowardViewer || _captionAnchor == null || viewer == null)
                return _captionOffset;
            var flat = Vector3.ProjectOnPlane(viewer.position - _captionAnchor.position, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                return _captionOffset;
            _captionDirection = Vector3.Slerp(_captionDirection, flat.normalized, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime)).normalized;
            var ahead = Mathf.Min(new Vector2(_captionOffset.x, _captionOffset.z).magnitude, Mathf.Max(0.3f, flat.magnitude - 0.4f));
            return _captionDirection * ahead + Vector3.up * _captionOffset.y;
        }

        void LateUpdate()
        {
            if (_captionRoot != null)
            {
                var viewer = Viewer?.Invoke();
                var position = _captionAnchor != null ? _captionAnchor.position + CaptionOffset(viewer) : _captionOffset;
                var facing = viewer != null ? position - viewer.position : _captionRoot.forward;
                _captionRoot.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing, Vector3.up) : _captionRoot.rotation);
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }
        }

        /// <summary>
        /// Show <paramref name="caption"/> by the media (narrated media), or nothing when it's null or empty.
        /// </summary>
        public void SetCaption(string caption)
        {
            var show = !string.IsNullOrEmpty(caption);
            if (_caption == null)
            {
                if (!show)
                    return;
                var root = CaptionCanvas();
                _captionGlass = WorldUi.Rect(root, "Caption Glass", Vector2.zero, new Vector2(Width, 84f), new Color(0.03f, 0.045f, 0.07f, 0.78f));
                _captionGlass.sprite = UiSprites.Panel;
                _captionGlass.type = Image.Type.Sliced;
                _caption = WorldUi.Label(root, "Caption", string.Empty, 22, Vector2.zero, new Vector2(Width - 36f, 76f),
                    FontStyle.Normal, TextAnchor.MiddleCenter);
            }

            _captionGlass.gameObject.SetActive(show);
            _caption.gameObject.SetActive(show);
            if (show && _caption.text != caption)
                _caption.text = caption;
        }

        /// <summary>The caption showing now, or null.</summary>
        public string Caption => _caption != null && _caption.gameObject.activeSelf ? _caption.text : null;

        /// <summary>The caption's place in the world.</summary>
        public Vector3 CaptionPosition => _captionRoot != null ? _captionRoot.position : PlaceOf(_captionAnchor, _captionOffset);

        /// <summary>The media's name on the panel's tab.</summary>
        public string Title => _title != null ? _title.text : null;

        static Vector3 PlaceOf(Transform anchor, Vector3 offset) => anchor != null ? anchor.position + offset : offset;

        /// <summary>The caption's own canvas: not a <see cref="PanelZones"/> zone, so hands still reach the media beside it.</summary>
        RectTransform CaptionCanvas()
        {
            _captionRoot = new GameObject("Caption", typeof(RectTransform), typeof(Canvas)).transform;
            _captionRoot.SetParent(transform, false);
            _captionRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)_captionRoot;
            root.sizeDelta = new Vector2(Width, 84f);
            root.localScale = Vector3.one * 0.001f;
            LateUpdate();
            return root;
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

        public void ToggleReplay()
        {
            _media?.SetReplay(!_media.Replay);
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
            SetInteractable(_replayButton, null, _media.HasTimeline);
            _replayTrack.color = _media.Replay ? WorldUi.OnColor : Track;
            _replayKnob.anchoredPosition = new Vector2(_media.Replay ? SwitchTravel : -SwitchTravel, 0f);

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

        /// <summary>True when the Replay switch shows on (its knob at the right).</summary>
        public bool ShowsReplay => _replayKnob != null && _replayKnob.anchoredPosition.x > 0f;

        /// <summary>Seconds as m:ss.</summary>
        public static string FormatTime(double seconds)
        {
            var whole = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }

        // ---------------------------------------------------------------- building

        void Build(string title)
        {
            _panel = new GameObject("Panel").AddComponent<WithinReach>();
            _panel.transform.SetParent(transform, false);
            _panel.transform.localScale = Vector3.one * PanelScale;
            _panel.Viewer = Viewer;
            _panel.Hold = () => _seeking || _adjusting || (Hold != null && Hold());
            var root = WorldUi.CreateCanvas(_panel.transform, new Vector2(Width, Height));

            var glass = root.Find("Background").GetComponent<Image>();
            glass.sprite = UiSprites.Panel;
            glass.type = Image.Type.Sliced;
            glass.color = Glass;
            glass.raycastTarget = false; // only the controls catch the ray, so a panel's glass never hides another panel's buttons
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

            _replayButton = Switch(root, "Replay", new Vector2(40f, -30f), out _replayTrack, out _replayKnob, ToggleReplay);

            _speakerIcon = IconButton(root, "Speaker", new Vector2(146f, -30f), 50f, UiSprites.Speaker, 0.66f, ToggleMute);
            _speakerButton = _speakerIcon.GetComponentInParent<Button>();
            _volume = CreateSlider(root, "Volume", new Vector2(229f, -30f), new Vector2(104f, 30f), 22f);
            _volume.onValueChanged.AddListener(OnVolumeChanged);
            var volumeEvents = _volume.gameObject.AddComponent<PressEvents>();
            volumeEvents.Pressed = () => _adjusting = true;
            volumeEvents.Released = () => _adjusting = false;

            // A tab above the panel's left edge names what it plays, as the panel no longer stands by the media.
            var tab = WorldUi.Rect(root, "Title Tab", new Vector2(-Width * 0.5f + 190f, Height * 0.5f + 26f), new Vector2(380f, 44f), new Color(0.03f, 0.045f, 0.07f, 0.62f));
            tab.sprite = UiSprites.Panel;
            tab.type = Image.Type.Sliced;
            _title = WorldUi.Label(tab.transform, "Title", title ?? string.Empty, 24, Vector2.zero, new Vector2(352f, 40f),
                FontStyle.Bold, TextAnchor.MiddleLeft);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
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

        /// <summary>
        /// A switch: its name, then a rounded track with a solid round knob, pressed as one button (the word too).
        /// <see cref="Refresh"/> slides the knob and colours the track to show whether it's on.
        /// </summary>
        static Button Switch(Transform parent, string label, Vector2 position, out Image track, out RectTransform knob, UnityAction onClick)
        {
            var area = WorldUi.Rect(parent, label, position, new Vector2(138f, 44f), Color.clear);
            area.raycastTarget = true;
            WorldUi.Label(area.transform, "Label", label, 22, new Vector2(-33f, 0f), new Vector2(72f, 34f),
                FontStyle.Normal, TextAnchor.MiddleRight).horizontalOverflow = HorizontalWrapMode.Overflow;

            track = WorldUi.Rect(area.transform, "Track", new Vector2(41f, 0f), new Vector2(54f, 30f), Track);
            track.sprite = UiSprites.Panel;
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 40f / 15f; // the panel's 40 px corners at half its height: round ends, as the seek bar has
            var dot = WorldUi.Rect(track.transform, "Knob", new Vector2(-SwitchTravel, 0f), new Vector2(24f, 24f), Solid);
            dot.sprite = UiSprites.Circle;
            knob = dot.rectTransform;

            var button = area.gameObject.AddComponent<Button>();
            button.targetGraphic = dot;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Solid;
            colors.selectedColor = Solid;
            colors.highlightedColor = new Color(0.82f, 0.9f, 1f);
            colors.pressedColor = new Color(0.62f, 0.74f, 0.9f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return button;
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
