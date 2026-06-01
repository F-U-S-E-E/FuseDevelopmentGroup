using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Covers <see cref="FuseAssetPackRegistry.NormalizeLegacyAssetPackIdentifier"/>, which lets
    /// legacy scheme-prefixed asset-pack references (e.g. an older "scheme://owner/pack" form)
    /// collapse onto the base-game-native "owner/pack" aliases FUSE registers — without FUSE's
    /// source naming any particular legacy scheme. Pins the two guarantees: FUSE's own
    /// fuseasset:// scheme is preserved verbatim, and everything else is reduced to the
    /// base-game form so existing content keeps resolving.
    /// </summary>
    public class FuseLegacyAssetPackAliasTests
    {
        [Theory]
        // Base-game-native composite (the form the game's AssetReference uses); backslashes
        // normalize to forward slashes. This is the Goldfinch case.
        [InlineData("RLW RSP-4 Goldfinch Class\\rlw-g3-bc", "RLW RSP-4 Goldfinch Class/rlw-g3-bc")]
        [InlineData("Owner/Pack", "Owner/Pack")]
        // A legacy scheme-prefixed reference collapses onto the bare base-game form.
        [InlineData("zsc://Owner/Pack", "Owner/Pack")]
        [InlineData("zsc://Owner/SCAssetPacks/bulkhead1", "Owner/SCAssetPacks/bulkhead1")]
        // Scheme handling is generic, not tied to any one legacy scheme name.
        [InlineData("anything://Owner/Pack", "Owner/Pack")]
        // Whitespace + trailing separators are trimmed.
        [InlineData("  zsc://Owner/Pack/  ", "Owner/Pack")]
        public void Normalizes_LegacyAndBareForms_ToBaseGameForm(string input, string expected)
        {
            Assert.Equal(expected, FuseAssetPackRegistry.NormalizeLegacyAssetPackIdentifier(input));
        }

        [Theory]
        // FUSE's own direct-store scheme must be preserved verbatim so those identifiers
        // keep resolving to themselves (never collapsed like a legacy scheme).
        [InlineData("fuseasset://F:/SteamLibrary/Mods/Pack/sub", "fuseasset://F:/SteamLibrary/Mods/Pack/sub")]
        [InlineData("FUSEASSET://Mixed/Case", "FUSEASSET://Mixed/Case")]
        public void Preserves_FuseDirectStoreScheme(string input, string expected)
        {
            Assert.Equal(expected, FuseAssetPackRegistry.NormalizeLegacyAssetPackIdentifier(input));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void HandlesEmptyAndNull(string input, string expected)
        {
            Assert.Equal(expected, FuseAssetPackRegistry.NormalizeLegacyAssetPackIdentifier(input));
        }
    }
}
