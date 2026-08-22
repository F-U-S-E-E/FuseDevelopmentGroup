using System.Collections.Generic;
using FUSE.Compatibility;
using FUSE.Interface.Console;
using KeyValue.Runtime;
using System.Linq;
using System.Reflection;
using Xunit;

namespace FUSE.Tests.Compatibility
{
    public sealed class FuseConfusingSupplementsCompatibilityTests
    {
        [Fact]
        public void BodygroupsComponent_UsesLegacyWireKindWithFuseOwnedType()
        {
            var component = new FuseConfusingSupplementsBodygroupsComponent();

            Assert.Equal("ConfusingSupplements.Bodygroups", component.Kind);
            Assert.Empty(component.Groups);
            Assert.Contains(component.Kind, FuseConfusingSupplementsCompatibility.ImplementedComponentKinds);
        }

        [Fact]
        public void BodygroupsBuilder_EmptySavedValueSelectsFirstDeclaredOption()
        {
            var options = new List<KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>>
            {
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "pilot",
                    new FuseConfusingSupplementsBodygroupOption { Name = "Pilot" }),
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "snowplow",
                    new FuseConfusingSupplementsBodygroupOption { Name = "Snowplow" })
            };

            var selected = FuseConfusingSupplementsBodygroupsBuilder.ReadSelectedOption(
                Value.Null(),
                options);

            Assert.Equal("pilot", selected);
            Assert.Equal(
                "cs.bodygroups.pilot",
                FuseConfusingSupplementsBodygroupsBuilder.SavedPropertyKey("pilot"));
        }

        [Fact]
        public void BodygroupsBuilder_SavedValueWinsOverDefault()
        {
            var options = new List<KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>>
            {
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "pilot",
                    new FuseConfusingSupplementsBodygroupOption()),
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "snowplow",
                    new FuseConfusingSupplementsBodygroupOption())
            };

            var selected = FuseConfusingSupplementsBodygroupsBuilder.ReadSelectedOption(
                Value.String("snowplow"),
                options);

            Assert.Equal("snowplow", selected);
        }

        [Fact]
        public void BodygroupsBuilder_StaleSavedValueSelectsFirstDeclaredOption()
        {
            var options = new List<KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>>
            {
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "pilot",
                    new FuseConfusingSupplementsBodygroupOption()),
                new KeyValuePair<string, FuseConfusingSupplementsBodygroupOption>(
                    "snowplow",
                    new FuseConfusingSupplementsBodygroupOption())
            };

            var selected = FuseConfusingSupplementsBodygroupsBuilder.ReadSelectedOption(
                Value.String("removed-option"),
                options);

            Assert.Equal("pilot", selected);
        }

        [Fact]
        public void ReplacementSurface_AccountsForEveryRegisteredRollingStockKind()
        {
            var expected = new[]
            {
                "ConfusingSupplements.Bodygroups",
                "ConfusingSupplements.DestinationSign",
                "ConfusingSupplements.LabelPrinter",
                "CS.LiverySwap",
                "ConfusingSupplements.Refiller"
            };
            var actual = FuseConfusingSupplementsCompatibility.ImplementedComponentKinds;

            Assert.Equal(expected.Length, actual.Count);
            Assert.All(expected, kind => Assert.Contains(kind, actual));
            Assert.All(actual, kind => Assert.Contains(kind, expected));
        }

        [Theory]
        [InlineData(-1, 3, false, 0)]
        [InlineData(2, 3, false, -1)]
        [InlineData(-1, 3, true, 2)]
        [InlineData(0, 3, true, -1)]
        [InlineData(0, 0, false, -1)]
        public void DestinationSign_CyclesThroughHiddenAndNamedStates(
            int current,
            int count,
            bool previous,
            int expected)
        {
            Assert.Equal(
                expected,
                FuseConfusingSupplementsDestinationSignPolicy.NextIndex(current, count, previous));
        }

        [Theory]
        [InlineData("paint.PNG", true)]
        [InlineData("paint.jpeg", true)]
        [InlineData("paint.JPG", true)]
        [InlineData("readme.txt", false)]
        public void LiveryRegistry_AcceptsOnlySupportedTextureFiles(string path, bool expected)
        {
            Assert.Equal(expected, FuseConfusingSupplementsLiveryPolicy.IsTextureFile(path));
        }

        [Fact]
        public void LiveryCompatibility_RegistersRefreshAndDiagnosticCommands()
        {
            var commandTypes = new[]
            {
                typeof(FuseLiveryRefreshCommand),
                typeof(FuseLiveryRefreshAliasCommand),
                typeof(FuseLiveryReportCommand)
            };
            var keywords = commandTypes.Select(commandType => CustomAttributeData
                    .GetCustomAttributes(commandType)
                    .Single(attribute =>
                        attribute.AttributeType.FullName ==
                        "UI.Console.ConsoleCommandAttribute")
                    .ConstructorArguments[0]
                    .Value as string)
                .ToArray();

            Assert.Contains("/cs-livery-refresh", keywords);
            Assert.Contains("/fuse.livery.refresh", keywords);
            Assert.Contains("/fuse.liveries", keywords);
        }

        [Theory]
        [InlineData("diesel", "DIESEL", true)]
        [InlineData("diesel", "coal", false)]
        [InlineData(null, "diesel", false)]
        [InlineData("diesel", null, false)]
        [InlineData("", "diesel", false)]
        [InlineData("diesel", "", false)]
        public void RefillerCompatibility_MatchesLoadIdentifiersCaseInsensitively(
            string sourceIdentifier,
            string targetIdentifier,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseConfusingSupplementsRefillerPolicy.CanTargetReceiveFromSource(
                    new[] { sourceIdentifier },
                    new[] { targetIdentifier }));
        }

        [Fact]
        public void RefillerTransferBudget_IsSharedAcrossTargetSlots()
        {
            var remainingTransfer = 10f;

            var firstSlot = FuseConfusingSupplementsRefillerPolicy.Take(
                ref remainingTransfer,
                6f,
                10f);
            var secondSlot = FuseConfusingSupplementsRefillerPolicy.Take(
                ref remainingTransfer,
                8f,
                10f);

            Assert.Equal(6f, firstSlot);
            Assert.Equal(4f, secondSlot);
            Assert.Equal(0f, remainingTransfer);
        }

    }
}
