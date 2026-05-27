using System.Collections.Generic;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FuseSceneryAssetManagerEditorMenuPatchTests
    {
        [Fact]
        public void FilterDirectOnlyIdentifiersForEditorMenu_RemovesOnlyDirectOnlyEntries()
        {
            var identifiers = new[] { "local-low-shed", "mod-folder-tree", "local-low-crossing" };
            var directOnly = new HashSet<string> { "mod-folder-tree" };

            var filtered = FuseSceneryAssetManagerEditorMenuPatch.FilterDirectOnlyIdentifiersForEditorMenu(
                identifiers,
                directOnly);

            Assert.Equal(new[] { "local-low-shed", "local-low-crossing" }, filtered);
        }

        [Fact]
        public void FilterDirectOnlyIdentifiersForEditorMenu_PreservesOrderWhenNothingIsDirectOnly()
        {
            var identifiers = new[] { "a", "b", "c" };

            var filtered = FuseSceneryAssetManagerEditorMenuPatch.FilterDirectOnlyIdentifiersForEditorMenu(
                identifiers,
                new HashSet<string>());

            Assert.Equal(identifiers, filtered);
        }
    }
}
