namespace ImmersiveX
{
    /// <summary>
    /// The contract every platform implements. An adapter says what the device can do and registers the
    /// providers that fill the gaps AR Foundation leaves (permissions, system room scan, boundary, …).
    /// </summary>
    /// <remarks>
    /// Adapters register themselves with <see cref="PlatformRegistry"/> from a
    /// <c>[RuntimeInitializeOnLoadMethod]</c>, so installing an adapter package is all it takes.
    /// Start a new platform by copying <c>Platforms/_Template</c>.
    /// </remarks>
    public abstract class PlatformAdapter
    {
        /// <summary>Short stable ID, matching the <c>id</c> in the platform's <c>platform.json</c> (e.g. "metaquest").</summary>
        public abstract string Id { get; }

        /// <summary>Name shown to people (e.g. "Meta Quest").</summary>
        public abstract string DisplayName { get; }

        public abstract PlatformCapabilities Capabilities { get; }

        /// <summary>When several adapters are active (a device adapter and the editor simulation), the highest wins.</summary>
        public virtual int Priority => 0;

        /// <summary>True when the app is running on this platform right now.</summary>
        public abstract bool IsActive();

        /// <summary>Register this platform's providers with <see cref="Services"/>. Called once, after this adapter is chosen.</summary>
        public virtual void RegisterProviders() { }
    }
}
