using System.Collections.Generic;

namespace ImmersiveX
{
    /// <summary>Keeps every installed platform adapter and picks the one for the current device.</summary>
    public static class PlatformRegistry
    {
        static readonly List<PlatformAdapter> Adapters = new List<PlatformAdapter>();

        public static IReadOnlyList<PlatformAdapter> All => Adapters;

        /// <summary>Add an adapter. Registering the same ID again replaces the earlier one.</summary>
        public static void Register(PlatformAdapter adapter)
        {
            Adapters.RemoveAll(existing => existing.Id == adapter.Id);
            Adapters.Add(adapter);
        }

        /// <summary>Remove an adapter (used by tests).</summary>
        internal static void Unregister(string id) => Adapters.RemoveAll(existing => existing.Id == id);

        /// <summary>The highest-priority active adapter, or a no-capability fallback when none is active.</summary>
        public static PlatformAdapter ResolveActive()
        {
            PlatformAdapter best = null;
            foreach (var adapter in Adapters)
            {
                if (adapter.IsActive() && (best == null || adapter.Priority > best.Priority))
                    best = adapter;
            }

            if (best != null)
                return best;

            var registered = Adapters.Count > 0 ? string.Join(", ", Adapters.ConvertAll(adapter => adapter.Id)) : "none";
            ImmersiveXLog.Warn($"No platform adapter matches this device (registered: {registered}). Install one with ImmersiveX ▸ Platform Setup. Running with no XR capabilities.");
            return new UnknownPlatformAdapter();
        }

        sealed class UnknownPlatformAdapter : PlatformAdapter
        {
            public override string Id => "unknown";
            public override string DisplayName => "Unknown platform";
            public override PlatformCapabilities Capabilities { get; } = new PlatformCapabilities();
            public override bool IsActive() => true;
        }
    }
}
