using FUSE.Authoring.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseMapSessionTests
    {
        private static FuseModDefinition MapPackage(string id) => new FuseModDefinition
        {
            Id = id,
            Name = id,
            Map = new FuseMapDeclaration { DisplayName = id, MapFolder = "Map" }
        };

        private static FuseModDefinition NormalPackage(string id) => new FuseModDefinition
        {
            Id = id,
            Name = id
        };

        [Fact]
        public void ShouldApply_NullDefinition_IsTrue()
        {
            Assert.True(FuseMapSession.ShouldApply(null, "some-map"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("some-map")]
        public void ShouldApply_PackageWithoutMap_IsTrueRegardlessOfActiveMap(string activeMapId)
        {
            Assert.True(FuseMapSession.ShouldApply(NormalPackage("pkg"), activeMapId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ShouldApply_MapPackageWithNoActiveMap_IsFalse(string activeMapId)
        {
            Assert.False(FuseMapSession.ShouldApply(MapPackage("prr-middle-division"), activeMapId));
        }

        [Fact]
        public void ShouldApply_MapPackageMatchingActiveMap_IsTrue()
        {
            Assert.True(FuseMapSession.ShouldApply(MapPackage("prr-middle-division"), "prr-middle-division"));
        }

        [Fact]
        public void ShouldApply_MatchIsCaseInsensitiveAndTrimmed()
        {
            Assert.True(FuseMapSession.ShouldApply(MapPackage("PRR-Middle-Division"), "  prr-middle-division  "));
        }

        [Fact]
        public void ShouldApply_MapPackageForDifferentMap_IsFalse()
        {
            Assert.False(FuseMapSession.ShouldApply(MapPackage("prr-middle-division"), "other-map"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("other-map")]
        public void InactiveSkipReason_StartsWithOptionalSkipPrefix(string activeMapId)
        {
            var reason = FuseMapSession.InactiveSkipReason(activeMapId);

            Assert.StartsWith(FuseMapSession.InactiveSkipReasonPrefix, reason);
            Assert.True(FusePackageFaultRegistry.IsOptionalSkipReason(reason));
        }

        [Fact]
        public void IsOptionalSkipReason_StillAcceptsMixintoReason_AndRejectsOthers()
        {
            Assert.True(FusePackageFaultRegistry.IsOptionalSkipReason("mixinto dependency missing: someMod"));
            Assert.False(FusePackageFaultRegistry.IsOptionalSkipReason("runtime apply exception"));
        }
    }
}
