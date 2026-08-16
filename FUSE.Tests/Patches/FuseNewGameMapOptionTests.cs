using System.Collections.Generic;
using System.Linq;
using FUSE.Loading;
using FUSE.Patches;
using Game.State;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseNewGameMapOptionTests
    {
        [Fact]
        public void Build_UsesNativeStockOptionThenValidCustomMaps()
        {
            var maps = new List<FuseRegisteredMap>
            {
                new FuseRegisteredMap("z-map", "Zulu", "", "C:\\maps\\z", ""),
                new FuseRegisteredMap("bad-map", "Broken", "", "", "Map.json missing"),
                new FuseRegisteredMap("a-map", "Alpha", "", "C:\\maps\\a", ""),
            };

            var options = FuseNewGameMapOption.Build(maps);

            Assert.Collection(
                options,
                option =>
                {
                    Assert.True(option.IsStock);
                    Assert.Equal("Railroader Base Map", option.DisplayName);
                },
                option => Assert.Equal("a-map", option.MapId),
                option => Assert.Equal("z-map", option.MapId));
        }

        [Fact]
        public void Build_DeduplicatesMapIdsIgnoringCase()
        {
            var maps = new[]
            {
                new FuseRegisteredMap("prr", "PRR", "", "C:\\maps\\prr", ""),
                new FuseRegisteredMap("PRR", "PRR duplicate", "", "C:\\maps\\prr2", ""),
            };

            var options = FuseNewGameMapOption.Build(maps);

            Assert.Equal(2, options.Count);
        }

        [Fact]
        public void SelectionMarker_RoundTripsNormalizedMapId()
        {
            var marker = FuseNewGameMapOption.CreateSelectionMarker("  prr-middle  ");

            Assert.True(FuseNewGameMapOption.TryParseSelectionMarker(marker, out var mapId));
            Assert.Equal("prr-middle", mapId);
        }

        [Fact]
        public void MarkAndClearSelection_PreserveNewGameIdentityAndMode()
        {
            var original = new NewGameSetup(
                "Test Railroad",
                "TEST",
                GameMode.Company,
                "ewh",
                "ewh-steam");

            var marked = FuseNewGameMapOption.MarkSelection(original, "prr-middle");
            var cleared = FuseNewGameMapOption.ClearSelectionMarker(marked);

            Assert.Equal(original.RailroadName, cleared.RailroadName);
            Assert.Equal(original.ReportingMark, cleared.ReportingMark);
            Assert.Equal(original.Mode, cleared.Mode);
            Assert.Equal(original.ProgressionId, cleared.ProgressionId);
            Assert.Equal(original.SetupId, cleared.SetupId);
        }

        [Fact]
        public void MarkSelection_StoresMapOutsideProgressionFields()
        {
            var original = new NewGameSetup(
                "Test Railroad",
                "TEST",
                GameMode.Company,
                "custom-progression",
                "custom-setup");

            var marked = FuseNewGameMapOption.MarkSelection(
                original,
                "prr-middle");

            Assert.Equal(
                original.ProgressionId,
                marked.ProgressionId);
            Assert.True(
                FuseNewGameMapOption.TryParseSelectionMarker(
                    marked.SetupId,
                    out var mapId,
                    out var originalSetupId));
            Assert.Equal("prr-middle", mapId);
            Assert.Equal("custom-setup", originalSetupId);
        }

        [Fact]
        public void ProgressionOptions_UseMapProgressionsOrNoProgressionFallback()
        {
            var withProgressions = new FuseRegisteredMap(
                "prr",
                "PRR",
                "",
                "C:\\maps\\prr",
                "",
                progressionIds: new[] { "late-era", "early-era" });
            var withoutProgressions = new FuseRegisteredMap(
                "rutland",
                "Rutland",
                "",
                "C:\\maps\\rutland",
                "");

            var progressionOptions =
                FuseNewGameProgressionOption.Build(withProgressions);
            var fallbackOptions =
                FuseNewGameProgressionOption.Build(withoutProgressions);

            Assert.Equal(
                new[] { "early-era", "late-era" },
                progressionOptions.Select(option => option.ProgressionId));
            Assert.Single(fallbackOptions);
            Assert.Null(fallbackOptions[0].ProgressionId);
            Assert.Equal(
                FuseNewGameProgressionOption.NoProgressionName,
                fallbackOptions[0].DisplayName);
        }
    }
}
