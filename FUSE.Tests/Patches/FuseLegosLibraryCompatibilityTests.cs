using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FuseLegosLibraryCompatibilityTests
    {
        [Theory]
        [InlineData(-2, true)]
        [InlineData(-1, false)]
        [InlineData(0, false)]
        [InlineData(1, false)]
        public void Detail_model_refresh_runs_only_after_iterator_completion(int state, bool expected)
        {
            Assert.Equal(expected, FuseLegosLibraryCompatibility.IsCompletedIteratorState(state));
        }

        [Fact]
        public void Detail_model_refresh_rejects_missing_or_unexpected_state_values()
        {
            Assert.False(FuseLegosLibraryCompatibility.IsCompletedIteratorState(null));
            Assert.False(FuseLegosLibraryCompatibility.IsCompletedIteratorState("-2"));
        }
    }
}
