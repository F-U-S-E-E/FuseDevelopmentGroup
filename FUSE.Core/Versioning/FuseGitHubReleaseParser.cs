using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Fuse.Core.Versioning
{
    // Pure, game-free parsing of a GitHub Releases API response into the minimal
    // projection FuseReleaseSelection compares. Compiled into FUSE.Core (tested in
    // FUSE.Core.Tests against a real captured payload) AND linked into the in-game
    // mod (see the <Compile Include> in FUSE/FUSE.csproj), so the exact parse the
    // mod runs is the one under test. Newtonsoft.Json binds to the game's in-box
    // copy in the mod and to the package copy in FUSE.Core; both expose the same
    // JArray/JObject API used here.
    public static class FuseGitHubReleaseParser
    {
        /// <summary>
        /// Parses the releases from a Releases API body. Returns false — with an
        /// empty list — for a null/blank body or one that is not a JSON array (an
        /// error object, an HTML error page, a truncated response), so the caller
        /// can tell "could not parse" apart from "parsed, but no stable release".
        /// Never throws.
        /// </summary>
        public static bool TryParse(string json, out IReadOnlyList<FuseGitHubRelease> releases)
        {
            releases = Array.Empty<FuseGitHubRelease>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            JArray array;
            try
            {
                array = JArray.Parse(json);
            }
            catch (Exception)
            {
                return false;
            }

            var list = new List<FuseGitHubRelease>();
            foreach (var token in array)
            {
                if (!(token is JObject obj))
                {
                    continue;
                }

                var tag = obj.Value<string>("tag_name");
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                // Absent flags default to false, matching the API's own default
                // and treating a partial entry as an ordinary full release.
                var draft = obj.Value<bool?>("draft") ?? false;
                var prerelease = obj.Value<bool?>("prerelease") ?? false;
                list.Add(new FuseGitHubRelease(tag, draft, prerelease));
            }

            releases = list;
            return true;
        }
    }
}
