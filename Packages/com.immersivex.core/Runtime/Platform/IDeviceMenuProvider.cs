namespace ImmersiveX
{
    /// <summary>A platform's way of showing its own system menu over the app (Quest: the universal menu's dock).</summary>
    public interface IDeviceMenuProvider
    {
        /// <summary>Show the system menu. Returns what was opened, or null when it couldn't be.</summary>
        string Open();
    }
}
