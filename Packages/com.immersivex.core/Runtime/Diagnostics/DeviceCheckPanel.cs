using System;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ImmersiveX.Diagnostics
{
    /// <summary>
    /// A world-space panel for checking ImmersiveX on a device: platform, see-through, boundary, room data,
    /// anchors and frame rate. Its buttons run the headset checks from spikes S1–S3:
    /// start the system room scan, and save / load / erase a persistent anchor.
    /// Drop it into any ImmersiveX scene; it builds its own UI and places itself to the user's left.
    /// </summary>
    [AddComponentMenu("ImmersiveX/Diagnostics/Device Check Panel")]
    public sealed class DeviceCheckPanel : MonoBehaviour
    {
        const string SavedAnchorKey = "ImmersiveX.DeviceCheck.AnchorId";
        const float RefreshSeconds = 0.25f;
        const float Width = 520f;
        const float Height = 660f;

        [SerializeField, Tooltip("Metres from the user: X to the right, Y up from eye height, Z forward.")]
        Vector3 _offsetFromUser = new Vector3(-0.55f, -0.05f, 0.75f);

        [SerializeField, Tooltip("Object whose pose is saved as the test anchor. Defaults to the first ImmersiveContent in the scene.")]
        Transform _anchorSubject;

        ImmersiveXSession _session;
        Font _font;
        Text _status;
        Text _messages;
        GameObject _marker;
        string _anchorText = "Anchor: none saved yet.";
        string _spaceText = "Space Setup: not requested.";
        bool _spaceSetupRequested;
        float _fps;
        float _nextRefresh;

        string AnchorMessage
        {
            set { _anchorText = value; ImmersiveXLog.Info("Device check · " + value); }
        }

        string SpaceMessage
        {
            set { _spaceText = value; ImmersiveXLog.Info("Device check · " + value); }
        }

        static string Describe(XRResultStatus status) => $"{status.statusCode}, native code {status.nativeStatusCode}";

        void Start()
        {
            _session = ImmersiveXSession.Instance;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUi();
            PanelGrabHandle.Attach(gameObject, new Vector2(Width, Height) * 0.001f); // grab the bar underneath to move the panel
            if (_session != null)
                _session.StateChanged += OnSessionStateChanged;
        }

        void OnDestroy()
        {
            if (_session != null)
                _session.StateChanged -= OnSessionStateChanged;
        }

        void OnApplicationPause(bool paused)
        {
            // Space Setup pauses the app and resumes it when the user is done (spike S1).
            // Other pauses (headset off, Quest menu) aren't Space Setup, so they aren't reported here.
            if (paused || !_spaceSetupRequested)
                return;

            _spaceSetupRequested = false;
            SpaceMessage = "Space Setup: finished; the app is back.";
        }

        void Update()
        {
            var fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _fps = _fps <= 0f ? fps : Mathf.Lerp(_fps, fps, 0.1f);

            if (Time.unscaledTime < _nextRefresh || _status == null)
                return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _status.text = BuildStatus();
            _messages.text = _spaceText + "\n" + _anchorText;
        }

        void OnSessionStateChanged(SessionState state)
        {
            if (state != SessionState.Ready)
                return;

            var user = _session.UserPose;
            var pose = UserRelativePlacement.Apply(user, _offsetFromUser);
            var fromUser = pose.position - user.position;
            fromUser.y = 0f;
            transform.SetPositionAndRotation(pose.position, Quaternion.LookRotation(fromUser.normalized, Vector3.up));

            if (PlayerPrefs.HasKey(SavedAnchorKey))
                LoadAnchor(); // restore automatically after a restart (spike S2)
        }

        string BuildStatus()
        {
            var text = new StringBuilder();
            if (_session == null)
                return "No ImmersiveX Session in the scene.";

            var platform = _session.Platform;
            text.AppendLine($"Platform:  {platform?.DisplayName ?? "resolving…"}");
            text.AppendLine($"Session:  {_session.State}");
            if (platform != null)
            {
                text.AppendLine($"See-through:  {platform.Capabilities.SeeThrough}");
                var boundary = Services.TryGet<IBoundaryProvider>(out var provider) ? provider.State : BoundaryState.NotSupported;
                text.AppendLine($"Boundary:  {boundary}");
                text.AppendLine($"Anchors:  {platform.Capabilities.Anchors}");
            }

            text.AppendLine($"Room access:  {(_session.HasRoomAccess ? "granted" : "not granted")}");
            if (_session.Origin != null)
            {
                var planes = _session.Origin.GetComponent<ARPlaneManager>();
                var boxes = _session.Origin.GetComponent<ARBoundingBoxManager>();
                var planeCount = Count(planes, planes != null ? planes.trackables.count : 0);
                var boxCount = Count(boxes, boxes != null ? boxes.trackables.count : 0);
                text.AppendLine($"Room data:  {planeCount} planes · {boxCount} boxes");
            }

            text.Append($"Frame rate:  {_fps:0} fps");
            return text.ToString();
        }

        static string Count(Behaviour manager, int count) =>
            manager != null && manager.enabled ? count.ToString() : "–";

        // ---------------------------------------------------------------- S1: system room scan

        /// <summary>Same as pressing the panel button.</summary>
        public void RunSpaceSetup()
        {
            if (!Services.TryGet<ISpaceProvider>(out var space) || !space.CanRequestSystemScan)
            {
                SpaceMessage = "Space Setup: this platform has no system room scan.";
                return;
            }

            _spaceSetupRequested = space.RequestSystemScan();
            SpaceMessage = _spaceSetupRequested
                ? "Space Setup: requested. The app pauses until you finish."
                : "Space Setup: the request was refused.";
        }

        // ---------------------------------------------------------------- S2: persistent anchors

        /// <summary>Same as pressing the panel button.</summary>
        public async void SaveAnchor()
        {
            var manager = AnchorManager();
            if (manager == null)
                return;
            if (manager.descriptor == null || !manager.descriptor.supportsSaveAnchor)
            {
                AnchorMessage = "Anchor: this platform can't save anchors (ImmersiveX uses the room-relative fallback).";
                return;
            }

            var subject = _anchorSubject != null ? _anchorSubject : FindAnyObjectByType<ImmersiveContent>()?.transform;
            if (subject == null)
            {
                AnchorMessage = "Anchor: nothing to anchor. Add an ImmersiveContent object.";
                return;
            }

            AnchorMessage = "Anchor: saving…";
            var added = await manager.TryAddAnchorAsync(new Pose(subject.position, subject.rotation));
            if (!added.status.IsSuccess())
            {
                AnchorMessage = $"Anchor: couldn't create one ({Describe(added.status)}).";
                return;
            }

            var saved = await manager.TrySaveAnchorAsync(added.value);
            if (!saved.status.IsSuccess())
            {
                AnchorMessage = $"Anchor: couldn't save ({Describe(saved.status)}).";
                return;
            }

            PlayerPrefs.SetString(SavedAnchorKey, saved.value.guid.ToString());
            PlayerPrefs.Save();
            ShowMarker(added.value.transform);
            AnchorMessage = $"Anchor: saved {Short(saved.value)} at the hologram. Restart the app to test it.";
        }

        /// <summary>Same as pressing the panel button.</summary>
        public async void LoadAnchor()
        {
            var manager = AnchorManager();
            if (manager == null)
                return;
            if (manager.descriptor == null || !manager.descriptor.supportsLoadAnchor)
            {
                AnchorMessage = "Anchor: this platform can't load saved anchors (ImmersiveX uses the room-relative fallback).";
                return;
            }

            if (!TryGetSavedAnchor(out var id))
            {
                AnchorMessage = "Anchor: none saved yet. Press Save anchor first.";
                return;
            }

            AnchorMessage = $"Anchor: loading {Short(id)}…";
            var loaded = await manager.TryLoadAnchorAsync(id);
            if (!loaded.status.IsSuccess())
            {
                AnchorMessage = $"Anchor: couldn't load {Short(id)} ({Describe(loaded.status)}).";
                return;
            }

            ShowMarker(loaded.value.transform);
            AnchorMessage = $"Anchor: loaded {Short(id)}. The blue marker should be where you saved it.";
        }

        /// <summary>Same as pressing the panel button.</summary>
        public async void EraseAnchor()
        {
            var manager = AnchorManager();
            if (manager == null)
                return;
            if (!TryGetSavedAnchor(out var id))
            {
                AnchorMessage = "Anchor: nothing to erase.";
                return;
            }

            var status = await manager.TryEraseAnchorAsync(id);
            PlayerPrefs.DeleteKey(SavedAnchorKey);
            if (_marker != null)
                Destroy(_marker);
            AnchorMessage = status.IsSuccess() ? $"Anchor: erased {Short(id)}." : $"Anchor: erase returned {Describe(status)}.";
        }

        ARAnchorManager AnchorManager()
        {
            var manager = _session != null && _session.Origin != null ? _session.Origin.GetComponent<ARAnchorManager>() : null;
            if (manager == null)
                AnchorMessage = "Anchor: no ARAnchorManager (is the session running?).";
            return manager;
        }

        static bool TryGetSavedAnchor(out SerializableGuid id)
        {
            id = default;
            if (!Guid.TryParse(PlayerPrefs.GetString(SavedAnchorKey, string.Empty), out var guid))
                return false;
            id = new SerializableGuid(guid);
            return true;
        }

        static string Short(SerializableGuid id) => id.guid.ToString("N").Substring(0, 8);

        void ShowMarker(Transform anchor)
        {
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "Anchor Marker";
                Destroy(_marker.GetComponent<Collider>());
                _marker.transform.localScale = Vector3.one * 0.06f;
                _marker.GetComponent<Renderer>().material.color = new Color(0.25f, 0.6f, 1f);
            }

            _marker.transform.SetParent(anchor, false);
            _marker.transform.localPosition = new Vector3(0f, 0.18f, 0f); // just above the anchored object
        }

        // ---------------------------------------------------------------- UI

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }

        void BuildUi()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)canvasObject.transform;
            root.sizeDelta = new Vector2(Width, Height);
            root.localScale = Vector3.one * 0.001f; // 1 UI unit = 1 mm

            var background = Box(root, "Background", Vector2.zero, new Vector2(Width, Height)).gameObject.AddComponent<Image>();
            background.color = new Color(0.05f, 0.1f, 0.18f, 0.92f);

            Label(root, "Title", "ImmersiveX · Device check", 30, new Vector2(0f, 290f), new Vector2(Width - 40f, 44f), FontStyle.Bold);
            _status = Label(root, "Status", string.Empty, 22, new Vector2(0f, 120f), new Vector2(Width - 40f, 270f), FontStyle.Normal);
            _messages = Label(root, "Messages", string.Empty, 19, new Vector2(0f, -70f), new Vector2(Width - 40f, 100f), FontStyle.Italic);

            Button(root, "Run Space Setup", new Vector2(0f, -150f), new Vector2(Width - 40f, 56f), RunSpaceSetup);
            Button(root, "Save anchor", new Vector2(-165f, -225f), new Vector2(150f, 56f), SaveAnchor);
            Button(root, "Load anchor", new Vector2(0f, -225f), new Vector2(150f, 56f), LoadAnchor);
            Button(root, "Erase anchor", new Vector2(165f, -225f), new Vector2(150f, 56f), EraseAnchor);
            Label(root, "Hint", "Grab the cube with a hand or controller. Point and pull the trigger (or pinch) to press buttons. Grab the bar below to move this panel.",
                16, new Vector2(0f, -295f), new Vector2(Width - 40f, 44f), FontStyle.Normal);
        }

        static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        Text Label(Transform parent, string name, string value, int fontSize, Vector2 position, Vector2 size, FontStyle style)
        {
            var text = Box(parent, name, position, size).gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = new Color(0.92f, 0.95f, 1f);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        void Button(Transform parent, string label, Vector2 position, Vector2 size, UnityAction onClick)
        {
            var rect = Box(parent, label, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.42f, 0.8f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = Label(rect, "Label", label, 20, Vector2.zero, size, FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
        }
    }
}
