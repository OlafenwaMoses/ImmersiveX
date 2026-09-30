using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
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
    }

    public enum MediaPlacement
    {
        RoomCentre,
        InFrontOfUser,
    }

    /// <summary>
    /// Plays any supported media in the user's room with one component: set <see cref="Source"/> and the format is
    /// detected and the right player attached, all with the same acrylic controls.
    /// <list type="bullet">
    /// <item>GenXR 3.5D holograms and packed 4D splat captures (<c>stream.json</c>), Gaussian splats
    /// (<c>.ply .splat .spz .ksplat</c>), point clouds (<c>.ply</c>), models (<c>.glb .gltf</c>, including Draco and KTX2),
    /// meshes (<c>.obj .ply .stl</c>), frame sequences (a sequence <c>.json</c> or a folder of frames), video
    /// (<c>.mp4 .m4v .mov .webm</c>) and photos (<c>.jpg .png</c>): flat, 180° or 360°, mono or stereo.</item>
    /// <item>Standing media is fitted to <see cref="Height"/> with its feet on the floor, at the centre of the mapped room,
    /// facing the user. Grab it to slide it across the floor; it turns about the up axis only.</item>
    /// <item>It moves only when the user moves it. Where it's put is kept with a spatial anchor (see
    /// <see cref="ContentAnchor"/>), so it stays in the same real spot when the headset comes off and after a relaunch.</item>
    /// <item>Flat video and photos are an upright screen; 180°/360° video and photos surround the user.</item>
    /// <item>It waits, paused, for the Play button unless <c>Play On Start</c> is set.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ImmersiveX/Media/Immersive Media")]
    public sealed class ImmersiveMedia : MonoBehaviour, IMediaTransport
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

        [SerializeField, Tooltip("Where it stands: the centre of the mapped room (in front of the user when there's no room), or in front of the user.")]
        MediaPlacement _placement = MediaPlacement.RoomCentre;

        [SerializeField, Min(0.5f), Tooltip("How far in front of the user it stands when it isn't at the room's centre, in metres.")]
        float _distanceFromUser = 2f;

        [SerializeField, Min(-1), Tooltip("With several pieces of media: which spot this one takes until the user moves it. 0 is the centre, 1 and up stand around it. -1 = automatic (by order in the hierarchy).")]
        int _spot = -1;

        [SerializeField, Tooltip("Which way is up in the file. Auto uses the format's usual convention.")]
        UpAxis _up = UpAxis.Auto;

        [SerializeField, Tooltip("Show the media controls.")]
        bool _showControls = true;

        [SerializeField, Tooltip("Show the side handles: a move bar under the controls, turn bars at the sides and a resize corner.")]
        bool _showHandles = true;

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
        MediaHandles _handles;
        float _loadHeight = 1.6f;
        FloorGrabTransformer _grabber;
        XRGrabInteractable _grab;
        BoxCollider _collider;
        ContentAnchor _anchoring;
        bool _wantPlaying;
        bool _placing;
        bool _placed;
        bool _lost;
        bool _resumeAfterPause;
        bool _muted;
        bool _loading;
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

            _collider = gameObject.AddComponent<BoxCollider>();
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
            _grab.farAttachMode = InteractableFarAttachMode.Far; // a far grab moves from where the ray hit
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
                StartCoroutine(Place());

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
                StartCoroutine(Place());
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

            if (_playable.Presentation != MediaPresentation.Surround && !_grabber.IsHeld && Time.unscaledTime >= _nextCollider)
            {
                _nextCollider = Time.unscaledTime + ColliderRefreshSeconds;
                SizeCollider(_playable.Bounds);
            }
        }

        /// <summary>Show or hide the media with its controls and handles: hidden until placed, and while its anchor is lost.</summary>
        void Show(bool shown)
        {
            if (_content.gameObject.activeSelf != shown)
                _content.gameObject.SetActive(shown);
            if (_controls != null && _controls.gameObject.activeSelf != shown)
                _controls.gameObject.SetActive(shown);
            if (_handles != null && _handles.gameObject.activeSelf != shown)
                _handles.gameObject.SetActive(shown);
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

        /// <summary>Surround media isn't grabbed or placed on the floor; standing media and screens are.</summary>
        void ConfigureForPresentation()
        {
            var surround = _playable.Presentation == MediaPresentation.Surround;
            _grab.enabled = !surround;
            _collider.enabled = !surround;
            if (surround)
            {
                MediaLayout.Release(this); // surround media doesn't take a spot in the room
                if (_handles != null)
                    Destroy(_handles.gameObject);
            }
            else if (_showHandles && _handles == null)
            {
                _handles = MediaHandles.Create(this);
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
        /// Put it back where the user left it: at its spatial anchor, else where it was saved in the room. The first time,
        /// stand it on the floor at the centre of the mapped room (or in front of the user when there's no room), facing
        /// the user, and anchor it there.
        /// </summary>
        IEnumerator Place()
        {
            if (_placing || _placed || Viewer() == null)
                yield break;
            _placing = true;
            RestoredPlacement restored = null;
            var restoring = true;
            _anchoring.Key = PlacementKey;
            _anchoring.Restore(result =>
            {
                restored = result;
                restoring = false;
            });
            while (restoring)
                yield return null;
            _placing = false;

            var head = Viewer();
            if (head == null)
                yield break;
            var user = UserRelativePlacement.UserPose(head);
            var floor = FloorHeight();
            var forward = user.rotation * Vector3.forward;
            Vector3 position;
            Quaternion rotation;
            string where;
            if (restored != null)
            {
                position = restored.Pose.position;
                if (restored.FromAnchor)
                    floor = position.y; // the anchor sits on the real floor
                else
                    position.y = floor;
                rotation = restored.Pose.rotation;
                if (restored.Size > 0f)
                {
                    _height = Mathf.Clamp(restored.Size, 0.3f, 4f);
                    ApplySize();
                }

                where = restored.FromAnchor ? $"at its spatial anchor ({ContentAnchor.Short(_anchoring.SavedAnchorId)})" : "where it was left in the room";
            }
            else
            {
                Vector3 anchor;
                if (_placement == MediaPlacement.RoomCentre && TryRoomCentre(out var centre))
                {
                    anchor = new Vector3(centre.x, floor, centre.z);
                    where = $"at the centre of {ImmersiveXSession.Instance.CurrentSpace?.Name ?? "the room"}";
                }
                else
                {
                    anchor = user.position + forward * _distanceFromUser;
                    anchor.y = floor;
                    where = "in front of the user";
                }

                // Several pieces of media share the room: the first takes the spot, the rest stand around it.
                var slot = _spot >= 0 ? _spot : MediaLayout.Claim(this);
                var towardUserFromAnchor = Vector3.ProjectOnPlane(user.position - anchor, Vector3.up);
                towardUserFromAnchor = towardUserFromAnchor.sqrMagnitude > 0.09f ? towardUserFromAnchor.normalized : -forward;
                position = anchor + MediaLayout.Offset(slot, towardUserFromAnchor);
                position.y = floor;
                for (var pull = 0.9f; !IsWalkable(position) && pull > 0.2f; pull -= 0.1f)
                    position = anchor + MediaLayout.Offset(slot, towardUserFromAnchor) * pull;
                for (var distance = _distanceFromUser; !IsWalkable(position) && distance > 0.8f; distance -= 0.1f)
                    position = new Vector3(user.position.x, floor, user.position.z) + forward * distance;
                if (slot > 0)
                    where += $" (spot {slot + 1})";

                // Face the user; if they're standing on the spot, face them as if it were in front of them.
                var towardUser = Vector3.ProjectOnPlane(user.position - position, Vector3.up);
                towardUser = towardUser.sqrMagnitude > 0.09f ? towardUser.normalized : -forward;
                rotation = Quaternion.LookRotation(-towardUser, Vector3.up);
            }

            transform.SetPositionAndRotation(position, rotation);
            _grabber.FloorHeight = floor;
            _grabber.IsAllowed = IsWalkable;
            _grabber.ResetConstraint(position);

            _placed = true;
            PlaceControls();
            ImmersiveXLog.Info($"Media: placed {where}, {Vector3.Distance(Flat(position), Flat(user.position)):0.0} m from the user, on the floor at {floor:0.00} m.");
            if (restored == null || !restored.FromAnchor)
                SavePlacement(); // anchor it here, so it's in this spot next time too
            if (_wantPlaying && _playable != null && _playable.IsLoaded)
                Play();
        }

        /// <summary>Put the controls where they suit the media: in front of it, under a screen, or in front of the user.</summary>
        void PlaceControls()
        {
            if (!_showControls)
                return;
            var head = Viewer();
            if (head == null)
                return;
            if (_controls == null)
                _controls = MediaControls.Create(this, transform);

            var presentation = _playable?.Presentation ?? MediaPresentation.Standing;
            if (presentation == MediaPresentation.Surround)
            {
                // Floating in front of the user, a little below the eyes; it stays where it is.
                var user = UserRelativePlacement.UserPose(head);
                var at = user.position + user.rotation * new Vector3(0f, -0.45f, 0.85f);
                _controls.Follow(null);
                _controls.Place(at, head.position);
                return;
            }

            var towardUser = -transform.forward;
            var offset = presentation == MediaPresentation.Screen
                ? towardUser * 0.3f + Vector3.up * (ScreenBottom - 0.2f)
                : towardUser * 0.6f + Vector3.up * 0.8f;
            _controls.Follow(transform);
            _controls.Place(offset, head.position);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// The floor under the user: the mapped room's floor; else the XR Origin when tracking starts at the floor; else the
        /// lowest upward-facing surface found so far; else a standing eye height below the head.
        /// </summary>
        static float FloorHeight()
        {
            const float StandingEyeHeight = 1.5f;
            var session = ImmersiveXSession.Instance;
            var head = Viewer();
            if (session != null && session.Walkable != null)
                return session.FloorPose.position.y;

            var origin = session != null ? session.Origin : null;
            if (origin == null)
                return head != null ? head.position.y - StandingEyeHeight : 0f;
            if (origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor)
                return origin.transform.position.y;

            var eye = head != null ? head.position.y : origin.transform.position.y;
            var floor = eye - StandingEyeHeight;
            var found = false;
            var planes = origin.GetComponent<ARPlaneManager>();
            if (planes != null)
            {
                foreach (var plane in planes.trackables)
                {
                    var y = plane.transform.position.y;
                    if (plane.alignment == PlaneAlignment.HorizontalUp && y < eye - 0.8f && (!found || y < floor))
                    {
                        floor = y;
                        found = true;
                    }
                }
            }

            return floor;
        }

        /// <summary>The middle of the mapped room's walkable area, in world space.</summary>
        static bool TryRoomCentre(out Vector3 centre)
        {
            centre = default;
            var session = ImmersiveXSession.Instance;
            var middle = session != null && session.Walkable != null ? session.Walkable.Centre() : null;
            if (!middle.HasValue)
                return false;
            var floor = session.FloorPose;
            centre = floor.position + floor.rotation * new Vector3(middle.Value.x, 0f, middle.Value.y);
            return true;
        }

        static bool IsWalkable(Vector3 world)
        {
            var session = ImmersiveXSession.Instance;
            if (session == null || session.Walkable == null)
                return true;
            var floor = session.FloorPose;
            var local = Quaternion.Inverse(floor.rotation) * (world - floor.position);
            return session.Walkable.Contains(new Vector2(local.x, local.z));
        }
    }
}
