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
using FUSE.Cache;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.API
{
    public static class ProgressionAPI
    {
        private static readonly FieldInfo ManagerFeaturesField = typeof(MapFeatureManager).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic);
        // TrackSpan's cached segments + recompute method are private; we read them
        // by reflection for the diagnostic MapEnhancer-simulation dump so we can
        // see exactly which TrackSegments Map Enhancer would classify as industrial
        // when it walks IndustryComponent.TrackSpans in its IndustryTrackClassPatch.
        private static readonly FieldInfo TrackSpanCachedSegmentsField = typeof(TrackSpan).GetField("_cachedSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo TrackSpanUpdateCachedPointsMethod = typeof(TrackSpan).GetMethod("UpdateCachedPointsIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerProgressionsField = typeof(ProgressionManager).GetField("_progressions", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ManagerCurrentProgressionField = typeof(ProgressionManager).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ManagerFeatureEnablesProperty = typeof(MapFeatureManager).GetProperty("FeatureEnables", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ManagerHandleFeatureEnablesChangedMethod = typeof(MapFeatureManager).GetMethod("HandleFeatureEnablesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProgressionSectionsField = typeof(Progression).GetField("<Sections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ProgressionUpdateSectionStatesMethod = typeof(Progression).GetMethod("UpdateSectionStates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SectionInterchangeTransfersField = typeof(Section).GetField("<InterchangeTransfers>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferFromField = typeof(InterchangeTransfer).GetField("from", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo InterchangeTransferToField = typeof(InterchangeTransfer).GetField("to", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string FuseInterchangeTransferPrefix = "FUSE Interchange Transfer ";

        public static MapFeature AddMapFeature(string id, FuseMapFeature definition)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetMapFeature(id) != null)
            {
                throw new InvalidOperationException($"Map feature '{id}' already exists.");
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                throw new InvalidOperationException("MapFeatureManager was not found.");
            }

            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(manager.transform, false);
            var feature = gameObject.AddComponent<MapFeature>();
            feature.identifier = id;
            ApplyMapFeatureDefinition(feature, definition);
            FuseMapFeatureRuntimeIndex.Instance.Set(id, feature);
            RefreshMapFeatureManager(manager);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, id, definition);
            return feature;
        }

        public static void UpdateMapFeature(string id, FuseMapFeature definition)
        {
            var feature = RequireMapFeature(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyMapFeatureDefinition(feature, definition);
            FuseMapFeatureRuntimeIndex.Instance.Set(id, feature);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, id, definition);
        }

        public static void RemoveMapFeature(string id)
        {
            var feature = RequireMapFeature(id);
            feature.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(feature.gameObject);
            FuseMapFeatureRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.MapFeature, id);
            if (MapFeatureManager.Shared != null)
            {
                RefreshMapFeatureManager(MapFeatureManager.Shared);
            }
        }

        public static MapFeature GetMapFeature(string id)
        {
            if (FuseMapFeatureRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (MapFeature)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<MapFeature>(true).FirstOrDefault(feature =>
                    string.Equals(feature.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<MapFeature> GetAllMapFeatures()
        {
            return UnityEngine.Object.FindObjectsOfType<MapFeature>(true);
        }

        internal static bool TryGetMapFeatureEnabledState(MapFeature feature, out bool enabled)
        {
            enabled = false;
            if (feature == null || string.IsNullOrWhiteSpace(feature.identifier))
            {
                return false;
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                return false;
            }

            enabled = IsFeatureEnabled(feature, ReadFeatureEnables(manager));
            return true;
        }

        public static FuseMapFeature GetMapFeatureDefinition(string id)
        {
            return GetDefinition(GetMapFeature(id));
        }

        public static FuseMapFeature GetDefinition(MapFeature feature)
        {
            if (feature == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.MapFeature, feature.identifier, out FuseMapFeature definition);
            definition = definition ?? new FuseMapFeature();
            definition.DisplayName = feature.displayName;
            definition.Description = feature.description;
            definition.InitiallyEnabled = feature.defaultEnableInSandbox;
            // When we serialize a live feature back out as a FuseMapFeature
            // we emit the replacement-set shape (FromSet), because the snapshot
            // is meant to round-trip the EXACT current state of the runtime
            // feature, not a per-id patch on top of something else.
            definition.GroupIds = FuseStringPatch.FromSet(feature.trackGroupsEnableOnUnlock ?? feature.trackGroupsAvailableOnUnlock);
            definition.PrerequisiteFeatureIds = FuseStringPatch.FromSet(ToFeatureIds(feature.prerequisites));
            definition.TrackGroupsEnableOnUnlock = FuseStringPatch.FromSet(feature.trackGroupsEnableOnUnlock);
            definition.TrackGroupsAvailableOnUnlock = FuseStringPatch.FromSet(feature.trackGroupsAvailableOnUnlock);
            definition.AreasEnableOnUnlock = FuseStringPatch.FromSet(ToAreaIds(feature.areasEnableOnUnlock));
            definition.GameObjectsEnableOnUnlock = FuseStringPatch.FromSet(ToGameObjectPaths(feature.gameObjectsEnableOnUnlock));
            definition.UnlockIncludeIndustries = FuseStringPatch.FromSet(ToIndustryIds(feature.unlockIncludeIndustries));
            definition.UnlockExcludeIndustries = FuseStringPatch.FromSet(ToIndustryIds(feature.unlockExcludeIndustries));
            definition.UnlockIncludeIndustryComponents = FuseStringPatch.FromSet(ToIndustryComponentIds(feature.unlockIncludeIndustryComponents));
            return definition;
        }

        public static Progression AddProgression(string id, FuseProgression definition)
        {
            return AddProgression(id, definition, null);
        }

        internal static Progression AddProgression(string id, FuseProgression definition, string packageId)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetProgression(id) != null)
            {
                throw new InvalidOperationException($"Progression '{id}' already exists.");
            }

            var root = GameObject.Find("Progressions") ?? new GameObject("Progressions");
            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(root.transform, false);
            var progression = gameObject.AddComponent<Progression>();
            progression.identifier = id;
            progression.mapFeatureManager = MapFeatureManager.Shared;

            ApplyProgressionDefinition(progression, definition, packageId);
            FuseProgressionRuntimeIndex.Instance.Set(id, progression);
            RefreshProgressionManager();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Progression, id, definition);
            return progression;
        }

        public static void UpdateProgression(string id, FuseProgression definition)
        {
            UpdateProgression(id, definition, null);
        }

        internal static void UpdateProgression(string id, FuseProgression definition, string packageId)
        {
            var progression = RequireProgression(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            ApplyProgressionDefinition(progression, definition, packageId);
            FuseProgressionRuntimeIndex.Instance.Set(id, progression);
            RefreshProgressionManager();
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Progression, id, definition);
        }

        public static void RemoveProgression(string id)
        {
            var progression = RequireProgression(id);
            progression.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(progression.gameObject);
            FuseProgressionRuntimeIndex.Instance.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Progression, id);
            RefreshProgressionManager();
        }

        public static Progression GetProgression(string id)
        {
            if (FuseProgressionRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Progression)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Progression>(true).FirstOrDefault(progression =>
                    string.Equals(progression.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        public static IEnumerable<Progression> GetAllProgressions()
        {
            return UnityEngine.Object.FindObjectsOfType<Progression>(true);
        }

        public static FuseProgression GetProgressionDefinition(string id)
        {
            return GetDefinition(GetProgression(id));
        }

        public static FuseProgression GetDefinition(Progression progression)
        {
            if (progression == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Progression, progression.identifier, out FuseProgression definition);
            definition = definition ?? new FuseProgression();
            definition.Sections = definition.Sections ?? new Dictionary<string, FuseSection>();

            var sections = ProgressionSectionsField?.GetValue(progression) as Section[] ??
                           progression.GetComponentsInChildren<Section>(true);
            foreach (var section in sections.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier)))
            {
                definition.Sections.TryGetValue(section.identifier, out var existingSection);
                // Snapshot the live section back out to a FuseSection. The
                // sectional unlock-fan-out fields (industries/areas/...) are
                // not directly mirrored on the runtime Section object — they
                // live on the synthesized section-unlock MapFeature instead —
                // so we copy whatever the previous serialized definition had
                // for them through unchanged. Section-shaped fields
                // (prereqs, enableFeaturesOnUnlock, etc.) are reflected from
                // the runtime state via FromSet because the snapshot
                // describes the exact current state, not a merge patch.
                definition.Sections[section.identifier] = new FuseSection
                {
                    Id = section.identifier,
                    ProgressionId = progression.identifier,
                    DisplayName = section.displayName,
                    Description = section.description,
                    PrerequisiteSectionIds = FuseStringPatch.FromSet(ToSectionIds(section.prerequisiteSections)),
                    EnableFeaturesOnUnlock = FuseStringPatch.FromSet(ToFeatureIds(section.enableFeaturesOnUnlock)),
                    DisableFeaturesOnUnlock = FuseStringPatch.FromSet(ToFeatureIds(section.disableFeaturesOnUnlock)),
                    EnableFeaturesOnAvailable = FuseStringPatch.FromSet(ToFeatureIds(section.enableFeaturesOnAvailable)),
                    UnlockIncludeIndustries = existingSection?.UnlockIncludeIndustries,
                    UnlockExcludeIndustries = existingSection?.UnlockExcludeIndustries,
                    UnlockIncludeIndustryComponents = existingSection?.UnlockIncludeIndustryComponents,
                    AreasEnableOnUnlock = existingSection?.AreasEnableOnUnlock,
                    GameObjectsEnableOnUnlock = existingSection?.GameObjectsEnableOnUnlock,
                    TrackGroupsEnableOnUnlock = existingSection?.TrackGroupsEnableOnUnlock,
                    TrackGroupsAvailableOnUnlock = existingSection?.TrackGroupsAvailableOnUnlock,
                    InterchangeTransfers = ToInterchangeTransfers(section.InterchangeTransfers) ?? existingSection?.InterchangeTransfers,
                    DeliveryPhases = ToDeliveryPhases(section.deliveryPhases)
                };
            }

            return definition;
        }

        public static void SetFeatureEnabled(string id, bool enabled)
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                throw new InvalidOperationException("MapFeatureManager was not found.");
            }

            manager.SetFeatureEnabled(id, enabled);
        }

        // Track whether the game's Progression.Configure has run at least once.
        // Set by the Harmony postfix in FuseProgressionConfigureHookPatch. Until
        // this is true, StateManager.IsSandbox is unreliable (the save's GameMode
        // hasn't been deserialized; it returns its default Sandbox=true). Any
        // FUSE code that depends on IsSandbox — and the game internals we invoke
        // via reflection that depend on it — must be deferred.
        private static volatile bool _gameProgressionConfigured;

        // Track-group ids referenced by mod-added segments. Populated by
        // FuseModLoader.PreEnableInitialTrackGroups for every groupId it
        // collects from segments — regardless of whether PreEnable actually
        // flipped the bit (groups already in enabledGroupIds via base-scene
        // pre-baked data come back changed=false but still belong here).
        //
        // After the post-Configure refresh has run all the game's normal
        // per-feature SetGroupEnabled passes, any of these groups that no
        // MapFeature has claimed (via tracksEnable / tracksAvail) is an
        // orphan and must be disabled — otherwise mod-added segments in
        // that group (e.g. the MaconCounty mod's s3a Alarka Jct tracks) keep
        // rendering with no progression control.
        private static readonly HashSet<string> _transientlyPreEnabledTrackGroups =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _transientlyPreEnabledLock = new object();

        // Snapshot of every track-group id that was claimed by some
        // MapFeature's tracksEnable / tracksAvail BEFORE FUSE applied
        // any mod progression patches. Captured at MapDidLoad time by
        // <see cref="CaptureBaseFeatureClaimsSnapshot"/> and consulted
        // by <see cref="RevokeTransientlyPreEnabledOrphanGroups"/> to
        // tell two kinds of "no current owner" orphans apart:
        //
        //   * In this set → group WAS supposed to be progression-gated
        //     (the base game's <c>alarka</c> feature originally listed
        //     <c>s3a</c> in tracksEnable); a mod's feature patch
        //     stripped the owner list (e.g. an extension's
        //     <c>alarka-patched</c> replaces tracksEnable with
        //     <c>[alext-off]</c>). We hide the group to preserve the
        //     "locked until unlock" intent.
        //   * NOT in this set → genuinely decorative orphan (mod-added
        //     group with no progression hook by author intent, e.g.
        //     CollieDillsboroOverhaul's <c>e-c1</c> interchange
        //     extension siding). We keep it visible-but-unavailable.
        //
        // Cleared by <see cref="ClearBaseFeatureClaimsSnapshot"/> on
        // MapWillUnload so the next map starts fresh.
        private static readonly HashSet<string> _baseFeatureClaimedGroupIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _baseFeatureClaimsLock = new object();
        private static bool _baseFeatureClaimsCaptured;

        internal static void RecordTransientlyPreEnabledTrackGroup(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }
            lock (_transientlyPreEnabledLock)
            {
                _transientlyPreEnabledTrackGroups.Add(groupId);
            }
        }

        /// <summary>
        /// Capture the set of track-group ids claimed by any MapFeature's
        /// <c>trackGroupsEnableOnUnlock</c> or
        /// <c>trackGroupsAvailableOnUnlock</c> at the moment the map
        /// finished loading and BEFORE any FUSE mod progression patches
        /// have applied. Mirrors the existing
        /// <c>TrackAPI.CaptureBaseGraphSnapshot</c> contract for the
        /// progression layer. Idempotent — only the first call per
        /// map-load wins. Pair with
        /// <see cref="ClearBaseFeatureClaimsSnapshot"/> on map unload
        /// so the next map starts clean.
        /// </summary>
        public static void CaptureBaseFeatureClaimsSnapshot(string reason)
        {
            lock (_baseFeatureClaimsLock)
            {
                if (_baseFeatureClaimsCaptured)
                {
                    return;
                }

                var manager = MapFeatureManager.Shared;
                if (manager == null)
                {
                    FuseLog.Info(
                        $"FUSE progression base-feature claim snapshot deferred reason='{reason ?? "unspecified"}': " +
                        "MapFeatureManager.Shared not available yet.");
                    return;
                }

                _baseFeatureClaimedGroupIds.Clear();
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
                            _baseFeatureClaimedGroupIds.Add(group);
                        }
                    }
                    foreach (var group in feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(group))
                        {
                            _baseFeatureClaimedGroupIds.Add(group);
                        }
                    }
                }

                _baseFeatureClaimsCaptured = true;
                FuseLog.Info(
                    $"FUSE progression captured base-feature claim snapshot reason='{reason ?? "unspecified"}' " +
                    $"baseClaimedGroupIds={_baseFeatureClaimedGroupIds.Count}.");
            }
        }

        /// <summary>
        /// Drop the base-feature claim snapshot so the next map-load
        /// captures fresh vanilla state. Safe to call repeatedly.
        /// </summary>
        public static void ClearBaseFeatureClaimsSnapshot()
        {
            lock (_baseFeatureClaimsLock)
            {
                _baseFeatureClaimedGroupIds.Clear();
                _baseFeatureClaimsCaptured = false;
            }
        }

        private static bool WasGroupClaimedByBaseFeature(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }
            lock (_baseFeatureClaimsLock)
            {
                return _baseFeatureClaimedGroupIds.Contains(groupId);
            }
        }

        // If RefreshRuntimeStateAfterApply is called before Configure has fired,
        // we record the reason here and re-fire after Configure runs. Only the
        // latest pending reason is kept; we coalesce repeated pre-Configure
        // refresh requests into a single deferred run.
        private static string _pendingRefreshReason;
        private static readonly object _pendingRefreshLock = new object();

        /// <summary>
        /// Called by the Harmony postfix on <c>Game.Progression.Progression.Configure</c>.
        /// At that point the save's GameMode has been deserialized into StateManager
        /// and IsSandbox returns the real value. If a refresh was deferred, run it
        /// now.
        /// </summary>
        internal static void NotifyGameProgressionConfigured()
        {
            string pendingReason;
            lock (_pendingRefreshLock)
            {
                _gameProgressionConfigured = true;
                pendingReason = _pendingRefreshReason;
                _pendingRefreshReason = null;
            }

            var sandbox = StateManager.IsSandbox;
            var gameMode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
            FuseLog.Info(
                $"FUSE progression Configure observed (game progression initialized) " +
                $"IsSandbox={sandbox} GameMode={gameMode} hadPendingRefresh={(pendingReason != null)}.");

            if (pendingReason != null)
            {
                RefreshRuntimeStateAfterApply($"deferred from pre-Configure: {pendingReason}");
            }
        }

        /// <summary>
        /// Called by the Harmony postfix on <c>Game.Progression.Progression.Unconfigure</c>.
        /// Resets the configured-flag so the next save load can correctly detect
        /// its own pre-Configure racy window. Without this reset, reloading a
        /// save would leave the flag set from the previous load, and FUSE's
        /// refresh would run immediately with stale IsSandbox + empty KVO
        /// (because the new save's snapshot hasn't been applied yet), polluting
        /// the graph with feature defaults derived from sandbox-true.
        /// </summary>
        internal static void NotifyGameProgressionUnconfigured()
        {
            lock (_pendingRefreshLock)
            {
                _gameProgressionConfigured = false;
                // Drop any deferred reason — its requester is gone with the
                // previous load. The next load will produce its own refresh
                // request via FuseModLoader.
                _pendingRefreshReason = null;
            }

            FuseLog.Info(
                "FUSE progression Unconfigure observed (game progression torn down); " +
                "configured-flag cleared so the next load's pre-Configure window is detected fresh.");
        }

        public static void RefreshRuntimeStateAfterApply(string reason)
        {
            // Game-mode checkpoint. If IsSandbox is true here AND the player is in a
            // company-mode save, the save's GameMode hasn't been deserialized yet
            // and any feature-default writes / graph mutations will use the wrong
            // assumption. We track this with _gameProgressionConfigured (set in a
            // Harmony postfix on Game.Progression.Progression.Configure).
            var sandboxAtEntry = StateManager.IsSandbox;
            var gameModeAtEntry = StateManager.Shared?.GameMode.ToString() ?? "<null>";
            var configured = _gameProgressionConfigured;
            FuseLog.Info(
                $"FUSE progression refresh entry reason='{reason ?? "unspecified"}' " +
                $"IsSandbox={sandboxAtEntry} GameMode={gameModeAtEntry} configured={configured}.");

            if (!configured)
            {
                // The game's Progression.Configure hasn't run yet, so IsSandbox is
                // not trustworthy. If we proceed to ForceApplyCurrentMapFeatureState
                // we'd invoke the game's HandleFeatureEnablesChanged with stale
                // IsSandbox=true; that pass calls graph.SetGroupEnabled(<group>, true)
                // for every feature whose defaultEnableInSandbox is true, persisting
                // an enabled state in the graph. The game's late Configure-driven
                // dict update then can't undo it: HandleFeatureEnablesChanged with
                // initial=false skips features whose oldDefault equals their new
                // value (both evaluate to false once IsSandbox flips), so no
                // SetGroupEnabled(false) is emitted. The visible regression was
                // Alarka's Ela bridge appearing in a company-mode save.
                //
                // Park the request and let the Configure postfix re-fire it once
                // GameMode is reliable.
                lock (_pendingRefreshLock)
                {
                    _pendingRefreshReason = reason;
                }
                FuseLog.Info(
                    $"FUSE progression refresh deferred until Progression.Configure " +
                    $"(reason='{reason ?? "unspecified"}', stale IsSandbox would corrupt graph state).");
                return;
            }

            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh skipped package='<all>' operation='refresh progression state' " +
                    $"kind='map feature manager' id='<shared>' reason='{reason ?? "unspecified"}' message='MapFeatureManager.Shared was not available'.");
                return;
            }

            RefreshMapFeatureManager(manager);
            RefreshProgressionManager();

            var invokedCurrentProgression = false;
            try
            {
                var progressionManager = UnityEngine.Object.FindObjectOfType<ProgressionManager>();
                var current = progressionManager != null
                    ? ManagerCurrentProgressionField?.GetValue(progressionManager) as Progression
                    : null;
                if (current != null && ProgressionUpdateSectionStatesMethod != null)
                {
                    ProgressionUpdateSectionStatesMethod.Invoke(current, Array.Empty<object>());
                    invokedCurrentProgression = true;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='refresh progression state' " +
                    $"kind='progression' id='<current>' reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
            }

            // NOTE: InitializeMissingMapFeatureStates now intentionally returns 0
            // and writes nothing — it remains only as a diagnostic logger. Writing
            // pre-fill defaults to KVO was the Alarka Branch / Ela bridge
            // regression: the early-load run saw IsSandbox=true (GameMode not yet
            // deserialized) and persisted defaultEnableInSandbox=true entries
            // that survived the game's later save-driven dict merge. The game's
            // own HandleSnapshotProperties computes the same defaults at read
            // time using the (then-reliable) IsSandbox, so no pre-fill is
            // required for correctness.
            var initialized = InitializeMissingMapFeatureStates(manager);
            var forcedFeatureState = ForceApplyCurrentMapFeatureState(manager, reason);
            var restoredTrackGroups = RestoreDisabledTrackGroups(manager, reason);
            var finalisedOrphans = RevokeTransientlyPreEnabledOrphanGroups(manager, reason);
            FuseLog.Info(
                $"FUSE refreshed progression runtime state package='<all>' operation='refresh progression state' " +
                $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' " +
                $"currentProgressionRefreshed={invokedCurrentProgression} initializedFeatureStates={initialized} " +
                $"forcedFeatureState={forcedFeatureState} restoredDisabledTrackGroups={restoredTrackGroups} " +
                $"finalisedOrphanTrackGroups={finalisedOrphans}.");

            if (FuseSettings.VerboseApplyReportDetails)
            {
                DumpProgressionStateForDiagnostics(manager, reason);
            }
        }

        /// <summary>
        /// Builds a structured, JSON-serializable snapshot of the live progression
        /// graph: every MapFeature with its track-group / area / industry gating
        /// targets, every Section with current unlocked/available state and prereqs,
        /// every referenced track group with the feature owners that would set it
        /// enabled vs disabled, and every Area / Industry (with components and
        /// track spans) and PassengerStop with the parent-chain a panel filter
        /// would walk. Mirrors the same data the verbose log dump emits, but as
        /// an object tree so callers can write it to a file and grep/diff offline.
        /// </summary>
        /// <param name="reason">Free-form label echoed into the payload; used to
        /// distinguish dumps taken at different points (e.g. "console dump",
        /// "post-apply", "before save").</param>
        /// <returns>An anonymous object suitable for direct
        /// <c>JsonConvert.SerializeObject</c>. If the MapFeatureManager isn't
        /// available yet (no map loaded), returns a sentinel object with
        /// <c>available=false</c> rather than throwing.</returns>
        public static object BuildProgressionDiagnosticPayload(string reason = "console dump")
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                return new
                {
                    available = false,
                    reason = reason ?? "unspecified",
                    message = "MapFeatureManager.Shared was not available; load a map with FUSE active before dumping.",
                };
            }

            var states = ReadFeatureEnables(manager);
            var features = (manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
                .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .ToArray();

            var featurePayloads = features
                .Select(feature =>
                {
                    bool? kvoUnlocked = states != null && states.TryGetValue(feature.identifier, out var kv)
                        ? (bool?)kv
                        : null;
                    var defaultedTo = feature.defaultEnableInSandbox && StateManager.IsSandbox;
                    return (object)new
                    {
                        id = feature.identifier,
                        displayName = feature.displayName,
                        defaultEnableInSandbox = feature.defaultEnableInSandbox,
                        kvoUnlocked = kvoUnlocked,
                        defaultedTo = defaultedTo,
                        trackGroupsEnableOnUnlock = feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>(),
                        trackGroupsAvailableOnUnlock = feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>(),
                        areasEnableOnUnlock = ListComponentIds(feature.areasEnableOnUnlock),
                        industriesInclude = ListComponentIds(feature.unlockIncludeIndustries),
                        industriesExclude = ListComponentIds(feature.unlockExcludeIndustries),
                        prerequisiteFeatureIds = ListFeatureIds(feature.prerequisites),
                    };
                })
                .ToArray();

            var sectionsRaw = UnityEngine.Object.FindObjectsOfType<Section>(true) ?? Array.Empty<Section>();
            var sectionPayloads = sectionsRaw
                .Where(section => section != null)
                .OrderBy(section => section.identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(section => (object)new
                {
                    id = section.identifier ?? string.Empty,
                    name = section.name ?? string.Empty,
                    displayName = section.displayName ?? string.Empty,
                    unlocked = section.Unlocked,
                    available = section.Available,
                    paidCount = section.PaidCount,
                    fulfilledCount = section.FulfilledCount,
                    prerequisiteSectionIds = ListSectionIds(section.prerequisiteSections),
                    enableFeaturesOnUnlock = ListFeatureIds(section.enableFeaturesOnUnlock),
                    deliveryPhaseCount = section.deliveryPhases?.Length ?? 0,
                })
                .ToArray();

            object[] trackGroupPayloads = Array.Empty<object>();
            var graph = Graph.Shared;
            if (graph != null)
            {
                var enabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var disabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var feature in features)
                {
                    var groups = FeatureTrackGroups(feature).ToArray();
                    if (groups.Length == 0)
                    {
                        continue;
                    }
                    var enabled = IsFeatureEnabled(feature, states);
                    var owners = enabled ? enabledOwners : disabledOwners;
                    foreach (var groupId in groups)
                    {
                        if (!owners.TryGetValue(groupId, out var list))
                        {
                            list = new List<string>();
                            owners[groupId] = list;
                        }
                        list.Add(feature.identifier);
                    }
                }

                // Build the union of groups referenced by a MapFeature with
                // groups actually used by live segments. The latter is needed
                // to surface "orphan" groups (segments carry the groupId but
                // no feature claims it) — without them the trackGroups list
                // hides exactly the cases worth investigating, e.g. graph-
                // only mods that ship visible-but-locked decorative track via
                // unowned group ids.
                var allGroups = new HashSet<string>(enabledOwners.Keys, StringComparer.OrdinalIgnoreCase);
                allGroups.UnionWith(disabledOwners.Keys);
                var segmentGroupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (graph.Segments != null)
                {
                    foreach (var segment in graph.Segments)
                    {
                        if (segment == null || string.IsNullOrWhiteSpace(segment.groupId))
                        {
                            continue;
                        }
                        allGroups.Add(segment.groupId);
                        segmentGroupCounts.TryGetValue(segment.groupId, out var count);
                        segmentGroupCounts[segment.groupId] = count + 1;
                    }
                }

                trackGroupPayloads = allGroups
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                    .Select(groupId =>
                    {
                        var isEnabledNow = graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId);
                        var isAvailableNow = graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId);
                        enabledOwners.TryGetValue(groupId, out var enabledBy);
                        disabledOwners.TryGetValue(groupId, out var disabledBy);
                        // Dedupe: a feature that lists the same group in both
                        // trackGroupsEnableOnUnlock AND trackGroupsAvailableOnUnlock
                        // yields the group twice from FeatureTrackGroups. Owners
                        // are about which features touch this group, not how
                        // many times.
                        var enabledByDistinct = enabledBy != null && enabledBy.Count > 0
                            ? enabledBy.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : Array.Empty<string>();
                        var disabledByDistinct = disabledBy != null && disabledBy.Count > 0
                            ? disabledBy.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : Array.Empty<string>();
                        var hasOwner = enabledByDistinct.Length > 0 || disabledByDistinct.Length > 0;
                        segmentGroupCounts.TryGetValue(groupId, out var segCount);
                        return (object)new
                        {
                            id = groupId,
                            graphEnabled = isEnabledNow,
                            graphAvailable = isAvailableNow,
                            segmentCount = segCount,
                            orphan = !hasOwner,
                            enabledBy = enabledByDistinct,
                            disabledBy = disabledByDistinct,
                        };
                    })
                    .ToArray();
            }

            object[] passengerStopPayloads = Array.Empty<object>();
            try
            {
                var stops = Model.Ops.PassengerStop.FindAll()
                    .Where(stop => stop != null && !string.IsNullOrWhiteSpace(stop.identifier))
                    .ToArray();
                passengerStopPayloads = stops
                    .OrderBy(s => s.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(stop =>
                    {
                        var industry = stop.GetComponentInParent<Industry>(true);
                        var component = stop.GetComponentInParent<IndustryComponent>(true);
                        var area = stop.GetComponentInParent<Area>(true);
                        return (object)new
                        {
                            id = stop.identifier,
                            progressionDisabled = stop.ProgressionDisabled,
                            parentIndustryId = industry != null ? industry.identifier : null,
                            parentIndustryProgressionDisabled = industry != null ? (bool?)industry.ProgressionDisabled : null,
                            parentComponentId = component != null ? component.Identifier : null,
                            parentComponentProgressionDisabled = component != null ? (bool?)component.ProgressionDisabled : null,
                            parentAreaId = area != null ? area.identifier : null,
                            activeSelf = stop.gameObject.activeSelf,
                            activeInHierarchy = stop.gameObject.activeInHierarchy,
                            wouldPassPanelFilter = !stop.ProgressionDisabled,
                            path = FormatGameObjectPath(stop.transform),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE progression dump payload passengerStops failed: {ex.Message}");
            }

            object[] areaPayloads = Array.Empty<object>();
            try
            {
                var areas = UnityEngine.Object.FindObjectsOfType<Area>(true)
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.identifier))
                    .ToArray();
                areaPayloads = areas
                    .OrderBy(a => a.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(area =>
                    {
                        var industries = area.Industries?.ToArray() ?? Array.Empty<Industry>();
                        var stopsActive = area.GetComponentsInChildren<Model.Ops.PassengerStop>();
                        var stopsAll = area.GetComponentsInChildren<Model.Ops.PassengerStop>(true);
                        return (object)new
                        {
                            id = area.identifier,
                            industryCount = industries.Length,
                            passengerStopsActiveCount = stopsActive.Length,
                            passengerStopsAllCount = stopsAll.Length,
                            activeInHierarchy = area.gameObject.activeInHierarchy,
                            industries = industries
                                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.identifier))
                                .Select(i => new { id = i.identifier, progressionDisabled = i.ProgressionDisabled })
                                .ToArray(),
                            passengerStops = stopsActive
                                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.identifier))
                                .Select(s => new { id = s.identifier, progressionDisabled = s.ProgressionDisabled })
                                .ToArray(),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE progression dump payload areas failed: {ex.Message}");
            }

            object[] industryPayloads = Array.Empty<object>();
            try
            {
                var industries = UnityEngine.Object.FindObjectsOfType<Industry>(true)
                    .Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                    .ToArray();
                industryPayloads = industries
                    .OrderBy(i => i.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(industry =>
                    {
                        var area = industry.GetComponentInParent<Area>(true);
                        var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                            .Where(c => c != null)
                            .ToArray();
                        return (object)new
                        {
                            id = industry.identifier,
                            name = industry.name,
                            progressionDisabled = industry.ProgressionDisabled,
                            componentCount = components.Length,
                            parentAreaId = area != null ? area.identifier : null,
                            activeInHierarchy = industry.gameObject.activeInHierarchy,
                            path = FormatGameObjectPath(industry.transform),
                            components = components
                                .OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                .Select(component =>
                                {
                                    var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                                        .Where(s => s != null)
                                        .ToArray();
                                    return new
                                    {
                                        id = component.Identifier,
                                        type = component.GetType().FullName,
                                        progressionDisabled = component.ProgressionDisabled,
                                        isVisible = component.IsVisible,
                                        loadId = TryReadLoadId(component),
                                        trackSpanCount = spans.Length,
                                        spans = spans
                                            .Select(s => new
                                            {
                                                id = s.id ?? s.name,
                                                lowerSegmentId = s.lower?.segment?.id,
                                                lowerSegmentGroup = s.lower?.segment?.groupId,
                                                upperSegmentId = s.upper?.segment?.id,
                                                upperSegmentGroup = s.upper?.segment?.groupId,
                                            })
                                            .ToArray(),
                                    };
                                })
                                .ToArray(),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE progression dump payload industries failed: {ex.Message}");
            }

            return new
            {
                available = true,
                reason = reason ?? "unspecified",
                counts = new
                {
                    features = featurePayloads.Length,
                    sections = sectionPayloads.Length,
                    trackGroups = trackGroupPayloads.Length,
                    passengerStops = passengerStopPayloads.Length,
                    areas = areaPayloads.Length,
                    industries = industryPayloads.Length,
                },
                features = featurePayloads,
                sections = sectionPayloads,
                trackGroups = trackGroupPayloads,
                passengerStops = passengerStopPayloads,
                areas = areaPayloads,
                industries = industryPayloads,
            };
        }

        private static string[] ListComponentIds<T>(T[] components) where T : UnityEngine.Component
        {
            if (components == null || components.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(components.Length);
            foreach (var component in components)
            {
                if (component == null) continue;
                var id = component.GetType().GetProperty("identifier")?.GetValue(component) as string
                    ?? component.GetType().GetField("identifier")?.GetValue(component) as string
                    ?? component.name;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    list.Add(id);
                }
            }
            return list.ToArray();
        }

        private static string[] ListFeatureIds(MapFeature[] features)
        {
            if (features == null || features.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(features.Length);
            foreach (var feature in features)
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier)) continue;
                list.Add(feature.identifier);
            }
            return list.ToArray();
        }

        private static string[] ListSectionIds(Section[] sections)
        {
            if (sections == null || sections.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(sections.Length);
            foreach (var section in sections)
            {
                if (section == null || string.IsNullOrWhiteSpace(section.identifier)) continue;
                list.Add(section.identifier);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Verbose dump of every MapFeature, Section, and track-group state after
        /// a progression refresh. Gated behind <see cref="FuseSettings.VerboseApplyReportDetails"/>
        /// because in a busy mod set this can run into thousands of lines; useful when
        /// diagnosing "feature unlocked when it shouldn't be" or "track group visible
        /// when its controlling feature is locked" reports.
        /// </summary>
        private static void DumpProgressionStateForDiagnostics(MapFeatureManager manager, string reason)
        {
            try
            {
                var states = ReadFeatureEnables(manager);
                var features = (manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
                    .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                    .ToArray();

                FuseLog.Info(
                    $"FUSE progression dump begin reason='{reason ?? "unspecified"}' features={features.Length} " +
                    $"featureStateEntries={states?.Count ?? 0}.");

                // Per-feature line: identifier, defaultSandbox, current unlock from KVO,
                // and the track-group / area / industry references the feature gates.
                foreach (var feature in features)
                {
                    var enabledInKvo = states != null && states.TryGetValue(feature.identifier, out var kv)
                        ? kv.ToString()
                        : "<unset>";
                    var defaultedTo = feature.defaultEnableInSandbox && StateManager.IsSandbox ? "true" : "false";
                    FuseLog.Info(
                        "  feature " +
                        $"id='{feature.identifier}' display='{feature.displayName}' " +
                        $"defaultSandbox={feature.defaultEnableInSandbox} kvoUnlocked={enabledInKvo} " +
                        $"defaultedTo={defaultedTo} " +
                        $"tracksEnable=[{FormatIdList(feature.trackGroupsEnableOnUnlock)}] " +
                        $"tracksAvail=[{FormatIdList(feature.trackGroupsAvailableOnUnlock)}] " +
                        $"areas=[{FormatComponentIds(feature.areasEnableOnUnlock)}] " +
                        $"industriesInclude=[{FormatComponentIds(feature.unlockIncludeIndustries)}] " +
                        $"prereqIds=[{FormatFeatureIds(feature.prerequisites)}].");
                }

                // Per-section line: identifier, name, current Unlocked/Available, prereq sections.
                var sections = UnityEngine.Object.FindObjectsOfType<Section>(true);
                FuseLog.Info($"FUSE progression dump sections count={sections?.Length ?? 0}.");
                if (sections != null)
                {
                    foreach (var section in sections)
                    {
                        if (section == null) continue;
                        FuseLog.Info(
                            "  section " +
                            $"id='{section.identifier ?? string.Empty}' name='{section.name ?? string.Empty}' " +
                            $"display='{section.displayName ?? string.Empty}' " +
                            $"unlocked={section.Unlocked} available={section.Available} " +
                            $"paid={section.PaidCount} fulfilled={section.FulfilledCount} " +
                            $"prereqSections=[{FormatSectionIds(section.prerequisiteSections)}] " +
                            $"enableFeaturesOnUnlock=[{FormatFeatureIds(section.enableFeaturesOnUnlock)}] " +
                            $"deliveryPhases={section.deliveryPhases?.Length ?? 0}.");
                    }
                }

                // Per-track-group line: id, current enabled/available, feature owners that
                // would set it enabled, feature owners that would set it disabled, what
                // its computed final state should be. Surfaces the "feature X has group Y
                // in trackGroupsEnableOnUnlock but Y is enabled despite X being locked"
                // pattern that took most of a debugging session to trace manually.
                var graph = Graph.Shared;
                if (graph != null)
                {
                    var enabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    var disabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var feature in features)
                    {
                        var groups = FeatureTrackGroups(feature).ToArray();
                        if (groups.Length == 0) continue;
                        var enabled = IsFeatureEnabled(feature, states);
                        var owners = enabled ? enabledOwners : disabledOwners;
                        foreach (var groupId in groups)
                        {
                            if (!owners.TryGetValue(groupId, out var list))
                            {
                                list = new List<string>();
                                owners[groupId] = list;
                            }
                            list.Add(feature.identifier);
                        }
                    }

                    var allGroups = new HashSet<string>(enabledOwners.Keys, StringComparer.OrdinalIgnoreCase);
                    allGroups.UnionWith(disabledOwners.Keys);
                    FuseLog.Info($"FUSE progression dump trackGroups count={allGroups.Count}.");
                    foreach (var groupId in allGroups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
                    {
                        var isEnabledNow = graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId);
                        var isAvailableNow = graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId);
                        enabledOwners.TryGetValue(groupId, out var enabledBy);
                        disabledOwners.TryGetValue(groupId, out var disabledBy);
                        FuseLog.Info(
                            "  trackGroup " +
                            $"id='{groupId}' graphEnabled={isEnabledNow} graphAvailable={isAvailableNow} " +
                            $"enabledBy=[{(enabledBy != null ? string.Join(",", enabledBy) : string.Empty)}] " +
                            $"disabledBy=[{(disabledBy != null ? string.Join(",", disabledBy) : string.Empty)}].");
                    }
                }

                // Per-passenger-stop dump: identifier, ProgressionDisabled,
                // closest parent Industry (id + flag), closest parent Area
                // (id), full GameObject hierarchy path, and whether the FUSE
                // car destination panel filter would currently let this stop
                // through. This is the ground truth for diagnosing "locked
                // station appears in passenger car destination picker".
                //
                // The path matters: the game's UpdateFeatureForUnlocked finds
                // PassengerStops via `area.GetComponentsInChildren<PassengerStop>()`
                // (no includeInactive). If a stop's path does not descend
                // from the feature's areasEnableOnUnlock Area, the game's
                // pass cannot reach it — and no amount of refreshing on our
                // end will help.
                try
                {
                    var stops = Model.Ops.PassengerStop.FindAll()
                        .Where(stop => stop != null && !string.IsNullOrWhiteSpace(stop.identifier))
                        .ToArray();
                    FuseLog.Info($"FUSE progression dump passengerStops count={stops.Length}.");
                    foreach (var stop in stops.OrderBy(s => s.identifier, StringComparer.OrdinalIgnoreCase))
                    {
                        var industry = stop.GetComponentInParent<Industry>(true);
                        var industryId = industry != null ? industry.identifier : "<none>";
                        var industryDisabled = industry != null ? industry.ProgressionDisabled : false;
                        var area = stop.GetComponentInParent<Area>(true);
                        var areaId = area != null ? area.identifier : "<none>";
                        var component = stop.GetComponentInParent<IndustryComponent>(true);
                        var componentId = component != null ? component.Identifier : "<none>";
                        var componentDisabled = component != null ? component.ProgressionDisabled : false;
                        var wouldPassFilter = !stop.ProgressionDisabled;
                        var path = FormatGameObjectPath(stop.transform);
                        var isActiveSelf = stop.gameObject.activeSelf;
                        var isActiveInHierarchy = stop.gameObject.activeInHierarchy;
                        FuseLog.Info(
                            $"  passengerStop id='{stop.identifier}' progressionDisabled={stop.ProgressionDisabled} " +
                            $"parentIndustry='{industryId}' industryProgDisabled={industryDisabled} " +
                            $"parentComponent='{componentId}' componentProgDisabled={componentDisabled} " +
                            $"parentArea='{areaId}' activeSelf={isActiveSelf} activeInHierarchy={isActiveInHierarchy} " +
                            $"wouldPassPanelFilter={wouldPassFilter} path='{path}'.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump passengerStops failed: {ex.Message}");
                }

                // Per-Area dump: mirrors what the game's UpdateFeatureForUnlocked
                // sees when it iterates a feature's areasEnableOnUnlock. Logs
                // exactly which Industry / PassengerStop children the game's
                // GetComponentsInChildren call would discover (using the same
                // includeInactive=false semantics) — so we can cross-reference
                // against the per-feature areas listing above and confirm or
                // refute "the area is empty when the game looks at it".
                try
                {
                    var areas = UnityEngine.Object.FindObjectsOfType<Area>(true)
                        .Where(a => a != null && !string.IsNullOrWhiteSpace(a.identifier))
                        .ToArray();
                    FuseLog.Info($"FUSE progression dump areas count={areas.Length}.");
                    foreach (var area in areas.OrderBy(a => a.identifier, StringComparer.OrdinalIgnoreCase))
                    {
                        var industries = area.Industries?.ToArray() ?? Array.Empty<Industry>();
                        // The game uses (false) implicitly; mirror that.
                        var stopsActive = area.GetComponentsInChildren<Model.Ops.PassengerStop>();
                        var stopsAll = area.GetComponentsInChildren<Model.Ops.PassengerStop>(true);
                        var industryIds = industries
                            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.identifier))
                            .Select(i => $"{i.identifier}(disabled={i.ProgressionDisabled})")
                            .ToArray();
                        var stopIds = stopsActive
                            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.identifier))
                            .Select(s => $"{s.identifier}(disabled={s.ProgressionDisabled})")
                            .ToArray();
                        FuseLog.Info(
                            $"  area id='{area.identifier}' industries={industries.Length} " +
                            $"passengerStopsActive={stopsActive.Length} passengerStopsAll={stopsAll.Length} " +
                            $"activeInHierarchy={area.gameObject.activeInHierarchy} " +
                            $"industryList=[{string.Join(",", industryIds)}] " +
                            $"passengerStopList=[{string.Join(",", stopIds)}].");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump areas failed: {ex.Message}");
                }

                // Per-Industry dump: ground truth for "industry track shows
                // up on map when it shouldn't" and "captive freight service
                // looking wrong" reports. Shows ProgressionDisabled on the
                // industry, every IndustryComponent child (with its own
                // ProgressionDisabled, IsVisible — the actual map-visibility
                // predicate — type name, load id, and track-span resolution),
                // and the closest Area / GameObject path. The IsVisible
                // column matters: per game IL, IsVisible only checks the
                // component's own ProgressionDisabled and trackSpans.Length;
                // it does NOT propagate from the parent Industry. So a
                // locked Industry with components whose ProgressionDisabled
                // is false will still draw those components on the map.
                try
                {
                    var industries = UnityEngine.Object.FindObjectsOfType<Industry>(true)
                        .Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                        .ToArray();
                    FuseLog.Info($"FUSE progression dump industries count={industries.Length}.");
                    foreach (var industry in industries.OrderBy(i => i.identifier, StringComparer.OrdinalIgnoreCase))
                    {
                        var area = industry.GetComponentInParent<Area>(true);
                        var areaId = area != null ? area.identifier : "<none>";
                        var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                            .Where(c => c != null)
                            .ToArray();
                        var path = FormatGameObjectPath(industry.transform);
                        FuseLog.Info(
                            $"  industry id='{industry.identifier}' name='{industry.name}' " +
                            $"progressionDisabled={industry.ProgressionDisabled} " +
                            $"componentCount={components.Length} parentArea='{areaId}' " +
                            $"activeInHierarchy={industry.gameObject.activeInHierarchy} " +
                            $"path='{path}'.");

                        foreach (var component in components.OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                        {
                            var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                                .Where(s => s != null)
                                .ToArray();
                            var spanIds = spans
                                .Select(s =>
                                {
                                    var lowerSegment = s.lower?.segment?.id ?? "<null>";
                                    var lowerGroup = s.lower?.segment?.groupId ?? "<null>";
                                    var upperSegment = s.upper?.segment?.id ?? "<null>";
                                    var upperGroup = s.upper?.segment?.groupId ?? "<null>";
                                    return $"{s.id ?? s.name}(lower={lowerSegment}@{lowerGroup},upper={upperSegment}@{upperGroup})";
                                })
                                .ToArray();
                            // Resolve a load id when the component carries one
                            // (LoadConsumer / FUSE loaders/unloaders).
                            var loadId = TryReadLoadId(component);
                            var typeName = component.GetType().FullName ?? "<unknown>";
                            FuseLog.Info(
                                $"    component id='{component.Identifier}' type='{typeName}' " +
                                $"progressionDisabled={component.ProgressionDisabled} isVisible={component.IsVisible} " +
                                $"loadId='{loadId ?? "<n/a>"}' trackSpans={spans.Length} " +
                                $"spans=[{string.Join(",", spanIds)}].");
                        }
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump industries failed: {ex.Message}");
                }

                // MapEnhancer-simulation dump: mirrors Map Enhancer's industry
                // track lookup exactly, so we can compare what FUSE presents
                // vs what Map Enhancer reads when painting the map. MapEnhancer
                // walks OpsController.Shared.Areas -> area.Industries ->
                // industry.Components looking for each IndustryComponent;
                // when found, uses that Area's tagColor for every cached
                // TrackSegment of the component's TrackSpans (and marks them
                // as `_industrialSegments`). If the iteration misses the
                // component, MapEnhancer position-falls-back to
                // OpsController.Shared.ClosestAreaForGamePosition — that
                // fallback is the usual culprit when an industry track
                // shows the wrong colour, since it can pick an unrelated
                // adjacent area.
                try
                {
                    var ops = UnityEngine.Object.FindObjectOfType<Model.Ops.OpsController>();
                    var areasList = ops?.Areas?.ToArray() ?? Array.Empty<Area>();
                    FuseLog.Info(
                        $"FUSE progression dump mapEnhancer-sim ops={(ops != null)} areasCount={areasList.Length}.");

                    var components = UnityEngine.Object.FindObjectsOfType<IndustryComponent>()
                        .Where(c => c != null && !(c is Model.Ops.ProgressionIndustryComponent))
                        .ToArray();
                    FuseLog.Info(
                        $"FUSE progression dump mapEnhancer-sim industryComponents (excluding ProgressionIndustryComponent) count={components.Length}.");

                    foreach (var component in components.OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        // Mirror MapEnhancer's exact lookup: walk
                        // OpsController.Shared.Areas -> Industries -> Components.
                        Area foundArea = null;
                        if (ops != null)
                        {
                            foreach (var area in areasList)
                            {
                                if (area?.Industries == null) continue;
                                foreach (var industry in area.Industries)
                                {
                                    if (industry?.Components == null) continue;
                                    var hit = false;
                                    foreach (var comp in industry.Components)
                                    {
                                        if (ReferenceEquals(comp, component))
                                        {
                                            foundArea = area;
                                            hit = true;
                                            break;
                                        }
                                    }
                                    if (hit) break;
                                }
                                if (foundArea != null) break;
                            }
                        }

                        // Position fallback used when the registry walk fails.
                        Area fallbackArea = null;
                        if (foundArea == null && ops != null)
                        {
                            try
                            {
                                var gamePos = Helpers.WorldTransformer.WorldToGame(component.transform.position);
                                fallbackArea = ops.ClosestAreaForGamePosition(new UnityEngine.Vector2(gamePos.x, gamePos.z));
                            }
                            catch
                            {
                            }
                        }

                        var pickedArea = foundArea ?? fallbackArea;
                        var pickedAreaId = pickedArea != null ? pickedArea.identifier : "<none>";
                        var pickedTag = pickedArea != null ? pickedArea.tagColor : default(UnityEngine.Color);
                        var pickedTagHex = pickedTag == default(UnityEngine.Color)
                            ? "<default>"
                            : $"#{(int)(pickedTag.r * 255):X2}{(int)(pickedTag.g * 255):X2}{(int)(pickedTag.b * 255):X2}";

                        var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                            .Where(s => s != null)
                            .ToArray();
                        var cachedSegmentSummaries = new List<string>();
                        var spanCachedSegmentsField = TrackSpanCachedSegmentsField;
                        var spanUpdateMethod = TrackSpanUpdateCachedPointsMethod;
                        foreach (var span in spans)
                        {
                            try
                            {
                                spanUpdateMethod?.Invoke(span, null);
                            }
                            catch
                            {
                            }
                            var cachedRaw = spanCachedSegmentsField?.GetValue(span) as System.Collections.IList;
                            if (cachedRaw == null) continue;
                            foreach (var item in cachedRaw)
                            {
                                if (!(item is TrackSegment seg) || seg == null) continue;
                                cachedSegmentSummaries.Add(
                                    $"{seg.id ?? "<null>"}(group='{seg.groupId ?? "<null>"}'," +
                                    $"available={seg.Available},groupEnabled={seg.GroupEnabled})");
                            }
                        }

                        FuseLog.Info(
                            $"  mapEnhancer-sim component id='{component.Identifier}' " +
                            $"foundAreaViaIteration='{(foundArea != null ? foundArea.identifier : "<none>")}' " +
                            $"positionFallbackArea='{(fallbackArea != null ? fallbackArea.identifier : "<none>")}' " +
                            $"pickedArea='{pickedAreaId}' pickedTagColor={pickedTagHex} " +
                            $"componentProgDisabled={component.ProgressionDisabled} isVisible={component.IsVisible} " +
                            $"cachedSegments=[{string.Join(",", cachedSegmentSummaries)}].");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump mapEnhancer-sim failed: {ex.Message}");
                }

                // KeyValueBoolAnimator inventory: snapshot every animator in the
                // scene, its observed key, whether it found a KeyValueObject in
                // its parent chain, and the current bool value. Industry loader
                // / water column / coal chute / turntable animations all run on
                // this pipeline; a missing-parent-KVO entry here means that
                // animator will never play, which manifests as "loader doesn't
                // rotate/open when I expect it to."
                try
                {
                    var animators = UnityEngine.Object.FindObjectsOfType<RollingStock.Controls.KeyValueBoolAnimator>(true);
                    var withKvo = 0;
                    var withoutKvo = 0;
                    foreach (var animator in animators)
                    {
                        if (animator == null) continue;
                        if (animator.GetComponentInParent<KeyValueObject>() != null) withKvo++; else withoutKvo++;
                    }
                    FuseLog.Info(
                        $"FUSE progression dump keyValueBoolAnimators count={animators.Length} " +
                        $"withParentKVO={withKvo} withoutParentKVO={withoutKvo}.");

                    foreach (var animator in animators)
                    {
                        if (animator == null) continue;
                        var kvo = animator.GetComponentInParent<KeyValueObject>();
                        var animPath = FormatGameObjectPath(animator.transform);
                        var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-found>";
                        bool? currentBool = null;
                        try
                        {
                            if (kvo != null && !string.IsNullOrEmpty(animator.key))
                            {
                                currentBool = kvo[animator.key].BoolValue;
                            }
                        }
                        catch
                        {
                        }
                        FuseLog.Info(
                            $"  animator path='{animPath}' key='{animator.key ?? "<null>"}' " +
                            $"parentKVO='{kvoPath}' currentBool={(currentBool.HasValue ? currentBool.Value.ToString() : "<n/a>")} " +
                            $"invert={animator.invert} active={animator.gameObject.activeInHierarchy} enabled={animator.enabled}.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump keyValueBoolAnimators failed: {ex.Message}");
                }

                // KeyValuePickableToggle inventory — these are the click
                // handlers for water columns / fueling stands / coal towers /
                // roundhouse stall doors. For a click to take effect:
                //   1. A Collider must exist on the GameObject or a child
                //      (raycast hit).
                //   2. GetComponentInParent<KeyValueObject>() must return a
                //      KVO (so the toggle can read/write the bool).
                //   3. The KVO must be registered globally
                //      (GlobalKeyValueObject + non-empty globalObjectId) so
                //      the PropertyChange message can be routed when the
                //      toggle fires.
                //
                // Any of those missing produces "I click the loader and
                // nothing happens." This dump exposes each precondition.
                try
                {
                    var toggles = UnityEngine.Object.FindObjectsOfType<RollingStock.Controls.KeyValuePickableToggle>(true);
                    FuseLog.Info($"FUSE progression dump keyValuePickableToggles count={toggles.Length}.");
                    foreach (var toggle in toggles)
                    {
                        if (toggle == null) continue;
                        var togglePath = FormatGameObjectPath(toggle.transform);
                        var kvo = toggle.GetComponentInParent<KeyValueObject>();
                        var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-found>";

                        // Was the KVO registered globally (so PropertyChange messages route)?
                        // The presence of GlobalKeyValueObject on the same GameObject with a
                        // non-empty globalObjectId is the signal. Looked up reflectively to
                        // avoid pulling in the Unity Physics reference for a tiny check.
                        string globalId = null;
                        if (kvo != null)
                        {
                            try
                            {
                                var globalType = Type.GetType("RollingStock.Controls.GlobalKeyValueObject, Assembly-CSharp");
                                if (globalType != null)
                                {
                                    var globalComp = kvo.GetComponent(globalType);
                                    if (globalComp != null)
                                    {
                                        var idField = globalType.GetField("globalObjectId");
                                        globalId = idField?.GetValue(globalComp) as string;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }

                        FuseLog.Info(
                            $"  pickableToggle path='{togglePath}' key='{toggle.key ?? "<null>"}' " +
                            $"parentKVO='{kvoPath}' globalObjectId='{globalId ?? "<none>"}' " +
                            $"active={toggle.gameObject.activeInHierarchy} enabled={toggle.enabled} " +
                            $"maxPickDistance={toggle.MaxPickDistance}.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump keyValuePickableToggles failed: {ex.Message}");
                }

                // CarLoaderSequencer inventory — the bridge that turns a
                // click-toggled `request` bool into `prepareLoad` /
                // `animateLoad` writes consumed by the animator. If a water
                // column / fueling stand fires KeyValuePickableToggle.Activate
                // but no PropertyChanged ever fires on the animation keys,
                // this sequencer is likely missing, disabled, has an unbound
                // keyValueObject SerializeField, or its host isn't the
                // multiplayer host.
                try
                {
                    var sequencers = UnityEngine.Object.FindObjectsOfType<RollingStock.CarLoaderSequencer>(true);
                    FuseLog.Info($"FUSE progression dump carLoaderSequencers count={sequencers.Length}.");
                    foreach (var sequencer in sequencers)
                    {
                        if (sequencer == null) continue;
                        var path = FormatGameObjectPath(sequencer.transform);
                        var kvo = sequencer.keyValueObject;
                        var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-assigned>";
                        // Compare assigned kvo to the GameObject-tree KVO (GetComponentInParent)
                        // — sometimes the sequencer's SerializeField points at the wrong KVO
                        // (e.g. a stale reference from before cloning).
                        var nearestKvo = sequencer.GetComponentInParent<KeyValueObject>();
                        var nearestKvoPath = nearestKvo != null ? FormatGameObjectPath(nearestKvo.transform) : "<not-found>";
                        var matches = ReferenceEquals(kvo, nearestKvo);
                        FuseLog.Info(
                            $"  carLoaderSequencer path='{path}' kvoRef='{kvoPath}' " +
                            $"nearestParentKvo='{nearestKvoPath}' refMatchesNearest={matches} " +
                            $"readWants='{sequencer.readWantsLoadingKey}' readIsLoading='{sequencer.readIsLoadingKey}' " +
                            $"writeCanLoad='{sequencer.writeCanLoadKey}' writePrepare='{sequencer.writePrepareLoadKey}' " +
                            $"writeAnimate='{sequencer.writeAnimateLoadKey}' " +
                            $"active={sequencer.gameObject.activeInHierarchy} enabled={sequencer.enabled}.");
                    }
                }
                catch (Exception ex)
                {
                    FuseLog.Warning($"FUSE progression dump carLoaderSequencers failed: {ex.Message}");
                }

                FuseLog.Info("FUSE progression dump end.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression diagnostic dump failed reason='{reason ?? "unspecified"}': {ex.Message}");
            }
        }

        private static void ApplyMapFeatureDefinition(MapFeature feature, FuseMapFeature definition)
        {
            // PATCH SEMANTICS: this method is invoked both when a mod creates a
            // brand-new MapFeature AND when a mod's progression definition patches
            // a feature that already exists in the scene (a base-game feature the
            // mod selectively overrides). The contract differs by JSON shape per
            // field — see <see cref="FuseStringPatch"/> docs:
            //
            //   * Field omitted from JSON  -> definition.X is null -> NO CHANGE.
            //                                 Runtime keeps its existing value
            //                                 (e.g. base-game alarka MapFeature's
            //                                 areasEnableOnUnlock pointing at the
            //                                 actual Alarka area, even when the
            //                                 patch only wants to change the
            //                                 displayName).
            //
            //   * Field present as JSON array, e.g. "tracks": ["a","b"]
            //                                -> definition.X.Set is non-null
            //                                -> REPLACE existing with [a, b].
            //
            //   * Field present as JSON object, e.g. "tracks": {"a": true, "b": false}
            //                                -> definition.X.Patch is non-null
            //                                -> per-id MERGE on top of existing.
            //                                   "a" added if absent, "b" removed
            //                                   if present, anything else kept.
            //
            // The same field on the wire-data side carries both intents; the
            // FuseStringPatch container preserves the distinction so we can apply
            // the right semantics here without losing information the way the
            // earlier converter did.
            if (!string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                feature.displayName = definition.DisplayName;
            }
            else if (string.IsNullOrWhiteSpace(feature.displayName))
            {
                feature.displayName = feature.identifier;
            }
            if (definition.Description != null)
            {
                feature.description = definition.Description;
            }
            feature.defaultEnableInSandbox = definition.InitiallyEnabled;

            ApplyTrackGroupPatch(definition.TrackGroupsEnableOnUnlock, definition.GroupIds,
                ref feature.trackGroupsEnableOnUnlock);
            ApplyTrackGroupPatch(definition.TrackGroupsAvailableOnUnlock, definition.GroupIds,
                ref feature.trackGroupsAvailableOnUnlock);

            if (definition.PrerequisiteFeatureIds != null)
            {
                var existingIds = (feature.prerequisites ?? Array.Empty<MapFeature>())
                    .Where(prereq => prereq != null)
                    .Select(prereq => prereq.identifier);
                feature.prerequisites = ResolveMapFeatures(definition.PrerequisiteFeatureIds.ApplyTo(existingIds));
            }
            if (definition.GameObjectsEnableOnUnlock != null)
            {
                var existingPaths = (feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>())
                    .Where(go => go != null)
                    .Select(GetGameObjectPath);
                feature.gameObjectsEnableOnUnlock = ResolveGameObjects(definition.GameObjectsEnableOnUnlock.ApplyTo(existingPaths));
            }
            if (definition.AreasEnableOnUnlock != null)
            {
                var existingIds = (feature.areasEnableOnUnlock ?? Array.Empty<Area>())
                    .Where(area => area != null)
                    .Select(area => area.identifier);
                feature.areasEnableOnUnlock = ResolveAreas(definition.AreasEnableOnUnlock.ApplyTo(existingIds));
            }
            if (definition.UnlockExcludeIndustries != null)
            {
                var existingIds = (feature.unlockExcludeIndustries ?? Array.Empty<Industry>())
                    .Where(industry => industry != null)
                    .Select(industry => industry.identifier);
                feature.unlockExcludeIndustries = ResolveIndustries(definition.UnlockExcludeIndustries.ApplyTo(existingIds));
            }
            if (definition.UnlockIncludeIndustries != null)
            {
                var existingIds = (feature.unlockIncludeIndustries ?? Array.Empty<Industry>())
                    .Where(industry => industry != null)
                    .Select(industry => industry.identifier);
                feature.unlockIncludeIndustries = ResolveIndustries(definition.UnlockIncludeIndustries.ApplyTo(existingIds));
            }
            if (definition.UnlockIncludeIndustryComponents != null)
            {
                var existingIds = (feature.unlockIncludeIndustryComponents ?? Array.Empty<IndustryComponent>())
                    .Where(component => component != null)
                    .Select(SafeIndustryComponentId)
                    .Where(id => !string.IsNullOrWhiteSpace(id));
                feature.unlockIncludeIndustryComponents = ResolveIndustryComponents(
                    definition.UnlockIncludeIndustryComponents.ApplyTo(existingIds));
            }
            SanitizeMapFeature(feature);

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    "FUSE progression map feature applied " +
                    $"id='{feature.identifier}' display='{feature.displayName}' defaultSandbox={feature.defaultEnableInSandbox} " +
                    $"prereqIds=[{FormatPatchInputs(definition.PrerequisiteFeatureIds)}] " +
                    $"prereqResolvedCount={feature.prerequisites?.Length ?? 0} " +
                    $"tracksEnable=[{FormatIdList(feature.trackGroupsEnableOnUnlock)}] " +
                    $"tracksAvail=[{FormatIdList(feature.trackGroupsAvailableOnUnlock)}] " +
                    $"areas=[{FormatComponentIds(feature.areasEnableOnUnlock)}] " +
                    $"industriesInclude=[{FormatComponentIds(feature.unlockIncludeIndustries)}] " +
                    $"industriesExclude=[{FormatComponentIds(feature.unlockExcludeIndustries)}] " +
                    $"gameObjects={feature.gameObjectsEnableOnUnlock?.Length ?? 0}.");
            }
        }

        /// <summary>
        /// Applies a <see cref="FuseStringPatch"/> against the supplied track-group
        /// array (a raw string[] field on the live MapFeature). The legacy
        /// <c>GroupIds</c> fallback feeds the resolution when the explicit track
        /// group field is absent so older packages that pre-date the split into
        /// enable/available stay loadable.
        /// </summary>
        private static void ApplyTrackGroupPatch(FuseStringPatch explicitPatch, FuseStringPatch fallbackPatch, ref string[] target)
        {
            var chosen = explicitPatch != null && explicitPatch.HasValue ? explicitPatch : fallbackPatch;
            if (chosen == null || !chosen.HasValue)
            {
                return;
            }
            target = chosen.ApplyTo(target ?? Array.Empty<string>());
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;
            var segments = new List<string>();
            var cursor = gameObject.transform;
            var depth = 0;
            while (cursor != null && depth < 32)
            {
                segments.Add(cursor.name);
                cursor = cursor.parent;
                depth++;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string FormatPatchInputs(FuseStringPatch patch)
        {
            if (patch == null || !patch.HasValue) return string.Empty;
            return string.Join(",", patch.EffectiveAdditions);
        }

        private static string FormatIdList(string[] ids)
        {
            return ids == null || ids.Length == 0 ? string.Empty : string.Join(",", ids);
        }

        /// <summary>
        /// Reflectively reads the load identifier from an IndustryComponent. Different
        /// subtypes expose their load on different members (<c>load</c>,
        /// <c>passengerLoad</c>, <c>loadId</c>), so probing by reflection avoids a
        /// brittle type switch and still surfaces the right id for diagnostic logs.
        /// </summary>
        private static string TryReadLoadId(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                var type = component.GetType();
                var candidateNames = new[] { "load", "passengerLoad", "Load", "PassengerLoad" };
                foreach (var name in candidateNames)
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (field != null)
                    {
                        var value = field.GetValue(component);
                        var id = ExtractLoadIdentifier(value);
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (property != null && property.CanRead)
                    {
                        var value = property.GetValue(component);
                        var id = ExtractLoadIdentifier(value);
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ExtractLoadIdentifier(object loadObj)
        {
            if (loadObj == null)
            {
                return null;
            }
            // String load id (used by some FUSE component shapes).
            if (loadObj is string s)
            {
                return s;
            }
            // Load / similar ScriptableObject reference: read its 'id' field/property.
            var type = loadObj.GetType();
            var idMember = type.GetField("id", BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetField("identifier", BindingFlags.Public | BindingFlags.Instance);
            if (idMember != null)
            {
                return idMember.GetValue(loadObj) as string;
            }
            var idProperty = type.GetProperty("id", BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetProperty("identifier", BindingFlags.Public | BindingFlags.Instance);
            if (idProperty != null && idProperty.CanRead)
            {
                return idProperty.GetValue(loadObj) as string;
            }
            return loadObj.ToString();
        }

        /// <summary>
        /// Formats a GameObject's hierarchy path as "Root/Child/Grandchild/...".
        /// Used by the verbose passenger-stop dump so we can verify the actual
        /// scene-graph location of each stop against the assumed
        /// Area > Industry > IndustryComponent > PassengerStop layout.
        /// </summary>
        private static string FormatGameObjectPath(UnityEngine.Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var segments = new List<string>();
            var cursor = transform;
            var depth = 0;
            while (cursor != null && depth < 16)
            {
                segments.Add(cursor.name);
                cursor = cursor.parent;
                depth++;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string FormatComponentIds<T>(T[] components) where T : UnityEngine.Component
        {
            if (components == null || components.Length == 0) return string.Empty;
            var parts = new List<string>(components.Length);
            foreach (var component in components)
            {
                if (component == null) continue;
                var idProp = component.GetType().GetProperty("identifier")?.GetValue(component) as string
                    ?? component.GetType().GetField("identifier")?.GetValue(component) as string
                    ?? component.name;
                parts.Add(idProp ?? "<null>");
            }
            return string.Join(",", parts);
        }

        private static string FormatFeatureIds(MapFeature[] features)
        {
            if (features == null || features.Length == 0) return string.Empty;
            var parts = new List<string>(features.Length);
            foreach (var feature in features)
            {
                parts.Add(feature?.identifier ?? "<null>");
            }
            return string.Join(",", parts);
        }

        private static string FormatSectionIds(Section[] sections)
        {
            if (sections == null || sections.Length == 0) return string.Empty;
            var parts = new List<string>(sections.Length);
            foreach (var section in sections)
            {
                parts.Add(section?.identifier ?? "<null>");
            }
            return string.Join(",", parts);
        }

        private static void ApplyProgressionDefinition(Progression progression, FuseProgression definition, string packageId)
        {
            if (progression.mapFeatureManager == null)
            {
                progression.mapFeatureManager = MapFeatureManager.Shared;
            }

            var sectionDefinitions = definition.Sections ?? new Dictionary<string, FuseSection>();
            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null || section.transform.parent != progression.transform)
                {
                    var gameObject = new GameObject(sectionDefinition.Key);
                    gameObject.transform.SetParent(progression.transform, false);
                    section = gameObject.AddComponent<Section>();
                    section.identifier = sectionDefinition.Key;
                }

                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null)
                {
                    throw new InvalidOperationException($"Progression section '{sectionDefinition.Key}' could not be created.");
                }

                ApplySectionDefinition(section, sectionDefinition.Value, packageId);
                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            ProgressionSectionsField?.SetValue(progression, progression.GetComponentsInChildren<Section>());
        }

        private static void ApplySectionDefinition(Section section, FuseSection definition, string packageId)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            section.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName;
            section.description = definition.Description ?? string.Empty;
            var sectionUnlockFeature = EnsureSectionUnlockFeature(section, definition);

            // Section list fields use the same patch semantics as
            // MapFeature fields (see FuseStringPatch docs). If the JSON
            // is an array, replace; if it's an object dict, merge per-id;
            // if it's omitted, keep the runtime value untouched.
            var prereqPatch = definition.PrerequisiteSectionIds ?? definition.PrerequisiteSections;
            if (prereqPatch != null && prereqPatch.HasValue)
            {
                var existingIds = (section.prerequisiteSections ?? Array.Empty<Section>())
                    .Where(s => s != null)
                    .Select(s => s.identifier);
                section.prerequisiteSections = ResolveSections(prereqPatch.ApplyTo(existingIds));
            }

            if (definition.EnableFeaturesOnUnlock != null && definition.EnableFeaturesOnUnlock.HasValue)
            {
                var existingIds = (section.enableFeaturesOnUnlock ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.enableFeaturesOnUnlock = ResolveMapFeatures(definition.EnableFeaturesOnUnlock.ApplyTo(existingIds));
            }

            if (definition.EnableFeaturesOnAvailable != null && definition.EnableFeaturesOnAvailable.HasValue)
            {
                var existingIds = (section.enableFeaturesOnAvailable ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.enableFeaturesOnAvailable = ResolveMapFeatures(definition.EnableFeaturesOnAvailable.ApplyTo(existingIds));
            }

            if (definition.DisableFeaturesOnUnlock != null && definition.DisableFeaturesOnUnlock.HasValue)
            {
                var existingIds = (section.disableFeaturesOnUnlock ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.disableFeaturesOnUnlock = ResolveMapFeatures(definition.DisableFeaturesOnUnlock.ApplyTo(existingIds));
            }

            section.deliveryPhases = (definition.DeliveryPhases ?? Array.Empty<FuseDeliveryPhase>()).Select(CreateDeliveryPhase).ToArray();
            ApplyInterchangeTransfers(section, definition.InterchangeTransfers, packageId);

            // Null-safety: a freshly-created Section MonoBehaviour starts with
            // every array field null. If the mod's patch leaves a field as
            // "no change" (definition.X null OR HasValue false), our
            // conditional assignment above leaves the runtime field null
            // too. The game's Progression.PrerequisitesMet calls
            // section.prerequisiteSections.All(...) without a null check, so
            // a null array crashes Configure with ArgumentNullException.
            // Same exposure for every other Section[] / MapFeature[] field
            // the game iterates. Default them to empty arrays so the game's
            // existing null-naive .All / foreach calls survive.
            section.prerequisiteSections = section.prerequisiteSections ?? Array.Empty<Section>();
            section.enableFeaturesOnUnlock = section.enableFeaturesOnUnlock ?? Array.Empty<MapFeature>();
            section.enableFeaturesOnAvailable = section.enableFeaturesOnAvailable ?? Array.Empty<MapFeature>();
            section.disableFeaturesOnUnlock = section.disableFeaturesOnUnlock ?? Array.Empty<MapFeature>();

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    "FUSE progression section applied " +
                    $"id='{section.identifier}' display='{section.displayName}' package='{packageId ?? string.Empty}' " +
                    $"prereqSectionIds=[{FormatPatchInputs(prereqPatch)}] " +
                    $"prereqSectionsResolvedCount={section.prerequisiteSections?.Length ?? 0} " +
                    $"prereqSectionsResolved=[{FormatSectionIds(section.prerequisiteSections)}] " +
                    $"enableFeaturesOnUnlock=[{FormatFeatureIds(section.enableFeaturesOnUnlock)}] " +
                    $"enableFeaturesOnAvailable=[{FormatFeatureIds(section.enableFeaturesOnAvailable)}] " +
                    $"disableFeaturesOnUnlock=[{FormatFeatureIds(section.disableFeaturesOnUnlock)}] " +
                    $"deliveryPhases={section.deliveryPhases?.Length ?? 0} " +
                    $"hasSectionUnlockFeature={(sectionUnlockFeature != null)}.");
            }
        }

        private static void ApplyInterchangeTransfers(Section section, IDictionary<string, string> transfers, string packageId)
        {
            var preserved = (section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
                .Where(transfer => transfer != null && !IsFuseInterchangeTransfer(transfer))
                .ToList();

            foreach (var transfer in section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
            {
                if (transfer == null || !IsFuseInterchangeTransfer(transfer))
                {
                    continue;
                }

                UnityEngine.Object.Destroy(transfer.gameObject);
            }

            var created = new List<InterchangeTransfer>();
            if (transfers != null && transfers.Count > 0)
            {
                foreach (var transfer in transfers)
                {
                    if (string.IsNullOrWhiteSpace(transfer.Key))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank source id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank source id'.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(transfer.Value))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank target id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank target id'.");
                        continue;
                    }

                    var from = ResolveInterchange(transfer.Key);
                    var to = ResolveInterchange(transfer.Value);
                    if (from == null || to == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "one or both interchange components were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='one or both interchange components were not found'.");
                        continue;
                    }

                    if (InterchangeTransferFromField == null || InterchangeTransferToField == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "base game fields were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='base game fields were not found'.");
                        continue;
                    }

                    var gameObject = new GameObject(FuseInterchangeTransferPrefix + SanitizeObjectName(transfer.Key));
                    gameObject.transform.SetParent(section.transform, false);
                    var component = gameObject.AddComponent<InterchangeTransfer>();
                    InterchangeTransferFromField.SetValue(component, from);
                    InterchangeTransferToField.SetValue(component, to);
                    created.Add(component);
                    FuseLog.Info($"FUSE progression section '{section.identifier}' added interchange transfer '{transfer.Key}' -> '{transfer.Value}'.");
                }
            }

            RefreshSectionInterchangeTransfers(section, preserved.Concat(created).ToArray());
        }

        private static bool IsFuseInterchangeTransfer(InterchangeTransfer transfer)
        {
            return transfer != null &&
                   transfer.gameObject != null &&
                   transfer.gameObject.name.StartsWith(FuseInterchangeTransferPrefix, StringComparison.Ordinal);
        }

        private static void RefreshSectionInterchangeTransfers(Section section, InterchangeTransfer[] transfers)
        {
            if (section == null)
            {
                return;
            }

            SectionInterchangeTransfersField?.SetValue(section, transfers ?? Array.Empty<InterchangeTransfer>());
        }

        private static string SanitizeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '_').ToArray();
            return new string(chars);
        }

        private static MapFeature EnsureSectionUnlockFeature(Section section, FuseSection definition)
        {
            if (section == null || definition == null || !HasSectionUnlockFeaturePayload(definition))
            {
                return null;
            }

            var featureId = GetSectionUnlockFeatureId(section.identifier);
            var featureDefinition = new FuseMapFeature
            {
                DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName,
                Description = definition.Description,
                InitiallyEnabled = false,
                TrackGroupsEnableOnUnlock = definition.TrackGroupsEnableOnUnlock,
                TrackGroupsAvailableOnUnlock = definition.TrackGroupsAvailableOnUnlock,
                AreasEnableOnUnlock = definition.AreasEnableOnUnlock,
                GameObjectsEnableOnUnlock = definition.GameObjectsEnableOnUnlock,
                UnlockIncludeIndustries = definition.UnlockIncludeIndustries,
                UnlockExcludeIndustries = definition.UnlockExcludeIndustries,
                UnlockIncludeIndustryComponents = definition.UnlockIncludeIndustryComponents
            };

            var existing = GetMapFeature(featureId);
            if (existing != null)
            {
                ApplyMapFeatureDefinition(existing, featureDefinition);
                FuseMapFeatureRuntimeIndex.Instance.Set(featureId, existing);
                if (MapFeatureManager.Shared != null)
                {
                    RefreshMapFeatureManager(MapFeatureManager.Shared);
                }

                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, featureId, featureDefinition);
                FuseLog.Info($"FUSE refreshed progression section unlock feature '{featureId}' for section '{section.identifier}'.");
                return existing;
            }

            var created = AddMapFeature(featureId, featureDefinition);
            FuseLog.Info($"FUSE created progression section unlock feature '{featureId}' for section '{section.identifier}'.");
            return created;
        }

        private static bool HasSectionUnlockFeaturePayload(FuseSection definition)
        {
            // A section payload is "interesting" if any of the unlock-fan-out
            // patches authors anything — either an explicit replacement set
            // or a non-empty merge dict. EffectiveAdditions surfaces the
            // truthy-keys-only view, which is the right "is there anything
            // here to apply?" probe for the synthesized section-unlock
            // feature.
            return HasAny(definition.TrackGroupsEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.TrackGroupsAvailableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.AreasEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.GameObjectsEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.UnlockIncludeIndustries?.EffectiveAdditions) ||
                   HasAny(definition.UnlockExcludeIndustries?.EffectiveAdditions) ||
                   HasAny(definition.UnlockIncludeIndustryComponents?.EffectiveAdditions);
        }

        private static string GetSectionUnlockFeatureId(string sectionId)
        {
            return "fuse.progression.section." + (sectionId ?? string.Empty) + ".unlock";
        }

        private static MapFeature[] AppendFeature(MapFeature[] features, MapFeature feature)
        {
            if (feature == null)
            {
                return features ?? Array.Empty<MapFeature>();
            }

            return (features ?? Array.Empty<MapFeature>())
                .Concat(new[] { feature })
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.identifier ?? candidate.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static Section.DeliveryPhase CreateDeliveryPhase(FuseDeliveryPhase definition)
        {
            var deliveries = definition.Deliveries ?? Array.Empty<FuseDelivery>();
            var phase = new Section.DeliveryPhase
            {
                cost = definition.Cost,
                deliveries = deliveries.Select(CreateDelivery).ToArray()
            };

            if (deliveries.Length > 0)
            {
                phase.industryComponent = !string.IsNullOrWhiteSpace(definition.IndustryComponentId)
                    ? ResolveIndustryComponent(definition.IndustryComponentId)
                    : ResolveDeliveryPhaseIndustryComponent(definition);
            }

            return phase;
        }

        private static Section.Delivery CreateDelivery(FuseDelivery definition)
        {
            return new Section.Delivery
            {
                carTypeFilter = new CarTypeFilter(definition.CarTypeFilter ?? string.Empty),
                count = definition.Count,
                load = ResolveLoad(definition.LoadId),
                direction = ParseDeliveryDirection(definition.Direction)
            };
        }

        private static ProgressionIndustryComponent ResolveDeliveryPhaseIndustryComponent(FuseDeliveryPhase definition)
        {
            var destinationIds = (definition.Deliveries ?? Array.Empty<FuseDelivery>())
                .Select(delivery => delivery?.DestinationIndustryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (destinationIds.Length != 1)
            {
                throw new InvalidOperationException("Progression delivery phases with deliveries require industryComponentId, or a single destinationIndustryId that resolves to one ProgressionIndustryComponent.");
            }

            var industry = ResolveIndustry(destinationIds[0]);
            if (industry == null)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' was not found.");
            }

            var candidates = industry.GetComponentsInChildren<ProgressionIndustryComponent>(true)
                .Where(component => component != null)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has no ProgressionIndustryComponent. Set industryComponentId explicitly.");
            }

            throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has {candidates.Length} ProgressionIndustryComponent entries. Set industryComponentId explicitly.");
        }

        private static Section.Delivery.Direction ParseDeliveryDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return Section.Delivery.Direction.LoadToIndustry;
            }

            switch (direction.Trim().ToLowerInvariant())
            {
                case "1":
                case "loadfromindustry":
                case "fromindustry":
                case "from":
                case "export":
                    return Section.Delivery.Direction.LoadFromIndustry;
                case "0":
                case "loadtoindustry":
                case "toindustry":
                case "to":
                case "import":
                    return Section.Delivery.Direction.LoadToIndustry;
                default:
                    return Section.Delivery.Direction.LoadToIndustry;
            }
        }

        private static Section[] ResolveSections(string[] ids)
        {
            return ResolveObjects(ids, GetSection, "section");
        }

        private static MapFeature[] ResolveMapFeatures(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<MapFeature>();
            }

            var resolved = new List<MapFeature>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var feature = GetMapFeature(id);
                if (feature == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved map feature reference '{id}'.");
                    continue;
                }

                resolved.Add(feature);
            }

            return resolved
                .GroupBy(feature => feature.identifier ?? feature.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static Area[] ResolveAreas(string[] ids)
        {
            return ResolveObjects(ids, ResolveArea, "area");
        }

        private static Industry[] ResolveIndustries(string[] ids)
        {
            return ResolveObjects(ids, ResolveIndustry, "industry");
        }

        private static IndustryComponent[] ResolveIndustryComponents(string[] ids)
        {
            return ResolveObjects(ids, ResolveAnyIndustryComponent, "industry component");
        }

        private static GameObject[] ResolveGameObjects(string[] paths)
        {
            return ResolveOptionalObjects(paths, ResolveGameObject, "game object");
        }

        private static T[] ResolveObjects<T>(string[] ids, Func<string, T> resolver, string label)
            where T : class
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<T>();
            }

            var resolved = new List<T>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var value = resolver(id);
                if (value == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved {label} reference '{id}'.");
                    continue;
                }

                resolved.Add(value);
            }

            return resolved.ToArray();
        }

        private static T[] ResolveOptionalObjects<T>(string[] ids, Func<string, T> resolver, string label)
            where T : class
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<T>();
            }

            var resolved = new List<T>();
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                var value = resolver(id);
                if (value == null)
                {
                    FuseLog.Warning($"FUSE progression skipped unresolved optional {label} reference '{id}'.");
                    continue;
                }

                resolved.Add(value);
            }

            return resolved.ToArray();
        }

        private static Area ResolveArea(string id)
        {
            var area = TrackAPI.GetArea(id);
            if (area != null)
            {
                return area;
            }

            return UnityEngine.Object.FindObjectsOfType<Area>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        private static Industry ResolveIndustry(string id)
        {
            var industry = IndustryAPI.GetIndustry(id);
            if (industry != null)
            {
                return industry;
            }

            return UnityEngine.Object.FindObjectsOfType<Industry>(true).FirstOrDefault(candidate =>
                candidate != null &&
                (string.Equals(candidate.identifier, id, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.name, id, StringComparison.OrdinalIgnoreCase)));
        }

        private static IndustryComponent ResolveAnyIndustryComponent(string id)
        {
            if (FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return cached as IndustryComponent;
            }

            return UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true)
                .FirstOrDefault(component => ComponentMatchesId(component, id));
        }

        private static Interchange ResolveInterchange(string id)
        {
            var cached = ResolveAnyIndustryComponent(id) as Interchange;
            if (cached != null)
            {
                return cached;
            }

            var sceneMatch = UnityEngine.Object.FindObjectsOfType<Interchange>(true)
                .FirstOrDefault(component => ComponentMatchesId(component, id));
            if (sceneMatch != null)
            {
                return sceneMatch;
            }

            var industryMatch = ResolveInterchangeFromLegacyIndustryComponentId(id);
            if (industryMatch != null)
            {
                return industryMatch;
            }

            return null;
        }

        private static Interchange ResolveInterchangeFromLegacyIndustryComponentId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var dot = id.LastIndexOf('.');
            if (dot <= 0 || dot >= id.Length - 1)
            {
                return null;
            }

            var industryId = id.Substring(0, dot);
            var legacySubId = id.Substring(dot + 1);
            var industry = ResolveIndustry(industryId);
            if (industry == null)
            {
                return null;
            }

            var interchanges = industry.GetComponentsInChildren<Interchange>(true)
                .Where(component => component != null)
                .ToArray();
            if (interchanges.Length == 0)
            {
                return null;
            }

            var exactSubId = interchanges.FirstOrDefault(component =>
                string.Equals(component.subIdentifier, legacySubId, StringComparison.OrdinalIgnoreCase));
            if (exactSubId != null)
            {
                return exactSubId;
            }

            var canonicalSubId = interchanges.FirstOrDefault(component =>
                string.Equals(component.subIdentifier, "interchange", StringComparison.OrdinalIgnoreCase));
            if (canonicalSubId != null &&
                (string.Equals(legacySubId, "t1", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(legacySubId, "interchange", StringComparison.OrdinalIgnoreCase)))
            {
                FuseLog.Info($"FUSE resolved legacy interchange transfer id '{id}' to '{industry.identifier}.{canonicalSubId.subIdentifier}'.");
                return canonicalSubId;
            }

            if (interchanges.Length == 1)
            {
                FuseLog.Info($"FUSE resolved legacy interchange transfer id '{id}' to only interchange component '{industry.identifier}.{interchanges[0].subIdentifier}'.");
                return interchanges[0];
            }

            return null;
        }

        private static GameObject ResolveGameObject(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var marker = path.IndexOf("://", StringComparison.Ordinal);
            if (marker >= 0)
            {
                var scheme = path.Substring(0, marker);
                var value = path.Substring(marker + 3);
                if (string.Equals(scheme, "scenery", StringComparison.OrdinalIgnoreCase))
                {
                    var scenery = SceneryAPI.GetScenery(value);
                    if (scenery != null)
                    {
                        return scenery.gameObject;
                    }

                    return ResolveGameObjectPath(value);
                }

                if (string.Equals(scheme, "sceneClone", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(scheme, "sceneclone", StringComparison.OrdinalIgnoreCase))
                {
                    return SceneCloneAPI.GetSceneClone(value) ?? ResolveGameObjectPath(value);
                }

                if (string.Equals(scheme, "path", StringComparison.OrdinalIgnoreCase))
                {
                    const string scenePrefix = "scene/";
                    if (value.StartsWith(scenePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        value = value.Substring(scenePrefix.Length);
                    }

                    return ResolveGameObjectPath(value) ?? ResolveAuthoredWorldObject(value);
                }

                if (string.Equals(scheme, "scene", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveGameObjectPath(value) ?? ResolveAuthoredWorldObject(value);
                }
            }

            return ResolveGameObjectPath(path) ?? ResolveAuthoredWorldObject(path);
        }

        private static GameObject ResolveAuthoredWorldObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var id = value.Trim();
            var scenery = SceneryAPI.GetScenery(id) ?? SceneryAPI.GetScenery(GetPathLeaf(id));
            if (scenery != null)
            {
                return scenery.gameObject;
            }

            return SceneCloneAPI.GetSceneClone(id) ?? SceneCloneAPI.GetSceneClone(GetPathLeaf(id));
        }

        private static string GetPathLeaf(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var normalized = value.Trim().Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash >= 0 && slash < normalized.Length - 1
                ? normalized.Substring(slash + 1)
                : normalized;
        }

        private static GameObject ResolveGameObjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var direct = GameObject.Find(path);
            if (direct != null)
            {
                return direct;
            }

            var resolved = FusePrefabResolver.ResolveScenePath(path);
            if (resolved != null)
            {
                return resolved;
            }

            return UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(transform =>
                    string.Equals(transform.name, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(GetScenePath(transform), path, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        private static bool ComponentMatchesId(IndustryComponent component, string id)
        {
            if (component == null || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            try
            {
                if (string.Equals(component.Identifier, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (LooseIdEquals(component.Identifier, id))
                {
                    return true;
                }
            }
            catch
            {
                // Some freshly cloned components have incomplete parent identity.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            if (industry == null ||
                string.IsNullOrWhiteSpace(industry.identifier) ||
                string.IsNullOrWhiteSpace(component.subIdentifier))
            {
                return false;
            }

            var fullId = industry.identifier + "." + component.subIdentifier;
            return string.Equals(fullId, id, StringComparison.OrdinalIgnoreCase) ||
                   LooseIdEquals(fullId, id);
        }

        private static bool LooseIdEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(NormalizeLooseId(left), NormalizeLooseId(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLooseId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return string.Empty;
            }

            return new string(id
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string[] ToSectionIds(IEnumerable<Section> sections)
        {
            return sections?.Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier))
                .Select(section => section.identifier)
                .ToArray();
        }

        private static string[] ToFeatureIds(IEnumerable<MapFeature> features)
        {
            return features?.Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .Select(feature => feature.identifier)
                .ToArray();
        }

        private static string[] ToAreaIds(IEnumerable<Area> areas)
        {
            return areas?.Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier))
                .Select(area => area.identifier)
                .ToArray();
        }

        private static string[] ToIndustryIds(IEnumerable<Industry> industries)
        {
            return industries?.Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                .Select(industry => industry.identifier)
                .ToArray();
        }

        private static string[] ToIndustryComponentIds(IEnumerable<IndustryComponent> components)
        {
            return components?.Where(component => component != null)
                .Select(SafeIndustryComponentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }

        private static string[] ToGameObjectPaths(IEnumerable<GameObject> gameObjects)
        {
            return gameObjects?.Where(gameObject => gameObject != null)
                .Select(gameObject => GetScenePath(gameObject.transform))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        private static Dictionary<string, string> ToInterchangeTransfers(IEnumerable<InterchangeTransfer> transfers)
        {
            if (transfers == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var transfer in transfers.Where(transfer => transfer != null))
            {
                var from = InterchangeTransferFromField?.GetValue(transfer) as Interchange;
                if (from == null)
                {
                    continue;
                }

                var fromId = SafeIndustryComponentId(from);
                if (string.IsNullOrWhiteSpace(fromId))
                {
                    continue;
                }

                var to = InterchangeTransferToField?.GetValue(transfer) as Interchange;
                result[fromId] = to != null ? SafeIndustryComponentId(to) : null;
            }

            return result.Count > 0 ? result : null;
        }

        private static FuseDeliveryPhase[] ToDeliveryPhases(IEnumerable<Section.DeliveryPhase> phases)
        {
            return phases?.Where(phase => phase != null)
                .Select(phase => new FuseDeliveryPhase
                {
                    Cost = phase.cost,
                    IndustryComponentId = phase.industryComponent != null ? phase.industryComponent.Identifier : null,
                    Deliveries = ToDeliveries(phase.deliveries)
                })
                .ToArray();
        }

        private static FuseDelivery[] ToDeliveries(IEnumerable<Section.Delivery> deliveries)
        {
            return deliveries?.Where(delivery => delivery != null)
                .Select(delivery => new FuseDelivery
                {
                    CarTypeFilter = delivery.carTypeFilter.ToString(),
                    LoadId = delivery.load != null ? delivery.load.id : null,
                    Count = delivery.count,
                    Direction = delivery.direction == Section.Delivery.Direction.LoadFromIndustry ? "loadFromIndustry" : "loadToIndustry"
                })
                .ToArray();
        }

        private static Section GetSection(string id)
        {
            if (FuseSectionRuntimeIndex.Instance.TryGetValue(id, out var cached))
            {
                return (Section)cached;
            }

            return !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Section>(true).FirstOrDefault(section =>
                    string.Equals(section.identifier, id, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private static ProgressionIndustryComponent ResolveIndustryComponent(string id)
        {
            if (!FuseIndustryComponentRuntimeIndex.Instance.TryGetValue(id, out var cached) || cached == null)
            {
                cached = UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true)
                    .FirstOrDefault(component => ComponentMatchesId(component, id));
            }

            var component = cached as ProgressionIndustryComponent;
            if (component == null)
            {
                component = ResolveProgressionIndustryComponentFromIndustry(id);
            }

            if (component == null)
            {
                throw new InvalidOperationException($"Progression industry component '{id}' was not found.");
            }

            FuseIndustryComponentRuntimeIndex.Instance.Set(id, component);
            return component;
        }

        private static ProgressionIndustryComponent ResolveProgressionIndustryComponentFromIndustry(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var splitIndex = id.LastIndexOf('.');
            if (splitIndex <= 0 || splitIndex >= id.Length - 1)
            {
                return null;
            }

            var industryId = id.Substring(0, splitIndex);
            var componentId = id.Substring(splitIndex + 1);
            var industry = IndustryAPI.GetIndustry(industryId);
            if (industry == null)
            {
                return null;
            }

            return industry.GetComponentsInChildren<ProgressionIndustryComponent>(true)
                .FirstOrDefault(component =>
                    component != null &&
                    (string.Equals(component.subIdentifier, componentId, StringComparison.OrdinalIgnoreCase) ||
                     LooseIdEquals(component.subIdentifier, componentId) ||
                     ComponentMatchesId(component, id)));
        }

        private static Load ResolveLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var load = LoadAPI.GetLoad(loadId) ??
                       LoadAPI.GetOrCreatePlaceholderLoad(loadId, "progression delivery references a load id that is not defined by any loaded package");
            if (load == null)
            {
                throw new InvalidOperationException($"Load '{loadId}' was not found.");
            }

            FuseLoadRuntimeIndex.Instance.Set(load.id, load);
            return load;
        }

        private static string[] PreferExplicit(string[] explicitValues, string[] fallbackValues)
        {
            return HasAny(explicitValues) ? explicitValues : (fallbackValues ?? Array.Empty<string>());
        }

        private static bool HasAny(string[] values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string SafeIndustryComponentId(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(component.Identifier))
                {
                    return component.Identifier;
                }
            }
            catch
            {
                // Incomplete cloned components can throw while their parent industry identity is being rebuilt.
            }

            var industry = component.GetComponentInParent<Industry>(true);
            return industry != null &&
                   !string.IsNullOrWhiteSpace(industry.identifier) &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier)
                ? industry.identifier + "." + component.subIdentifier
                : null;
        }

        private static string GetScenePath(Transform transform)
        {
            if (transform == null)
            {
                return null;
            }

            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static MapFeature RequireMapFeature(string id)
        {
            var feature = GetMapFeature(id);
            if (feature == null)
            {
                throw new InvalidOperationException($"Map feature '{id}' was not found.");
            }

            return feature;
        }

        private static Progression RequireProgression(string id)
        {
            var progression = GetProgression(id);
            if (progression == null)
            {
                throw new InvalidOperationException($"Progression '{id}' was not found.");
            }

            return progression;
        }

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
                    // <see cref="FUSE.Data.FuseStringPatch"/>'s merge
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

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
