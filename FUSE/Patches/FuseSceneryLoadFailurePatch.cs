using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Helpers;
using UI.Common;

namespace FUSE.Patches
{
    /// <summary>
    /// Bubbles permanently-failing scenery asset loads up to the user. The game's
    /// scenery loader faults the returned task when a pack's bundle does not
    /// contain an asset its catalog/definitions declare (or the bundle itself
    /// cannot load), logs to Player.log, and then retries the same load on every
    /// culling-band transition — forever, invisibly, with a hitch per retry
    /// cluster. The user never learns which pack is broken.
    ///
    /// This postfix watches every scenery load task and, on the first fault per
    /// asset identifier, records a <see cref="FuseLoadReport"/> entry (visible in
    /// FUSE Health → Issues, the main-menu status panel, and /fuse.report) and
    /// raises one toast per asset pack. Resolution of pack/owner names uses Unity
    /// APIs, so faults are queued from the task continuation (which may complete
    /// off the main thread) and drained on the main thread by
    /// <see cref="DrainPending"/>. Everything is fail-open: a failure inside this
    /// patch never affects the load path itself.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetManager), nameof(SceneryAssetManager.LoadScenery))]
    internal static class FuseSceneryLoadFailurePatch
    {
        private static readonly ConcurrentQueue<PendingFailure> Pending = new ConcurrentQueue<PendingFailure>();

        // Identifiers already queued/recorded this map, so the game's endless
        // retries of the same broken asset don't grow the queue. Cleared by
        // FuseLifecycle at map-load start (see ResetForNewMap), in lockstep with
        // the load report registry this feeds.
        private static readonly HashSet<string> SeenIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SeenLock = new object();

        // One toast per asset pack, not per asset: a broken pack usually breaks
        // many assets at once and the report carries the full list.
        private static readonly HashSet<string> ToastedPacks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Distinct failing scenery assets recorded since startup (diagnostics).</summary>
        internal static long RecordedFailures => FuseRuntimeGuardCounters.SceneryLoadFailures;

        internal static void ResetForNewMap()
        {
            lock (SeenLock)
            {
                SeenIdentifiers.Clear();
                FailureCounts.Clear();
                QuarantineRequested.Clear();
            }

            ToastedPacks.Clear();
            while (Pending.TryDequeue(out _))
            {
            }

            while (PendingQuarantine.TryDequeue(out _))
            {
            }

            // The bundle audit shares this dedupe lifecycle: its findings land in
            // the same report bucket, so both repopulate together after a reload.
            FuseAssetPackBundleAuditPatch.ResetForNewMap();
        }

        // ---- Broken-scenery quarantine -------------------------------------
        //
        // A scenery asset that cannot load (bundle/catalog mismatch) is retried
        // by the loader on every culling pass near its placements, forever — in
        // the field one missing asset produced 180 retries in three minutes,
        // each costing an exception, three log blocks, and periodic crash-report
        // uploads. Retrying an asset that can NEVER load is pure waste, so once
        // an asset is known-broken — immediately for bundle-audit-confirmed
        // misses, after a few observed failures for everything else — its
        // placements are disabled for the session. Disabling a scenery host is
        // a vanilla-exercised path (progression gating does the same), and the
        // quarantine re-arms per map load, so fixing the pack brings the
        // placements back on the next load.

        // Runtime failures before quarantine. The task watch and the log hook
        // can each count the same retry, so this is a heuristic bound on real
        // retry churn, not an exact retry count.
        private const int RuntimeFailureQuarantineThreshold = 5;

        private static readonly Dictionary<string, int> FailureCounts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> QuarantineRequested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<string> PendingQuarantine = new ConcurrentQueue<string>();

        /// <summary>
        /// Requests session-quarantine of every placement of an asset that is
        /// known to be unloadable (bundle audit) or has failed repeatedly at
        /// runtime. Thread-safe; execution happens on the main thread in
        /// <see cref="DrainPending"/>. Idempotent per identifier per map.
        /// </summary>
        internal static void RequestQuarantine(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            lock (SeenLock)
            {
                if (!QuarantineRequested.Add(identifier))
                {
                    return;
                }
            }

            PendingQuarantine.Enqueue(identifier);
        }

        /// <summary>Quarantine requests queued but not yet executed (test hook).</summary>
        internal static int QuarantinePendingCountForTests => PendingQuarantine.Count;

