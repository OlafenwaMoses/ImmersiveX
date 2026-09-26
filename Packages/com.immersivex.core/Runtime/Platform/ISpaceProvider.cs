namespace ImmersiveX
{
    /// <summary>
    /// Room services the platform provides itself. Milestone M1 covers the system room scan; room recognition arrives in M2.
    /// </summary>
    public interface ISpaceProvider
    {
        /// <summary>True when the app can start the operating system's room scan (Quest Space Setup).</summary>
        bool CanRequestSystemScan { get; }

        /// <summary>Start the system room scan. The app is usually paused until the user finishes. Returns false if refused.</summary>
        bool RequestSystemScan();
    }
}
