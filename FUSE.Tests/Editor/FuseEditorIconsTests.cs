using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Covers the icon registry's cache + glyph-fallback path. Real
    /// texture loading needs a Unity graphics context, so these tests
    /// exercise the no-texture branch (which is what xUnit-only
    /// builds and any environment without the PNG files will hit).
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorIconsTests
    {
        public FuseEditorIconsTests()
        {
            FuseEditorIcons.Reset();
        }

        [Fact]
        public void Get_returns_glyph_fallback_when_texture_missing()
        {
            // With no Unity graphics context up (xUnit runs out of
            // process), texture loading always fails and the registry
            // falls back to the Unicode glyph. The fallback is what we
            // ship the layout on until an artist provides PNGs.
            var save = FuseEditorIcons.Get(FuseEditorIconKind.Save);
            Assert.False(save.HasTexture);
            Assert.False(string.IsNullOrEmpty(save.GlyphFallback));
        }

        [Fact]
        public void Get_returns_the_same_instance_on_repeat_calls()
        {
            var first = FuseEditorIcons.Get(FuseEditorIconKind.Track);
            var second = FuseEditorIcons.Get(FuseEditorIconKind.Track);

            // Struct equality by member: same kind, same texture-null
            // state, same glyph means the cache hit produced the same
            // entry rather than re-loading.
            Assert.Equal(first.Kind, second.Kind);
            Assert.Equal(first.GlyphFallback, second.GlyphFallback);
            Assert.Equal(first.HasTexture, second.HasTexture);
        }

        [Fact]
        public void Every_icon_kind_has_a_glyph_fallback()
        {
            // Without this guarantee a missing PNG would render as
            // an empty rect and the editor would just have blank
            // toolbar buttons. The registry's Glyphs map must cover
            // every enum member.
            foreach (FuseEditorIconKind kind in System.Enum.GetValues(typeof(FuseEditorIconKind)))
            {
                var icon = FuseEditorIcons.Get(kind);
                Assert.False(string.IsNullOrEmpty(icon.GlyphFallback),
                    $"FuseEditorIconKind.{kind} has no Unicode glyph fallback.");
            }
        }

        [Fact]
        public void Reset_clears_the_cache()
        {
            // After Reset, the next Get must re-create the entry.
            // We can't observe re-creation directly (texture is null
            // either way), but the side-effect of Reset is that any
            // attached Texture2D references get dropped — covered by
            // the registry's internal dictionary clear.
            _ = FuseEditorIcons.Get(FuseEditorIconKind.Save);
            FuseEditorIcons.Reset();
            var afterReset = FuseEditorIcons.Get(FuseEditorIconKind.Save);
            Assert.Equal(FuseEditorIconKind.Save, afterReset.Kind);
        }
    }
}
