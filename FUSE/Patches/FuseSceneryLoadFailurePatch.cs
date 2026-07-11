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
using UI.Common;

namespace FUSE.Patches
{
    /// <summary>
    /// Reports failing scenery loads and stops repeated failures from becoming
    /// permanent culling-time retry churn. The first failure for an identifier
    /// is reported; several observed failures request map-scoped quarantine of
    /// its placements. Decisions are based only on failures observed at runtime,
    /// never on asset names or naming patterns.
    ///
    /// Task continuations and the Unity log hook can run off the main thread, so
    /// they only update locked state and queues. <see cref="DrainPending"/> does
    /// all Unity object and UI work on the main thread. Every path is fail-open:
    /// diagnostics and quarantine must never interfere with the load itself.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetManager), nameof(SceneryAssetManager.LoadScenery))]
    internal static class FuseSceneryLoadFailurePatch
    {
        private const int RuntimeFailureQuarantineThreshold = 5;
        private const string GameLogErrorPrefix = "Error loading scenery ";

        // Multiple placements that reference one missing prefab fault together in
        // one culling/load wave. Treat observations less than one second after the
        // last counted episode as part of that episode so a large placement count
        // cannot trip quarantine on its first attempt. A continuous fault storm
        // still counts once per second and reaches quarantine after about 4 seconds.
        private static readonly long FailureEpisodeCoalesceWindowTicks =
            Math.Max(1L, Stopwatch.Frequency);

        private static readonly ConcurrentQueue<PendingFailure> Pending =
            new ConcurrentQueue<PendingFailure>();
        private static readonly ConcurrentQueue<string> PendingQuarantine =
            new ConcurrentQueue<string>();

        private static readonly HashSet<string> SeenIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FailureObservationCounts> FailureCounts =
            new Dictionary<string, FailureObservationCounts>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> QuarantinedIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SeenLock = new object();
        private static int _hasQuarantinedIdentifiers;

        // One toast per asset pack, not per asset: a broken pack usually breaks
        // many assets at once and the report carries the full list.
        private static readonly HashSet<string> ToastedPacks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool _logHookInstalled;
        private static int _mapGeneration;
        private static long _recorded;

        /// <summary>Distinct failing scenery assets recorded since startup.</summary>
        internal static long RecordedFailures => _recorded;

        internal static int PendingCountForTests => Pending.Count;

