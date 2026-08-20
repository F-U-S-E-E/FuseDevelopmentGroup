using System;
using System.Linq;
using FUSE.Compatibility;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public sealed class FuseLegacyTypeRegistryTests
    {
        [Fact]
        public void RegisterSubType_ReplacesSameIdentifierCaseInsensitively()
        {
            var identifier = "test-" + Guid.NewGuid().ToString("N");

            FuseLegacyTypeRegistry.RegisterSubType(typeof(TestBase), identifier, typeof(FirstImplementation));
            FuseLegacyTypeRegistry.RegisterSubType(typeof(TestBase), identifier.ToUpperInvariant(), typeof(SecondImplementation));

            var registrations = FuseLegacyTypeRegistry.Snapshot(typeof(TestBase));
            var registration = Assert.Single(registrations, pair =>
                string.Equals(pair.Key, identifier, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(typeof(SecondImplementation), registration.Value);
        }

        [Fact]
        public void RegisterSubType_RejectsUnrelatedImplementation()
        {
            Assert.Throws<ArgumentException>(() =>
                FuseLegacyTypeRegistry.RegisterSubType(
                    typeof(TestBase),
                    "invalid-" + Guid.NewGuid().ToString("N"),
                    typeof(string)));
        }

        private abstract class TestBase
        {
        }

        private sealed class FirstImplementation : TestBase
        {
        }

        private sealed class SecondImplementation : TestBase
        {
        }
    }
}
