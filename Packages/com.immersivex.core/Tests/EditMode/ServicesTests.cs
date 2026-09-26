using System;
using NUnit.Framework;

namespace ImmersiveX.Tests
{
    public class ServicesTests
    {
        interface IGreeter
        {
            string Hello();
        }

        sealed class Greeter : IGreeter
        {
            public string Hello() => "hello";
        }

        [SetUp]
        [TearDown]
        public void Reset() => Services.Clear();

        [Test]
        public void RegisteredServiceCanBeResolvedByInterface()
        {
            Services.Register<IGreeter>(new Greeter());

            Assert.IsTrue(Services.TryGet<IGreeter>(out var greeter));
            Assert.AreEqual("hello", greeter.Hello());
            Assert.AreSame(greeter, Services.Get<IGreeter>());
        }

        [Test]
        public void MissingServiceIsReportedClearly()
        {
            Assert.IsFalse(Services.TryGet<IGreeter>(out _));
            var error = Assert.Throws<InvalidOperationException>(() => Services.Get<IGreeter>());
            StringAssert.Contains("IGreeter", error.Message);
        }

        [Test]
        public void ClearRemovesEverything()
        {
            Services.Register<IGreeter>(new Greeter());
            Services.Clear();
            Assert.IsFalse(Services.TryGet<IGreeter>(out _));
        }

        [Test]
        public void NullServiceIsRejected() =>
            Assert.Throws<ArgumentNullException>(() => Services.Register<IGreeter>(null));
    }
}
