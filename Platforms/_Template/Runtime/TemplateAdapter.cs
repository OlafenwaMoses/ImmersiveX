using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace ImmersiveX.Platforms.Template
{
    /// <summary>
    /// A platform adapter says what the device can do and registers providers for what AR Foundation doesn't cover.
    /// Rename the class, the namespace and <see cref="Id"/>, then fill in each part marked "TEMPLATE".
    /// </summary>
    [Preserve]
    public sealed class TemplateAdapter : PlatformAdapter
    {
        // TEMPLATE: match "id" in platform.json.
        public override string Id => "template";

        public override string DisplayName => "Template platform";

        // TEMPLATE: higher than the editor simulation (-100) so the device wins when both are active.
        public override int Priority => 100;

        // TEMPLATE: declare honestly. Features pick strategies and fallbacks from these values.
        public override PlatformCapabilities Capabilities { get; } = new PlatformCapabilities
        {
            SeeThrough = SeeThroughMode.None,
            RoomData = RoomDataSource.None,
            Anchors = AnchorPersistence.RoomRelative,
            Input = InputModes.Controllers,
            CanHideBoundary = false,
        };

        // TEMPLATE: return true only when running on this platform, e.g. check Application.platform and the
        // running XR loader or session subsystem. The template never activates.
        public override bool IsActive() => false;

        public override void RegisterProviders()
        {
            // TEMPLATE: register what the platform provides, for example:
            // Services.Register<IPermissionProvider>(new TemplatePermissions());
            // Services.Register<ISpaceProvider>(new TemplateSpace());
            // Services.Register<IBoundaryProvider>(new TemplateBoundary());
            Services.Register<IPermissionProvider>(new TemplatePermissions());
        }

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => PlatformRegistry.Register(new TemplateAdapter());

        /// <summary>TEMPLATE: ask the OS for room-data access here. Platforms without a permission grant it immediately.</summary>
        sealed class TemplatePermissions : IPermissionProvider
        {
            public void RequestRoomAccess(Action<bool> onResult) => onResult(true);
        }
    }
}
