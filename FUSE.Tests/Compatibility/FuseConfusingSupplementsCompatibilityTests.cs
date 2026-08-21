using System.Collections.Generic;
using FUSE.Compatibility;
using FUSE.Interface.Console;
using KeyValue.Runtime;
using System.Linq;
using System.Reflection;
using System.IO;
using Newtonsoft.Json.Linq;
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
            Assert.Equal(
                new[]
                {
                    "ConfusingSupplements.Bodygroups",
                    "ConfusingSupplements.DestinationSign",
                    "ConfusingSupplements.LabelPrinter",
                    "CS.LiverySwap",
                    "ConfusingSupplements.Refiller"
                },
                FuseConfusingSupplementsCompatibility.ImplementedComponentKinds);
        }

        [Fact]
        public void LabelPrinter_SavedPropertyIdTrimsGroupThenNameFallback()
        {
            var grouped = new FuseConfusingSupplementsLabelPrinterComponent
            {
                Name = "Visible Name",
                Group = " shared-label "
            };
            var ungrouped = new FuseConfusingSupplementsLabelPrinterComponent
            {
                Name = " road-name ",
                Group = "   "
            };

            Assert.Equal("shared-label", grouped.SavedPropertyId);
            Assert.Equal("road-name", ungrouped.SavedPropertyId);
            Assert.Equal(
                "cs.labelprinter.road-name",
                FuseConfusingSupplementsLabelPrinterBuilder.SavedPropertyKey(ungrouped.SavedPropertyId));
            Assert.Equal(string.Empty, FuseConfusingSupplementsLabelPrinterBuilder.ReadText(Value.Null()));
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
                FuseConfusingSupplementsDestinationSignController.NextIndex(current, count, previous));
        }

        [Theory]
        [InlineData("paint.PNG", true)]
        [InlineData("paint.jpeg", true)]
        [InlineData("paint.JPG", true)]
        [InlineData("readme.txt", false)]
        public void LiveryRegistry_AcceptsOnlySupportedTextureFiles(string path, bool expected)
        {
            Assert.Equal(expected, FuseConfusingSupplementsLiveryRegistry.IsTextureFile(path));
        }

        [Fact]
        public void LiveryCompatibility_RegistersRefreshAndDiagnosticCommands()
        {
            var commands = FuseConsoleCommands.CreateAll();
            var keywords = commands.Select(command => CustomAttributeData
                    .GetCustomAttributes(command.GetType())
                    .Single(attribute =>
                        attribute.AttributeType.FullName ==
                        "UI.Console.ConsoleCommandAttribute")
                    .ConstructorArguments[0]
                    .Value as string)
                .ToArray();

            Assert.Contains("/cs-livery-refresh", keywords);
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
        public void RefillerCompatibility_SearchesAllSourceAndTargetSlots()
        {
            Assert.True(FuseConfusingSupplementsRefillerPolicy.CanTargetReceiveFromSource(
                new[] { "water", "diesel" },
                new[] { "coal", "DIESEL" }));
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

        [Fact]
        public void StrangeCustomsFileCache_NormalizesPathsAndTracksEntryState()
        {
            var relative = Path.Combine("fixtures", "audio.wav");
            Assert.Equal(Path.GetFullPath(relative), StrangeCustoms.FileCache.NormalizePath(relative));

            var entry = new StrangeCustoms.FileCache.CacheEntry<string>(relative);
            string observed = null;
            entry.Register(value => observed = value);
            entry.Set("loaded");

            Assert.True(entry.IsValid);
            Assert.False(entry.IsLoading);
            Assert.Equal("loaded", entry.Value);
            Assert.Equal("loaded", observed);

            entry.Invalidate();
            Assert.False(entry.IsValid);
            Assert.Null(entry.Value);
        }

        [Fact]
        public void StrangeCustomsFlowyBuilder_AdaptsLegacyDataToNativeSpliney()
        {
            var definition = StrangeCustoms.FlowyThingBuilder.ConvertDefinition(
                JObject.Parse(@"{
                    'handler': 'StrangeCustoms.FlowyThingBuilder',
                    'style': 'River',
                    'profile': 'River profile',
                    'points': [
                        { 'position': { 'x': 1, 'y': 2, 'z': 3 }, 'width': 4 },
                        { 'position': { 'x': 5, 'y': 6, 'z': 7 }, 'width': 8 }
                    ]
                }"));

            Assert.Equal("river", definition.Type);
            Assert.Equal("River profile", definition.Profile);
            Assert.Equal(-0.1f, definition.OffsetY);
            Assert.Equal(2, definition.Points.Length);
            Assert.Equal(4f, definition.Points[0].Width);
            Assert.Equal(7f, definition.Points[1].Position.z);
        }
    }
}
