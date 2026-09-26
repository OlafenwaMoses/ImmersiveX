using System;

namespace ImmersiveX
{
    /// <summary>How the user sees the real world.</summary>
    public enum SeeThroughMode
    {
        /// <summary>VR only: the user sees a virtual environment.</summary>
        None,
        /// <summary>Headset cameras show the room behind the content (Quest, Vision Pro, Android XR).</summary>
        VideoPassthrough,
        /// <summary>A phone or tablet shows its camera feed behind the content.</summary>
        CameraFeed,
        /// <summary>The user looks through transparent lenses (HoloLens, Magic Leap).</summary>
        Optical,
    }

    /// <summary>Where room geometry (floor, walls, furniture) comes from.</summary>
    public enum RoomDataSource
    {
        /// <summary>No room data.</summary>
        None,
        /// <summary>The operating system scans and stores the room (Quest Space Setup).</summary>
        SystemScan,
        /// <summary>Planes and meshes are detected live while the app runs (ARKit, ARCore, XR Simulation).</summary>
        LiveDetection,
        /// <summary>Only the play-area boundary is known (PC VR, PS VR2).</summary>
        PlayArea,
    }

    /// <summary>How content placement survives app restarts.</summary>
    public enum AnchorPersistence
    {
        /// <summary>Placement is not kept.</summary>
        None,
        /// <summary>AR Foundation persistent anchors (Quest, Android XR).</summary>
        Native,
        /// <summary>Anchors saved inside an ARKit world map.</summary>
        WorldMap,
        /// <summary>The operating system keeps anchors itself (visionOS, HoloLens).</summary>
        System,
        /// <summary>ImmersiveX stores poses relative to the saved room's origin (fallback).</summary>
        RoomRelative,
    }

    /// <summary>Ways the user can interact.</summary>
    [Flags]
    public enum InputModes
    {
        None = 0,
        Controllers = 1 << 0,
        Hands = 1 << 1,
        Touch = 1 << 2,
        GazePinch = 1 << 3,
    }

    /// <summary>
    /// What a device can do. Each platform adapter declares one; features read it to choose a strategy or a fallback.
    /// </summary>
    [Serializable]
    public sealed class PlatformCapabilities
    {
        public SeeThroughMode SeeThrough = SeeThroughMode.None;
        public RoomDataSource RoomData = RoomDataSource.None;
        public AnchorPersistence Anchors = AnchorPersistence.RoomRelative;
        public InputModes Input = InputModes.Controllers;

        /// <summary>True when the app can hide the system boundary so the user never has to draw one.</summary>
        public bool CanHideBoundary;

        public override string ToString() =>
            $"See-through: {SeeThrough} · Room data: {RoomData} · Anchors: {Anchors} · Input: {Input} · Hide boundary: {(CanHideBoundary ? "yes" : "no")}";
    }
}
