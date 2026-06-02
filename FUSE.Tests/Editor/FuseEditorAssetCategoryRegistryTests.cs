using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Covers active-category persistence and the placeholder-slot
    /// rejection rule for the F1–F6 entity-kind selector.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorAssetCategoryRegistryTests
    {
        public FuseEditorAssetCategoryRegistryTests()
        {
            FuseEditorAssetCategoryRegistry.Reset();
        }

        [Fact]
        public void Default_active_category_is_Tracks()
        {
            Assert.Equal(FuseEditorAssetCategory.Tracks, FuseEditorAssetCategoryRegistry.Active);
        }

        [Fact]
        public void All_lists_six_categories_in_F1_F6_order()
        {
            var all = FuseEditorAssetCategoryRegistry.All;
            Assert.Equal(6, all.Count);
            Assert.Equal(FuseEditorAssetCategory.Tracks, all[0].Kind);
            Assert.Equal(FuseEditorAssetCategory.Switches, all[1].Kind);
            Assert.Equal(FuseEditorAssetCategory.Scenery, all[2].Kind);
            Assert.Equal(FuseEditorAssetCategory.Mandelas, all[3].Kind);
            Assert.Equal(FuseEditorAssetCategory.PlaceholderA, all[4].Kind);
            Assert.Equal(FuseEditorAssetCategory.PlaceholderB, all[5].Kind);
        }

        [Fact]
        public void SetActive_changes_active_for_available_kinds()
        {
            FuseEditorAssetCategoryRegistry.SetActive(FuseEditorAssetCategory.Scenery);
            Assert.Equal(FuseEditorAssetCategory.Scenery, FuseEditorAssetCategoryRegistry.Active);

            FuseEditorAssetCategoryRegistry.SetActive(FuseEditorAssetCategory.Mandelas);
            Assert.Equal(FuseEditorAssetCategory.Mandelas, FuseEditorAssetCategoryRegistry.Active);
        }

        [Fact]
        public void SetActive_silently_ignores_placeholder_slots()
        {
            // Start on Tracks (the default); attempting to activate a
            // placeholder MUST leave the active category alone so a
            // keyboard F5/F6 mash doesn't strand the UI in a slot
            // with no asset list.
            FuseEditorAssetCategoryRegistry.SetActive(FuseEditorAssetCategory.PlaceholderA);
            Assert.Equal(FuseEditorAssetCategory.Tracks, FuseEditorAssetCategoryRegistry.Active);

            FuseEditorAssetCategoryRegistry.SetActive(FuseEditorAssetCategory.PlaceholderB);
            Assert.Equal(FuseEditorAssetCategory.Tracks, FuseEditorAssetCategoryRegistry.Active);
        }

        [Fact]
        public void Get_returns_metadata_for_each_kind()
        {
            var info = FuseEditorAssetCategoryRegistry.Get(FuseEditorAssetCategory.Scenery);
            Assert.Equal(FuseEditorAssetCategory.Scenery, info.Kind);
            Assert.Equal("fuse.editor.assets.scenery", info.LabelKey);
            Assert.Equal(FuseEditorIconKind.Scenery, info.IconKind);
            Assert.True(info.IsAvailable);
        }

        [Fact]
        public void Get_marks_placeholders_as_unavailable_with_reason()
        {
            var info = FuseEditorAssetCategoryRegistry.Get(FuseEditorAssetCategory.PlaceholderA);
            Assert.False(info.IsAvailable);
            Assert.Equal("fuse.editor.assets.placeholder.reason", info.UnavailableReasonKey);
        }

        [Fact]
        public void Reset_returns_active_to_Tracks()
        {
            FuseEditorAssetCategoryRegistry.SetActive(FuseEditorAssetCategory.Mandelas);
            Assert.Equal(FuseEditorAssetCategory.Mandelas, FuseEditorAssetCategoryRegistry.Active);

            FuseEditorAssetCategoryRegistry.Reset();
            Assert.Equal(FuseEditorAssetCategory.Tracks, FuseEditorAssetCategoryRegistry.Active);
        }
    }
}