        private static void ExecuteQuarantine(string identifier)
        {
            var disabled = 0;
            foreach (var instance in UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>())
            {
                if (instance == null ||
                    !string.Equals(instance.identifier, identifier, StringComparison.OrdinalIgnoreCase) ||
                    !instance.gameObject.activeSelf)
                {
                    continue;
                }

                instance.gameObject.SetActive(false);
                disabled++;
                FuseRuntimeGuardCounters.RecordSceneryPlacementQuarantined();
            }

            if (disabled == 0)
            {
                return;
            }

            FuseLog.Warning(
                $"FUSE quarantined {disabled} scenery placement(s) of '{identifier}' for this session: the asset " +
                "cannot load, and the loader would otherwise retry it on every culling pass near each placement. " +
                "Fixing the pack restores the placements on the next map load.");
            FuseLoadReport.RecordNotice(
                $"{disabled} scenery placement(s) of '{identifier}' were disabled for this session because the " +
                "asset cannot load (see the asset load failures section). Fixing the pack restores them.");
        }

        /// <summary>
        /// Entry point for the bundle audit: reports an asset a pack's
        /// Catalog.json declares but its bundle does not contain. Same dedupe,
        /// drain, report bucket, and per-pack toast as runtime load failures —
        /// an asset caught by both the audit and a later load attempt is
        /// recorded once. The pack is known at the call site, so no resolution
        /// fallback is needed.
        /// </summary>
        internal static void ReportCatalogMismatch(string identifier, string packIdentifier, string filename)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            var declaredAs = string.IsNullOrWhiteSpace(filename) ? identifier : filename;
            EnqueueFailure(
                identifier,
                $"declared in the pack's Catalog.json (filename '{declaredAs}') but not present in its bundle — " +
                "the pack needs its bundle rebuilt or the catalog entry (and content referencing it) removed",
                string.IsNullOrWhiteSpace(packIdentifier) ? null : packIdentifier);
        }

