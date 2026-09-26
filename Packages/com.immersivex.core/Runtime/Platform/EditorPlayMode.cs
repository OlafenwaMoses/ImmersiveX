namespace ImmersiveX
{
    /// <summary>
    /// Which XR runtime the editor's Play mode uses, chosen from <b>ImmersiveX ▸ Play Mode</b> and stored per user.
    /// Platform adapters can add their own simulators (for example the Meta XR Simulator).
    /// </summary>
    public static class EditorPlayMode
    {
        /// <summary>AR Foundation's XR Simulation: works for every platform, no headset needed (the default).</summary>
        public const string XRSimulation = "xr-simulation";

        const string PreferenceKey = "ImmersiveX.PlayModeRuntime";

#if UNITY_EDITOR
        public static string Current
        {
            get => UnityEditor.EditorPrefs.GetString(PreferenceKey, XRSimulation);
            set => UnityEditor.EditorPrefs.SetString(PreferenceKey, value);
        }
#else
        public static string Current => XRSimulation;
#endif
    }
}
