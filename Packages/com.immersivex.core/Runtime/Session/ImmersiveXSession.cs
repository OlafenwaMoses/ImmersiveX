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

        /// <summary>One line describing the room mapping state, for status displays.</summary>
        public string RoomStatus => _room != null ? _room.Status
            : Platform != null && Platform.Capabilities.RoomData == RoomDataSource.None ? "This platform has no room data."
            : State < SessionState.Space ? "Waiting…"
            : "Room access wasn't granted.";

        public event Action<SessionState> StateChanged;

        /// <summary>Raised after a room is recognised, saved or rescanned.</summary>
        public event Action<MappedSpace> SpaceReady;

        RoomMappingFlow _room;

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

            Origin = FindAnyObjectByType<XROrigin>();
            if (Origin == null)
            {
                Fail("The scene has no XR Origin. Create scenes with ImmersiveX ▸ New Scene, or add an XR Origin rig.");
                yield break;
            }

            Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

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
            var roomManagers = PrepareArFoundation();

            Enter(SessionState.Permissions);
            yield return RequestRoomAccess();

            Enter(SessionState.SeeThrough);
            var seeThrough = Platform.Capabilities.SeeThrough;
            SeeThrough.Apply(Origin.Camera, seeThrough);
            if (seeThrough != SeeThroughMode.None && Settings.DisableLocomotionInSeeThrough)
                DisableLocomotion();
            yield return HideBoundaryIfSupported();

            Enter(SessionState.Space);
            if (HasRoomAccess && Platform.Capabilities.RoomData != RoomDataSource.None)
            {
                _room = new RoomMappingFlow(this, (ARPlaneManager)roomManagers[0], (ARBoundingBoxManager)roomManagers[1], new SpaceLibrary());
                yield return _room.Map(rescan: false);
                if (_room.Space != null)
                    SpaceReady?.Invoke(_room.Space);
            }

            Enter(SessionState.Content);
            yield return WaitForTracking();
            var cameraOffset = Origin.CameraFloorOffsetObject != null ? Origin.CameraFloorOffsetObject.transform.localPosition.y : 0f;
            ImmersiveXLog.Info($"Tracking origin: {Origin.CurrentTrackingOriginMode} · camera offset {cameraOffset:0.00} m · " +
                               $"head at {Origin.Camera.transform.position.y:0.00} m");
            PlaceContent();

            Enter(SessionState.Ready);
        }

        /// <summary>Map the room again (Quest: Space Setup) and update the saved room. Only while Ready.</summary>
        public void RescanRoom()
        {
            if (State != SessionState.Ready || _room == null || _room.IsBusy)
                return;
            StartCoroutine(RescanRoutine());
        }

        IEnumerator RescanRoutine()
        {
            yield return _room.Map(rescan: true);
            if (_room.Space != null)
                SpaceReady?.Invoke(_room.Space);
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
                if (Application.isFocused)
                    waitedWhileFocused += Time.unscaledDeltaTime;
                stableFor = tracked && Application.isFocused ? stableFor + Time.unscaledDeltaTime : 0f;
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
