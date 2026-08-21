using FUSE.Interface;
using Xunit;

namespace FUSE.Tests.Interface
{
    public sealed class FuseWorldLabelsOverlayTests
    {
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, false, true)]
        public void ShouldRender_hides_labels_while_disabled_paused_or_a_game_window_is_open(
            bool enabled,
            bool paused,
            bool shownWindow,
            bool expected)
        {
            Assert.Equal(expected, FuseWorldLabelsOverlay.ShouldRender(enabled, paused, shownWindow));
        }
    }
}
