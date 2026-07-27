using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FUSE.Infrastructure;
using FUSE.Interface;
using HarmonyLib;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Pure scheduling decisions for Railroader's streamed track mesh queues.
    /// Kept separate from the reflected private-entry adapter so the ordering and
    /// frame-budget invariants can be covered by the normal net48 test suite.
    /// </summary>
    internal static class FuseTrackStreamScheduling
    {
        // Railroader intended to spend 1/60 second in WorkBuildQueue, but measured
        // the loop with Time.time, which does not advance within a Unity frame.
        // Gameplay gets a tighter slice because the rest of the frame still needs
        // CPU time; the covered loading screen can drain aggressively.
        internal const double GameplayBuildBudgetMilliseconds = 8d;
        internal const double LoadingBuildBudgetMilliseconds = 40d;
        internal const int GameplayMaxBuildsPerFrame = 24;
        internal const int LoadingMaxBuildsPerFrame = 128;

        // Destroy is deferred by Unity until frame end. A small item cap matters
        // more than a generous timer because hundreds of cheap Destroy calls still
        // produce one expensive frame-end cleanup wave.
        internal const double GameplayDestroyBudgetMilliseconds = 1d;
        internal const double LoadingDestroyBudgetMilliseconds = 4d;
        internal const int GameplayMaxDestroysPerFrame = 8;
        internal const int LoadingMaxDestroysPerFrame = 64;

        internal static bool CanProcessAnother(
            int processed,
            double elapsedMilliseconds,
            double budgetMilliseconds,
            int maximumPerFrame)
        {
            // Always make forward progress even when one complex narrow-gauge
            // switch costs more than the whole target slice.
            return processed == 0 ||
                   (processed < maximumPerFrame &&
                    elapsedMilliseconds < budgetMilliseconds);
        }

        internal static bool IsCurrentBuildRequest(bool isInRange, bool isRegistered)
        {
            return isInRange && isRegistered;
        }

        /// <summary>
        /// Orders low priority first because the runtime list is consumed from its
        /// O(1) tail. Current requests beat stale requests, visible beats hidden,
        /// and nearer beats farther. Original FIFO order is the final tie breaker.
        /// </summary>
        internal static int CompareBuildPriority(
            bool leftCurrent,
            bool leftVisible,
            float leftDistanceSqr,
            int leftSequence,
            bool rightCurrent,
            bool rightVisible,
            float rightDistanceSqr,
            int rightSequence)
        {
            var comparison = leftCurrent.CompareTo(rightCurrent);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = leftVisible.CompareTo(rightVisible);
            if (comparison != 0)
            {
                return comparison;
            }

            // Farther first, nearer last.
            comparison = rightDistanceSqr.CompareTo(leftDistanceSqr);
            if (comparison != 0)
            {
                return comparison;
            }

            // Later FIFO entries first, original head last.
            return rightSequence.CompareTo(leftSequence);
        }
    }

    /// <summary>
    /// Replaces Railroader's unbounded streamed-track build/destroy drains.
    ///
    /// The stock WorkBuildQueue compares Time.time against Time.time + 1/60.
    /// Time.time is constant for the duration of a frame, so a teleport drains
    /// every queued mesh, roadbed, special-work part, and collider in one frame.
    /// Narrow Gauge makes the defect conspicuous because each descriptor performs
    /// substantially more synchronous mesh work.
    ///
    /// This adapter intentionally binds Railroader's private Entry layout at
    /// runtime. Any shape mismatch disables the optimization and runs the original
    /// methods unchanged. No track descriptor, mesh, collider, or culling distance
    /// is removed.
    /// </summary>
    internal static class FuseTrackRebuilderQueueProcessor
    {
        private static readonly FieldInfo BuildQueueField =
            AccessTools.Field(typeof(TrackRebuilder), "_buildQueue");
        private static readonly FieldInfo DestroyQueueField =
            AccessTools.Field(typeof(TrackRebuilder), "_destroyQueue");
        private static readonly FieldInfo SpheresField =
            AccessTools.Field(typeof(TrackRebuilder), "_spheres");
        private static readonly FieldInfo SphereEntriesField =
            AccessTools.Field(typeof(TrackRebuilder), "_sphereEntries");
        private static readonly FieldInfo SphereCountField =
            AccessTools.Field(typeof(TrackRebuilder), "_sphereCount");
        private static readonly Type EntryType =
            typeof(TrackRebuilder).GetNestedType(
                "Entry",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo EntryDescriptorField =
            EntryType?.GetField(
                "descriptor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo EntryInRangeField =
            EntryType?.GetField(
                "isInRange",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo EntryVisibleField =
            EntryType?.GetField(
                "isVisible",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo EntryGameObjectField =
            EntryType?.GetField(
                "gameObject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly Dictionary<object, float> DistanceByEntry =
            new Dictionary<object, float>(ReferenceComparer.Instance);
        private static readonly List<BuildCandidate> BuildCandidates =
            new List<BuildCandidate>();

        private static bool _runtimeDisabled;
        private static bool _activationLogged;
        private static long _built;
        private static long _destroyed;
        private static long _staleBuildsSkipped;
        private static int _buildQueueDepth;
        private static int _destroyQueueDepth;
        private static int _peakBuildQueueDepth;
        private static int _peakDestroyQueueDepth;

        internal static bool Available =>
            BuildQueueField != null &&
            DestroyQueueField != null &&
            SpheresField != null &&
            SphereEntriesField != null &&
            SphereCountField != null &&
            EntryType != null &&
            EntryDescriptorField != null &&
            EntryInRangeField != null &&
            EntryVisibleField != null &&
            EntryGameObjectField != null &&
            typeof(TrackObjectManager.ITrackDescriptor).IsAssignableFrom(
                EntryDescriptorField.FieldType) &&
            typeof(GameObject).IsAssignableFrom(EntryGameObjectField.FieldType);

        internal static long Built => _built;
        internal static long Destroyed => _destroyed;
        internal static long StaleBuildsSkipped => _staleBuildsSkipped;
        internal static int BuildQueueDepth => _buildQueueDepth;
        internal static int DestroyQueueDepth => _destroyQueueDepth;
        internal static int PeakBuildQueueDepth => _peakBuildQueueDepth;
        internal static int PeakDestroyQueueDepth => _peakDestroyQueueDepth;

        /// <summary>
        /// Returns true only when Harmony should run Railroader's original method.
        /// </summary>
        internal static bool ShouldRunOriginalBuildQueue(TrackRebuilder rebuilder)
        {
            if (_runtimeDisabled ||
                !Available ||
                rebuilder == null ||
                !Application.isPlaying ||
                rebuilder.BuildGameObject == null)
            {
                return true;
            }

            IList queue;
            try
            {
                queue = BuildQueueField.GetValue(rebuilder) as IList;
            }
            catch (Exception ex)
            {
                DisableRuntime("read track build queue", ex);
                return true;
            }

            if (queue == null)
            {
                DisableRuntime(
                    "bind track build queue",
                    new InvalidOperationException(
                        "TrackRebuilder._buildQueue no longer implements IList."));
                return true;
            }

            _buildQueueDepth = queue.Count;
            _peakBuildQueueDepth = Math.Max(_peakBuildQueueDepth, queue.Count);
            if (queue.Count == 0)
            {
                return false;
            }

            object currentEntry = null;
            GameObject created = null;
            var currentEntryRemoved = false;
            try
            {
                PrepareNearestFirstQueue(rebuilder, queue);

                var loading = FuseLoadingScreen.IsShowing;
                var budget = loading
                    ? FuseTrackStreamScheduling.LoadingBuildBudgetMilliseconds
                    : FuseTrackStreamScheduling.GameplayBuildBudgetMilliseconds;
                var maximum = loading
                    ? FuseTrackStreamScheduling.LoadingMaxBuildsPerFrame
                    : FuseTrackStreamScheduling.GameplayMaxBuildsPerFrame;
                var stopwatch = Stopwatch.StartNew();
                var builtThisFrame = 0;

                while (queue.Count > 0)
                {
                    var tail = queue.Count - 1;
                    currentEntry = queue[tail];
                    if (currentEntry == null)
                    {
                        queue.RemoveAt(tail);
                        _staleBuildsSkipped++;
                        continue;
                    }

                    var registered = DistanceByEntry.ContainsKey(currentEntry);
                    var inRange = (bool)EntryInRangeField.GetValue(currentEntry);
                    if (!FuseTrackStreamScheduling.IsCurrentBuildRequest(
                            inRange,
                            registered))
                    {
                        queue.RemoveAt(tail);
                        currentEntry = null;
                        _staleBuildsSkipped++;
                        continue;
                    }

                    if (!FuseTrackStreamScheduling.CanProcessAnother(
                            builtThisFrame,
                            stopwatch.Elapsed.TotalMilliseconds,
                            budget,
                            maximum))
                    {
                        break;
                    }

                    var descriptor =
                        EntryDescriptorField.GetValue(currentEntry)
                        as TrackObjectManager.ITrackDescriptor;
                    if (descriptor == null)
                    {
                        queue.RemoveAt(tail);
                        currentEntry = null;
                        _staleBuildsSkipped++;
                        continue;
                    }

                    queue.RemoveAt(tail);
                    currentEntryRemoved = true;
                    created = rebuilder.BuildGameObject(descriptor);
                    EntryGameObjectField.SetValue(currentEntry, created);
                    created = null;
                    currentEntry = null;
                    currentEntryRemoved = false;
                    builtThisFrame++;
                    _built++;
                }

                _buildQueueDepth = queue.Count;
                if (!_activationLogged)
                {
                    _activationLogged = true;
                    FuseLog.Info(
                        "FUSE bounded Railroader streamed-track queues: " +
                        $"gameplayBuildBudgetMs={FuseTrackStreamScheduling.GameplayBuildBudgetMilliseconds:0.#}, " +
                        $"loadingBuildBudgetMs={FuseTrackStreamScheduling.LoadingBuildBudgetMilliseconds:0.#}, " +
                        $"gameplayDestroyCap={FuseTrackStreamScheduling.GameplayMaxDestroysPerFrame}. " +
                        "Visible/nearest track is built first; stale pre-teleport requests are discarded.");
                }

                return false;
            }
            catch (Exception ex)
            {
                // Restore the current request before failing open. If a GameObject
                // was created but could not be assigned, destroy it so the original
                // method does not duplicate an orphaned mesh hierarchy.
                if (created != null)
                {
                    UnityEngine.Object.Destroy(created);
                }

                if (currentEntryRemoved && currentEntry != null)
                {
                    queue.Insert(0, currentEntry);
                }

                DisableRuntime("process track build queue", ex);
                return true;
            }
            finally
            {
                DistanceByEntry.Clear();
                BuildCandidates.Clear();
            }
        }

        /// <summary>
        /// Returns true only when Harmony should run Railroader's original method.
        /// </summary>
        internal static bool ShouldRunOriginalDestroyQueue(TrackRebuilder rebuilder)
        {
            if (_runtimeDisabled ||
                !Available ||
                rebuilder == null ||
                !Application.isPlaying)
            {
                return true;
            }

            IList queue;
            try
            {
                queue = DestroyQueueField.GetValue(rebuilder) as IList;
            }
            catch (Exception ex)
            {
                DisableRuntime("read track destroy queue", ex);
                return true;
            }

            if (queue == null)
            {
                DisableRuntime(
                    "bind track destroy queue",
                    new InvalidOperationException(
                        "TrackRebuilder._destroyQueue no longer implements IList."));
                return true;
            }

            _destroyQueueDepth = queue.Count;
            _peakDestroyQueueDepth = Math.Max(_peakDestroyQueueDepth, queue.Count);
            if (queue.Count == 0)
            {
                return false;
            }

            object currentEntry = null;
            var currentEntryRemoved = false;
            try
            {
                var loading = FuseLoadingScreen.IsShowing;
                var budget = loading
                    ? FuseTrackStreamScheduling.LoadingDestroyBudgetMilliseconds
                    : FuseTrackStreamScheduling.GameplayDestroyBudgetMilliseconds;
                var maximum = loading
                    ? FuseTrackStreamScheduling.LoadingMaxDestroysPerFrame
                    : FuseTrackStreamScheduling.GameplayMaxDestroysPerFrame;
                var stopwatch = Stopwatch.StartNew();
                var destroyedThisFrame = 0;

                while (queue.Count > 0)
                {
                    if (!FuseTrackStreamScheduling.CanProcessAnother(
                            destroyedThisFrame,
                            stopwatch.Elapsed.TotalMilliseconds,
                            budget,
                            maximum))
                    {
                        break;
                    }

                    // Preserve FIFO age here: old inactive entries are evicted first,
                    // while newer entries remain cached long enough for a quick
                    // teleport-back to cancel their pending destruction.
                    currentEntry = queue[0];
                    queue.RemoveAt(0);
                    currentEntryRemoved = true;
                    if (currentEntry == null)
                    {
                        currentEntryRemoved = false;
                        continue;
                    }

                    // InRangeDidChange normally removes a returning entry from this
                    // queue. Recheck defensively so ordering between Harmony patches
                    // cannot destroy track that has just become current again.
                    if ((bool)EntryInRangeField.GetValue(currentEntry))
                    {
                        currentEntry = null;
                        currentEntryRemoved = false;
                        continue;
                    }

                    var gameObject =
                        EntryGameObjectField.GetValue(currentEntry) as GameObject;
                    if (gameObject != null)
                    {
                        UnityEngine.Object.Destroy(gameObject);
                        _destroyed++;
                        destroyedThisFrame++;
                    }

                    EntryGameObjectField.SetValue(currentEntry, null);
                    currentEntry = null;
                    currentEntryRemoved = false;
                }

                _destroyQueueDepth = queue.Count;
                return false;
            }
            catch (Exception ex)
            {
                if (currentEntryRemoved && currentEntry != null)
                {
                    queue.Insert(0, currentEntry);
                }

                DisableRuntime("process track destroy queue", ex);
                return true;
            }
        }

        internal static void OnQueueCleared()
        {
            _buildQueueDepth = 0;
            _destroyQueueDepth = 0;
            DistanceByEntry.Clear();
            BuildCandidates.Clear();
        }

        internal static void Shutdown()
        {
            _runtimeDisabled = false;
            _activationLogged = false;
            _built = 0;
            _destroyed = 0;
            _staleBuildsSkipped = 0;
            _buildQueueDepth = 0;
            _destroyQueueDepth = 0;
            _peakBuildQueueDepth = 0;
            _peakDestroyQueueDepth = 0;
            DistanceByEntry.Clear();
            BuildCandidates.Clear();
        }

        private static void PrepareNearestFirstQueue(
            TrackRebuilder rebuilder,
            IList queue)
        {
            DistanceByEntry.Clear();
            BuildCandidates.Clear();

            var spheres = SpheresField.GetValue(rebuilder) as BoundingSphere[];
            var sphereEntries = SphereEntriesField.GetValue(rebuilder) as Array;
            var sphereCount = (int)SphereCountField.GetValue(rebuilder);
            if (spheres == null || sphereEntries == null)
            {
                throw new InvalidOperationException(
                    "TrackRebuilder culling sphere storage changed shape.");
            }

            sphereCount = Math.Min(
                sphereCount,
                Math.Min(spheres.Length, sphereEntries.Length));
            var camera = FuseSceneryCameraRef.Resolve();
            var hasCamera = camera != null;
            var cameraPosition = hasCamera
                ? camera.transform.position
                : Vector3.zero;

            for (var index = 0; index < sphereCount; index++)
            {
                var entry = sphereEntries.GetValue(index);
                if (entry == null)
                {
                    continue;
                }

                var distanceSqr = hasCamera
                    ? (spheres[index].position - cameraPosition).sqrMagnitude
                    : 0f;
                if (!DistanceByEntry.TryGetValue(entry, out var current) ||
                    distanceSqr < current)
                {
                    DistanceByEntry[entry] = distanceSqr;
                }
            }

            if (BuildCandidates.Capacity < queue.Count)
            {
                BuildCandidates.Capacity = queue.Count;
            }

            for (var index = 0; index < queue.Count; index++)
            {
                var entry = queue[index];
                var distanceSqr = float.MaxValue;
                var registered =
                    entry != null &&
                    DistanceByEntry.TryGetValue(entry, out distanceSqr);
                var inRange =
                    entry != null &&
                    (bool)EntryInRangeField.GetValue(entry);
                var visible =
                    entry != null &&
                    (bool)EntryVisibleField.GetValue(entry);
                BuildCandidates.Add(
                    new BuildCandidate(
                        entry,
                        FuseTrackStreamScheduling.IsCurrentBuildRequest(
                            inRange,
                            registered),
                        visible,
                        distanceSqr,
                        index));
            }

            BuildCandidates.Sort(BuildCandidateComparer.Instance);
            queue.Clear();
            for (var index = 0; index < BuildCandidates.Count; index++)
            {
                queue.Add(BuildCandidates[index].Entry);
            }
        }

        private static void DisableRuntime(string operation, Exception ex)
        {
            if (_runtimeDisabled)
            {
                return;
            }

            _runtimeDisabled = true;
            FuseLog.Warning(
                $"FUSE streamed-track queue optimization disabled after it could not {operation}: " +
                $"{ex.GetBaseException().Message}. Railroader's original queue methods will run unchanged.");
        }

        private readonly struct BuildCandidate
        {
            internal BuildCandidate(
                object entry,
                bool current,
                bool visible,
                float distanceSqr,
                int sequence)
            {
                Entry = entry;
                Current = current;
                Visible = visible;
                DistanceSqr = distanceSqr;
                Sequence = sequence;
            }

            internal object Entry { get; }
            internal bool Current { get; }
            internal bool Visible { get; }
            internal float DistanceSqr { get; }
            internal int Sequence { get; }
        }

        private sealed class BuildCandidateComparer : IComparer<BuildCandidate>
        {
            internal static readonly BuildCandidateComparer Instance =
                new BuildCandidateComparer();

            public int Compare(BuildCandidate left, BuildCandidate right)
            {
                return FuseTrackStreamScheduling.CompareBuildPriority(
                    left.Current,
                    left.Visible,
                    left.DistanceSqr,
                    left.Sequence,
                    right.Current,
                    right.Visible,
                    right.DistanceSqr,
                    right.Sequence);
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance =
                new ReferenceComparer();

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }

    [HarmonyPatch(typeof(TrackRebuilder), "WorkBuildQueue")]
    internal static class FuseTrackRebuilderBuildQueuePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(TrackRebuilder __instance)
        {
            return FuseTrackRebuilderQueueProcessor
                .ShouldRunOriginalBuildQueue(__instance);
        }
    }

    [HarmonyPatch(typeof(TrackRebuilder), "WorkDestroyQueue")]
    internal static class FuseTrackRebuilderDestroyQueuePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(TrackRebuilder __instance)
        {
            return FuseTrackRebuilderQueueProcessor
                .ShouldRunOriginalDestroyQueue(__instance);
        }
    }

    [HarmonyPatch(typeof(TrackRebuilder), nameof(TrackRebuilder.Clear))]
    internal static class FuseTrackRebuilderClearPatch
    {
        private static void Postfix()
        {
            FuseTrackRebuilderQueueProcessor.OnQueueCleared();
        }
    }
}
