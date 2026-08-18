using System.Collections.Generic;
using Fuse.Core.Versioning;
using Xunit;

namespace Fuse.Core.Tests;

public class FuseReleaseSelectionTests
{
    private static FuseGitHubRelease Rel(string tag, bool draft = false, bool prerelease = false) =>
        new FuseGitHubRelease(tag, draft, prerelease);

    // --- FuseSemVer.TryParse -------------------------------------------------

    [Theory]
    [InlineData("1.0.2", 1, 0, 2)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("  1.2.3  ", 1, 2, 3)]
    public void TryParse_accepts_core_versions(string text, int major, int minor, int patch)
    {
        Assert.True(FuseSemVer.TryParse(text, out var version));
        Assert.Equal(new FuseSemVer(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-rc2")]
    [InlineData("mod-v1.2.3")]
    [InlineData("1.2.x")]
    [InlineData("99999999999999999999.0.0")] // overflows Int32 -> malformed
    public void TryParse_rejects_non_core_versions(string? text)
    {
        Assert.False(FuseSemVer.TryParse(text!, out _));
    }

    // --- FuseSemVer ordering -------------------------------------------------

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.0.10")] // numeric, not lexical
    [InlineData("1.1.9", "1.2.0")]
    [InlineData("1.9.9", "2.0.0")]
    [InlineData("0.18.0", "1.0.0")]
    public void Ordering_is_numeric(string lower, string higher)
    {
        Assert.True(FuseSemVer.TryParse(lower, out var a));
        Assert.True(FuseSemVer.TryParse(higher, out var b));
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(FuseReleaseSelection.IsOutdated(a, b));
        Assert.False(FuseReleaseSelection.IsOutdated(b, a));
    }

    [Fact]
    public void Equal_versions_are_not_outdated()
    {
        Assert.True(FuseSemVer.TryParse("1.0.2", out var a));
        Assert.True(FuseSemVer.TryParse("1.0.2", out var b));
        Assert.Equal(a, b);
        Assert.False(FuseReleaseSelection.IsOutdated(a, b));
    }

    // --- Stable mod tag parsing ---------------------------------------------

    [Theory]
    [InlineData("mod-v1.0.2", 1, 0, 2)]
    [InlineData("mod-v0.13.0", 0, 13, 0)]
    public void TryParseStableModTag_accepts_stable_mod_tags(string tag, int major, int minor, int patch)
    {
        Assert.True(FuseReleaseSelection.TryParseStableModTag(tag, out var version));
        Assert.Equal(new FuseSemVer(major, minor, patch), version);
    }

    [Theory]
    [InlineData("mod-v1.0.1-rc2")]   // release candidate: full release but not stable
    [InlineData("mod-v1.0.0-beta.1")]
    [InlineData("externaleditor-v1.0.0")] // other lane
    [InlineData("tools-v0.2.0")]           // other lane
    [InlineData("v1.0.2")]
    [InlineData("1.0.2")]
    [InlineData("mod-v1.0")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseStableModTag_rejects_other_tags(string? tag)
    {
        Assert.False(FuseReleaseSelection.TryParseStableModTag(tag!, out _));
    }

    // --- Latest-stable selection --------------------------------------------

    [Fact]
    public void SelectLatestStableMod_ignores_other_release_lanes()
    {
        // externaleditor-v* and tools-v* releases can be the single newest entry
        // in the repo-wide feed; they must never be mistaken for a mod update.
        var releases = new List<FuseGitHubRelease>
        {
            Rel("externaleditor-v2.0.0"),
            Rel("tools-v9.9.9"),
            Rel("mod-v1.0.2"),
            Rel("mod-v1.0.1"),
        };

        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out var tag));
        Assert.Equal(new FuseSemVer(1, 0, 2), latest);
        Assert.Equal("mod-v1.0.2", tag);
    }

    [Fact]
    public void SelectLatestStableMod_skips_release_candidates()
    {
        // A newer RC is published as a full release, but a player on the stable
        // 1.0.2 must be told 1.0.2 is current, not nudged onto 1.1.0-rc2.
        var releases = new List<FuseGitHubRelease>
        {
            Rel("mod-v1.1.0-rc2"),
            Rel("mod-v1.0.2"),
        };

        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out var tag));
        Assert.Equal(new FuseSemVer(1, 0, 2), latest);
        Assert.Equal("mod-v1.0.2", tag);
    }

    [Fact]
    public void SelectLatestStableMod_skips_drafts_and_prereleases()
    {
        var releases = new List<FuseGitHubRelease>
        {
            Rel("mod-v2.0.0", draft: true),
            Rel("mod-v1.5.0", prerelease: true),
            Rel("mod-v1.0.2"),
        };

        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out _));
        Assert.Equal(new FuseSemVer(1, 0, 2), latest);
    }

    [Fact]
    public void SelectLatestStableMod_picks_highest_regardless_of_order()
    {
        var releases = new List<FuseGitHubRelease>
        {
            Rel("mod-v1.0.2"),
            Rel("mod-v1.0.10"),
            Rel("mod-v1.0.9"),
        };

        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out var tag));
        Assert.Equal(new FuseSemVer(1, 0, 10), latest);
        Assert.Equal("mod-v1.0.10", tag);
    }

    [Fact]
    public void SelectLatestStableMod_returns_false_when_no_stable_mod_release()
    {
        var releases = new List<FuseGitHubRelease>
        {
            Rel("mod-v1.0.0-rc.1"),
            Rel("externaleditor-v1.0.0"),
            Rel("tools-v0.2.0"),
        };

        Assert.False(FuseReleaseSelection.TrySelectLatestStableMod(releases, out _, out var tag));
        Assert.Null(tag);
    }

    [Fact]
    public void SelectLatestStableMod_handles_empty_and_null()
    {
        Assert.False(FuseReleaseSelection.TrySelectLatestStableMod(new List<FuseGitHubRelease>(), out _, out _));
        Assert.False(FuseReleaseSelection.TrySelectLatestStableMod(null, out _, out _));
    }

    [Fact]
    public void End_to_end_outdated_decision()
    {
        // Player on mod-v1.0.1; feed advertises 1.0.2 stable (plus noise).
        Assert.True(FuseSemVer.TryParse("1.0.1", out var current));

        var releases = new List<FuseGitHubRelease>
        {
            Rel("mod-v1.1.0-rc2"),
            Rel("externaleditor-v3.0.0"),
            Rel("mod-v1.0.2"),
        };

        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out _));
        Assert.True(FuseReleaseSelection.IsOutdated(current, latest));
    }
}