        internal static void Postfix(string identifier, Task __result)
        {
            if (__result == null || string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            try
            {
                __result.ContinueWith(
                    task => Enqueue(identifier, task.Exception),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                // Liveness counter (guards line): proves from a pasted report
                // whether this postfix is seeing scenery loads at all, since a
                // third-party loader replacing the load path is otherwise
                // indistinguishable from "no failures happened".
                FuseRuntimeGuardCounters.RecordSceneryLoadWatchAttached();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure watch could not attach", ex);
            }
        }

        private static void Enqueue(string identifier, AggregateException exception)
        {
            var message = exception != null
                ? exception.GetBaseException().Message
                : "asset load failed";
            EnqueueFailure(identifier, message, packIdentifier: null);
        }

        private static void EnqueueFailure(string identifier, string message, string packIdentifier)
        {
            try
            {
                var quarantine = false;
                lock (SeenLock)
                {
                    // Count every observed failure (pre-dedupe): repeated
                    // failures of the same identifier are the retry churn the
                    // quarantine exists to stop.
                    FailureCounts.TryGetValue(identifier, out var count);
                    FailureCounts[identifier] = ++count;
                    quarantine = count == RuntimeFailureQuarantineThreshold;

                    if (SeenIdentifiers.Add(identifier))
                    {
                        Pending.Enqueue(new PendingFailure(identifier, message, packIdentifier));
                    }
                }

                if (quarantine)
                {
                    RequestQuarantine(identifier);
                }
            }
            catch (Exception ex)
            {
                // Never let reporting interfere with the load continuation chain.
                FuseLog.Exception("FUSE scenery load-failure watch could not queue a fault", ex);
            }
        }

        // ---- Game-log fallback net ----------------------------------------
        //
        // Both the vanilla scenery loader and load-replacing mods (observed in
        // the field with SceneryLoadRaceFix, whose SetLoaded replacement drives
        // the loads and swallows the faulted task after logging) emit the same
        // "Error loading scenery <identifier>" line when an asset load fails.
        // Hooking the Unity log stream and matching that prefix reports the
        // failure no matter which mod owns the load path or what it does with
        // the task. Feeds the same per-map dedupe as the task watch, so an
        // asset caught by both nets is still recorded once.

        private const string GameLogErrorPrefix = "Error loading scenery ";

        private static bool _logHookInstalled;

        internal static void EnsureGameLogHook()
        {
            if (_logHookInstalled)
            {
                return;
            }

            try
            {
                // The threaded variant sees messages from every thread; our
                // queue + dedupe are already thread-safe and the handler cost
                // is one prefix check per log message.
                UnityEngine.Application.logMessageReceivedThreaded += OnGameLogMessage;
                _logHookInstalled = true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure log hook could not install", ex);
            }
        }

        internal static void Shutdown()
        {
            if (!_logHookInstalled)
            {
                return;
            }

            try
            {
                UnityEngine.Application.logMessageReceivedThreaded -= OnGameLogMessage;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure log hook could not uninstall", ex);
            }

            _logHookInstalled = false;
        }

        private static void OnGameLogMessage(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception)
            {
                return;
            }

            if (!TryParseSceneryLoadErrorLine(condition, out var identifier))
            {
                return;
            }

            EnqueueFailure(
                identifier,
                "the game logged 'Error loading scenery' for this asset (see Player.log for the exception; " +
                "usually the pack's bundle does not contain an asset its catalog declares)",
                packIdentifier: null);
        }

        /// <summary>
        /// Matches the loader's "Error loading scenery &lt;identifier&gt;" line
        /// (pure; unit-tested).
        /// </summary>
        internal static bool TryParseSceneryLoadErrorLine(string condition, out string identifier)
        {
            identifier = null;
            if (condition == null ||
                !condition.StartsWith(GameLogErrorPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var value = condition.Substring(GameLogErrorPrefix.Length).Trim();
            if (value.Length == 0)
            {
                return false;
            }

            identifier = value;
            return true;
        }

        /// <summary>Queued-but-undrained failure count (test hook).</summary>
        internal static int PendingCountForTests => Pending.Count;

        /// <summary>
        /// Resolves and records queued failures, then executes queued
        /// quarantines. Main thread only (touches Unity object queries and the
        /// toast UI); driven every frame by <see cref="FUSE.Runtime.Lifecycle.FuseRuntimePump"/>
        /// — an always-on host, deliberately NOT an optional UI component (the
        /// original FuseHealthUi.Update driver was never instantiated after the
        /// menu-UI rewrite, silently starving this drain in the field).
        /// </summary>
        internal static void DrainPending()
        {
            while (Pending.TryDequeue(out var failure))
            {
                try
                {
                    Record(failure);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE could not record scenery load failure for '{failure.Identifier}'", ex);
                }
            }

            while (PendingQuarantine.TryDequeue(out var identifier))
            {
                try
                {
                    ExecuteQuarantine(identifier);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE could not quarantine scenery placements of '{identifier}'", ex);
                }
            }
        }

        private static void Record(PendingFailure failure)
        {
            // Audit-sourced failures carry their pack; runtime faults resolve it.
            var pack = failure.PackIdentifier ?? ResolvePackIdentifier(failure.Identifier);
            var owner = ResolveOwnerPackage(failure.Identifier);

            if (!FuseLoadReport.RecordSceneryLoadFailure(failure.Identifier, pack, owner, failure.Message))
            {
                return;
            }

            FuseRuntimeGuardCounters.RecordSceneryLoadFailure();
            FuseLog.Error(
                $"FUSE scenery asset '{failure.Identifier}' is failing to load and will keep failing on " +
                $"every retry: pack='{pack}' package='{owner}' reason='{failure.Message}'. The pack's " +
                "bundle likely does not contain an asset its catalog declares.");

            if (ToastedPacks.Add(pack))
            {
                try
                {
                    Toast.Present(
                        $"FUSE: assets in pack '{pack}' are failing to load - first: '{failure.Identifier}'. " +
                        "See FUSE Health > Issues.",
                        ToastPosition.Middle);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE could not display scenery load-failure toast", ex);
                }
            }
        }

        private static string ResolvePackIdentifier(string identifier)
        {
            try
            {
                var prefabStore = TrainController.Shared?.PrefabStore;
                var pack = prefabStore?.AssetPackIdentifierContainingDefinition(identifier);
                return string.IsNullOrWhiteSpace(pack) ? "<unknown>" : pack;
            }
            catch
            {
                return "<unknown>"; // no mounted store claims the identifier.
            }
        }

        private static string ResolveOwnerPackage(string identifier)
        {
            try
            {
                // Only FUSE-created scenery carries a marker; vanilla/asset-pack
                // scenery legitimately resolves to no owner.
                foreach (var marker in UnityEngine.Object.FindObjectsOfType<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>(true))
                {
                    if (marker == null)
                    {
                        continue;
                    }

                    var scenery = marker.GetComponent<SceneryAssetInstance>();
                    if (scenery == null ||
                        !string.Equals(scenery.identifier, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var owner = FUSE.Runtime.Registry.FuseRegistry.GetExclusiveOwner(
                        FUSE.Runtime.Registry.FuseClaimKind.Scenery, marker.Id);
                    if (!string.IsNullOrWhiteSpace(owner))
                    {
                        return owner;
                    }
                }
            }
            catch (Exception ex)
            {
                // Attribution is best-effort; the asset identifier alone is actionable.
                FuseLog.Exception(
                    $"FUSE could not attribute failing scenery asset '{identifier}' to a package", ex);
            }

            return "<unknown>";
        }

        private readonly struct PendingFailure
        {
            internal PendingFailure(string identifier, string message, string packIdentifier)
            {
                Identifier = identifier;
                Message = message;
                PackIdentifier = packIdentifier;
            }

            internal string Identifier { get; }

            internal string Message { get; }

            /// <summary>Known owning pack, or null to resolve at record time.</summary>
            internal string PackIdentifier { get; }
        }
    }
}
