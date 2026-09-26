namespace ImmersiveX
{
    public enum BoundaryState
    {
        Unknown,
        Visible,
        Hidden,
        NotSupported,
    }

    /// <summary>Controls the system play-area boundary, so mixed reality apps can run without one.</summary>
    public interface IBoundaryProvider
    {
        BoundaryState State { get; }

        /// <summary>Ask the system to hide the boundary. On Quest this only succeeds while passthrough is showing.</summary>
        void RequestHide();
    }
}
