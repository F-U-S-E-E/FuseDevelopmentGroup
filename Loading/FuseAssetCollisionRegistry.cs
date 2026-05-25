using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Registry;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Detects and tracks asset-pack-bundle collisions caused by two or more
    /// pack folders inside one mod folder publishing the same Catalog
    /// identifier. The owning <see cref="FuseAssetPackRegistry"/> calls
    /// <see cref="ScanForCollisions"/> after it has enumerated every pack
    /// folder for a session, the registry returns the populated map, and
    /// the asset-bundle path patch reads
    /// <see cref="TryGetBundleRedirect(string,out string)"/> to redirect
    /// every loser store's <c>AssetBundlePath</c> to the winner's bundle
    /// file. See <see cref="FuseAssetCollision"/> for the semantic model.
    ///
    /// <para>Winner selection is deterministic: the pack folder whose path
    /// lives inside a <c>SCAssetPacks</c> subfolder wins over a pack folder
    /// at the mod root. Rationale: the legacy <c>SCAssetPacks</c> convention
    /// is what mod authors used for the most recent / canonical builds of
    /// their content in the era this mod was authored; root-level packs of
    /// the same identifier are usually stale leftovers. If neither or both
    /// participants are convention-folder packs, the participant with the
    /// larger bundle file wins (the larger build is typically the more
    /// detailed / more recent one). All ties resolve by ordinal-stable
    /// folder-path sort so re-runs produce the same winner.</para>
    /// </summary>
    internal static class FuseAssetCollisionRegistry
    {
        // Sentinel used in the redirect map so we can distinguish "no
        // collision recorded for this folder" from "collision recorded but
        // the folder IS the winner, no redirect needed".
        private static readonly string NoRedirectSentinel = string.Empty;

        private static readonly object Sync = new object();
        // Per pack-folder absolute path → winner bundle path (or the
        // <see cref="NoRedirectSentinel"/> if this folder IS the winner).
        // Lookup uses the SAME absolute path the store's Identifier
        // decodes to, so the AssetBundlePath patch only needs the store's
        // identifier (URL-decoded) to find a redirect.
        private static readonly Dictionary<string, string> RedirectsByFolderPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FuseAssetCollision> Collisions = new List<FuseAssetCollision>();
        // Tracks every (claimKindId, claimOwner) tuple we registered with
        // <see cref="FuseRegistry"/>. <see cref="Reset"/> releases each
        // tuple so the registry's shared-owner table goes back to its
        // pre-scan state — tests assert on registry state after running
        // their own scenarios, and a static registry that holds stale
        // entries from a previous scan would break their assertions.
        private static readonly List<(string id, string owner)> RecordedClaims =
            new List<(string id, string owner)>();

        /// <summary>
        /// All collisions discovered during the last scan, ordered by the
        /// shared catalog identifier. Safe to enumerate from anywhere; the
        /// returned list is a snapshot, not the live store.
        /// </summary>
        public static IReadOnlyList<FuseAssetCollision> CurrentCollisions
        {
            get
            {
                lock (Sync)
                {
                    return Collisions.ToArray();
                }
            }
        }

        /// <summary>
        /// Returns the winner's bundle path for <paramref name="bundleFolderPath"/>
        /// when that folder is a recorded loser in any collision; returns
        /// <c>false</c> when there is no redirect (no collision, or the
        /// folder is itself the winner).
        /// </summary>
        public static bool TryGetBundleRedirect(string bundleFolderPath, out string redirectedBundlePath)
        {
            redirectedBundlePath = null;
            var normalized = NormalizeFolderPath(bundleFolderPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            lock (Sync)
            {
                if (!RedirectsByFolderPath.TryGetValue(normalized, out var winnerBundle))
                {
                    return false;
                }

                if (ReferenceEquals(winnerBundle, NoRedirectSentinel))
                {
                    // Folder participates in a collision but is the winner;
                    // its own bundle path is the right answer, no redirect.
                    return false;
                }

                redirectedBundlePath = winnerBundle;
                return true;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="bundleFolderPath"/> is a
        /// recorded LOSER in any collision — i.e. it shares a leaf
        /// folder name with another pack within the same mod and was
        /// not chosen as the winner. Used by the prefab-store patches
        /// to hide a loser pack's car/load/etc. definitions from the
        /// interchange spawn pool and the
        /// <c>AssetPackContainingIdentifier</c> lookup, so the loser
        /// pack's legacy car definitions never get spawned and the
        /// loser pack's bundle never has to load (which would conflict
        /// with the winner's bundle CAB).
        /// </summary>
        public static bool IsLoserFolder(string bundleFolderPath)
        {
            var normalized = NormalizeFolderPath(bundleFolderPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            lock (Sync)
            {
                if (!RedirectsByFolderPath.TryGetValue(normalized, out var winnerBundle))
                {
                    return false;
                }
                return !ReferenceEquals(winnerBundle, NoRedirectSentinel);
            }
        }

        /// <summary>
        /// Returns the winner folder's absolute path for any pack folder
        /// recorded as a loser, or <c>false</c> if the folder is not a
        /// participant or is itself the winner. Used by the LoadedBundle
        /// patch to find the winner store at runtime and reuse its bundle
        /// task instead of issuing a second
        /// <see cref="UnityEngine.AssetBundle.LoadFromFileAsync(string)"/>
        /// call (which Unity rejects when the internal manifest is
        /// already loaded, regardless of file path).
        /// </summary>
        public static bool TryGetWinnerFolder(string loserFolderPath, out string winnerFolderPath)
        {
            winnerFolderPath = null;
            var normalized = NormalizeFolderPath(loserFolderPath);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            lock (Sync)
            {
                if (!RedirectsByFolderPath.TryGetValue(normalized, out var winnerBundle) ||
                    ReferenceEquals(winnerBundle, NoRedirectSentinel))
                {
                    return false;
                }

                // The bundle path is "<winnerFolder>/Bundle"; strip the
                // last segment to recover the folder.
                winnerFolderPath = Path.GetDirectoryName(winnerBundle);
                return !string.IsNullOrWhiteSpace(winnerFolderPath);
            }
        }

        /// <summary>
        /// Normalizes a folder path for use as a dictionary key:
        /// resolves to an absolute path, replaces forward slashes with
        /// the OS-native separator, and strips trailing separators. We
        /// have to do this because callers can pass paths in a variety
        /// of formats (decoded URLs may keep forward slashes; reflective
        /// reads of BasePath have a trailing-slash quirk on some Unity
        /// builds; tests pass simulated paths with arbitrary separators)
        /// and the redirect table must hit consistently.
        /// </summary>
        private static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var absolute = Path.GetFullPath(path);
                return absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resets the collision table and releases every
        /// <see cref="FuseClaimKind.AssetCollision"/> shared-owner claim
        /// previously recorded. Idempotent. Used during pack-mount
        /// reset so re-scans don't accrete stale entries.
        /// </summary>
        public static void Reset()
        {
            List<(string id, string owner)> claimsToRelease = null;
            lock (Sync)
            {
                if (Collisions.Count == 0 && RedirectsByFolderPath.Count == 0 && RecordedClaims.Count == 0)
                {
                    return;
                }
                Collisions.Clear();
                RedirectsByFolderPath.Clear();
                if (RecordedClaims.Count > 0)
                {
                    claimsToRelease = new List<(string id, string owner)>(RecordedClaims);
                    RecordedClaims.Clear();
                }
            }

            // Release outside the lock — FuseRegistry has its own lock and
            // we don't want to interleave the two while holding ours.
            if (claimsToRelease != null)
            {
                foreach (var claim in claimsToRelease)
                {
                    try
                    {
                        FuseRegistry.Release(FuseClaimKind.AssetCollision, claim.id, claim.owner);
                    }
                    catch
                    {
                        // Best-effort release; a stuck registry entry is
                        // better than a thrown reset.
                    }
                }
            }
        }

        /// <summary>
        /// Scan the supplied pack folders for asset-bundle collisions and
        /// populate the redirect table. Returns the list of newly
        /// detected collisions (also discoverable via
        /// <see cref="CurrentCollisions"/>). Must be called with the full
        /// folder set FUSE intends to register, BEFORE any
        /// <c>AddStore</c> call is made for those folders, so the bundle
        /// path patch can consult the table on the very first store
        /// query.
        ///
        /// <para>Collisions are flagged when two pack folders share
        /// BOTH the SAME host mod package AND the SAME leaf folder name
        /// (case-insensitive). The leaf folder name is the strongest
        /// reliable signal we have at discovery time that two bundles
        /// were built from the same source: a mod author who duplicates
        /// a pack — root-level copy plus an <c>SCAssetPacks</c> copy —
        /// uses the same pack folder name in both locations, and Unity's
        /// duplicate-AssetBundle check is keyed on the bundle's internal
        /// manifest name, which Unity build tooling typically derives
        /// from that pack folder name. Distinct packs that happen to
        /// share a Catalog.json <c>identifier</c> or <c>name</c> (e.g.,
        /// a mod with multiple steam-loco variants where the author
        /// typo'd the same identifier into every variant's Catalog) do
        /// NOT share a folder name and are therefore left alone — those
        /// were the false positives in earlier catalog-identifier-based
        /// detection. Cross-mod overlap is similarly excluded by the
        /// host-mod scope.</para>
        /// </summary>
        public static IReadOnlyList<FuseAssetCollision> ScanForCollisions(
            IReadOnlyCollection<string> packFolders,
            Func<string, string> readPackageIdForPackFolder,
            Func<string, string> readHostPackageFolderForPackFolder)
        {
            if (packFolders == null || packFolders.Count == 0)
            {
                return Array.Empty<FuseAssetCollision>();
            }

            // Group every (folder → leaf folder name) by (host package
            // root, leaf folder name). Two packs are only considered for
            // collision when both keys match — see the comment block on
            // this method for the rationale. Skip any pack folder whose
            // host package folder cannot be located.
            var groupsByPackageAndLeaf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in packFolders)
            {
                var normalized = NormalizeFolderPath(folder);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                var hostPackageFolder = readHostPackageFolderForPackFolder?.Invoke(normalized);
                var hostPackageNormalized = NormalizeFolderPath(hostPackageFolder);
                if (string.IsNullOrEmpty(hostPackageNormalized))
                {
                    // Without a host package context we cannot tell a
                    // within-mod collision apart from cross-mod overlap,
                    // so skip — better than risking a wrong redirect.
                    continue;
                }

                var leafFolderName = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(leafFolderName))
                {
                    continue;
                }

                var groupKey = hostPackageNormalized + "\0" + leafFolderName;
                if (!groupsByPackageAndLeaf.TryGetValue(groupKey, out var members))
                {
                    members = new List<string>();
                    groupsByPackageAndLeaf[groupKey] = members;
                }

                if (!members.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    members.Add(normalized);
                }
            }

            var detected = new List<FuseAssetCollision>();
            lock (Sync)
            {
                foreach (var pair in groupsByPackageAndLeaf)
                {
                    var compositeKey = pair.Key;
                    var members = pair.Value;
                    if (members.Count < 2)
                    {
                        continue;
                    }

                    // Recover the leaf name — everything after the NUL
                    // separator in the composite group key. We surface it
                    // through the collision record as the "shared
                    // identifier" so downstream consumers (console
                    // commands, claim records) have a human-meaningful
                    // label that matches what the mod author named the
                    // duplicated folder. The Catalog.json identifier is
                    // intentionally NOT used here because it is unreliable
                    // (mod authors typo-share it across distinct packs).
                    var nulIndex = compositeKey.IndexOf('\0');
                    var sharedIdentifier = nulIndex >= 0
                        ? compositeKey.Substring(nulIndex + 1)
                        : compositeKey;

                    var winner = ChooseWinner(members);
                    var winnerBundle = Path.Combine(winner, "Bundle");
                    var losers = members
                        .Where(folder => !string.Equals(folder, winner, StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var collision = new FuseAssetCollision(sharedIdentifier, winner, winnerBundle, losers);
                    Collisions.Add(collision);

                    // Record the winner with the sentinel so the patch can
                    // tell "I am a participant" from "no collision touches
                    // me," but still won't actually redirect the winner's
                    // own bundle load away from itself.
                    RedirectsByFolderPath[winner] = NoRedirectSentinel;
                    foreach (var loser in losers)
                    {
                        RedirectsByFolderPath[loser] = winnerBundle;
                    }

                    // Record one shared claim per pack folder, mirroring
                    // how suppression records every owner. The package id
                    // is the host mod's package id (the same id used to
                    // register the pack with FUSE), falling back to the
                    // pack folder name if discovery cannot determine it.
                    foreach (var member in members)
                    {
                        var ownerLabel = TryGetOwnerLabel(member, readPackageIdForPackFolder)
                                         ?? Path.GetFileName(member);
                        if (string.IsNullOrWhiteSpace(ownerLabel))
                        {
                            continue;
                        }

                        try
                        {
                            FuseRegistry.TryClaim(FuseClaimKind.AssetCollision, sharedIdentifier, ownerLabel);
                            RecordedClaims.Add((sharedIdentifier, ownerLabel));
                        }
                        catch (Exception ex)
                        {
                            FuseLog.Warning(
                                $"FUSE asset-collision claim record failed softly for " +
                                $"'{sharedIdentifier}' owner '{ownerLabel}': {ex.Message}");
                        }
                    }

                    detected.Add(collision);
                    FuseLog.Warning(
                        $"FUSE asset pack collision detected: catalog identifier '{sharedIdentifier}' is " +
                        $"published by {members.Count} pack folder(s); winner='{ShortenForLog(winner)}', " +
                        $"loser(s)={string.Join(",", losers.Select(ShortenForLog))}. Loser bundles will " +
                        $"redirect to the winner's bundle to avoid Unity's duplicate-AssetBundle restriction.");
                }
            }

            return detected;
        }

        private static string ChooseWinner(IReadOnlyList<string> members)
        {
            // Prefer the participant at the mod ROOT (i.e. NOT under a
            // legacy <c>SCAssetPacks/</c> subfolder). Empirically, mod
            // authors who ship duplicate pack folders for the same
            // leaf name keep the MODERN, current-build definitions at
            // the root and leave the SCAssetPacks/&lt;X&gt; copy as a
            // legacy fallback — e.g. TOFC Cars ships modern
            // multi-car definitions like <c>spinecar-01</c>..06 at
            // <c>TOFC Cars/spinecar1/</c> and the single-car
            // <c>spinecar1</c> definition at
            // <c>TOFC Cars/SCAssetPacks/spinecar1/</c>. Marking the
            // root pack as the winner means the modern definitions
            // flow into PrefabStore unmodified and the legacy ones
            // get filtered out so the random interchange picker
            // can't roll a car type whose bundle would collide with
            // the modern bundle's CAB.
            //
            // <para>If, by some inversion, only an SCAssetPacks copy
            // exists (no root sibling — which the collision scanner
            // wouldn't have flagged in the first place), we still
            // want a deterministic pick: smallest bundle path
            // breaks the tie because a fresh save with cars
            // referencing that pack would only know about the
            // SCAssetPacks variant. Bundle file size is no longer in
            // the tie-break because empirically the LARGER bundle is
            // usually the legacy one (it ships every prefab variant
            // up to that point), not the modern one.</para>
            var rootMembers = members
                .Where(m => !IsConventionFolderPack(m))
                .ToArray();
            var candidates = rootMembers.Length > 0 ? rootMembers : members.ToArray();
            return candidates
                .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static bool IsConventionFolderPack(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }
            // Match either Mod/SCAssetPacks/<pack> or any deeper nesting
            // beneath an SCAssetPacks segment. The match is path-segment
            // aware so a sibling folder literally named e.g.
            // "SCAssetPacks-old" does not accidentally count.
            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var segments = folder.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < segments.Length - 1; index++)
            {
                if (string.Equals(segments[index], "SCAssetPacks", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static long GetBundleFileSize(string folder)
        {
            try
            {
                var bundlePath = Path.Combine(folder, "Bundle");
                if (!File.Exists(bundlePath))
                {
                    return 0L;
                }
                return new FileInfo(bundlePath).Length;
            }
            catch
            {
                return 0L;
            }
        }

        private static string TryReadCatalogIdentifier(string packFolder)
        {
            try
            {
                var catalogPath = Path.Combine(packFolder, "Catalog.json");
                if (!File.Exists(catalogPath))
                {
                    return null;
                }
                // Best-effort, tolerant read: missing fields just return null.
                // Catalog.json is small, parsing inline is cheap.
                var text = File.ReadAllText(catalogPath);
                var root = JObject.Parse(text);
                // Authors have historically misspelled the field; accept both.
                var token = root["identifier"]
                            ?? root["indentifier"]
                            ?? root["name"];
                return token == null ? null : ((string)token)?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetOwnerLabel(string packFolder, Func<string, string> readPackageIdForPackFolder)
        {
            if (string.IsNullOrWhiteSpace(packFolder))
            {
                return null;
            }
            try
            {
                return readPackageIdForPackFolder?.Invoke(packFolder);
            }
            catch
            {
                return null;
            }
        }

        private static string ShortenForLog(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return "<unknown>";
            }
            // Strip the mods-folder ancestors so log lines stay readable.
            // We do not have the mods folder root here directly, so just
            // keep the last two segments — that is enough to identify a
            // pack folder uniquely within a single mod.
            try
            {
                var parts = fullPath.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 2)
                {
                    return fullPath;
                }
                return ".../" + string.Join("/", parts.Skip(parts.Length - 3));
            }
            catch
            {
                return fullPath;
            }
        }
    }
}
