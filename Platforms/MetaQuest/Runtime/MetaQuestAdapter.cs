using System;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.Scripting;

namespace ImmersiveX.Platforms.MetaQuest
{
    /// <summary>ImmersiveX on Meta Quest: colour passthrough, Space Setup room data, native persistent anchors, hands and controllers.</summary>
    [Preserve]
    public sealed class MetaQuestAdapter : PlatformAdapter
    {
        public override string Id => "metaquest";
        public override string DisplayName => "Meta Quest";
        public override int Priority => 100;

        public override PlatformCapabilities Capabilities { get; } = new PlatformCapabilities
        {
            SeeThrough = SeeThroughMode.VideoPassthrough,
            RoomData = RoomDataSource.SystemScan,
            Anchors = AnchorPersistence.Native,
            Input = InputModes.Controllers | InputModes.Hands,
            CanHideBoundary = true,
        };

        public override bool IsActive()
        {
            // On a headset, or in the editor when Play mode runs on the Meta XR Simulator.
            if (Application.platform != RuntimePlatform.Android && !Application.isEditor)
                return false;

            // Most reliable signal: Unity OpenXR: Meta's session subsystem is the one running.
            var loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                ? XRGeneralSettings.Instance.Manager.activeLoader
                : null;
            var metaSession = loader != null && loader.GetLoadedSubsystem<XRSessionSubsystem>() is MetaOpenXRSessionSubsystem;
            var metaRuntime = IsMetaRuntime(OpenXRRuntime.name);
            if (!metaSession && !metaRuntime)
                ImmersiveXLog.Info($"Meta Quest adapter inactive: loader '{loader?.name ?? "none"}', OpenXR runtime '{OpenXRRuntime.name}'.");
            return metaSession || metaRuntime;
        }

        public override void RegisterProviders()
        {
            ImmersiveXLog.Info($"OpenXR runtime: {OpenXRRuntime.name} {OpenXRRuntime.version}");
            Services.Register<IPermissionProvider>(new MetaQuestPermissions());
            Services.Register<ISpaceProvider>(new MetaQuestSpace());
            Services.Register<IBoundaryProvider>(new MetaQuestBoundary());
            if (Application.platform == RuntimePlatform.Android) // in the editor (Meta XR Simulator) Unity's VideoPlayer plays audio
                Services.Register<IStreamAudioProvider>(new AndroidStreamAudioProvider());
        }

        static bool IsMetaRuntime(string runtimeName) =>
            !string.IsNullOrEmpty(runtimeName) &&
            (runtimeName.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
             runtimeName.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0);

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => PlatformRegistry.Register(new MetaQuestAdapter());
    }
}
