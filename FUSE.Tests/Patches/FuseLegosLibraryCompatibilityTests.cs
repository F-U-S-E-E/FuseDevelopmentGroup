using FUSE.Patches;
using Model.Definition;
using System.Collections.Generic;
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

        [Fact]
        public void Container_fast_path_skips_unrelated_definition_stores()
        {
            var container = new Container();
            container.Objects.Add(new ContainerItem { Identifier = "unrelated-car" });

            Assert.False(FuseLegosLibraryCompatibility.ContainerMayContainEditedDefinition(
                container,
                new HashSet<string> { "edited-car" }));
        }

        [Fact]
        public void Container_fast_path_preserves_stores_targeted_by_lego_edits()
        {
            var container = new Container();
            container.Objects.Add(new ContainerItem { Identifier = "edited-car" });

            Assert.True(FuseLegosLibraryCompatibility.ContainerMayContainEditedDefinition(
                container,
                new HashSet<string> { "edited-car" }));
        }
    }
}
