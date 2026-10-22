using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveX.Media
{
    /// <summary>The kind of media, when you'd rather not rely on detection.</summary>
    public enum MediaFormat
    {
        Auto,
        HologramStream,
        GaussianSplats,
        PointCloud,
        Model,
        Mesh,
        Sequence,
        [InspectorName("Flat video or photo")] Video,
        [InspectorName("360° video or photo")] Video360,
        [InspectorName("180° video or photo")] Video180,
        [InspectorName("Web page (three.js)")] WebPage,
        [InspectorName("4D mesh (.sp4m)")] Mesh4D,
    }

    /// <summary>
    /// Plays any supported media in the user's room with one component: set <see cref="Source"/> and the format is
    /// detected and the right player attached, all with the same acrylic controls.
    /// <list type="bullet">
    /// <item>GenXR 3.5D holograms and packed 4D splat captures (<c>stream.json</c>), Gaussian splats
    /// (<c>.ply .splat .spz .ksplat</c>), point clouds (<c>.ply</c>), models (<c>.glb .gltf</c>, including Draco and KTX2),
    /// meshes (<c>.obj .ply .stl</c>), frame sequences (a sequence <c>.json</c> or a folder of frames), video
    /// (<c>.mp4 .m4v .mov .webm</c>) and photos (<c>.jpg .png</c>): flat, 180° or 360°, mono or stereo; 4D meshes
    /// (<c>.sp4m</c>, <c>.sp4m.gz</c>: a scene baked over time, with its captions and callouts).</item>
    /// <item>Standing media is fitted to <see cref="Height"/> with its feet on the floor under the user, in front of them and
    /// facing them, when the app starts and whenever it's picked in the content menu. Grab it with a hand or controller at it to slide it across the floor; it turns about the up axis
    /// only. The ray from a hand or controller only clicks (see <see cref="DirectGrabOnly"/>).</item>
    /// <item>Then it moves only when the user moves it: it's held in place with a spatial anchor (see
    /// <see cref="ContentAnchor"/>), so it stays in the same real spot through tracking corrections and the headset coming
    /// off, until the next start or pick puts it in front of the user again.</item>
    /// <item>Flat video and photos are an upright screen; 180°/360° video and photos surround the user.</item>
    /// <item>It waits, paused, for the Play button unless <c>Play On Start</c> is set.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/Media/Immersive Media")]
    public sealed class ImmersiveMedia : MonoBehaviour, IMediaTransport, IHandleTarget, IContentItem
    {
        const float StatusLogSeconds = 10f;
        const float ColliderRefreshSeconds = 0.5f;
        const float ScreenBottom = 0.8f;

        [Header("Source")]
        [SerializeField, Tooltip("A URL, a file path, or a path inside StreamingAssets. See the component's documentation for the formats.")]
        string _source = string.Empty;

        [SerializeField, Tooltip("Auto detects the format. Set it if detection guesses wrong, e.g. a 360° video or photo with no hint in its name.")]
        MediaFormat _format = MediaFormat.Auto;

        [SerializeField, Tooltip("360°/180° video and photos: how the frame is split between the eyes. Auto reads the file name (_tb, _sbs…), then the shape: a square 360° frame is top-bottom, a 2:1 180° frame side-by-side.")]
        StereoLayout _stereo = StereoLayout.Auto;

        [SerializeField, Tooltip("Shown in logs.")]
        string _title = string.Empty;

        [Header("Playback")]
        [SerializeField, Tooltip("Start as soon as it's ready. Off: it waits, paused, for the Play button.")]
        bool _playOnStart;

        [SerializeField, Tooltip("Start again at the end. Off: it stops at the end; Play starts it again.")]
        bool _loop;

        [SerializeField, Range(0f, 1f)]
        float _volume = 0.8f;

        [SerializeField, Min(0f), Tooltip("Frames per second for a folder of frames or a sequence file. 0 keeps the sequence file's rate (30 for a folder).")]
        float _frameRate;

        [Header("Size and placement")]
        [SerializeField, Min(0.3f), Tooltip("Height of standing media (holograms, splats, models, meshes) in metres; the width follows the content.")]
        float _height = 1.6f;

        [SerializeField, Min(0.2f), Tooltip("Height of a flat video screen in metres.")]
        float _screenHeight = 1f;

        [SerializeField, Min(0.5f), Tooltip("How far in front of the user it stands when the app starts or it's picked in the content menu, in metres.")]
        float _distanceFromUser = 2f;

        [SerializeField, Min(-1), Tooltip("Without the content menu, with several pieces of media showing: which spot this one takes. 0 is in front of the user, 1 and up stand around it. -1 = automatic (by order in the hierarchy). With the content menu, the piece showing always takes the spot in front.")]
        int _spot = -1;

        [SerializeField, Tooltip("Which way is up in the file. Auto uses the format's usual convention.")]
        UpAxis _up = UpAxis.Auto;

        [SerializeField, Tooltip("Show the media controls.")]
        bool _showControls = true;

        [SerializeField, Tooltip("Show the side handles: a move bar under the controls, turn bars at the sides and a resize corner.")]
        bool _showHandles = true;

        [SerializeField, Tooltip("Content with several pieces (a model's meshes, a web scene's actors): a ray click selects a piece " +
                                 "(click again for the assembly it's in) and a hand at it picks it up. The handles then move the whole. " +
                                 "Off: the whole is grabbed as one piece.")]
        bool _selectableParts = true;

        [Header("Streaming and detail")]
        [SerializeField, Tooltip("Hologram streams: the quality tier (base, low, medium, high, full). Base streams the fewest bytes.")]
        string _quality = "base";

        [SerializeField, Min(0), Tooltip("Most Gaussians or points drawn; bigger files keep the most visible. 0 = automatic (400,000 on standalone headsets and phones, 2,000,000 elsewhere).")]
        int _maxGaussians;

        [SerializeField, Range(4, 48), Tooltip("Frames downloaded at once for streams and sequences.")]
        int _parallelDownloads = 24;

        [SerializeField, Min(0.5f), Tooltip("Seconds of frames kept downloaded ahead of the playhead.")]
        float _bufferSeconds = 3f;

        [SerializeField, Min(0.1f), Tooltip("Seconds of frames needed before playback starts or resumes.")]
        float _prerollSeconds = 1f;

        IMediaPlayable _playable;
        Transform _content;
        MediaControls _controls;
        MediaCallouts _callouts;
        ContentHandles _handles;
        float _loadHeight = 1.6f;
        FloorGrabTransformer _grabber;
        XRGrabInteractable _grab;
        BoxCollider _collider;
        ContentAnchor _anchoring;
        bool _wantPlaying;
        bool _placed;
        bool _lost;
        bool _resumeAfterPause;
        bool _muted;
        bool _loading;
        bool _hiddenByMenu;
        bool? _ownControlsShown;
        bool _bodyGrab = true;
        float _nextStatusLog;
        float _nextCollider;
        float _fps;
        string _status = "Loading…";

        /// <summary>What to play. Set it before the component starts, or call <see cref="Open"/>.</summary>
        public string Source
        {
            get => _source;
            set => _source = value;
        }

        public MediaFormat Format
        {
            get => _format;
            set => _format = value;
        }

        /// <summary>Height of standing media in metres (screens scale by the same factor). Changing it resizes the media.</summary>
        public float Height
        {
            get => _height;
            set
            {
                _height = Mathf.Clamp(value, 0.3f, 4f);
                ApplySize();
            }
        }

        /// <summary>The media's controls, once placed (null when they're hidden).</summary>
        public MediaControls Controls => _controls;

        Transform IHandleTarget.Root => transform;

        Transform IHandleTarget.Controls => _controls != null ? _controls.Panel : null;

        /// <summary>The visible media's bounds in world space (for handles and layout).</summary>
        public Bounds WorldBounds
        {
            get
            {
                var local = _playable != null && _playable.IsLoaded ? _playable.Bounds : new Bounds(new Vector3(0f, _loadHeight * 0.5f, 0f), new Vector3(0.6f, _loadHeight, 0.6f));
                var scale = _content.lossyScale.x;
                var centre = _content.TransformPoint(local.center);
                var size = local.size * scale;
                return new Bounds(centre, new Vector3(Mathf.Max(size.x, size.z), size.y, Mathf.Max(size.x, size.z)));
            }
        }

        /// <summary>A stable name for saving where this media was put: scene, object name and source.</summary>
        string PlacementKey => $"{gameObject.scene.name}/{name}/{_source}";

        /// <summary>The player attached for the current source, once detected.</summary>
        public IMediaPlayable Playable => _playable;

        public bool IsPlaced => _placed;

        // ---------------------------------------------------------------- IMediaTransport

        public bool IsPlaying => _wantPlaying;
        public bool IsBuffering => _wantPlaying && _playable != null && _playable.IsBuffering;
        public float BufferProgress => _playable?.BufferProgress ?? 0f;
        public double Position => _playable?.Position ?? 0d;
        public double Duration => _playable?.Duration ?? 0d;
        public bool HasTimeline => _playable != null && _playable.IsLoaded && _playable.Duration > 0d;
        public bool HasAudio => _playable != null && _playable.HasAudio;
        public float Volume => _volume;
        public bool Muted => _muted;

        public string Status =>
            _status ?? (_playable != null && _playable.IsLoaded && _playable.Duration <= 0d ? _playable.Description : null);

        public void Play()
        {
            if (_hiddenByMenu)
                return; // hidden by the content menu: it plays once it's shown and Play is pressed
            if (_playable != null && _playable.IsLoaded && _playable.Duration <= 0d)
                return; // a still: nothing to play
            _wantPlaying = true;
            if (_playable == null || !_placed)
                return; // starts once it's loaded and in the room
            if (!_loop && _playable.Duration > 0d && _playable.Position >= _playable.Duration - 0.05)
                _playable.Seek(0d); // at the end: Play starts it again
            _playable.Play();
        }

        public void Pause()
        {
            _wantPlaying = false;
            _playable?.Pause();
        }

        public void TogglePlay()
        {
            if (_wantPlaying)
                Pause();
            else
                Play();
        }

        public void Seek(double seconds) => _playable?.Seek(Math.Max(0d, Math.Min(seconds, Math.Max(0d, Duration - 1e-3))));

        public void SetVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            ApplyVolume();
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            ApplyVolume();
        }

        /// <summary>Turn standing media about the up axis, as a grab would.</summary>
        public void Turn(float degrees) => transform.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * transform.rotation;

        /// <summary>Slide standing media across the floor, as a grab would (it stays inside the mapped room).</summary>
        public void MoveBy(Vector3 worldOffset) => MoveTo(transform.position + worldOffset);

        /// <summary>Put the media at <paramref name="position"/> on the floor (kept inside the mapped room).</summary>
        public void MoveTo(Vector3 position) => transform.position = _grabber.Constrain(position);

        /// <summary>
        /// Remember where the media is now: anchored there and saved, so it's back in the same real spot after the headset
        /// comes off or the app restarts. Called when the user lets go of it.
        /// </summary>
        public void SavePlacement()
        {
            if (!_placed || _playable?.Presentation == MediaPresentation.Surround)
                return;
            _anchoring.Key = PlacementKey;
            _anchoring.Commit(new Pose(transform.position, transform.rotation), _height);
        }

        void ApplySize()
        {
            if (_content != null && _playable?.Presentation != MediaPresentation.Surround)
                _content.localScale = Vector3.one * (_height / Mathf.Max(_loadHeight, 0.01f));
        }

        /// <summary>Replace what's playing with <paramref name="source"/> (format detected unless <paramref name="format"/> is set).</summary>
        public void Open(string source, MediaFormat format = MediaFormat.Auto)
        {
            _source = source;
            _format = format;
            if (isActiveAndEnabled)
                StartCoroutine(Load());
        }

        /// <summary>One line for logs and automation.</summary>
        public string DescribeStatus()
        {
            if (_playable == null || !_playable.IsLoaded)
                return $"{Name}: {_status ?? "loading"}";
            var state = !HasTimeline ? "still" : IsBuffering ? "buffering" : _wantPlaying ? "playing" : "paused";
            var time = HasTimeline ? $" {MediaControls.FormatTime(Position)} / {MediaControls.FormatTime(Duration)}" : string.Empty;
            return $"{Name}: {state}{time} · {_playable.Description} · {_playable.Presentation} · {_fps:0} fps · " +
                   $"{transform.position:F2} yaw {transform.eulerAngles.y:0}° · {_anchoring.Status}";
        }

        string Name => !string.IsNullOrEmpty(_title) ? _title : MediaSource.FileName(_source);

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);

            _collider = ContentLayer.CreateBody(transform).AddComponent<BoxCollider>(); // hands grab it; rays pass through it
            SizeCollider(new Bounds(new Vector3(0f, _height * 0.5f, 0f), new Vector3(0.6f, _height, 0.6f)));

            var body = GetComponent<Rigidbody>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;

            _grab = gameObject.AddComponent<XRGrabInteractable>();
            _grab.addDefaultGrabTransformers = false; // FloorGrabTransformer does the moving: across the floor, turning about Y only
            _grab.selectMode = InteractableSelectMode.Multiple;
            _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            _grab.trackScale = false;
            _grab.throwOnDetach = false;
            _grab.useDynamicAttach = true;
            DirectGrabOnly.Apply(_grab); // the ray only clicks; moving it takes a hand or controller at it
            _grabber = gameObject.AddComponent<FloorGrabTransformer>();
            _grab.selectExited.AddListener(_ =>
            {
                if (_grab.interactorsSelecting.Count == 0)
                    SavePlacement(); // let go: remember where it was put
            });

            _anchoring = gameObject.AddComponent<ContentAnchor>();
            _anchoring.KeepUpright = true;
            _anchoring.Moved += () =>
            {
                // Tracking was corrected and the media moved with its anchor: grabs continue from where it is now.
                _grabber.FloorHeight = transform.position.y;
                _grabber.ResetConstraint(transform.position);
            };
        }

        IEnumerator Start()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.State != SessionState.Ready && session.State != SessionState.Failed)
                session.StateChanged += OnSessionStateChanged;
            else
                Place("the app started");

            yield return Load();
        }

        IEnumerator Load()
        {
            if (_loading)
                yield break;
            _loading = true;
            Unload();
            _status = "Loading…";

            var url = MediaSource.Resolve(_source);
            if (url == null)
            {
                Fail("No source set.");
                _loading = false;
                yield break;
            }

            MediaDetection detection = null;
            yield return MediaDetector.Detect(url, _format, d => detection = d);
            if (detection.Error != null)
            {
                Fail(detection.Error);
                _loading = false;
                yield break;
            }

            _playable = MediaPlayables.Create(detection);
            var context = new MediaContext
            {
                Url = detection.Url,
                Title = Name,
                Root = _content,
                Host = gameObject,
                Height = _loadHeight = _height,
                ScreenHeight = _screenHeight,
                Loop = _loop,
                Quality = _quality,
                Up = _up,
                Stereo = _stereo,
                FrameRate = _frameRate,
                MaxGaussians = _maxGaussians > 0 ? _maxGaussians : DefaultMaxGaussians,
                SelectableParts = _selectableParts,
                ParallelDownloads = _parallelDownloads,
                BufferSeconds = _bufferSeconds,
                PrerollSeconds = _prerollSeconds,
                Prefetched = detection.Bytes,
                Viewer = Viewer,
                StartRoutine = StartCoroutine,
            };

            var started = Time.realtimeSinceStartup;
            yield return _playable.Load(context);
            _loading = false;
            if (_playable.Error != null)
            {
                Fail(_playable.Error);
                yield break;
            }

            _status = null;
            ImmersiveXLog.Info($"Media: {Name} · {detection.Kind} · {_playable.Description} · loaded in {Time.realtimeSinceStartup - started:0.0} s.");
            ApplyVolume();
            ConfigureForPresentation();
            ApplySize();
            if (_placed)
                PlaceControls();
            if (_playOnStart)
                _wantPlaying = true;
            if (_wantPlaying && _placed)
                Play();
        }

        void Unload()
        {
            _playable?.Dispose();
            _playable = null;
            _wantPlaying = false;
        }

        static int DefaultMaxGaussians =>
            Application.isMobilePlatform || Application.platform == RuntimePlatform.Android ? 400_000 : 2_000_000;

        void OnSessionStateChanged(SessionState state)
        {
            if (state == SessionState.Ready || state == SessionState.Failed)
                Place("the app started");
        }

        void OnDestroy()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null)
                session.StateChanged -= OnSessionStateChanged;
            Unload();
            MediaLayout.Release(this);
            if (_controls != null)
                Destroy(_controls.gameObject);
            if (_handles != null)
                Destroy(_handles.gameObject);
            if (_callouts != null)
                Destroy(_callouts.gameObject);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _resumeAfterPause = _wantPlaying;
                Pause();
            }
            else if (_resumeAfterPause)
            {
                Play();
            }
        }

        void Update()
        {
            var rate = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _fps = _fps <= 0f ? rate : Mathf.Lerp(_fps, rate, 0.05f);
            if (_playable == null || !_playable.IsLoaded)
                return;

            _playable.Tick(Time.unscaledDeltaTime);
            if (_wantPlaying && _playable.Duration > 0d && !_loop && !_playable.IsBuffering &&
                _playable.Position >= _playable.Duration - 0.05)
                Pause(); // reached the end: the button shows Play again

            if (_placed && Time.unscaledTime >= _nextStatusLog)
            {
                _nextStatusLog = Time.unscaledTime + StatusLogSeconds;
                ImmersiveXLog.Info("Media: " + DescribeStatus());
            }
        }

        void LateUpdate()
        {
            if (_hiddenByMenu)
            {
                Show(false); // another piece of content is showing (content menu)
                if (_callouts != null)
                    _callouts.Show(null, null, false);
                return;
            }

            if (_playable == null || !_playable.IsLoaded)
                return;
            var viewer = Viewer();
            _playable.LateTick(viewer);
            var lost = _anchoring.IsLost && _playable.Presentation != MediaPresentation.Surround;
            if (lost != _lost)
            {
                _lost = lost;
                ImmersiveXLog.Info(lost ? $"Media: {Name} is hidden until the headset finds its anchor again." : $"Media: {Name} is back: its anchor was found.");
            }

            Show(_placed && !lost);

            // Narrated media: its caption on the controls, its callouts as cards beside the parts they explain.
            if (_playable is IMediaAnnotations notes)
            {
                if (_callouts == null)
                    _callouts = MediaCallouts.Create(Name);
                var visible = _placed && !lost;
                _callouts.Show(notes.Callouts, viewer, visible);
                if (_controls != null)
                    _controls.SetCaption(visible ? notes.Caption : null);
            }

            // Parts move only while the content is paused, so a hand reaching through playing content never moves it.
            if (_playable is IMediaParts withParts && withParts.Parts != null)
                withParts.Parts.Movable = !(_playable is IMediaOwnControls own ? own.IsPlaying : _wantPlaying);

            if (_playable.Presentation != MediaPresentation.Surround && !_grabber.IsHeld && Time.unscaledTime >= _nextCollider)
            {
                _nextCollider = Time.unscaledTime + ColliderRefreshSeconds;
                SizeCollider(_playable.Bounds);
            }
        }

        /// <summary>
        /// Show or hide the media with its controls, handles and grab: hidden until placed, while its anchor is lost, and while
        /// the content menu shows something else.
        /// </summary>
        void Show(bool shown)
        {
            if (_content.gameObject.activeSelf != shown)
                _content.gameObject.SetActive(shown);
            if (_playable is IMediaOwnControls own && _ownControlsShown != shown)
            {
                _ownControlsShown = shown;
                own.SetShown(shown); // a web page's panel, and the page waiting while it's hidden
            }

            if (_controls != null && _controls.gameObject.activeSelf != shown)
                _controls.gameObject.SetActive(shown);
            if (_handles != null && _handles.gameObject.activeSelf != shown)
                _handles.gameObject.SetActive(shown);
            var grab = shown && _bodyGrab;
            if (_grab.enabled != grab)
                _grab.enabled = grab;
            if (_collider.enabled != grab)
                _collider.enabled = grab;
        }

        // ---------------------------------------------------------------- content menu (IContentItem)

        string IContentItem.Key => PlacementKey;

        string IContentItem.Title => Name;

        string IContentItem.Kind
        {
            get
            {
                if (_playable == null || !_playable.IsLoaded)
                    return _status != null && _status != "Loading…" ? "Can't play" : "Loading…";
                var description = _playable.Description;
                var dot = description.IndexOf(" · ", StringComparison.Ordinal);
                return dot > 0 ? description.Substring(0, dot) : description;
            }
        }

        /// <summary>False while the content menu shows something else.</summary>
        public bool IsShown => !_hiddenByMenu;

        /// <summary>Show or hide it (the content menu). Hiding pauses it; showing it again leaves it paused.</summary>
        public void SetShown(bool shown)
        {
            if (_hiddenByMenu == !shown)
                return;
            if (!shown)
                Pause();
            _hiddenByMenu = !shown;
            if (!shown)
                Show(false);
            else if (_placed)
                Place("it was picked in the content menu"); // in front of the user again, on the floor they're on
        }

        void ApplyVolume() => _playable?.SetVolume(_muted ? 0f : _volume);

        void Fail(string message)
        {
            _status = message;
            ImmersiveXLog.Error($"Media '{Name}': {message}");
        }

        void SizeCollider(Bounds bounds)
        {
            var size = bounds.size;
            _collider.center = bounds.center;
            _collider.size = new Vector3(Mathf.Clamp(size.x, 0.3f, 4f), Mathf.Max(size.y, 0.3f), Mathf.Clamp(size.z, 0.3f, 4f));
        }

        /// <summary>
        /// Surround media isn't grabbed or placed on the floor; standing media and screens are. Content with selectable parts
        /// isn't grabbed as a whole either: hands pick up its parts, and the handles move the whole.
        /// </summary>
        void ConfigureForPresentation()
        {
            var surround = _playable.Presentation == MediaPresentation.Surround;
            var parts = (_playable as IMediaParts)?.Parts;
            _bodyGrab = !surround && parts == null;
            _grab.enabled = _bodyGrab && !_hiddenByMenu;
            _collider.enabled = _bodyGrab && !_hiddenByMenu;
            if (parts != null)
                ImmersiveXLog.Info($"Media: {Name} has {parts.Parts.Count} selectable parts: a ray click selects one, a hand picks it up; the handles move the whole.");
            if (surround)
            {
                MediaLayout.Release(this); // surround media doesn't take a spot in the room
                if (_handles != null)
                    Destroy(_handles.gameObject);
            }
            else if (_showHandles && _handles == null)
            {
                _handles = ContentHandles.Create(this);
            }
        }

        // ---------------------------------------------------------------- placement

        /// <summary>The user's head (camera) transform.</summary>
        public static Transform ViewerTransform() => Viewer();

        static Transform Viewer()
        {
            var session = ImmersiveXSession.Instance;
            if (session != null && session.Origin != null && session.Origin.Camera != null)
                return session.Origin.Camera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>
        /// Stand it in front of the user, on the floor under them, facing them: when the app starts and whenever it's picked
        /// in the content menu (<paramref name="why"/> says which, for the log). It then stays there, anchored, until the user
        /// moves it or the next start or pick. With the content menu the piece showing takes the spot in front; without it,
        /// several pieces stand around that spot (<see cref="MediaLayout"/>). Saved anchors from earlier visits aren't used:
        /// the room may be another one, on another floor.
        /// </summary>
        void Place(string why)
        {
            var head = Viewer();
            if (head == null)
                return;
            var user = UserRelativePlacement.UserPose(head);
            var floor = FloorHeight();
            var forward = user.rotation * Vector3.forward;
            var anchor = user.position + forward * _distanceFromUser;
            anchor.y = floor;

            var slot = ContentMenu.Instance != null ? 0 : _spot >= 0 ? _spot : MediaLayout.Claim(this);
            var towardUserFromAnchor = Vector3.ProjectOnPlane(user.position - anchor, Vector3.up);
            towardUserFromAnchor = towardUserFromAnchor.sqrMagnitude > 0.09f ? towardUserFromAnchor.normalized : -forward;
            var position = anchor + MediaLayout.Offset(slot, towardUserFromAnchor);
            position.y = floor;
            for (var pull = 0.9f; !IsWalkable(position) && pull > 0.2f; pull -= 0.1f)
                position = anchor + MediaLayout.Offset(slot, towardUserFromAnchor) * pull;
            for (var distance = _distanceFromUser; !IsWalkable(position) && distance > 0.8f; distance -= 0.1f)
                position = new Vector3(user.position.x, floor, user.position.z) + forward * distance;
            position.y = floor;

            // Face the user; if they're standing on the spot, face them as if it were in front of them.
            var towardUser = Vector3.ProjectOnPlane(user.position - position, Vector3.up);
            towardUser = towardUser.sqrMagnitude > 0.09f ? towardUser.normalized : -forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(-towardUser, Vector3.up));
            _grabber.FloorHeight = floor;
            _grabber.IsAllowed = IsWalkable;
            _grabber.ResetConstraint(position);

            _placed = true;
            PlaceControls();
            ImmersiveXLog.Info($"Media: {Name} placed in front of the user ({why}){(slot > 0 ? $", spot {slot + 1}" : string.Empty)}, " +
                               $"{Vector3.Distance(Flat(position), Flat(user.position)):0.0} m away, facing them, on the floor at {floor:0.00} m " +
                               $"({(RoomFloor.RoomIsHere(head) ? $"the floor of {ImmersiveXSession.Instance.CurrentSpace?.Name ?? "the mapped room"}" : "the floor under the user")}).");
            SavePlacement(); // anchored here for this visit
            if (_wantPlaying && _playable != null && _playable.IsLoaded && !_hiddenByMenu)
                Play();
        }

        /// <summary>
        /// The controls stay within reach of the user, low on their right (see <see cref="WithinReach"/>), whatever the media.
        /// Narrated media's caption goes where the user watches: in front of standing media, under a screen, or in front of
        /// the user for media all around them.
        /// </summary>
        void PlaceControls()
        {
            if (!_showControls || _playable is IMediaOwnControls)
            {
                // Content with its own player (a web page) shows its own controls instead.
                if (_controls != null)
                {
                    Destroy(_controls.gameObject);
                    _controls = null;
                }

                return;
            }

            var head = Viewer();
            if (head == null)
                return;
            if (_controls == null)
            {
                _controls = MediaControls.Create(this, Name, Viewer);
                _controls.Hold = () => _handles != null && _handles.IsDragging; // the move bar under it is in use
            }

            var presentation = _playable?.Presentation ?? MediaPresentation.Standing;
            if (presentation == MediaPresentation.Surround)
            {
                // Floating in front of the user, a little below the eyes; it stays where it is.
                var user = UserRelativePlacement.UserPose(head);
                _controls.PlaceCaption(null, user.position + user.rotation * new Vector3(0f, -0.32f, 0.85f));
                return;
            }

            var towardUser = -transform.forward;
            // standing content: in front of it, clear of its front edge when it's wider than the usual 0.6 m, but never
            // further out than 0.6 m short of the user (who may be standing inside a wide scene)
            var front = _playable != null && _playable.IsLoaded ? -_playable.Bounds.min.z : 0f;
            var toUser = Vector3.Distance(Flat(transform.position), Flat(head.position));
            var ahead = Mathf.Max(0.6f, Mathf.Min(front + 0.3f, toUser - 0.6f));
            var offset = presentation == MediaPresentation.Screen
                ? towardUser * 0.3f + Vector3.up * (ScreenBottom - 0.08f)
                : towardUser * ahead + Vector3.up * 0.92f;
            _controls.PlaceCaption(transform, offset, towardViewer: presentation == MediaPresentation.Standing);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The floor under the user (see <see cref="RoomFloor.Height"/>).</summary>
        static float FloorHeight() => RoomFloor.Height(Viewer());

        static bool IsWalkable(Vector3 world)
        {
            var session = ImmersiveXSession.Instance;
            if (session == null || session.Walkable == null || !RoomFloor.RoomIsHere(Viewer()))
                return true; // no mapped room, or it's another room (another floor): nothing to keep inside
            var floor = session.FloorPose;
            var local = Quaternion.Inverse(floor.rotation) * (world - floor.position);
            return session.Walkable.Contains(new Vector2(local.x, local.z));
        }
    }
}
