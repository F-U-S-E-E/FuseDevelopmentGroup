using System;
using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Coverage for the tiny save-indicator helper used by the bottom
    /// status bar. The format outputs are user-facing, so locking the
    /// breakpoints (just-now → seconds → minutes → hours → days) here
    /// keeps the UX consistent across editor builds.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorSaveTrackerTests : IDisposable
    {
        public FuseEditorSaveTrackerTests()
        {
            FuseEditorSaveTracker.Reset();
        }

        public void Dispose()
        {
            FuseEditorSaveTracker.Reset();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Default_LastSaveAt_is_null()
        {
            Assert.Null(FuseEditorSaveTracker.LastSaveAt);
            Assert.Equal("—", FuseEditorSaveTracker.GetDisplayString());
        }

        [Fact]
        public void MarkSaved_sets_LastSaveAt_to_recent_utc()
        {
            var before = DateTime.UtcNow;
            FuseEditorSaveTracker.MarkSaved();
            var after = DateTime.UtcNow;

            Assert.NotNull(FuseEditorSaveTracker.LastSaveAt);
            Assert.InRange(FuseEditorSaveTracker.LastSaveAt.Value, before, after);
        }

        [Fact]
        public void Reset_clears_LastSaveAt()
        {
            FuseEditorSaveTracker.MarkSaved();
            FuseEditorSaveTracker.Reset();

            Assert.Null(FuseEditorSaveTracker.LastSaveAt);
        }

        [Theory]
        [InlineData(-1, "just now")]
        [InlineData(0, "just now")]
        [InlineData(1, "1s ago")]
        [InlineData(30, "30s ago")]
        [InlineData(59, "59s ago")]
        [InlineData(60, "1m ago")]
        [InlineData(150, "2m ago")]
        [InlineData(3599, "59m ago")]
        [InlineData(3600, "1h ago")]
        [InlineData(7200, "2h ago")]
        [InlineData(86400, "1d ago")]
        [InlineData(259200, "3d ago")]
        public void FormatElapsed_picks_human_breakpoint(long elapsedSeconds, string expected)
        {
            var elapsed = TimeSpan.FromSeconds(elapsedSeconds);
            Assert.Equal(expected, FuseEditorSaveTracker.FormatElapsed(elapsed));
        }

        [Fact]
        public void GetDisplayString_round_trips_through_MarkSaved()
        {
            FuseEditorSaveTracker.MarkSaved();
            var display = FuseEditorSaveTracker.GetDisplayString();

            // Either "just now" or "0s ago" / "1s ago" depending on
            // sub-second timing. The exact bucket isn't important; the
            // contract is that we don't render "—" after a save.
            Assert.NotEqual("—", display);
        }
    }
}
