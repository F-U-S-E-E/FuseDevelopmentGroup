using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class ProgressionAPI
    {

        private static void RefreshMapFeatureManager(MapFeatureManager manager)
        {
            var features = manager.GetComponentsInChildren<MapFeature>();
            foreach (var feature in features)
            {
                SanitizeMapFeature(feature);
            }

            ManagerFeaturesField?.SetValue(manager, features);
        }

        // Originally this method pre-filled the MapFeatureManager's "features" KVO
        // entry with one bool per feature, using `defaultEnableInSandbox && IsSandbox`.
        // That turns out to be a hard data-corruption bug: FUSE's post-apply refresh
        // runs BEFORE the save's GameMode has been deserialized, so IsSandbox returns
        // its default value of true even when the save is a Company-mode game. Every
        // feature with `defaultEnableInSandbox=true` was being written into KVO as
        // unlocked, and because MapFeatureManager.SetFeatureEnables is a MERGE (not a
        // replace), those incorrect-true entries survived the later save-driven dict
        // write whenever the save dict didn't explicitly include that feature. The
        // visible symptom was Alarka Branch / Ela bridge etc. appearing in a save
        // where the player hadn't unlocked them.
        //
        // The game itself does NOT require any pre-fill. Look at
        // MapFeatureManager.HandleFeatureEnablesChanged: when iterating _features it
        // falls back to `defaultEnableInSandbox && IsSandbox` at read time for any
        // identifier missing from the dict. That computation uses the THEN-current
        // IsSandbox, which is reliable by the time HandleSnapshotProperties runs.
        //
        // So this method now writes nothing. We keep the diagnostic logging because
        // it remains useful for spotting future regressions, but the actual KVO
        // mutation has been removed.
        private static int InitializeMissingMapFeatureStates(MapFeatureManager manager)
        {
            if (manager == null)
            {
                return 0;
            }

            var features = manager.AvailableFeatures
                .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .ToArray();
            if (features.Length == 0)
            {
                return 0;
            }

            var existing = ReadFeatureEnables(manager);
            var defaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features)
            {
                if (existing.ContainsKey(feature.identifier))
                {
                    continue;
                }

                defaults[feature.identifier] = feature.defaultEnableInSandbox && StateManager.IsSandbox;
            }

            if (defaults.Count == 0)
            {
                return 0;
            }

            // Diagnostic-only: log what the old pre-fill WOULD have written, so we
            // can still spot the GameMode race in logs without persisting bad state.
            var sandbox = StateManager.IsSandbox;
            var gameMode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
            var wouldBeUnlocked = defaults.Count(kvp => kvp.Value);
            FuseLog.Info(
                $"FUSE progression pre-fill skipped (write disabled; game handles defaults at read time) " +
                $"count={defaults.Count} existingEntries={existing?.Count ?? 0} " +
                $"totalFeatures={features.Length} wouldHaveUnlocked={wouldBeUnlocked} " +
                $"wouldHaveLocked={defaults.Count - wouldBeUnlocked} " +
                $"StateManager.IsSandbox={sandbox} StateManager.GameMode={gameMode}.");

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info("FUSE progression pre-fill (skipped) entries that would have been written:");
                foreach (var kvp in defaults.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var feature = features.FirstOrDefault(f =>
                        string.Equals(f.identifier, kvp.Key, StringComparison.OrdinalIgnoreCase));
                    var defaultSandbox = feature != null ? feature.defaultEnableInSandbox.ToString() : "<unknown>";
                    FuseLog.Info(
                        $"  pre-fill (skipped) id='{kvp.Key}' wouldHaveWritten={kvp.Value} " +
                        $"feature.defaultEnableInSandbox={defaultSandbox} IsSandbox={sandbox}.");
                }
            }

            // Intentionally do NOT call manager.SetFeatureEnables(defaults). The game
            // computes missing-key defaults at read time using the current IsSandbox,
            // which is reliable by the time graph/industry consumers read it.
            return 0;
        }

        private static bool ForceApplyCurrentMapFeatureState(MapFeatureManager manager, string reason)
        {
            if (manager == null || ManagerHandleFeatureEnablesChangedMethod == null)
            {
                return false;
            }

            try
            {
                var current = ReadFeatureEnables(manager);
                ManagerHandleFeatureEnablesChangedMethod.Invoke(
                    manager,
                    new object[]
                    {
                        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                        current,
                        true
                    });
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='force apply map feature state' " +
                    $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
                return false;
            }
        }

        private static int RestoreDisabledTrackGroups(MapFeatureManager manager, string reason)
        {
            var graph = Graph.Shared;
            if (manager == null || graph == null)
            {
                return 0;
            }

            var states = ReadFeatureEnables(manager);
            var enabledGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var disabledGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Diagnostic: which features each group's enable-state attribute belongs to.
            var enabledGroupOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var disabledGroupOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // IMPORTANT: only consider trackGroupsEnableOnUnlock here. The game's
            // own MapFeatureManager.UpdateFeatureGraphGroups calls SetGroupEnabled
            // only for those, and uses SetGroupAvailable (a separate attribute)
            // for trackGroupsAvailableOnUnlock. If we conflate them and call
            // SetGroupEnabled(false) on availability-only groups, we permanently
            // remove track from the graph's enabled set — manifested as deleted
            // base-map track once the locked feature's tracksAvail entries got
            // disabled (Alarka regression: el-br locked → s2 + walker yanked from
            // enabledGroupIds even though they're base map track that should only
            // be marked unavailable).
            foreach (var feature in manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
                {
                    continue;
                }

                var enableGroups = (feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>())
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .ToArray();
                if (enableGroups.Length == 0)
                {
                    continue;
                }

                var enabled = IsFeatureEnabled(feature, states);
                var target = enabled ? enabledGroups : disabledGroups;
                var owners = enabled ? enabledGroupOwners : disabledGroupOwners;
                foreach (var group in enableGroups)
                {
                    target.Add(group);
                    if (!owners.TryGetValue(group, out var list))
                    {
                        list = new List<string>();
                        owners[group] = list;
                    }
                    list.Add(feature.identifier);
                }
            }

            // Conflict diagnostic: a track group with both enabled and disabled feature
            // owners gets stripped from disabledGroups by ExceptWith below, so the group
            // stays visible even though one or more of its owning features is locked.
            // Always log this — it's strong evidence of a misconfigured or unintended
            // feature unlock and was the breadcrumb we needed during the Alarka Branch
            // investigation.
            foreach (var conflict in disabledGroups.Intersect(enabledGroups))
            {
                var enabledBy = enabledGroupOwners.TryGetValue(conflict, out var en)
                    ? string.Join(",", en)
                    : "<none>";
                var disabledBy = disabledGroupOwners.TryGetValue(conflict, out var di)
                    ? string.Join(",", di)
                    : "<none>";
                FuseLog.Warning(
                    $"FUSE progression refresh track-group conflict group='{conflict}' " +
                    $"enabledBy=[{enabledBy}] disabledBy=[{disabledBy}] reason='{reason ?? "unspecified"}' " +
                    $"effect='group stays enabled because at least one owner is unlocked'.");
            }

            disabledGroups.ExceptWith(enabledGroups);

            var changed = 0;
            foreach (var group in disabledGroups)
            {
                try
                {
                    if (graph.SetGroupEnabled(group, false))
                    {
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE progression refresh package='<all>' operation='restore locked track groups' " +
                        $"kind='track group' id='{group}' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
                }
            }

            return changed;
        }

        /// <summary>
        /// Finalises track groups that <see cref="FUSE.Loading.FuseModLoader.PreEnableInitialTrackGroups"/>
        /// flipped into <c>Graph.enabledGroupIds</c> + <c>availableGroupIds</c>
        /// purely so segment binding would not cull mod-added segments. If
        /// no <see cref="MapFeature"/> claims the group via
        /// <c>trackGroupsEnableOnUnlock</c> or
        /// <c>trackGroupsAvailableOnUnlock</c>, it is an orphan — we keep
        /// it ENABLED (rails stay visible) but flip it to AVAILABLE=false
        /// (the player's network does not include it; routing/picking
        /// treat it as off-limits).
        ///
        /// The two flags map onto distinct game concerns:
        /// <list type="bullet">
        ///   <item><c>enabledGroupIds</c> — "Enabled groups are visible
        ///   track" (per the tooltip on the field in <c>Track.Graph</c>).
        ///   Drives mesh inclusion via <c>Graph.AddSegment</c>: a segment
        ///   whose <c>groupId</c> is not in this set early-returns and
        ///   never enters the runtime <c>segments</c> dictionary.</item>
        ///   <item><c>availableGroupIds</c> — "Available groups may be
        ///   picked." Drives whether the segment is part of the player's
        ///   reachable / interactive network.</item>
        /// </list>
        ///
        /// Earlier iterations of this method got this wrong twice over:
        /// once by calling <c>SetGroupEnabled(false)</c> (which deleted the
        /// rails from the mesh entirely on the next
        /// <c>RebuildCollections</c>), once by leaving both flags at
        /// <c>true</c> (which made the orphan track fully owned by the
        /// player — visible AND interactive). Holding enabled=true and
        /// available=false is the combination CollieDillsboroOverhaul-style
        /// graph-only mods need: the rails appear at the
        /// interchange-extension siding, but the player's routing /
        /// purchase / industry layer never treats them as owned.
        /// </summary>
        private static int RevokeTransientlyPreEnabledOrphanGroups(MapFeatureManager manager, string reason)
        {
            var graph = Graph.Shared;
            if (manager == null || graph == null)
            {
                return 0;
            }

            string[] candidates;
            lock (_transientlyPreEnabledLock)
            {
                if (_transientlyPreEnabledTrackGroups.Count == 0)
                {
                    return 0;
                }
                candidates = new string[_transientlyPreEnabledTrackGroups.Count];
                _transientlyPreEnabledTrackGroups.CopyTo(candidates);
                _transientlyPreEnabledTrackGroups.Clear();
            }

            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
            {
                if (feature == null)
                {
                    continue;
                }
                foreach (var group in feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        claimed.Add(group);
                    }
                }
                foreach (var group in feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        claimed.Add(group);
                    }
                }
            }

            var unavailabled = 0;
            foreach (var groupId in candidates)
            {
                if (claimed.Contains(groupId))
                {
                    // A feature owns this group; the game's HandleFeatureEnablesChanged
                    // is in charge of toggling its enabled and available state per progression.
                    continue;
                }

                try
                {
                    // Keep enabled=true so segments stay in the mesh
                    // (matches Railloader, which never disables an
                    // unowned group). Flip available=false so the
                    // player's network does not treat this orphan track
                    // as owned/reachable. This is the right state for
                    // genuine decorative-orphan track shipped by
                    // graph-only mods (e.g. CollieDillsboroOverhaul's
                    // e-c1 interchange-extension siding, intentionally
                    // disconnected from the active network and with no
                    // progression hook).
                    //
                    // Progression-gated track that LOOKS orphan because
                    // a mod's MapFeature patch stripped the original
                    // owner list (e.g. the JR MaconCounty mod patching
                    // <c>alarka</c> with <c>{ "alext-off": true }</c>
                    // for tracksEnableOnUnlock, which the legacy
                    // converter used to flatten into a REPLACE set,
                    // wiping out the base game's <c>[s3a]</c>) is now
                    // handled upstream: <see cref="NormalizeProgressionValue"/>
                    // preserves the object form so
                    // <see cref="FUSE.Authoring.Data.FuseStringPatch"/>'s merge
                    // semantics fire, and the base game's gating
                    // entries survive. By the time we get here, s3a
                    // is properly claimed by the patched alarka and
                    // this method never sees it.
                    var availableChanged = graph.SetGroupAvailable(groupId, false);
                    if (availableChanged)
                    {
                        unavailabled++;
                    }
                    FuseLog.Info(
                        $"FUSE finalised orphan track group '{groupId}' as visible-but-unavailable " +
                        $"reason='{reason ?? "unspecified"}' " +
                        $"availableFlagFlipped={availableChanged} " +
                        "(no MapFeature claims this group via tracksEnable/tracksAvail; " +
                        "enabled=true keeps rails in the mesh, available=false keeps the " +
                        "segments out of the player's owned/routable network).");
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE progression refresh package='<all>' operation='finalise orphan track group' " +
                        $"kind='track group' id='{groupId}' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
                }
            }

            return unavailabled;
        }

        private static int InferMapFeatureIndustryIncludes(MapFeatureManager manager, string reason)
        {
            if (manager == null)
            {
                return 0;
            }

            var features = (manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
                .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .ToArray();
            if (features.Length == 0)
            {
                return 0;
            }

            var trackGroupOwners = BuildTrackGroupOwnerMap(features);
            var changed = 0;
            foreach (var industry in UnityEngine.Object.FindObjectsOfType<Industry>(true))
            {
                if (industry == null || string.IsNullOrWhiteSpace(industry.identifier))
                {
                    continue;
                }

                var trackGroups = CollectIndustryTrackGroups(industry);
                var feature = InferFeatureFromTrackGroups(trackGroupOwners, trackGroups, industry, reason);
                var basis = "track-groups";
                if (feature == null)
                {
                    feature = InferFeatureFromScenery(features, industry, reason);
                    basis = "scenery";
                }

                if (feature == null || !CanAddInferredIndustryInclude(feature, industry))
                {
                    continue;
                }

                if (AddInferredIndustryInclude(feature, industry))
                {
                    changed++;
                    FuseLog.Info(
                        $"FUSE inferred progression industry include feature='{feature.identifier}' " +
                        $"industry='{industry.identifier}' basis='{basis}' " +
                        $"trackGroups=[{string.Join(",", trackGroups.OrderBy(group => group).ToArray())}] " +
                        $"reason='{reason ?? "unspecified"}'.");
                }
            }

            return changed;
        }

        private static Dictionary<string, List<MapFeature>> BuildTrackGroupOwnerMap(IEnumerable<MapFeature> features)
        {
            var owners = new Dictionary<string, List<MapFeature>>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features ?? Enumerable.Empty<MapFeature>())
            {
                if (feature == null)
                {
                    continue;
                }

                foreach (var groupId in FeatureTrackGroups(feature))
                {
                    if (!owners.TryGetValue(groupId, out var groupOwners))
                    {
                        groupOwners = new List<MapFeature>();
                        owners[groupId] = groupOwners;
                    }

                    if (!groupOwners.Any(existing => SameFeature(existing, feature)))
                    {
                        groupOwners.Add(feature);
                    }
                }
            }

            return owners;
        }

        private static MapFeature InferFeatureFromTrackGroups(
            Dictionary<string, List<MapFeature>> trackGroupOwners,
            HashSet<string> trackGroups,
            Industry industry,
            string reason)
        {
            if (trackGroupOwners == null || trackGroups == null || trackGroups.Count == 0)
            {
                return null;
            }

            var owners = new List<MapFeature>();
            foreach (var groupId in trackGroups)
            {
                if (!trackGroupOwners.TryGetValue(groupId, out var groupOwners))
                {
                    continue;
                }

                foreach (var owner in groupOwners)
                {
                    if (owner != null && !owners.Any(existing => SameFeature(existing, owner)))
                    {
                        owners.Add(owner);
                    }
                }
            }

            if (owners.Count == 1)
            {
                return owners[0];
            }

            if (owners.Count > 1 && FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE skipped progression industry inference industry='{industry?.identifier ?? "<unknown>"}' " +
                    $"reason='{reason ?? "unspecified"}' message='track spans are claimed by multiple features' " +
                    $"features=[{string.Join(",", owners.Select(feature => feature.identifier).OrderBy(id => id).ToArray())}] " +
                    $"trackGroups=[{string.Join(",", trackGroups.OrderBy(group => group).ToArray())}].");
            }

            return null;
        }

        private static MapFeature InferFeatureFromScenery(IEnumerable<MapFeature> features, Industry industry, string reason)
        {
            if (industry == null || !industry.usesContract)
            {
                return null;
            }

            var aliases = BuildIndustrySearchAliases(industry);
            if (aliases.Count == 0)
            {
                return null;
            }

            var matches = new List<MapFeature>();
            foreach (var feature in features ?? Enumerable.Empty<MapFeature>())
            {
                if (feature == null)
                {
                    continue;
                }

                var sceneryKey = BuildFeatureScenerySearchKey(feature);
                if (string.IsNullOrWhiteSpace(sceneryKey))
                {
                    continue;
                }

                if (aliases.Any(alias => sceneryKey.Contains(alias)))
                {
                    matches.Add(feature);
                }
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1 && FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    $"FUSE skipped progression industry scenery inference industry='{industry.identifier}' " +
                    $"reason='{reason ?? "unspecified"}' message='industry name matched multiple feature scenery paths' " +
                    $"features=[{string.Join(",", matches.Select(feature => feature.identifier).OrderBy(id => id).ToArray())}].");
            }

            return null;
        }

        private static bool CanAddInferredIndustryInclude(MapFeature feature, Industry industry)
        {
            if (feature == null || industry == null)
            {
                return false;
            }

            if ((feature.unlockExcludeIndustries ?? Array.Empty<Industry>()).Any(existing => SameIndustry(existing, industry)))
            {
                return false;
            }

            if ((feature.unlockIncludeIndustries ?? Array.Empty<Industry>()).Any(existing => SameIndustry(existing, industry)))
            {
                return false;
            }

            return !FeatureAreasContainIndustry(feature, industry);
        }

        private static bool AddInferredIndustryInclude(MapFeature feature, Industry industry)
        {
            var existing = feature.unlockIncludeIndustries ?? Array.Empty<Industry>();
            if (existing.Any(candidate => SameIndustry(candidate, industry)))
            {
                return false;
            }

            var merged = existing
                .Where(candidate => candidate != null)
                .Concat(new[] { industry })
                .GroupBy(candidate => string.IsNullOrWhiteSpace(candidate.identifier) ? candidate.GetInstanceID().ToString() : candidate.identifier,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            feature.unlockIncludeIndustries = merged;
            return true;
        }

        private static bool FeatureAreasContainIndustry(MapFeature feature, Industry industry)
        {
            foreach (var area in feature.areasEnableOnUnlock ?? Array.Empty<Area>())
            {
                if (area == null)
                {
                    continue;
                }

                try
                {
                    if ((area.Industries ?? Enumerable.Empty<Industry>()).Any(candidate => SameIndustry(candidate, industry)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Best effort only; a feature's explicit include/exclude lists still apply above.
                }
            }

            return false;
        }

        private static HashSet<string> CollectIndustryTrackGroups(Industry industry)
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (industry == null)
            {
                return groups;
            }

            IndustryComponent[] components;
            try
            {
                components = industry.GetComponentsInChildren<IndustryComponent>(true) ?? Array.Empty<IndustryComponent>();
            }
            catch
            {
                return groups;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                IEnumerable<TrackSpan> spans;
                try
                {
                    spans = component.TrackSpans ?? Enumerable.Empty<TrackSpan>();
                }
                catch
                {
                    continue;
                }

                foreach (var span in spans)
                {
                    AddTrackSpanGroupIds(span, groups);
                }
            }

            return groups;
        }

        private static void AddTrackSpanGroupIds(TrackSpan span, ISet<string> groups)
        {
            if (span == null || groups == null)
            {
                return;
            }

            try
            {
                var upper = span.upper;
                if (upper.HasValue)
                {
                    AddTrackSegmentGroupId(upper.Value.segment, groups);
                }
            }
            catch
            {
            }

            try
            {
                var lower = span.lower;
                if (lower.HasValue)
                {
                    AddTrackSegmentGroupId(lower.Value.segment, groups);
                }
            }
            catch
            {
            }

            try
            {
                foreach (var segment in span.GetSegments() ?? Enumerable.Empty<TrackSegment>())
                {
                    AddTrackSegmentGroupId(segment, groups);
                }
            }
            catch
            {
                // Invalid spans should not block the rest of the inference pass.
            }
        }

        private static void AddTrackSegmentGroupId(TrackSegment segment, ISet<string> groups)
        {
            if (segment == null || string.IsNullOrWhiteSpace(segment.groupId))
            {
                return;
            }

            groups.Add(segment.groupId);
        }

        private static List<string> BuildIndustrySearchAliases(Industry industry)
        {
            var aliases = new List<string>();
            AddSearchAlias(aliases, industry?.identifier);
            AddSearchAlias(aliases, industry?.name);
            return aliases;
        }

        private static void AddSearchAlias(List<string> aliases, string value)
        {
            var compact = CompactSearchKey(value);
            if (compact.Length < 8 || aliases.Contains(compact))
            {
                return;
            }

            aliases.Add(compact);
        }

        private static string BuildFeatureScenerySearchKey(MapFeature feature)
        {
            if (feature == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (var gameObject in feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>())
            {
                if (gameObject == null)
                {
                    continue;
                }

                parts.Add(gameObject.name);
                parts.Add(GetGameObjectPath(gameObject));
            }

            return CompactSearchKey(string.Join(" ", parts.ToArray()));
        }

        private static string CompactSearchKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = new char[value.Length];
            var count = 0;
            foreach (var ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                {
                    continue;
                }

                chars[count++] = char.ToLowerInvariant(ch);
            }

            return count == 0 ? string.Empty : new string(chars, 0, count);
        }

        private static bool SameFeature(MapFeature left, MapFeature right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left == right ||
                   (!string.IsNullOrWhiteSpace(left.identifier) &&
                    string.Equals(left.identifier, right.identifier, StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameIndustry(Industry left, Industry right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left == right ||
                   (!string.IsNullOrWhiteSpace(left.identifier) &&
                    string.Equals(left.identifier, right.identifier, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsFeatureEnabled(MapFeature feature, IDictionary<string, bool> states)
        {
            if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
            {
                return false;
            }

            return states != null && states.TryGetValue(feature.identifier, out var enabled)
                ? enabled
                : feature.defaultEnableInSandbox && StateManager.IsSandbox;
        }

        private static IEnumerable<string> FeatureTrackGroups(MapFeature feature)
        {
            if (feature == null)
            {
                yield break;
            }

            foreach (var group in feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    yield return group;
                }
            }

            foreach (var group in feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    yield return group;
                }
            }
        }

        private static Dictionary<string, bool> ReadFeatureEnables(MapFeatureManager manager)
        {
            if (manager == null || ManagerFeatureEnablesProperty == null)
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return ManagerFeatureEnablesProperty.GetValue(manager, null) as Dictionary<string, bool> ??
                       new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='read map feature state' " +
                    $"kind='map features' id='<all>' message='{ex.Message}'.");
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SanitizeMapFeature(MapFeature feature)
        {
            if (feature == null)
            {
                return;
            }

            feature.prerequisites = feature.prerequisites ?? Array.Empty<MapFeature>();
            feature.trackGroupsEnableOnUnlock = feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>();
            feature.trackGroupsAvailableOnUnlock = feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>();
            feature.gameObjectsEnableOnUnlock = feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>();
            feature.areasEnableOnUnlock = feature.areasEnableOnUnlock ?? Array.Empty<Area>();
            feature.unlockExcludeIndustries = feature.unlockExcludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustries = feature.unlockIncludeIndustries ?? Array.Empty<Industry>();
            feature.unlockIncludeIndustryComponents = feature.unlockIncludeIndustryComponents ?? Array.Empty<IndustryComponent>();
        }

        private static void RefreshProgressionManager()
        {
            var manager = UnityEngine.Object.FindObjectOfType<ProgressionManager>();
            if (manager != null)
            {
                ManagerProgressionsField?.SetValue(manager, UnityEngine.Object.FindObjectsOfType<Progression>());
            }
        }
    }
}
