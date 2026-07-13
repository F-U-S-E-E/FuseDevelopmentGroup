using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Helpers;

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
    /// Task continuations and the Unity log hook can run off the main thread, so
    /// they only update locked state and queues. <see cref="DrainPending"/> does
    /// all Unity object and report work on the main thread. Findings surface in
    /// the health report without per-pack popups. Everything is fail-open: a
    /// failure inside this patch never affects the load path itself.
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
        private static readonly HashSet<string> SeenCatalogMismatches =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SeenLock = new object();
        private static int _mapGeneration;
        private static int _hasQuarantinedIdentifiers;

        /// <summary>Distinct failing scenery assets recorded since startup (diagnostics).</summary>
        internal static long RecordedFailures => FuseRuntimeGuardCounters.SceneryLoadFailures;

        internal static void ResetForNewMap()
        {
            lock (SeenLock)
            {
                unchecked
                {
                    _mapGeneration++;
                }

                SeenIdentifiers.Clear();
                SeenCatalogMismatches.Clear();
                FailureCounts.Clear();
                QuarantinedIdentifiers.Clear();
                Volatile.Write(ref _hasQuarantinedIdentifiers, 0);

                // Producers update their dedupe/quarantine set and enqueue while
                // holding this same lock. Drain inside it so a map reset cannot
                // split those paired state transitions across maps.
                while (Pending.TryDequeue(out _))
                {
                }

                while (PendingQuarantine.TryDequeue(out _))
                {
                }
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
        // an asset has failed repeatedly at runtime its placements are disabled
        // for the session. A bundle audit alone never quarantines: catalog prefab
        // entries also represent cars, trucks, and audio definitions. Disabling
        // a scenery host is a vanilla-exercised path (progression gating does the
        // same), and the quarantine re-arms per map load, so fixing the pack
        // brings the placements back on the next load.

        // Runtime retry episodes before quarantine. One placement wave can
        // fault many placements at once, and the task watch plus log hook can
        // observe the same wave. Coalesce each source independently so density
        // and dual observation cannot make a first attempt look like retries.
        private const int RuntimeFailureQuarantineThreshold = 5;
        private static readonly long FailureEpisodeCoalesceWindowTicks =
            Math.Max(1L, Stopwatch.Frequency);

        private static readonly Dictionary<string, FailureObservationCounts> FailureCounts =
            new Dictionary<string, FailureObservationCounts>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> QuarantinedIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentQueue<string> PendingQuarantine = new ConcurrentQueue<string>();

        /// <summary>
        /// Requests session-quarantine of every placement of an asset that has
        /// failed repeatedly at runtime. The identifier is marked immediately so
        /// load requests are suppressed even before the main-thread placement
        /// scan runs. Idempotent per identifier per map.
        /// </summary>
        internal static void RequestQuarantine(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            lock (SeenLock)
            {
                QueueQuarantineUnderLock(identifier);
            }
        }

        private static void QueueQuarantineUnderLock(string identifier)
        {
            if (QuarantinedIdentifiers.Add(identifier))
            {
                Volatile.Write(ref _hasQuarantinedIdentifiers, 1);
                PendingQuarantine.Enqueue(identifier);
            }
        }

        /// <summary>Quarantine requests queued but not yet executed (test hook).</summary>
        internal static int QuarantinePendingCountForTests => PendingQuarantine.Count;

        /// <summary>
        /// True after runtime failures have quarantined an identifier for this map.
        /// The set intentionally outlives the placement scan: inactive/deferred
        /// scenery can become active later and must still have SetLoaded(true)
        /// suppressed.
        /// </summary>
        internal static bool IsQuarantined(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) ||
                Volatile.Read(ref _hasQuarantinedIdentifiers) == 0)
            {
                return false;
            }

            lock (SeenLock)
            {
                return QuarantinedIdentifiers.Contains(identifier);
            }
        }

        private static void ExecuteQuarantines(
            IReadOnlyList<string> identifiers,
            SceneScenerySnapshot snapshot)
        {
            var requested = BuildQuarantineIdentifierSet(identifiers);
            var disabledByIdentifier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in snapshot.Instances)
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.identifier) ||
                    !requested.Contains(instance.identifier) || !instance.gameObject.activeSelf)
                {
                    continue;
                }

                instance.gameObject.SetActive(false);
                disabledByIdentifier.TryGetValue(instance.identifier, out var disabled);
                disabledByIdentifier[instance.identifier] = disabled + 1;
                FuseRuntimeGuardCounters.RecordSceneryPlacementQuarantined();
            }

            foreach (var identifier in identifiers)
            {
                if (!disabledByIdentifier.TryGetValue(identifier, out var disabled) || disabled == 0)
                {
                    continue;
                }

                FuseLog.Warning(
                    $"FUSE quarantined {disabled} scenery placement(s) of '{identifier}' for this session: the asset " +
                    "cannot load, and the loader would otherwise retry it on every culling pass near each placement. " +
                    "Fixing the pack restores the placements on the next map load.");
                FuseLoadReport.RecordNotice(
                    $"{disabled} scenery placement(s) of '{identifier}' were disabled for this session because the " +
                    "asset cannot load (see the asset load failures section). Fixing the pack restores them.");
            }
        }

        internal static HashSet<string> BuildQuarantineIdentifierSet(IEnumerable<string> identifiers)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (identifiers == null)
            {
                return result;
            }

            foreach (var identifier in identifiers)
            {
                if (!string.IsNullOrWhiteSpace(identifier))
                {
                    result.Add(identifier);
                }
            }

            return result;
        }

        /// <summary>
        /// Entry point for the bundle audit: reports an asset a pack's
        /// Catalog.json declares but its bundle does not contain. Catalog audit
        /// findings use their own dedupe and do not contribute to the runtime
        /// quarantine threshold. The main-thread drain promotes only confirmed
        /// scenery to the scenery-failure channel; everything else becomes a
        /// generic load-report notice.
        /// </summary>
        internal static void ReportCatalogMismatch(
            FuseAssetPackBundleAuditPatch.CatalogAssetEntry entry,
            string packIdentifier)
        {
            if (string.IsNullOrWhiteSpace(entry.Identifier))
            {
                return;
            }

            try
            {
                var pack = string.IsNullOrWhiteSpace(packIdentifier) ? "<unknown>" : packIdentifier;
                lock (SeenLock)
                {
                    var key = pack + "\0" + entry.Identifier;
                    if (!SeenCatalogMismatches.Add(key))
                    {
                        return;
                    }

                    Pending.Enqueue(PendingFailure.ForCatalogMismatch(entry, pack));
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE asset pack bundle audit could not queue a mismatch", ex);
            }
        }

        internal static void Postfix(string identifier, Task __result)
        {
            if (__result == null || string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            try
            {
                var generation = Volatile.Read(ref _mapGeneration);
                __result.ContinueWith(
                    task => Enqueue(identifier, task.Exception, generation),
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

        private static void Enqueue(string identifier, AggregateException exception, int generation)
        {
            var message = exception != null
                ? exception.GetBaseException().Message
                : "asset load failed";
            EnqueueFailure(
                identifier,
                message,
                null,
                generation,
                FailureObservationSource.LoadTask,
                Stopwatch.GetTimestamp());
        }

        private static void EnqueueFailure(
            string identifier,
            string message,
            string packIdentifier,
            int generation,
            FailureObservationSource source,
            long monotonicTimestamp)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            try
            {
                lock (SeenLock)
                {
                    if (generation != _mapGeneration)
                    {
                        return; // a task from the previous map completed late.
                    }

                    FailureCounts.TryGetValue(identifier, out var counts);
                    var sourceCount = counts.Observe(source, monotonicTimestamp);
                    FailureCounts[identifier] = counts;
                    if (sourceCount >= RuntimeFailureQuarantineThreshold)
                    {
                        QueueQuarantineUnderLock(identifier);
                    }

                    if (SeenIdentifiers.Add(identifier))
                    {
                        Pending.Enqueue(new PendingFailure(identifier, message, packIdentifier));
                    }
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

        internal static long FailureEpisodeCoalesceWindowTicksForTests =>
            FailureEpisodeCoalesceWindowTicks;

        internal static void ObserveFailureForTests(
            string identifier,
            bool fromGameLog,
            long monotonicTimestamp)
        {
            EnqueueFailure(
                identifier,
                "test failure",
                null,
                Volatile.Read(ref _mapGeneration),
                fromGameLog ? FailureObservationSource.GameLog : FailureObservationSource.LoadTask,
                monotonicTimestamp);
        }

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
            if (_logHookInstalled)
            {
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

            ResetForNewMap();
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
                null,
                Volatile.Read(ref _mapGeneration),
                FailureObservationSource.GameLog,
                Stopwatch.GetTimestamp());
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
        /// quarantines. Main thread only (touches Unity object queries and report
        /// bookkeeping); driven every frame by <see cref="FUSE.Runtime.Lifecycle.FuseRuntimePump"/>
        /// — an always-on host, deliberately NOT an optional UI component (the
        /// original driver, the since-deleted Health window's Update, was never
        /// instantiated after the menu-UI rewrite, silently starving this drain
        /// in the field).
        /// </summary>
        internal static void DrainPending()
        {
            // The runtime pump calls this every frame. Avoid allocating the two
            // batch lists during the overwhelmingly common idle path;
            // ConcurrentQueue.IsEmpty is a lock-free snapshot, and the existing
            // post-dequeue check below still handles races safely.
            if (Pending.IsEmpty && PendingQuarantine.IsEmpty)
            {
                return;
            }

            // Drain BOTH queues fully into one batch: the expensive step below is
            // the single scene snapshot, not the per-item report strings or
            // SetActive calls, and both queues are bounded by the per-map dedupe
            // sets. Capping the batch would multiply the snapshot cost across
            // consecutive frames — a 100-mismatch burst under an 8-per-frame cap
            // meant 13 full include-inactive scene scans where one serves.
            var failures = new List<PendingFailure>();
            while (Pending.TryDequeue(out var failure))
            {
                failures.Add(failure);
            }

            var quarantineIdentifiers = new List<string>();
            while (PendingQuarantine.TryDequeue(out var identifier))
            {
                quarantineIdentifiers.Add(identifier);
            }

            if (failures.Count == 0 && quarantineIdentifiers.Count == 0)
            {
                return;
            }

            var snapshot = CaptureSceneSnapshot(failures, quarantineIdentifiers);

            foreach (var queuedFailure in failures)
            {
                try
                {
                    Record(queuedFailure, snapshot);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE could not record asset load failure for '{queuedFailure.Identifier}'", ex);
                }
            }

            if (quarantineIdentifiers.Count == 0)
            {
                return;
            }

            try
            {
                ExecuteQuarantines(quarantineIdentifiers, snapshot);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not quarantine scenery placements", ex);
            }
        }

        private static SceneScenerySnapshot CaptureSceneSnapshot(
            IReadOnlyList<PendingFailure> failures,
            IReadOnlyList<string> quarantineIdentifiers)
        {
            var requestedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownerIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var failure in failures)
            {
                if (!failure.IsCatalogMismatch)
                {
                    requestedIdentifiers.Add(failure.Identifier);
                    ownerIdentifiers.Add(failure.Identifier);
                    continue;
                }

                var classification = FuseAssetPackBundleAuditPatch.ClassifyCatalogAssetType(
                    failure.CatalogEntry.Type);
                if (classification == FuseAssetPackBundleAuditPatch.CatalogAssetTypeClassification.NonScenery)
                {
                    continue;
                }

                requestedIdentifiers.Add(failure.CatalogEntry.Identifier);
                if (!string.IsNullOrWhiteSpace(failure.CatalogEntry.Name))
                {
                    requestedIdentifiers.Add(failure.CatalogEntry.Name);
                    ownerIdentifiers.Add(failure.CatalogEntry.Name);
                }
                ownerIdentifiers.Add(failure.CatalogEntry.Identifier);
            }

            foreach (var identifier in quarantineIdentifiers)
            {
                if (!string.IsNullOrWhiteSpace(identifier))
                {
                    requestedIdentifiers.Add(identifier);
                }
            }

            if (requestedIdentifiers.Count == 0)
            {
                return SceneScenerySnapshot.Empty;
            }

            try
            {
                // One include-inactive global scan serves the whole report batch
                // and up to 32 quarantines. Never scan once per asset.
                var instances = UnityEngine.Object.FindObjectsByType<SceneryAssetInstance>(
                    UnityEngine.FindObjectsInactive.Include,
                    UnityEngine.FindObjectsSortMode.None);
                var presentIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var instance in instances)
                {
                    if (instance == null || string.IsNullOrWhiteSpace(instance.identifier) ||
                        !requestedIdentifiers.Contains(instance.identifier))
                    {
                        continue;
                    }

                    presentIdentifiers.Add(instance.identifier);
                    if (!ownerIdentifiers.Contains(instance.identifier) || owners.ContainsKey(instance.identifier))
                    {
                        continue;
                    }

                    var marker = instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>();
                    if (marker == null)
                    {
                        continue;
                    }

                    var owner = FUSE.Runtime.Registry.FuseRegistry.GetExclusiveOwner(
                        FUSE.Runtime.Registry.FuseClaimKind.Scenery, marker.Id);
                    if (!string.IsNullOrWhiteSpace(owner))
                    {
                        owners[instance.identifier] = owner;
                    }
                }

                return new SceneScenerySnapshot(instances, presentIdentifiers, owners);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not build the batched scenery failure index", ex);
                return SceneScenerySnapshot.Empty;
            }
        }

        private static void Record(PendingFailure failure, SceneScenerySnapshot snapshot)
        {
            if (failure.IsCatalogMismatch && !IsConfirmedScenery(failure.CatalogEntry, snapshot))
            {
                RecordGenericCatalogMismatch(failure);
                return;
            }

            // Audit-sourced failures carry their pack; runtime faults resolve it.
            var pack = failure.PackIdentifier ?? ResolvePackIdentifier(failure.Identifier);
            var owner = snapshot.ResolveOwner(
                failure.Identifier,
                failure.IsCatalogMismatch ? failure.CatalogEntry.Name : null);

            if (!FuseLoadReport.RecordSceneryLoadFailure(failure.Identifier, pack, owner, failure.Message))
            {
                return;
            }

            FuseRuntimeGuardCounters.RecordSceneryLoadFailure();
            // No popup here by design: broken-asset findings surface through the
            // health report (Issues rows, the brokenAssets summary count, and the
            // menu Status page) — the report's own single map-load toast already
            // signals "Needs Attention" when this bucket is non-empty.
            FuseLog.Error(
                $"FUSE scenery asset '{failure.Identifier}' is failing to load and will keep failing on " +
                $"every retry: pack='{pack}' package='{owner}' reason='{failure.Message}'. The pack's " +
                "bundle likely does not contain an asset its catalog declares.");
        }

        private static bool IsConfirmedScenery(
            FuseAssetPackBundleAuditPatch.CatalogAssetEntry entry,
            SceneScenerySnapshot snapshot)
        {
            var classification = FuseAssetPackBundleAuditPatch.ClassifyCatalogAssetType(entry.Type);
            if (classification == FuseAssetPackBundleAuditPatch.CatalogAssetTypeClassification.Scenery)
            {
                return true;
            }

            if (classification == FuseAssetPackBundleAuditPatch.CatalogAssetTypeClassification.NonScenery)
            {
                return false;
            }

            return snapshot.Contains(entry.Identifier) || snapshot.Contains(entry.Name);
        }

        private static void RecordGenericCatalogMismatch(PendingFailure failure)
        {
            var entry = failure.CatalogEntry;
            var type = string.IsNullOrWhiteSpace(entry.Type) ? "<unknown>" : entry.Type;
            var name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Identifier : entry.Name;
            var filename = string.IsNullOrWhiteSpace(entry.Filename) ? "<blank>" : entry.Filename;
            FuseLoadReport.RecordNotice(
                $"Asset pack catalog/bundle mismatch: pack='{failure.PackIdentifier ?? "<unknown>"}' " +
                $"identifier='{entry.Identifier}' name='{name}' type='{type}' filename='{filename}'. " +
                "The bundle must be rebuilt or the catalog entry removed.");
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

        private sealed class SceneScenerySnapshot
        {
            internal static readonly SceneScenerySnapshot Empty = new SceneScenerySnapshot(
                Array.Empty<SceneryAssetInstance>(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            internal SceneScenerySnapshot(
                SceneryAssetInstance[] instances,
                HashSet<string> presentIdentifiers,
                Dictionary<string, string> owners)
            {
                Instances = instances ?? Array.Empty<SceneryAssetInstance>();
                _presentIdentifiers = presentIdentifiers;
                _owners = owners;
            }

            private readonly HashSet<string> _presentIdentifiers;
            private readonly Dictionary<string, string> _owners;

            internal SceneryAssetInstance[] Instances { get; }

            internal bool Contains(string identifier)
            {
                return !string.IsNullOrWhiteSpace(identifier) && _presentIdentifiers.Contains(identifier);
            }

            internal string ResolveOwner(string identifier, string alternateIdentifier = null)
            {
                if (!string.IsNullOrWhiteSpace(identifier) && _owners.TryGetValue(identifier, out var owner))
                {
                    return owner;
                }

                return !string.IsNullOrWhiteSpace(alternateIdentifier) &&
                       _owners.TryGetValue(alternateIdentifier, out owner)
                    ? owner
                    : "<unknown>";
            }
        }

        private readonly struct PendingFailure
        {
            internal PendingFailure(string identifier, string message, string packIdentifier)
                : this(
                    identifier,
                    message,
                    packIdentifier,
                    isCatalogMismatch: false,
                    catalogEntry: default)
            {
            }

            private PendingFailure(
                string identifier,
                string message,
                string packIdentifier,
                bool isCatalogMismatch,
                FuseAssetPackBundleAuditPatch.CatalogAssetEntry catalogEntry)
            {
                Identifier = identifier;
                Message = message;
                PackIdentifier = packIdentifier;
                IsCatalogMismatch = isCatalogMismatch;
                CatalogEntry = catalogEntry;
            }

            internal static PendingFailure ForCatalogMismatch(
                FuseAssetPackBundleAuditPatch.CatalogAssetEntry entry,
                string packIdentifier)
            {
                var declaredAs = string.IsNullOrWhiteSpace(entry.Filename)
                    ? entry.Identifier
                    : entry.Filename;
                return new PendingFailure(
                    entry.Identifier,
                    $"declared in the pack's Catalog.json (name '{entry.Name}', type '{entry.Type}', " +
                    $"filename '{declaredAs}') but not present in its bundle — the pack needs its bundle " +
                    "rebuilt or the catalog entry (and content referencing it) removed",
                    packIdentifier,
                    isCatalogMismatch: true,
                    catalogEntry: entry);
            }

            internal string Identifier { get; }

            internal string Message { get; }

            /// <summary>Known owning pack, or null to resolve at record time.</summary>
            internal string PackIdentifier { get; }

            internal bool IsCatalogMismatch { get; }

            internal FuseAssetPackBundleAuditPatch.CatalogAssetEntry CatalogEntry { get; }
        }

        private enum FailureObservationSource
        {
            LoadTask,
            GameLog
        }

        private struct FailureObservationCounts
        {
            private FailureObservationState _loadTask;
            private FailureObservationState _gameLog;

            internal int Observe(FailureObservationSource source, long timestamp)
            {
                if (source == FailureObservationSource.GameLog)
                {
                    return _gameLog.Observe(timestamp);
                }

                return _loadTask.Observe(timestamp);
            }
        }

        private struct FailureObservationState
        {
            private int _episodes;
            private long _lastCountedEpisodeTimestamp;
            private bool _hasObservation;

            internal int Observe(long timestamp)
            {
                if (_hasObservation)
                {
                    var elapsed = timestamp - _lastCountedEpisodeTimestamp;
                    if (elapsed < FailureEpisodeCoalesceWindowTicks)
                    {
                        return _episodes;
                    }
                }
                else
                {
                    _hasObservation = true;
                }

                _lastCountedEpisodeTimestamp = timestamp;
                return ++_episodes;
            }
        }
    }
}
