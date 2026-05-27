using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Coverage for the new Locations + Assets window kinds added by
    /// the EDEN-style overhaul, and the deliberately-off-by-default
    /// ToolStrip kind (the icon toolbar replaces it as the primary
    /// gizmo surface).
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorWindowRegistryExtensionsTests
    {
        public FuseEditorWindowRegistryExtensionsTests()
        {
            FuseEditorWindowRegistry.ResetToDefaults();
        }

        [Fact]
        public void Locations_is_registered_and_open_by_default()
        {
            Assert.True(FuseEditorWindowRegistry.OpenByDefault(FuseEditorWindowKind.Locations));
            Assert.True(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Locations));
            Assert.Equal("fuse.editor.window.locations",
                FuseEditorWindowRegistry.NameKey(FuseEditorWindowKind.Locations));
        }

        [Fact]
        public void Assets_is_registered_and_open_by_default()
        {
            Assert.True(FuseEditorWindowRegistry.OpenByDefault(FuseEditorWindowKind.Assets));
            Assert.True(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Assets));
            Assert.Equal("fuse.editor.window.assets",
                FuseEditorWindowRegistry.NameKey(FuseEditorWindowKind.Assets));
        }

        [Fact]
        public void ToolStrip_is_registered_but_closed_by_default()
        {
            // The icon toolbar replaces the bottom tool strip as the
            // primary surface; the strip stays togglable from View
            // for users who want it back, but defaults off.
            Assert.False(FuseEditorWindowRegistry.OpenByDefault(FuseEditorWindowKind.ToolStrip));
            Assert.False(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.ToolStrip));
        }

        [Fact]
        public void Toggle_round_trips_the_new_kinds()
        {
            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.Locations);
            Assert.False(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Locations));
            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.Locations);
            Assert.True(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Locations));

            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.Assets);
            Assert.False(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Assets));
            FuseEditorWindowRegistry.Toggle(FuseEditorWindowKind.Assets);
            Assert.True(FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Assets));
        }

        [Fact]
        public void All_lists_every_kind_including_new_ones()
        {
            var all = new System.Collections.Generic.HashSet<FuseEditorWindowKind>(FuseEditorWindowRegistry.All());
            Assert.Contains(FuseEditorWindowKind.EntityTree, all);
            Assert.Contains(FuseEditorWindowKind.Locations, all);
            Assert.Contains(FuseEditorWindowKind.Properties, all);
            Assert.Contains(FuseEditorWindowKind.Assets, all);
            Assert.Contains(FuseEditorWindowKind.ToolStrip, all);
        }
    }
}
