using System;
using System.IO;
using Fuse.Core.Versioning;
using Xunit;

namespace Fuse.Core.Tests;

public class FuseGitHubReleaseParserTests
{
    // A real GitHub /releases payload for F-U-S-E-E/FuseDevelopmentGroup, captured
    // anonymously and trimmed to the fields the parser reads (plus a few real
    // extras it must ignore). This is the end-to-end proof: raw API JSON ->
    // parse -> select must land on the newest stable mod release. Regenerate by
    // re-fetching the API if the release history changes; the assertion below
    // pins the expectation that was true at capture time.
    private static string RealPayload()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-releases.json");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Real_payload_selects_latest_stable_mod_release()
    {
        Assert.True(FuseGitHubReleaseParser.TryParse(RealPayload(), out var releases));
        Assert.NotEmpty(releases);

        // The captured feed interleaves three lanes and contains RC tags that are
        // NOT flagged prerelease (they ship as full releases), old v0.x tags, a
        // tools-v release, and prerelease-flagged mod builds. Only mod-v1.0.2
        // qualifies as the newest stable mod release.
        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out var tag));
        Assert.Equal(new FuseSemVer(1, 0, 2), latest);
        Assert.Equal("mod-v1.0.2", tag);
    }

    [Fact]
    public void Real_payload_marks_an_older_stable_build_outdated_but_not_a_current_one()
    {
        Assert.True(FuseGitHubReleaseParser.TryParse(RealPayload(), out var releases));
        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out _));

        Assert.True(FuseSemVer.TryParse("1.0.1", out var older));
        Assert.True(FuseReleaseSelection.IsOutdated(older, latest));

        Assert.True(FuseSemVer.TryParse("1.0.2", out var current));
        Assert.False(FuseReleaseSelection.IsOutdated(current, latest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"message\":\"Not Found\"}")] // API error object, not an array
    [InlineData("<html>502 Bad Gateway</html>")] // proxy/error page
    [InlineData("[ this is not json")] // truncated
    public void TryParse_returns_false_for_non_array_bodies(string? body)
    {
        Assert.False(FuseGitHubReleaseParser.TryParse(body!, out var releases));
        Assert.Empty(releases);
    }

    [Fact]
    public void TryParse_reads_fields_and_skips_entries_without_a_tag()
    {
        const string json = @"[
            { ""tag_name"": ""mod-v1.0.2"", ""draft"": false, ""prerelease"": false, ""extra"": ""ignored"" },
            { ""tag_name"": ""mod-v2.0.0"", ""draft"": true,  ""prerelease"": false },
            { ""tag_name"": ""mod-v1.5.0"", ""prerelease"": true },
            { ""name"": ""no tag here"" },
            ""a bare string, not an object""
        ]";

        Assert.True(FuseGitHubReleaseParser.TryParse(json, out var releases));
        Assert.Equal(3, releases.Count); // the tagless object and the bare string are skipped

        // Selection then applies the draft/prerelease/stable-tag rules on top.
        Assert.True(FuseReleaseSelection.TrySelectLatestStableMod(releases, out var latest, out _));
        Assert.Equal(new FuseSemVer(1, 0, 2), latest);
    }
}
