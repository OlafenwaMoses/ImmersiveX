using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ImmersiveX.Diagnostics
{
    /// <summary>
    /// A world-space panel for checking ImmersiveX on a device: platform, see-through, boundary, room, anchors and
    /// frame rate. Its buttons rescan, forget or show the mapped room, and save / load / erase a persistent test anchor.
    /// Drop it into any ImmersiveX scene; it builds its own UI, places itself to the user's left, and can be moved
    /// with the grab bar underneath.
    /// </summary>
    [AddComponentMenu("ImmersiveX/Diagnostics/Device Check Panel")]
    public sealed class DeviceCheckPanel : MonoBehaviour
    {
        const string SavedAnchorKey = "ImmersiveX.DeviceCheck.AnchorId";
        const float RefreshSeconds = 0.25f;
        const float Width = 520f;
        const float Height = 760f;

        [SerializeField, Tooltip("Metres from the user: X to the right, Y up from eye height, Z forward.")]
        Vector3 _offsetFromUser = new Vector3(-0.6f, -0.05f, 0.75f);

        [SerializeField, Tooltip("Object whose pose is saved as the test anchor. Defaults to the first ImmersiveContent in the scene.")]
        Transform _anchorSubject;

        ImmersiveXSession _session;
        Text _status;
        Text _messages;
        GameObject _marker;
        string _anchorText = "Anchor: none saved yet.";
        float _fps;
        float _nextRefresh;

        string AnchorMessage
        {
            set
            {
                _anchorText = value;
                ImmersiveXLog.Info("Device check · " + value);
            }
        }

        void Start()
        {
            _session = ImmersiveXSession.Instance;
            BuildUi();
            PanelGrabHandle.Attach(gameObject, new Vector2(Width, Height) * 0.001f);
            if (_session != null)
                _session.StateChanged += OnSessionStateChanged;
        }

        void OnDestroy()
        {
            if (_session != null)
                _session.StateChanged -= OnSessionStateChanged;
        }

        void Update()
        {
            var fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _fps = _fps <= 0f ? fps : Mathf.Lerp(_fps, fps, 0.1f);

            if (Time.unscaledTime < _nextRefresh || _status == null)
                return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            _status.text = BuildStatus();
            _messages.text = _anchorText;
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
                LoadAnchor(); // restore automatically after a restart
        }

        string BuildStatus()
        {
            if (_session == null)
                return "No ImmersiveX Session in the scene.";

            var text = new StringBuilder();
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

            text.AppendLine($"Room:  {_session.RoomStatus}");
            if (_session.CurrentSpace != null)
                text.AppendLine($"Room data:  {RoomSummary()}");
            text.Append($"Frame rate:  {_fps:0} fps");
            return text.ToString();
        }

        string RoomSummary()
        {
            var planes = _session.Origin != null ? _session.Origin.GetComponent<ARPlaneManager>() : null;
            var tracking = planes != null && planes.enabled ? "tracking" : "tracking paused";
            var signature = _session.CurrentSpace.Signature;
            var walls = signature?.WallLengths?.Length ?? 0;
            return $"{signature?.FloorArea ?? 0f:0.0} m² floor · {walls} walls · {tracking}";
        }

        // ---------------------------------------------------------------- Room

        /// <summary>Same as pressing the panel button.</summary>
        public void RescanRoom()
        {
            if (_session == null)
                return;
            ImmersiveXLog.Info("Device check · Rescan room requested.");
            _session.RescanRoom();
        }

        /// <summary>Same as pressing the panel button.</summary>
        public void ForgetRoom() => _session?.ForgetRoom();

        /// <summary>Same as pressing the panel button.</summary>
        public void ShowWalkableArea() => _session?.ShowWalkableArea();

        /// <summary>Kept for automation scripts written before M2: same as <see cref="RescanRoom"/>.</summary>
        public void RunSpaceSetup() => RescanRoom();

        // ---------------------------------------------------------------- Persistent anchors

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

        static string Describe(XRResultStatus status) => $"{status.statusCode}, native code {status.nativeStatusCode}";

        void ShowMarker(Transform anchor)
        {
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _marker.name = "Anchor Marker";
                Destroy(_marker.GetComponent<Collider>());
                _marker.transform.localScale = Vector3.one * 0.06f;
                _marker.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.Create(new Color(0.25f, 0.6f, 1f));
            }

            _marker.transform.SetParent(anchor, false);
            _marker.transform.localPosition = new Vector3(0f, 0.18f, 0f); // just above the anchored object
        }

        // ---------------------------------------------------------------- UI

        void BuildUi()
        {
            var root = WorldUi.CreateCanvas(transform, new Vector2(Width, Height));
            WorldUi.Label(root, "Title", "ImmersiveX · Device check", 30, new Vector2(0f, 345f), new Vector2(Width - 40f, 44f), FontStyle.Bold);
            _status = WorldUi.Label(root, "Status", string.Empty, 22, new Vector2(0f, 165f), new Vector2(Width - 40f, 300f));
            _messages = WorldUi.Label(root, "Messages", string.Empty, 19, new Vector2(0f, -35f), new Vector2(Width - 40f, 70f), FontStyle.Italic);

            WorldUi.Label(root, "Room heading", "Room", 18, new Vector2(0f, -85f), new Vector2(Width - 40f, 26f), FontStyle.Bold);
            WorldUi.Button(root, "Rescan room", new Vector2(-165f, -130f), new Vector2(150f, 56f), RescanRoom);
            WorldUi.Button(root, "Forget room", new Vector2(0f, -130f), new Vector2(150f, 56f), ForgetRoom);
            WorldUi.Button(root, "Show area", new Vector2(165f, -130f), new Vector2(150f, 56f), ShowWalkableArea);

            WorldUi.Label(root, "Anchor heading", "Anchor", 18, new Vector2(0f, -185f), new Vector2(Width - 40f, 26f), FontStyle.Bold);
            WorldUi.Button(root, "Save anchor", new Vector2(-165f, -230f), new Vector2(150f, 56f), SaveAnchor);
            WorldUi.Button(root, "Load anchor", new Vector2(0f, -230f), new Vector2(150f, 56f), LoadAnchor);
            WorldUi.Button(root, "Erase anchor", new Vector2(165f, -230f), new Vector2(150f, 56f), EraseAnchor);

            WorldUi.Label(root, "Hint", "Grab the cube with a hand or controller. Point and pull the trigger (or pinch) to press buttons. " +
                                      "Grab the bar below to move this panel.", 16, new Vector2(0f, -320f), new Vector2(Width - 40f, 60f));
        }
    }
}
