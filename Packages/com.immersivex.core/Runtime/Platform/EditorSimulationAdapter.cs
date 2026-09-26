using System;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// The platform used in the Unity editor's Play mode, backed by AR Foundation's XR Simulation.
    /// XR Simulation can't save anchors, so placement uses the room-relative fallback here (see docs/spikes/S4).
    /// </summary>
    sealed class EditorSimulationAdapter : PlatformAdapter
    {
        public override string Id => "editor-simulation";
        public override string DisplayName => "Unity editor (XR Simulation)";
        public override int Priority => -100;

        public override PlatformCapabilities Capabilities { get; } = new PlatformCapabilities
        {
            SeeThrough = SeeThroughMode.CameraFeed,
            RoomData = RoomDataSource.LiveDetection,
            Anchors = AnchorPersistence.RoomRelative,
            Input = InputModes.Controllers | InputModes.Hands,
        };

        public override bool IsActive() => Application.isEditor;

        public override void RegisterProviders() => Services.Register<IPermissionProvider>(new AlwaysGranted());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => PlatformRegistry.Register(new EditorSimulationAdapter());

        sealed class AlwaysGranted : IPermissionProvider
        {
            public void RequestRoomAccess(Action<bool> onResult) => onResult(true);
        }
    }
}
