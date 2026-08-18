using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Where a running FUSE install most likely came from. Used only to point an
    /// out-of-date player at the right place to update.
    /// </summary>
    internal enum FuseInstallChannel
    {
        Unknown = 0,
        GitHub,
        Nexus,
    }

    /// <summary>
    /// Reads the provenance stamp the release flow writes into a packaged
    /// <c>Info.json</c> and resolves the update destination from it.
    ///
    /// GitHub is the canonical home: it holds the authoritative versioning and
    /// always carries the newest build. Every GitHub-published artifact ships
    /// <c>"Source": "github"</c>; the release flow re-stamps only the Nexus
    /// upload to <c>"nexus"</c> (see <c>scripts/stamp-info-source.ps1</c> and
    /// <c>.github/workflows/release.yml</c>). An absent, unreadable, or
    /// unrecognized stamp reads as <see cref="FuseInstallChannel.Unknown"/>,
    /// which the update check treats as GitHub.
    /// </summary>
    internal static class FuseInstallSource
    {
        internal const string RepositoryOwner = "F-U-S-E-E";
        internal const string RepositoryName = "FuseDevelopmentGroup";

        internal const string GitHubReleasesUrl =
            "https://github.com/F-U-S-E-E/FuseDevelopmentGroup/releases";

        internal const string NexusModUrl =
            "https://www.nexusmods.com/railroader/mods/1645";

        internal static FuseInstallChannel FromToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return FuseInstallChannel.Unknown;
            }

            var trimmed = token.Trim();
            if (string.Equals(trimmed, "nexus", StringComparison.OrdinalIgnoreCase))
            {
                return FuseInstallChannel.Nexus;
            }

            if (string.Equals(trimmed, "github", StringComparison.OrdinalIgnoreCase))
            {
                return FuseInstallChannel.GitHub;
            }

            return FuseInstallChannel.Unknown;
        }

        /// <summary>
        /// Reads the <c>Source</c> field from the mod folder's <c>Info.json</c>.
        /// </summary>
        internal static FuseInstallChannel ReadChannel(string modPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modPath))
                {
                    return FuseInstallChannel.Unknown;
                }

                var infoPath = Path.Combine(modPath, "Info.json");
                if (!File.Exists(infoPath))
                {
                    return FuseInstallChannel.Unknown;
                }

                var info = JObject.Parse(File.ReadAllText(infoPath));
                var source = info.Properties()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, "Source", StringComparison.OrdinalIgnoreCase));
                var token = source?.Value?.Type == JTokenType.String
                    ? source.Value.Value<string>()
                    : source?.Value?.ToString();
                return FromToken(token);
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not read install source from Info.json: {ex.GetBaseException().Message}");
                return FuseInstallChannel.Unknown;
            }
        }

        /// <summary>
        /// Deep link to a specific GitHub release tag (e.g. <c>mod-v1.0.2</c>),
        /// falling back to the releases list when no tag is known.
        /// </summary>
        internal static string GitHubReleaseTagUrl(string tag) =>
            string.IsNullOrWhiteSpace(tag)
                ? GitHubReleasesUrl
                : GitHubReleasesUrl + "/tag/" + Uri.EscapeDataString(tag.Trim());

        /// <summary>
        /// The place to send an out-of-date player: the Nexus page for
        /// Nexus-stamped installs, otherwise the canonical GitHub releases.
        /// </summary>
        internal static string PrimaryUpdateUrl(FuseInstallChannel channel) =>
            channel == FuseInstallChannel.Nexus ? NexusModUrl : GitHubReleasesUrl;

        internal static string DescribeChannel(FuseInstallChannel channel)
        {
            switch (channel)
            {
                case FuseInstallChannel.Nexus:
                    return "Nexus Mods";
                default:
                    return "GitHub";
            }
        }
    }
}
