using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Authoring.Validation;
using Xunit;

namespace FUSE.Tests.Validation
{
    public class FuseDefinitionValidatorMapTests
    {
        private static FuseModDefinition Definition(FuseMapDeclaration map) => new FuseModDefinition
        {
            Id = "pkg",
            Name = "Package",
            SchemaVersion = "1.0",
            Map = map
        };

        private static FuseDefinitionValidator NewValidator() => new FuseDefinitionValidator();

        [Fact]
        public void NoMapDeclaration_ProducesNoMapDiagnostics()
        {
            var result = NewValidator().Validate(Definition(null));

            Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("map"));
            Assert.DoesNotContain(result.Warnings, w => w.Field.StartsWith("map"));
        }

        [Fact]
        public void ValidMapDeclaration_ProducesNoMapDiagnostics()
        {
            var result = NewValidator().Validate(Definition(new FuseMapDeclaration
            {
                DisplayName = "PRR Middle Division",
                MapFolder = "Map"
            }));

            Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("map"));
            Assert.DoesNotContain(result.Warnings, w => w.Field.StartsWith("map"));
        }

        [Fact]
        public void BlankMapFolder_IsAnError()
        {
            var result = NewValidator().Validate(Definition(new FuseMapDeclaration
            {
                DisplayName = "PRR Middle Division"
            }));

            Assert.Contains(result.Errors, e => e.Field == "map.mapFolder" && e.Code == "fuse.map.folder.required");
        }

        [Theory]
        [InlineData("C:\\Maps\\PRR")]
        [InlineData("/maps/prr")]
        [InlineData("\\maps\\prr")]
        [InlineData("..\\outside")]
        [InlineData("Map/../../outside")]
        public void RootedOrEscapingMapFolder_IsAnError(string mapFolder)
        {
            var result = NewValidator().Validate(Definition(new FuseMapDeclaration
            {
                DisplayName = "PRR Middle Division",
                MapFolder = mapFolder
            }));

            Assert.Contains(result.Errors, e => e.Field == "map.mapFolder" && e.Code == "fuse.map.folder.outsidePackage");
        }

        [Fact]
        public void BlankDisplayName_IsAWarningOnly()
        {
            var result = NewValidator().Validate(Definition(new FuseMapDeclaration
            {
                MapFolder = "Map"
            }));

            Assert.Contains(result.Warnings, w => w.Field == "map.displayName" && w.Code == "fuse.map.displayName.blank");
            Assert.DoesNotContain(result.Errors, e => e.Field.StartsWith("map"));
        }
    }
}
