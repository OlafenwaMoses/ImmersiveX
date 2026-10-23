using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace ImmersiveX
{
    /// <summary>
    /// Runs the ImmersiveX start-up flow: pick the platform, ask for permissions, turn on see-through,
    /// prepare the room, then place content in front of the user. Put one in any scene with an XR Origin
    /// (<b>ImmersiveX ▸ New Scene</b> does this for you).
    /// With <see cref="BareMetalContent"/> in the scene it's only the host: it does the headset's groundwork and leaves the
    /// room, the menus and the layout to the app (see <see cref="SessionPlan"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    [AddComponentMenu("ImmersiveX/ImmersiveX Session")]
    public sealed class ImmersiveXSession : MonoBehaviour
    {
        const float TrackingSettleSeconds = 0.5f;
        const float TrackingTimeoutSeconds = 10f;
        const float PassthroughWarmUpSeconds = 0.5f;

        public static ImmersiveXSession Instance { get; private set; }

        public SessionState State { get; private set; } = SessionState.Boot;
        public PlatformAdapter Platform { get; private set; }
        public XROrigin Origin { get; private set; }
        public ImmersiveXSettings Settings { get; private set; }

        /// <summary>The scene's bare metal content (the app ImmersiveX hosts), or null.</summary>
        public BareMetalContent BareMetal { get; private set; }

        /// <summary>What this session sets up.</summary>
        public SessionPlan Plan { get; private set; }

        /// <summary>True when the user allowed access to room data (planes, furniture).</summary>
        public bool HasRoomAccess { get; private set; }

        /// <summary>Where the user stood and faced when content was placed.</summary>
        public Pose UserPose { get; private set; } = Pose.identity;

        public string FailureReason { get; private set; }

        /// <summary>The mapped room (F2), or null when none is mapped.</summary>
        public MappedSpace CurrentSpace => _room?.Space;

        /// <summary>Where the user can walk in <see cref="CurrentSpace"/>, in <see cref="FloorPose"/> space.</summary>
        public WalkableArea Walkable => _room?.Walkable;

        /// <summary>The floor's pose this session.</summary>
        public Pose FloorPose => _room?.FloorPose ?? Pose.identity;

        /// <summary>True while the room is being mapped (at start, or re-created from the content menu).</summary>
        public bool IsMappingRoom => _room != null && _room.IsBusy;

        /// <summary>True when <see cref="RescanRoom"/> (or <see cref="MapRoom"/>) would map the room now.</summary>
        public bool CanRescanRoom => State == SessionState.Ready && _room != null && !_room.IsBusy;

        /// <summary>The room's floor-to-ceiling height from its last scan, or 0 when it isn't known.</summary>
        public float CeilingHeight => _room?.LastRoom != null ? _room.LastRoom.CeilingHeight : 0f;

        /// <summary>One line describing the room mapping state, for status displays.</summary>
        public string RoomStatus => _room != null ? _room.Status
            : Platform != null && Platform.Capabilities.RoomData == RoomDataSource.None ? "This platform has no room data."
            : State < SessionState.Space ? "Waiting…"
            : "Room access wasn't granted.";

        public event Action<SessionState> StateChanged;

        /// <summary>Raised after a room is recognised, saved or rescanned.</summary>
        public event Action<MappedSpace> SpaceReady;

        RoomMappingFlow _room;
        List<Behaviour> _roomManagers;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                ImmersiveXLog.Warn($"Only one ImmersiveX Session is allowed. Disabling the one on '{name}'.");
                enabled = false;
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        IEnumerator Start()
        {
            Settings = ImmersiveXSettings.Load();
            Services.Clear();
            ImmersiveXLog.Info($"Started on {SystemInfo.deviceModel} · {SystemInfo.operatingSystem} · {Application.identifier} {Application.version}");
            BareMetal = FindAnyObjectByType<BareMetalContent>();
            Plan = SessionPlan.For(BareMetal, Settings);
            if (BareMetal != null)
                ImmersiveXLog.Info($"Bare metal: {BareMetal.Describe()}");

            Origin = FindAnyObjectByType<XROrigin>();
            if (Origin == null)
            {
                Fail("The scene has no XR Origin. Create scenes with ImmersiveX ▸ New Scene, or add an XR Origin rig.");
                yield break;
            }

            Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            WorldUi.EnsureEventSystem();
            ContentClickRaycaster.Ensure();
            ContentLayer.ConfigureRig(Origin); // rays pass through content bodies (they only click); hands reach them
            var watched = ImmersiveX.HandPointers.Configure(Origin, Settings.HandPointers); // hands touch and grab; controllers point
            ImmersiveXLog.Info(Settings.HandPointers
                ? "Input: hands and controllers have pointers."
                : $"Input: tracked hands have no pointer (they press and grab); controllers in hand do ({watched} interactors watched).");

            Enter(SessionState.ResolvePlatform);
            Platform = PlatformRegistry.ResolveActive();
            Platform.RegisterProviders();
            ImmersiveXLog.Info($"Platform: {Platform.DisplayName} ({Platform.Id}). {Platform.Capabilities}");
            if (Platform.Capabilities.SeeThrough != SeeThroughMode.None || Platform.Capabilities.RoomData != RoomDataSource.None)
            {
                // The camera, the room's planes and anchors must share one space. A rig's camera height offset (meant
                // for seated VR) would lift the camera above the real floor, so room content would sit below it and drift.
                Origin.CameraYOffset = 0f;
            }
            _roomManagers = PrepareArFoundation();

            Enter(SessionState.Permissions);
            if (Plan.RoomAccess)
                yield return RequestRoomAccess();
            else
                ImmersiveXLog.Info("Room access: not asked (the bare metal content doesn't use room data).");
            if (Plan.IsBareMetal && HasRoomAccess)
                GetOrAdd<ARRaycastManager>(Origin.gameObject); // depth and room ray casts (ImmersiveHost.Raycast); Quest needs room access first
            if (Plan.CameraAccess)
                yield return RequestCameraAccess();

            Enter(SessionState.SeeThrough);
            var seeThrough = Platform.Capabilities.SeeThrough;
            SeeThrough.Apply(Origin.Camera, seeThrough);
            if (seeThrough != SeeThroughMode.None && Settings.DisableLocomotionInSeeThrough)
                DisableLocomotion();
            if (Plan.HideBoundary)
                yield return HideBoundaryIfSupported();

            Enter(SessionState.Space);
            if (HasRoomAccess && Platform.Capabilities.RoomData != RoomDataSource.None)
            {
                _room = new RoomMappingFlow(this, (ARPlaneManager)_roomManagers[0], (ARBoundingBoxManager)_roomManagers[1], new SpaceLibrary());
                if (Plan.MapRoomAtStart)
                    yield return MapRoutine(rescan: false);
                else
                    ImmersiveXLog.Info("Room: not mapped at start (the bare metal content maps it when it wants: ImmersiveHost.MapRoom).");
            }

            Enter(SessionState.Content);
            yield return WaitForTracking();
            var cameraOffset = Origin.CameraFloorOffsetObject != null ? Origin.CameraFloorOffsetObject.transform.localPosition.y : 0f;
            ImmersiveXLog.Info($"Tracking origin: {Origin.CurrentTrackingOriginMode} · camera offset {cameraOffset:0.00} m · " +
                               $"head at {Origin.Camera.transform.position.y:0.00} m");
            if (Plan.PlaceContent)
                PlaceContent();
            if (seeThrough != SeeThroughMode.None && Plan.ViewModes)
                ViewModes.Create(this); // MR or VR: the menu's MR and VR buttons; every start is in MR
            if (Plan.ContentMenu)
                ContentMenu.Create(Origin).Open(); // open in front of the user at start; a fast sideways swipe opens it later

            Enter(SessionState.Ready);
            ImmersiveHost.NotifyReady();
            if (BareMetal != null)
                BareMetal.NotifyHostReady();
        }

        /// <summary>Map the room again (Quest: Space Setup) and update the saved room. Only while Ready.</summary>
        public void RescanRoom()
        {
            if (State != SessionState.Ready || _room == null || _room.IsBusy)
                return;
            StartCoroutine(MapRoutine(rescan: true));
        }

        /// <summary>
        /// Map the room now, as at start: a known room loads, a new one is mapped (Quest: Space Setup). For bare metal content
        /// that doesn't map it at start. Only while Ready, with room access. Returns true when mapping started.
        /// </summary>
        public bool MapRoom()
        {
            if (_room == null)
            {
                ImmersiveXLog.Warn("Room: can't map it without room access, or on a platform without room data.");
                return false;
            }

            if (State != SessionState.Ready || _room.IsBusy)
                return false;
            StartCoroutine(MapRoutine(rescan: false));
            return true;
        }

        IEnumerator MapRoutine(bool rescan)
        {
            yield return _room.Map(rescan);
            if (_room.Space != null)
                SpaceReady?.Invoke(_room.Space);
        }

        IEnumerator RequestCameraAccess()
        {
            var camera = ImmersiveHost.PassthroughCamera;
            if (!camera.IsSupported)
            {
                ImmersiveXLog.Info($"Camera: {camera.Status}.");
                yield break;
            }

            bool? granted = null;
            camera.RequestAccess(result => granted = result);
            while (granted == null)
                yield return null;
            ImmersiveXLog.Info($"Camera: access {(granted.Value ? "granted" : "denied")} ({camera.Provider.Name}).");
        }

        /// <summary>Delete ImmersiveX's saved copy of the current room.</summary>
        public void ForgetRoom() => _room?.Forget();

        /// <summary>Draw the walkable area on the floor for a few seconds.</summary>
        public void ShowWalkableArea() => _room?.ShowWalkableArea();

        /// <summary>Finish or skip the guided look-around (platforms where the app detects the room itself).</summary>
        public void FinishGuidedScan() => _room?.FinishGuidedScan();

        public void SkipGuidedScan() => _room?.SkipGuidedScan();

        void OnApplicationPause(bool paused) => _room?.OnApplicationPause(paused);

        void Enter(SessionState state)
        {
            State = state;
            ImmersiveXLog.Info($"Session: {state}");
            StateChanged?.Invoke(state);
        }

        void Fail(string reason)
        {
            FailureReason = reason;
            ImmersiveXLog.Error(reason);
            Enter(SessionState.Failed);
        }

        /// <summary>Make sure the AR Foundation pieces exist. Room managers stay off until room access is granted.</summary>
        List<Behaviour> PrepareArFoundation()
        {
            if (FindAnyObjectByType<ARSession>() == null)
                new GameObject("AR Session", typeof(ARSession));

            var originObject = Origin.gameObject;
            GetOrAdd<ARAnchorManager>(originObject);

            var roomManagers = new List<Behaviour>
            {
                GetOrAdd<ARPlaneManager>(originObject),
                GetOrAdd<ARBoundingBoxManager>(originObject),
            };
            foreach (var manager in roomManagers)
                manager.enabled = false;
            return roomManagers;
        }

        IEnumerator RequestRoomAccess()
        {
            if (!Services.TryGet<IPermissionProvider>(out var permissions))
            {
                HasRoomAccess = true;
                yield break;
            }

            bool? granted = null;
            permissions.RequestRoomAccess(result => granted = result);
            while (granted == null)
                yield return null;

            HasRoomAccess = granted.Value;
            if (!HasRoomAccess)
                ImmersiveXLog.Warn("Room access was denied. Content still works, but room data (walls, floor, furniture) isn't available.");
        }

        IEnumerator HideBoundaryIfSupported()
        {
            if (!Platform.Capabilities.CanHideBoundary || !Services.TryGet<IBoundaryProvider>(out var boundary))
                yield break;

            // The boundary can only be hidden once passthrough is actually on screen.
            yield return new WaitForSecondsRealtime(PassthroughWarmUpSeconds);
            boundary.RequestHide();
        }

        void DisableLocomotion()
        {
            // Moving or turning the rig would slide content away from the real room, so it's off in mixed reality.
            var providers = Origin.GetComponentsInChildren<LocomotionProvider>(true);
            foreach (var provider in providers)
                provider.enabled = false;
            if (providers.Length > 0)
                ImmersiveXLog.Info($"Turned off {providers.Length} locomotion provider(s) to keep content aligned with the room.");
        }

        IEnumerator WaitForTracking()
        {
            var stableFor = 0f;
            var waitedWhileFocused = 0f;
            while (waitedWhileFocused < TrackingTimeoutSeconds)
            {
                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                var tracked = (head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && isTracked) ||
                              ARSession.state == ARSessionState.SessionTracking;
                var focused = Application.isFocused || Application.isEditor; // on a device, unfocused means a system overlay is up
                if (focused)
                    waitedWhileFocused += Time.unscaledDeltaTime;
                stableFor = tracked && focused ? stableFor + Time.unscaledDeltaTime : 0f;
                if (stableFor >= TrackingSettleSeconds)
                    yield break;
                yield return null;
            }

            ImmersiveXLog.Warn("Head tracking wasn't confirmed in time. Placing content from the current camera pose.");
        }

        void PlaceContent()
        {
            UserPose = UserRelativePlacement.UserPose(Origin.Camera.transform);
            var contents = FindObjectsByType<ImmersiveContent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var content in contents)
                content.PlaceForUser(UserPose);
            ImmersiveXLog.Info($"Placed {contents.Length} content object(s) for the user.");
        }

        static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
