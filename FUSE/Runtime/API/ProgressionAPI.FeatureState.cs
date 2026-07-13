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

        // Track whether the game's Progression.Configure has run at least once.
        // Set by the Harmony postfix in FuseProgressionConfigureHookPatch. Until
        // this is true, StateManager.IsSandbox is unreliable (the save's GameMode
        // hasn't been deserialized; it returns its default Sandbox=true). Any
        // FUSE code that depends on IsSandbox — and the game internals we invoke
        // via reflection that depend on it — must be deferred.
        private static volatile bool _gameProgressionConfigured;

        // Track whether the load settled WITHOUT a configured progression. The
        // game decides this once per load, inside ProgressionManager's restore
        // handler: sandbox saves discard their progression blob ("Game is
        // sandbox but has progression ... Ignoring.") and company saves whose
        // progression id resolves to nothing both leave Progression unconfigured
        // — so Progression.Configure NEVER fires and a refresh parked on it
        // would wait forever (observed in the field: a whole sandbox session
        // with FUSE's pre-enabled track groups never re-gated). Set by the
        // Harmony postfix in FuseProgressionManagerNoProgressionHookPatch at the
        // exact point GameMode has become authoritative — the same guarantee
        // Configure gives on company loads.
        private static volatile bool _gameProgressionSettledWithoutProgression;

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
                // A configured progression supersedes any earlier no-progression
                // observation for this load.
                _gameProgressionSettledWithoutProgression = false;
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
        /// Called by the Harmony postfix on the game's per-load progression
        /// restore handler when it finished WITHOUT configuring a Progression
        /// (sandbox save, or a progression id that resolved to nothing). At
        /// that point the save's GameMode has been deserialized — the same
        /// trust guarantee <c>Progression.Configure</c> gives on company loads
        /// — but <c>Configure</c> itself will never fire this load, so a
        /// refresh parked on it must be replayed from here instead. The replay
        /// runs the reduced <see cref="ProgressionRefreshProfile.NoProgression"/>
        /// profile.
        /// </summary>
        internal static void NotifyGameProgressionSettledWithoutProgression()
        {
            string pendingReason;
            lock (_pendingRefreshLock)
            {
                if (_gameProgressionConfigured)
                {
                    // Configure already ran this load (its postfix fires from
                    // inside the same restore handler, before this one) — the
                    // full-profile notification has this covered.
                    return;
                }

                _gameProgressionSettledWithoutProgression = true;
                pendingReason = _pendingRefreshReason;
                _pendingRefreshReason = null;
            }

            var sandbox = StateManager.IsSandbox;
            var gameMode = StateManager.Shared?.GameMode.ToString() ?? "<null>";
            FuseLog.Info(
                $"FUSE progression settle observed (game finished progression restore with NO configured progression) " +
                $"IsSandbox={sandbox} GameMode={gameMode} hadPendingRefresh={(pendingReason != null)}.");

            if (pendingReason != null)
            {
                RefreshRuntimeStateAfterApply($"deferred from no-progression settle: {pendingReason}");
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
                _gameProgressionSettledWithoutProgression = false;
                // Drop any deferred reason — its requester is gone with the
                // previous load. The next load will produce its own refresh
                // request via FuseModLoader.
                _pendingRefreshReason = null;
            }

            FuseLog.Info(
                "FUSE progression Unconfigure observed (game progression torn down); " +
                "configured-flag cleared so the next load's pre-Configure window is detected fresh.");
        }

        /// <summary>
        /// Called from FuseLifecycle.OnMapWillUnload. Sandbox sessions never
        /// configure a Progression, so <c>Progression.Unconfigure</c> — and
        /// with it <see cref="NotifyGameProgressionUnconfigured"/> — never
        /// fires for them; without this unconditional reset a sandbox
        /// session's settled-flag would leak into the next load and let its
        /// staged refresh run inside the stale-IsSandbox window (the exact
        /// corruption class the deferral exists to prevent). Also drops the
        /// transiently pre-enabled track-group records: they name groups of
        /// the map being torn down, and if the session's refresh never drained
        /// them they must not leak into the next map's first refresh (which
        /// could flip available=false on same-named groups there).
        /// </summary>
        internal static void NotifyMapUnloading()
        {
            lock (_pendingRefreshLock)
            {
                _gameProgressionConfigured = false;
                _gameProgressionSettledWithoutProgression = false;
                _pendingRefreshReason = null;
            }

            int droppedTransient;
            lock (_transientlyPreEnabledLock)
            {
                droppedTransient = _transientlyPreEnabledTrackGroups.Count;
                _transientlyPreEnabledTrackGroups.Clear();
            }

            if (droppedTransient > 0)
            {
                FuseLog.Info(
                    $"FUSE progression map-unload reset dropped {droppedTransient} undrained " +
                    "transiently pre-enabled track-group record(s) from the unloading map.");
            }
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
            var profile = ProgressionRefreshProfiles.Determine(
                _gameProgressionConfigured,
                _gameProgressionSettledWithoutProgression);
            FuseLog.Info(
                $"FUSE progression refresh entry reason='{reason ?? "unspecified"}' " +
                $"IsSandbox={sandboxAtEntry} GameMode={gameModeAtEntry} configured={_gameProgressionConfigured} profile={profile}.");

            if (profile == ProgressionRefreshProfile.Deferred)
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
                // Park the request and let the Configure postfix — or, for
                // loads that never configure a progression, the
                // no-progression settle postfix — re-fire it once GameMode is
                // reliable.
                lock (_pendingRefreshLock)
                {
                    _pendingRefreshReason = reason;
                }
                FuseLog.Info(
                    $"FUSE progression refresh deferred until Progression.Configure or the no-progression settle point " +
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

            if (profile == ProgressionRefreshProfile.NoProgression)
            {
                // This session has no configured Progression (sandbox save, or
                // a save whose progression id resolved to nothing). The game's
                // own initial feature pass already applied explicit save
                // entries plus sandbox defaults, and there is no Progression to
                // derive section state from — so replaying
                // HandleFeatureEnablesChanged or re-disabling feature-claimed
                // track groups here would LOCK mod content that sandbox
                // convention keeps open (synthesized section-unlock features
                // are authored defaultEnableInSandbox=false). Run only the
                // mode-independent maintenance steps; the payoff is
                // orphan-group finalization, which never ran at all in these
                // sessions before this profile existed.
                RefreshMapFeatureManager(manager);
                RefreshProgressionManager();
                var initializedNoProgression = InitializeMissingMapFeatureStates(manager);
                var inferredNoProgression = InferMapFeatureIndustryIncludes(manager, reason);
                var reboundNoProgression = ReResolveMapFeatureLiveReferences(manager, reason);
                var finalisedNoProgression = RevokeTransientlyPreEnabledOrphanGroups(manager, reason);
                FuseLog.Info(
                    $"FUSE refreshed progression runtime state package='<all>' operation='refresh progression state' " +
                    $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' profile='no-progression' " +
                    $"initializedFeatureStates={initializedNoProgression} " +
                    $"inferredIndustryIncludes={inferredNoProgression} reboundLiveReferences={reboundNoProgression} " +
                    $"finalisedOrphanTrackGroups={finalisedNoProgression} " +
                    "(feature-state replay and track-group restore intentionally skipped: no progression is configured).");
                return;
            }

            RefreshMapFeatureManager(manager);
            RefreshProgressionManager();

            var invokedCurrentProgressionBeforeFeatureState = TryRefreshCurrentProgression(reason, "before map feature replay");

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
            var inferredIndustryIncludes = InferMapFeatureIndustryIncludes(manager, reason);
            // Re-bind each feature's industry/component gate arrays to live instances
            // BEFORE re-applying the feature state, so the gate toggles
            // ProgressionDisabled on the live industry rather than a destroyed
            // reference left behind by a Remove+Add re-apply.
            var reboundLiveReferences = ReResolveMapFeatureLiveReferences(manager, reason);
            var forcedFeatureState = ForceApplyCurrentMapFeatureState(manager, reason);
            var invokedCurrentProgressionAfterFeatureState = TryRefreshCurrentProgression(reason, "after map feature replay");
            var restoredTrackGroups = RestoreDisabledTrackGroups(manager, reason);
            var finalisedOrphans = RevokeTransientlyPreEnabledOrphanGroups(manager, reason);
            FuseLog.Info(
                $"FUSE refreshed progression runtime state package='<all>' operation='refresh progression state' " +
                $"kind='map features' id='<all>' reason='{reason ?? "unspecified"}' " +
                $"currentProgressionRefreshedBeforeFeatureState={invokedCurrentProgressionBeforeFeatureState} " +
                $"currentProgressionRefreshedAfterFeatureState={invokedCurrentProgressionAfterFeatureState} " +
                $"initializedFeatureStates={initialized} " +
                $"inferredIndustryIncludes={inferredIndustryIncludes} reboundLiveReferences={reboundLiveReferences} forcedFeatureState={forcedFeatureState} " +
                $"restoredDisabledTrackGroups={restoredTrackGroups} " +
                $"finalisedOrphanTrackGroups={finalisedOrphans}.");

            if (FuseSettings.VerboseApplyReportDetails)
            {
                DumpProgressionStateForDiagnostics(manager, reason);
            }
        }

        /// <summary>
        /// Whether the given ProgressionManager holds a configured current
        /// Progression. Used by the no-progression settle postfix to tell the
        /// two restore outcomes apart: on company loads the manager assigns and
        /// configures its current progression inside the restore handler, so a
        /// null current after it returns means this load runs progression-less.
        /// </summary>
        internal static bool HasConfiguredCurrentProgression(ProgressionManager manager)
        {
            try
            {
                return manager != null &&
                       ManagerCurrentProgressionField?.GetValue(manager) is Progression;
            }
            catch
            {
                // Reflection failure: report the pessimistic answer — the
                // settle notify path double-checks against the configured-flag
                // anyway, so a false negative cannot demote a configured load.
                return false;
            }
        }

        private static bool TryRefreshCurrentProgression(string reason, string stage)
        {
            try
            {
                var progressionManager = UnityEngine.Object.FindObjectOfType<ProgressionManager>();
                var current = progressionManager != null
                    ? ManagerCurrentProgressionField?.GetValue(progressionManager) as Progression
                    : null;
                if (current != null && ProgressionUpdateSectionStatesMethod != null)
                {
                    ProgressionUpdateSectionStatesMethod.Invoke(current, Array.Empty<object>());
                    return true;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression refresh package='<all>' operation='refresh progression state' " +
                    $"kind='progression' id='<current>' stage='{stage ?? "unspecified"}' " +
                    $"reason='{reason ?? "unspecified"}' message='{ex.Message}'.");
            }

            return false;
        }
    }
}
