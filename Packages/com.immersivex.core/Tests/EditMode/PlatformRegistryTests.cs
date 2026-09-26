using NUnit.Framework;

namespace ImmersiveX.Tests
{
    public class PlatformRegistryTests
    {
        sealed class FakeAdapter : PlatformAdapter
        {
            readonly string _id;
            readonly bool _active;
            readonly int _priority;

            public FakeAdapter(string id, bool active, int priority)
            {
                _id = id;
                _active = active;
                _priority = priority;
            }

            public override string Id => _id;
            public override string DisplayName => _id;
            public override int Priority => _priority;
            public override PlatformCapabilities Capabilities { get; } = new PlatformCapabilities();
            public override bool IsActive() => _active;
        }

        [TearDown]
        public void RemoveFakes()
        {
            foreach (var id in new[] { "test-low", "test-high", "test-inactive", "test-same" })
                PlatformRegistry.Unregister(id);
        }

        [Test]
        public void ResolveActive_PicksHighestPriorityActiveAdapter()
        {
            PlatformRegistry.Register(new FakeAdapter("test-low", active: true, priority: 1));
            PlatformRegistry.Register(new FakeAdapter("test-high", active: true, priority: 1000));
            PlatformRegistry.Register(new FakeAdapter("test-inactive", active: false, priority: 5000));

            Assert.AreEqual("test-high", PlatformRegistry.ResolveActive().Id);
        }

        [Test]
        public void Register_SameIdReplacesEarlierAdapter()
        {
            PlatformRegistry.Register(new FakeAdapter("test-same", active: false, priority: 0));
            PlatformRegistry.Register(new FakeAdapter("test-same", active: true, priority: 2000));

            Assert.AreEqual(1, CountWithId("test-same"));
            Assert.AreEqual("test-same", PlatformRegistry.ResolveActive().Id);
        }

        [Test]
        public void ResolveActive_WithNoActiveAdapter_ReturnsNoCapabilityFallback()
        {
            foreach (var adapter in PlatformRegistry.All)
            {
                if (adapter.IsActive())
                    Assert.Ignore("An installed adapter is active in this editor; fallback can't be observed.");
            }

            var fallback = PlatformRegistry.ResolveActive();

            Assert.AreEqual("unknown", fallback.Id);
            Assert.AreEqual(SeeThroughMode.None, fallback.Capabilities.SeeThrough);
        }

        static int CountWithId(string id)
        {
            var count = 0;
            foreach (var adapter in PlatformRegistry.All)
            {
                if (adapter.Id == id)
                    count++;
            }

            return count;
        }
    }
}
