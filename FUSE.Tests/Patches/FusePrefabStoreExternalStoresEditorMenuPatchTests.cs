using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public class FusePrefabStoreExternalStoresEditorMenuPatchTests
    {
        [Fact]
        public void ShouldShowExternalStoreIdentifier_HidesFuseDirectStores()
        {
            Assert.False(FusePrefabStoreExternalStoresEditorMenuPatch.ShouldShowExternalStoreIdentifier(
                "fuseasset://C%3A%5CSteam%5Csteamapps%5Ccommon%5CRailroader%5CMods%5CCALW.SceneryAssets%5CSCAssetPacks%5CCALWBuildings"));
        }

        [Fact]
        public void ShouldShowExternalStoreIdentifier_KeepsPhysicalStoreIdentifiers()
        {
            Assert.True(FusePrefabStoreExternalStoresEditorMenuPatch.ShouldShowExternalStoreIdentifier("CALWBuildings"));
            Assert.True(FusePrefabStoreExternalStoresEditorMenuPatch.ShouldShowExternalStoreIdentifier("shared"));
        }
    }
}