        internal static int QuarantinePendingCountForTests => PendingQuarantine.Count;

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
                Volatile.Read(ref _mapGeneration),
                fromGameLog ? FailureObservationSource.GameLog : FailureObservationSource.LoadTask,
                monotonicTimestamp);
        }

        internal static void ResetForNewMap()
        {
            lock (SeenLock)
            {
                unchecked
                {
                    _mapGeneration++;
                }

                SeenIdentifiers.Clear();
                FailureCounts.Clear();
                QuarantinedIdentifiers.Clear();
                Volatile.Write(ref _hasQuarantinedIdentifiers, 0);

                // Producers enqueue while holding this same lock. Draining the
                // queues here keeps their state transitions atomic with the map
                // reset instead of allowing an old-map item to land afterward.
                while (Pending.TryDequeue(out _))
                {
                }

                while (PendingQuarantine.TryDequeue(out _))
                {
                }
            }

            ToastedPacks.Clear();
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
                generation,
                FailureObservationSource.LoadTask,
                Stopwatch.GetTimestamp());
        }

        private static void EnqueueFailure(
            string identifier,
            string message,
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

                    // Count retry episodes before report dedupe. A single placement
                    // wave can produce many same-identifier faults at once, so raw
                    // fault count would make placement density look like retries.
                    FailureCounts.TryGetValue(identifier, out var counts);
                    var sourceCount = counts.Observe(source, monotonicTimestamp);
                    FailureCounts[identifier] = counts;
                    if (sourceCount == RuntimeFailureQuarantineThreshold)
                    {
                        QueueQuarantineUnderLock(identifier);
                    }

                    if (SeenIdentifiers.Add(identifier))
                    {
                        Pending.Enqueue(new PendingFailure(identifier, message));
                    }
                }

            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure watch could not queue a fault", ex);
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

        /// <summary>
        /// True for the rest of the current map once an identifier crosses the
        /// retry threshold. The persistent set suppresses later culler and
        /// deferred-pump load attempts even after the placement scan finishes.
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

        /// <summary>
        /// Installs a loader-agnostic fallback for mods that replace the normal
        /// LoadScenery path but still emit the game's standard error line.
        /// </summary>
        internal static void EnsureGameLogHook()
        {
            if (_logHookInstalled)
            {
                return;
            }

            try
            {
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

        internal static void OnGameLogMessage(
            string condition,
            string stackTrace,
            UnityEngine.LogType type)
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
                "the game logged 'Error loading scenery' for this asset; see Player.log for the exception",
                Volatile.Read(ref _mapGeneration),
                FailureObservationSource.GameLog,
                Stopwatch.GetTimestamp());
        }

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

        /// <summary>
        /// Resolves queued reports and disables placements for repeatedly
        /// failing assets. Main thread only; driven by FuseRuntimePump.
        /// </summary>
        internal static void DrainPending()
        {
            if (Pending.IsEmpty && PendingQuarantine.IsEmpty)
            {
                return;
            }

            var failures = new List<PendingFailure>();
            while (Pending.TryDequeue(out var failure))
            {
                failures.Add(failure);
            }

            if (failures.Count > 0)
            {
                var owners = ResolveOwnerPackages(failures);
                foreach (var pendingFailure in failures)
                {
                    try
                    {
                        Record(pendingFailure, owners);
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception(
                            $"FUSE could not record scenery load failure for '{pendingFailure.Identifier}'", ex);
                    }
                }
            }

            if (PendingQuarantine.IsEmpty)
            {
                return;
            }

            var quarantineIdentifiers = new List<string>();
            while (PendingQuarantine.TryDequeue(out var identifier))
            {
                quarantineIdentifiers.Add(identifier);
            }

            if (quarantineIdentifiers.Count == 0)
            {
                return;
            }

            try
            {
                ExecuteQuarantines(quarantineIdentifiers);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not quarantine scenery placements", ex);
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

        private static void ExecuteQuarantines(IReadOnlyList<string> identifiers)
        {
            var requested = BuildQuarantineIdentifierSet(identifiers);
            if (requested.Count == 0)
            {
                return;
            }

            var disabledByIdentifier =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // One include-inactive scene scan serves the entire quarantine
            // burst. Never multiply this global query by the number of assets.
            foreach (var instance in UnityEngine.Object.FindObjectsOfType<SceneryAssetInstance>(true))
            {
                if (instance == null || string.IsNullOrWhiteSpace(instance.identifier) ||
                    !requested.Contains(instance.identifier) ||
                    !instance.gameObject.activeSelf)
                {
                    continue;
                }

                instance.gameObject.SetActive(false);
                disabledByIdentifier.TryGetValue(instance.identifier, out var disabled);
                disabledByIdentifier[instance.identifier] = disabled + 1;
            }

            foreach (var identifier in identifiers)
            {
                if (!disabledByIdentifier.TryGetValue(identifier, out var disabled) || disabled == 0)
                {
                    continue;
                }

                FuseLog.Warning(
                    $"FUSE quarantined {disabled} scenery placement(s) of '{identifier}' for this map after " +
                    $"{RuntimeFailureQuarantineThreshold} observed retry episodes. Fixing the pack restores the " +
                    "placements on the next map load.");
                FuseLoadReport.RecordNotice(
                    $"{disabled} scenery placement(s) of '{identifier}' were disabled for this map after repeated " +
                    "asset load failures. Fixing the pack restores them on the next map load.");
            }
        }

        private static void Record(
            PendingFailure failure,
            Dictionary<string, string> owners)
        {
            var pack = ResolvePackIdentifier(failure.Identifier);
            var owner = owners.TryGetValue(failure.Identifier, out var resolvedOwner)
                ? resolvedOwner
                : "<unknown>";

            if (!FuseLoadReport.RecordSceneryLoadFailure(failure.Identifier, pack, owner, failure.Message))
            {
                return;
            }

            _recorded++;
            FuseLog.Error(
                $"FUSE scenery asset '{failure.Identifier}' is repeatedly failing to load: " +
                $"pack='{pack}' package='{owner}' reason='{failure.Message}'. The pack's bundle may not " +
                "contain an asset its catalog declares.");

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
                return "<unknown>";
            }
        }

        private static Dictionary<string, string> ResolveOwnerPackages(
            IReadOnlyList<PendingFailure> failures)
        {
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var failure in failures)
            {
                requested.Add(failure.Identifier);
            }

            try
            {
                // A drain can contain many distinct broken assets. Resolve all of
                // their package owners with one scene scan instead of one global
                // FindObjectsOfType call per identifier.
                foreach (var marker in UnityEngine.Object.FindObjectsOfType<
                             FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>(true))
                {
                    if (marker == null)
                    {
                        continue;
                    }

                    var scenery = marker.GetComponent<SceneryAssetInstance>();
                    if (scenery == null || string.IsNullOrWhiteSpace(scenery.identifier) ||
                        !requested.Contains(scenery.identifier) ||
                        owners.ContainsKey(scenery.identifier))
                    {
                        continue;
                    }

                    var owner = FUSE.Runtime.Registry.FuseRegistry.GetExclusiveOwner(
                        FUSE.Runtime.Registry.FuseClaimKind.Scenery, marker.Id);
                    if (!string.IsNullOrWhiteSpace(owner))
                    {
                        owners[scenery.identifier] = owner;
                        if (owners.Count == requested.Count)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE could not attribute failing scenery assets to packages", ex);
            }

            return owners;
        }

        private readonly struct PendingFailure
        {
            internal PendingFailure(string identifier, string message)
            {
                Identifier = identifier;
                Message = message;
            }

            internal string Identifier { get; }

            internal string Message { get; }
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
