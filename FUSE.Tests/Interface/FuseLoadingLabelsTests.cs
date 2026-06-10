using FUSE.Interface;
using Xunit;

namespace FUSE.Tests.Interface
{
    // Pins the game-flavor-string -> friendly-step mapping and the count formatter
    // for the enhanced loading screen (issue #83). Both are Unity-free, so the
    // contract is assertable without the engine or a live load.
    public class FuseLoadingLabelsTests
    {
        [Fact]
        public void MapProgressFlavor_SnapshotMarker_MapsToRestoringSavedGame()
        {
            var step = FuseLoadingLabels.MapProgressFlavor("Half a car...", 0.95f);
            Assert.Equal("Restoring saved game", step.Title);
            Assert.Equal("Reading world snapshot", step.Detail);
        }

        [Fact]
        public void MapProgressFlavor_SnapshotMarker_FlagsTheSyncHandoff()
        {
            // The 95% hand-off is the boundary into the blocking save restore, so
            // the bar must switch to its static band here rather than stay a fill.
            Assert.True(FuseLoadingLabels.MapProgressFlavor("Half a car...", 0.95f).SyncHandoff);
        }

        [Theory]
        [InlineData("Two cars to a couple...", 0.5f)] // async scene load is determinate
        [InlineData("Tyin' down...", 0f)]
        [InlineData("Brewing coffee", 0.5f)]          // unknown fallback
        public void MapProgressFlavor_NonSnapshotPhases_AreNotSyncHandoff(string gameText, float fraction)
        {
            Assert.False(FuseLoadingLabels.MapProgressFlavor(gameText, fraction).SyncHandoff);
        }

        [Theory]
        [InlineData("Tyin' down...")]
        [InlineData("Tying down")] // tolerate a spelling/apostrophe fix in a game update
        public void MapProgressFlavor_UnloadSpellings_MapToReturningToMainMenu(string gameText)
        {
            Assert.Equal("Returning to main menu", FuseLoadingLabels.MapProgressFlavor(gameText, 0f).Title);
        }

        [Theory]
        [InlineData(0.0f, "Loading world")]   // early menu/UI hand-off
        [InlineData(0.19f, "Loading world")]
        [InlineData(0.2f, "Loading terrain")] // long terrain/environment stream
        [InlineData(0.85f, "Loading terrain")]
        public void MapProgressFlavor_SceneLoad_SplitsByProgress(float fraction, string expectedTitle)
        {
            var step = FuseLoadingLabels.MapProgressFlavor("Two cars to a couple...", fraction);
            Assert.Equal(expectedTitle, step.Title);
        }

        [Fact]
        public void MapProgressFlavor_Unload_MapsToReturningToMainMenu()
        {
            var step = FuseLoadingLabels.MapProgressFlavor("Tyin' down...", 0f);
            Assert.Equal("Returning to main menu", step.Title);
            Assert.Null(step.Detail);
        }

        [Fact]
        public void MapProgressFlavor_UnknownString_FallsBackToRawTextAsDetail()
        {
            var step = FuseLoadingLabels.MapProgressFlavor("Brewing coffee", 0.5f);
            Assert.Equal("Loading world", step.Title);
            Assert.Equal("Brewing coffee", step.Detail);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MapProgressFlavor_NullOrEmpty_FallsBackWithoutDetail(string gameText)
        {
            var step = FuseLoadingLabels.MapProgressFlavor(gameText, 0.5f);
            Assert.Equal("Loading world", step.Title);
            Assert.Null(step.Detail);
        }

        [Fact]
        public void MapProgressFlavor_IsCaseAndWhitespaceInsensitive()
        {
            var step = FuseLoadingLabels.MapProgressFlavor("  HALF A CAR...  ", 0.95f);
            Assert.Equal("Restoring saved game", step.Title);
        }

        [Theory]
        [InlineData(0, "car", "cars", "0 cars")]
        [InlineData(1, "car", "cars", "1 car")]
        [InlineData(2, "car", "cars", "2 cars")]
        [InlineData(1432, "car", "cars", "1,432 cars")]
        [InlineData(1, "turntable", "turntables", "1 turntable")]
        [InlineData(3, "turntable", "turntables", "3 turntables")]
        public void DescribeCount_FormatsWithThousandsAndPluralization(int count, string singular, string plural, string expected)
        {
            Assert.Equal(expected, FuseLoadingLabels.DescribeCount(count, singular, plural));
        }

        [Fact]
        public void DescribeCount_NegativeSentinel_ReturnsNull()
        {
            // -1 means "couldn't read the snapshot collection" — show the title alone.
            Assert.Null(FuseLoadingLabels.DescribeCount(-1, "car", "cars"));
        }
    }
}
