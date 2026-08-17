using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fuse.Core.Versioning
{
    // Pure, game-free logic for the outdated-version warning. This single source
    // file is compiled into FUSE.Core (where FUSE.Core.Tests exercises it without
    // a game install) AND linked into the in-game FUSE mod (see the <Compile
    // Include> in FUSE/FUSE.csproj), so the tag-selection and comparison rules
    // that decide whether a player is "outdated" have exactly one implementation.
    //
    // The mod's own version comes from Info.json's "Version", which the release
    // flow always stamps as the version CORE (MAJOR.MINOR.PATCH — UMM cannot
    // parse pre-release suffixes). The canonical latest version comes from the
    // GitHub Releases API for F-U-S-E-E/FuseDevelopmentGroup.

    /// <summary>
    /// A three-part MAJOR.MINOR.PATCH version. This is the only shape a FUSE
    /// Info.json <c>Version</c> ever carries and the only shape a stable
    /// <c>mod-v</c> release tag carries, so a plain numeric triple is enough to
    /// order releases.
    /// </summary>
    public readonly struct FuseSemVer : IComparable<FuseSemVer>, IEquatable<FuseSemVer>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public FuseSemVer(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        // Anchored, culture-invariant MAJOR.MINOR.PATCH with no pre-release or
        // build suffix. Surrounding whitespace is tolerated; anything else
        // (a leading 'v', an -rc suffix, four segments) is rejected so a
        // non-stable or malformed value can never be read as a version.
        private static readonly Regex CoreVersion = new Regex(
            @"^\s*(\d+)\.(\d+)\.(\d+)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool TryParse(string text, out FuseSemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = CoreVersion.Match(text);
            if (!match.Success)
            {
                return false;
            }

            // int.TryParse guards against segments that overflow Int32; a version
            // component that large is malformed rather than "very new".
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            {
                return false;
            }

            version = new FuseSemVer(major, minor, patch);
            return true;
        }

        public int CompareTo(FuseSemVer other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
            {
                return minor;
            }

            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(FuseSemVer other) =>
            Major == other.Major && Minor == other.Minor && Patch == other.Patch;

        public override bool Equals(object obj) => obj is FuseSemVer other && Equals(other);

        public override int GetHashCode()
        {
            // Simple, allocation-free combine. Version components are small
            // non-negative integers, so a shift-and-xor spread is plenty.
            var hash = 17;
            hash = (hash * 31) + Major;
            hash = (hash * 31) + Minor;
            hash = (hash * 31) + Patch;
            return hash;
        }

        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." +
            Minor.ToString(CultureInfo.InvariantCulture) + "." +
            Patch.ToString(CultureInfo.InvariantCulture);

        public static bool operator <(FuseSemVer left, FuseSemVer right) => left.CompareTo(right) < 0;
        public static bool operator >(FuseSemVer left, FuseSemVer right) => left.CompareTo(right) > 0;
        public static bool operator <=(FuseSemVer left, FuseSemVer right) => left.CompareTo(right) <= 0;
        public static bool operator >=(FuseSemVer left, FuseSemVer right) => left.CompareTo(right) >= 0;
        public static bool operator ==(FuseSemVer left, FuseSemVer right) => left.Equals(right);
        public static bool operator !=(FuseSemVer left, FuseSemVer right) => !left.Equals(right);
    }

    /// <summary>
    /// The subset of a GitHub Releases API entry the update check needs: the tag
    /// and the two flags that mark a release as not-for-general-consumption.
    /// </summary>
    public sealed class FuseGitHubRelease
    {
        public FuseGitHubRelease(string tagName, bool draft, bool prerelease)
        {
            TagName = tagName ?? string.Empty;
            Draft = draft;
            Prerelease = prerelease;
        }

        public string TagName { get; }

        public bool Draft { get; }

        public bool Prerelease { get; }
    }

    /// <summary>
    /// Chooses the version a running FUSE mod should be compared against and
    /// decides whether it is outdated.
    /// </summary>
    public static class FuseReleaseSelection
    {
        /// <summary>Tag prefix for the mod release lane.</summary>
        public const string ModTagPrefix = "mod-v";

        // A STABLE mod release: mod-v<major>.<minor>.<patch> and nothing more.
        //
        // The repository publishes three independent release lanes into one
        // Releases feed — mod-v*, externaleditor-v*, and tools-v* — so filtering
        // to this prefix is what stops the external editor or the tools from
        // being mistaken for a newer FUSE mod. Requiring the tag to END right
        // after the patch number also excludes release candidates
        // (mod-v1.0.1-rc2), which ship as full GitHub releases but must not, by
        // product decision, tell a player on a stable build they are outdated.
        private static readonly Regex StableModTag = new Regex(
            @"^mod-v(\d+)\.(\d+)\.(\d+)$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Parses a stable mod release tag (<c>mod-v1.2.3</c>) into its version.
        /// Returns false for other lanes, release candidates, pre-release
        /// suffixes, and anything malformed.
        /// </summary>
        public static bool TryParseStableModTag(string tagName, out FuseSemVer version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            var match = StableModTag.Match(tagName.Trim());
            if (!match.Success)
            {
                return false;
            }

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            {
                return false;
            }

            version = new FuseSemVer(major, minor, patch);
            return true;
        }

        /// <summary>
        /// Selects the newest stable mod release from a Releases API response.
        /// Drafts and GitHub-flagged prereleases are skipped before the tag is
        /// even parsed. Returns false when the list contains no qualifying
        /// stable mod release.
        /// </summary>
        public static bool TrySelectLatestStableMod(
            IEnumerable<FuseGitHubRelease> releases,
            out FuseSemVer latest,
            out string latestTag)
        {
            latest = default;
            latestTag = null;
            if (releases == null)
            {
                return false;
            }

            var found = false;
            foreach (var release in releases)
            {
                if (release == null || release.Draft || release.Prerelease)
                {
                    continue;
                }

                if (!TryParseStableModTag(release.TagName, out var candidate))
                {
                    continue;
                }

                if (!found || candidate > latest)
                {
                    latest = candidate;
                    latestTag = release.TagName.Trim();
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// True when <paramref name="current"/> is strictly older than
        /// <paramref name="latest"/>. A current version equal to or ahead of the
        /// latest published release (e.g. a tester running an RC's core) is not
        /// outdated.
        /// </summary>
        public static bool IsOutdated(FuseSemVer current, FuseSemVer latest) => current < latest;
    }
}
