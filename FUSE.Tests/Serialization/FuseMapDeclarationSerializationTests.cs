using FUSE.Authoring.Data;
using FUSE.Authoring.Serialization;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public class FuseMapDeclarationSerializationTests
    {
        [Fact]
        public void MapDeclaration_RoundTripsThroughJson()
        {
            var definition = new FuseModDefinition
            {
                Id = "prr-middle-division",
                Name = "PRR Middle Division",
                Map = new FuseMapDeclaration
                {
                    DisplayName = "PRR Middle Division + EBT",
                    Description = "Altoona to Harrisburg with the East Broad Top.",
                    MapFolder = "Map",
                    SuppressBaseWorld = false
                }
            };

            var roundTripped = FuseSerializer.FromJson(FuseSerializer.ToJson(definition));

            Assert.NotNull(roundTripped.Map);
            Assert.Equal("PRR Middle Division + EBT", roundTripped.Map.DisplayName);
            Assert.Equal("Altoona to Harrisburg with the East Broad Top.", roundTripped.Map.Description);
            Assert.Equal("Map", roundTripped.Map.MapFolder);
            Assert.False(roundTripped.Map.SuppressBaseWorld);
        }

        [Fact]
        public void MapDeclaration_UsesCamelCaseJsonNames()
        {
            var json = FuseSerializer.ToJson(new FuseModDefinition
            {
                Id = "prr",
                Name = "PRR",
                Map = new FuseMapDeclaration { DisplayName = "PRR", MapFolder = "Map" }
            });

            Assert.Contains("\"map\"", json);
            Assert.Contains("\"displayName\"", json);
            Assert.Contains("\"mapFolder\"", json);
            Assert.Contains("\"suppressBaseWorld\"", json);
        }

        [Fact]
        public void MapDeclaration_SuppressesBaseWorldByDefault()
        {
            var declaration = new FuseMapDeclaration();

            Assert.True(declaration.SuppressBaseWorld);
        }

        [Fact]
        public void DefinitionWithoutMap_OmitsMapProperty()
        {
            var json = FuseSerializer.ToJson(new FuseModDefinition { Id = "plain", Name = "Plain" });

            Assert.DoesNotContain("\"map\"", json);
        }
    }
}
