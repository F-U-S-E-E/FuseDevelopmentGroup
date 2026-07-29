using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssetPack.Runtime;
using FUSE.Infrastructure;
using FUSE.Interface;
using HarmonyLib;
using Model;
using RollingStock;
using RollingStock.LoadModels;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Pure decisions shared by the equipment-retention patches and their
    /// game-free tests.
    /// </summary>
    internal static class FuseEquipmentRetentionPolicy
    {
        internal const double GameplayCompletionBudgetMilliseconds = 6d;
        internal const double LoadingCompletionBudgetMilliseconds = 35d;
        internal const int GameplayMaxCompletionsPerFrame = 4;
        internal const int LoadingMaxCompletionsPerFrame = 32;
        internal const double GameplayUnloadReleaseBudgetMilliseconds = 1d;
        internal const double LoadingUnloadReleaseBudgetMilliseconds = 4d;
        internal const int GameplayMaxUnloadReleasesPerFrame = 8;
        internal const int LoadingMaxUnloadReleasesPerFrame = 64;
        internal const double GameplayLoadRetainBudgetMilliseconds = 2d;
        internal const double LoadingLoadRetainBudgetMilliseconds = 8d;
        internal const int GameplayMaxLoadRetainsPerFrame = 8;
        internal const int LoadingMaxLoadRetainsPerFrame = 24;
        internal const int SceneryBusyMaxLoadRetainsPerFrame = 2;
        internal const int SceneryBusyMaxCompletionsPerFrame = 1;

        internal static bool ShouldReleaseAbandonedModel(bool modelLoadPending)
        {
            return !modelLoadPending;
        }

        internal static bool CanCompleteAnother(
            int completed,
            double elapsedMilliseconds,
            double budgetMilliseconds,
            int maximumPerFrame)
        {
            return completed == 0 ||
                   (completed < maximumPerFrame &&
                    elapsedMilliseconds < budgetMilliseconds);
        }

        internal static int LoadRetainMaximum(
            bool loading,
            bool sceneryBusy)
        {
            if (sceneryBusy)
            {
                return SceneryBusyMaxLoadRetainsPerFrame;
            }

            return loading
                ? LoadingMaxLoadRetainsPerFrame
                : GameplayMaxLoadRetainsPerFrame;
        }

        internal static int CompletionMaximum(
            bool loading,
            bool sceneryBusy)
        {
            if (sceneryBusy)
            {
                return SceneryBusyMaxCompletionsPerFrame;
            }

            return loading
                ? LoadingMaxCompletionsPerFrame
                : GameplayMaxCompletionsPerFrame;
        }

        /// <summary>
        /// Orders low priority first because the runtime queue is consumed from
        /// its tail. Visible and nearer equipment completes first.
        /// </summary>
        internal static int CompareCompletionPriority(
            bool leftVisible,
            float leftDistanceSqr,
            int leftSequence,
            bool rightVisible,
            float rightDistanceSqr,
            int rightSequence)
        {
            var comparison = leftVisible.CompareTo(rightVisible);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = rightDistanceSqr.CompareTo(leftDistanceSqr);
            if (comparison != 0)
            {
                return comparison;
            }

            return rightSequence.CompareTo(leftSequence);
        }

        internal static int CompareLoadPriority(
            bool leftVisible,
            float leftDistanceSqr,
            int leftSequence,
            bool rightVisible,
            float rightDistanceSqr,
            int rightSequence)
        {
            var comparison = rightVisible.CompareTo(leftVisible);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = leftDistanceSqr.CompareTo(rightDistanceSqr);
            if (comparison != 0)
            {
                return comparison;
            }

            return leftSequence.CompareTo(rightSequence);
        }
    }

    /// <summary>
    /// Processes all cheap equipment-load retains immediately, but staggers
    /// culler unload releases. Railroader drains the entire private dictionary
    /// in one Update; after a teleport that makes every delayed car teardown
    /// mature on the same later frame.
    /// </summary>
    internal static class FuseCarCullerPendingProcessor
    {
        private static readonly FieldInfo PendingField =
            AccessTools.Field(typeof(CarCuller), "_pending");
        private static readonly Type RecordType =
            typeof(CarCuller).GetNestedType(
                "Record",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type ActionType =
            typeof(CarCuller).GetNestedType(
                "Action",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RecordCarField =
            RecordType?.GetField(
                "Car",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo RecordLoadTokenField =
            RecordType?.GetField(
                "LoadToken",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly object LoadAction =
            ResolveAction("Load");
        private static readonly object UnloadAction =
            ResolveAction("Unload");
        private static readonly List<PendingAction> PendingSnapshot =
            new List<PendingAction>();

        private static bool _runtimeDisabled;
        private static bool _activationLogged;
        private static int _queueDepth;
        private static int _peakQueueDepth;
        private static long _loadRetains;
        private static long _unloadReleases;
        private static long _sceneryYieldFrames;

        internal static bool Available =>
            PendingField != null &&
            RecordType != null &&
            ActionType != null &&
            RecordCarField != null &&
            RecordLoadTokenField != null &&
            typeof(Car).IsAssignableFrom(RecordCarField.FieldType) &&
            typeof(IDisposable).IsAssignableFrom(RecordLoadTokenField.FieldType) &&
            LoadAction != null &&
            UnloadAction != null;

        internal static int QueueDepth => _queueDepth;
        internal static int PeakQueueDepth => _peakQueueDepth;
        internal static long LoadRetains => Interlocked.Read(ref _loadRetains);
        internal static long UnloadReleases => Interlocked.Read(ref _unloadReleases);
        internal static long SceneryYieldFrames =>
            Interlocked.Read(ref _sceneryYieldFrames);

        /// <summary>
        /// Returns true only when Harmony should run Railroader's original
        /// unbounded dictionary drain.
        /// </summary>
        internal static bool ShouldRunOriginal(CarCuller culler)
        {
            if (_runtimeDisabled ||
                !Available ||
                culler == null ||
                !Application.isPlaying)
            {
                return true;
            }

            IDictionary pending;
            try
            {
                pending = PendingField.GetValue(culler) as IDictionary;
            }
            catch (Exception ex)
            {
                DisableRuntime("read CarCuller._pending", ex);
                return true;
            }

            if (pending == null)
            {
                DisableRuntime(
                    "bind CarCuller._pending",
                    new InvalidOperationException(
                        "CarCuller._pending no longer implements IDictionary."));
                return true;
            }

            _queueDepth = pending.Count;
            _peakQueueDepth = Math.Max(_peakQueueDepth, _queueDepth);
            if (pending.Count == 0)
            {
                return false;
            }

            try
            {
                Snapshot(pending);
                PendingSnapshot.Sort(PendingActionComparer.Instance);

                var loading = FuseLoadingScreen.IsShowing;
                var sceneryBusy =
                    FuseSceneryLoadThrottlePatch.QueueDepth > 0 ||
                    FuseSceneryLoadThrottlePatch.InFlightLoads > 0;
                var loadBudget = loading
                    ? FuseEquipmentRetentionPolicy.LoadingLoadRetainBudgetMilliseconds
                    : FuseEquipmentRetentionPolicy.GameplayLoadRetainBudgetMilliseconds;
                var loadMaximum =
                    FuseEquipmentRetentionPolicy.LoadRetainMaximum(
                        loading,
                        sceneryBusy);
                if (sceneryBusy)
                {
                    Interlocked.Increment(ref _sceneryYieldFrames);
                }
                var loadStopwatch = Stopwatch.StartNew();
                var retainedThisFrame = 0;

                // Initiate visible/nearest equipment first and keep the broad
                // 1,500 m culling band from flooding asset I/O in one frame.
                for (var index = 0;
                     index < PendingSnapshot.Count &&
                     FuseEquipmentRetentionPolicy.CanCompleteAnother(
                         retainedThisFrame,
                         loadStopwatch.Elapsed.TotalMilliseconds,
                         loadBudget,
                         loadMaximum);
                     index++)
                {
                    var item = PendingSnapshot[index];
                    if (!item.IsLoad || !IsStillCurrent(pending, item))
                    {
                        continue;
                    }

                    var car = RecordCarField.GetValue(item.Record) as Car;
                    if (car == null)
                    {
                        pending.Remove(item.Record);
                        continue;
                    }

                    var token =
                        RecordLoadTokenField.GetValue(item.Record) as IDisposable;
                    if (token == null)
                    {
                        token = car.ModelLoadRetain("CarCuller");
                        RecordLoadTokenField.SetValue(item.Record, token);
                        retainedThisFrame++;
                        Interlocked.Increment(ref _loadRetains);
                    }

                    pending.Remove(item.Record);
                }

                var budget = loading
                    ? FuseEquipmentRetentionPolicy.LoadingUnloadReleaseBudgetMilliseconds
                    : FuseEquipmentRetentionPolicy.GameplayUnloadReleaseBudgetMilliseconds;
                var maximum = loading
                    ? FuseEquipmentRetentionPolicy.LoadingMaxUnloadReleasesPerFrame
                    : FuseEquipmentRetentionPolicy.GameplayMaxUnloadReleasesPerFrame;
                var stopwatch = Stopwatch.StartNew();
                var releasedThisFrame = 0;

                for (var index = 0;
                     index < PendingSnapshot.Count &&
                     FuseEquipmentRetentionPolicy.CanCompleteAnother(
                         releasedThisFrame,
                         stopwatch.Elapsed.TotalMilliseconds,
                         budget,
                         maximum);
                     index++)
                {
                    var item = PendingSnapshot[index];
                    if (item.IsLoad || !IsStillCurrent(pending, item))
                    {
                        continue;
                    }

                    var token =
                        RecordLoadTokenField.GetValue(item.Record) as IDisposable;
                    if (token != null)
                    {
                        token.Dispose();
                        RecordLoadTokenField.SetValue(item.Record, null);
                        releasedThisFrame++;
                        Interlocked.Increment(ref _unloadReleases);
                    }

                    pending.Remove(item.Record);
                }

                _queueDepth = pending.Count;
                if (!_activationLogged)
                {
                    _activationLogged = true;
                    FuseLog.Info(
                        "FUSE staggered equipment culler unload releases: " +
                        $"gameplayLoadCap={FuseEquipmentRetentionPolicy.GameplayMaxLoadRetainsPerFrame}, " +
                        $"loadingLoadCap={FuseEquipmentRetentionPolicy.LoadingMaxLoadRetainsPerFrame}, " +
                        $"sceneryBusyLoadCap={FuseEquipmentRetentionPolicy.SceneryBusyMaxLoadRetainsPerFrame}, " +
                        $"gameplayCap={FuseEquipmentRetentionPolicy.GameplayMaxUnloadReleasesPerFrame}, " +
                        $"loadingCap={FuseEquipmentRetentionPolicy.LoadingMaxUnloadReleasesPerFrame}. " +
                        "Visible/nearest equipment loads first.");
                }

                return false;
            }
            catch (Exception ex)
            {
                DisableRuntime("process CarCuller pending actions", ex);
                return true;
            }
            finally
            {
                PendingSnapshot.Clear();
            }
        }

        internal static void Shutdown()
        {
            _runtimeDisabled = false;
            _activationLogged = false;
            _queueDepth = 0;
            _peakQueueDepth = 0;
            _loadRetains = 0;
            _unloadReleases = 0;
            _sceneryYieldFrames = 0;
            PendingSnapshot.Clear();
        }

        private static void Snapshot(IDictionary pending)
        {
            PendingSnapshot.Clear();
            if (PendingSnapshot.Capacity < pending.Count)
            {
                PendingSnapshot.Capacity = pending.Count;
            }

            var camera = FuseSceneryCameraRef.Resolve();
            var hasCamera = camera != null;
            var cameraPosition = hasCamera
                ? camera.transform.position
                : Vector3.zero;
            var sequence = 0;
            foreach (DictionaryEntry entry in pending)
            {
                var isLoad = Equals(entry.Value, LoadAction);
                var car = RecordCarField.GetValue(entry.Key) as Car;
                var visible = car != null && car.IsVisible;
                var distanceSqr =
                    hasCamera && car != null
                        ? (car.transform.position - cameraPosition).sqrMagnitude
                        : 0f;
                PendingSnapshot.Add(
                    new PendingAction(
                        entry.Key,
                        entry.Value,
                        isLoad,
                        visible,
                        distanceSqr,
                        sequence++));
            }
        }

        private static bool IsStillCurrent(
            IDictionary pending,
            PendingAction item)
        {
            return pending.Contains(item.Record) &&
                   Equals(pending[item.Record], item.Action);
        }

        private static void DisableRuntime(string operation, Exception ex)
        {
            if (_runtimeDisabled)
            {
                return;
            }

            _runtimeDisabled = true;
            FuseLog.Warning(
                $"FUSE equipment culler optimization disabled after it could not {operation}: " +
                $"{ex.GetBaseException().Message}. Railroader's original method will run unchanged.");
        }

        private static object ResolveAction(string name)
        {
            try
            {
                return ActionType != null
                    ? Enum.Parse(ActionType, name)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private readonly struct PendingAction
        {
            internal PendingAction(
                object record,
                object action,
                bool isLoad,
                bool visible,
                float distanceSqr,
                int sequence)
            {
                Record = record;
                Action = action;
                IsLoad = isLoad;
                Visible = visible;
                DistanceSqr = distanceSqr;
                Sequence = sequence;
            }

            internal object Record { get; }
            internal object Action { get; }
            internal bool IsLoad { get; }
            internal bool Visible { get; }
            internal float DistanceSqr { get; }
            internal int Sequence { get; }
        }

        private sealed class PendingActionComparer : IComparer<PendingAction>
        {
            internal static readonly PendingActionComparer Instance =
                new PendingActionComparer();

            public int Compare(PendingAction left, PendingAction right)
            {
                if (left.IsLoad != right.IsLoad)
                {
                    return left.IsLoad ? -1 : 1;
                }

                return FuseEquipmentRetentionPolicy.CompareLoadPriority(
                    left.Visible,
                    left.DistanceSqr,
                    left.Sequence,
                    right.Visible,
                    right.DistanceSqr,
                    right.Sequence);
            }
        }
    }

    [HarmonyPatch(typeof(CarCuller), "ProcessPending")]
    internal static class FuseCarCullerProcessPendingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CarCuller __instance)
        {
            return FuseCarCullerPendingProcessor.ShouldRunOriginal(__instance);
        }
    }

    /// <summary>
    /// Releases a car-body asset reference when the car left model range while
    /// its asynchronous load was still in flight.
    ///
    /// Stock <c>Car.UnloadModels</c> clears <c>_modelLoadPending</c> and returns
    /// early while <c>BodyTransform</c> is null. When the request later
    /// completes, <c>HandleModelsLoaded</c> notices the cleared flag and also
    /// returns, but it never disposes or removes the completed references in
    /// <c>_modelLoadTasks</c>. That permanently pins the equipment prefab and
    /// potentially the rest of its asset bundle. Normal, still-requested car
    /// loads are not changed.
    /// </summary>
    internal static class FuseCarModelCompletionScheduler
    {
        private static readonly MethodInfo HandleModelsLoadedMethod =
            AccessTools.Method(typeof(Car), "HandleModelsLoaded");

        private static readonly AccessTools.FieldRef<Car, bool> ModelLoadPendingRef =
            BindField<bool>("_modelLoadPending");

        private static readonly AccessTools.FieldRef<
            Car,
            Dictionary<string, Task<LoadedAssetReference<GameObject>>>> ModelLoadTasksRef =
            BindField<Dictionary<string, Task<LoadedAssetReference<GameObject>>>>(
                "_modelLoadTasks");

        private static readonly List<Car> Pending = new List<Car>();
        private static readonly HashSet<int> PendingIds = new HashSet<int>();
        private static readonly List<CompletionCandidate> CompletionCandidates =
            new List<CompletionCandidate>();

        private static Car _allowOriginalFor;
        private static long _releasedReferences;
        private static long _completedModels;
        private static long _sceneryYieldFrames;
        private static int _queueDepth;
        private static int _peakQueueDepth;
        private static int _releaseFailureLogged;
        private static int _completionFailureLogged;
        private static bool _activationLogged;

        internal static long ReleasedReferences => Interlocked.Read(ref _releasedReferences);
        internal static long CompletedModels => Interlocked.Read(ref _completedModels);
        internal static long SceneryYieldFrames =>
            Interlocked.Read(ref _sceneryYieldFrames);
        internal static int QueueDepth => _queueDepth;
        internal static int PeakQueueDepth => _peakQueueDepth;

        internal static bool Available =>
            HandleModelsLoadedMethod != null &&
            ModelLoadPendingRef != null &&
            ModelLoadTasksRef != null;

        /// <summary>
        /// Returns true only when Harmony should run Railroader's original
        /// completion method immediately.
        /// </summary>
        internal static bool ShouldRunOriginal(Car car)
        {
            if (car == null || !Available || !Application.isPlaying)
            {
                return true;
            }

            if (ReferenceEquals(_allowOriginalFor, car))
            {
                return true;
            }

            if (FuseEquipmentRetentionPolicy.ShouldReleaseAbandonedModel(
                    ModelLoadPendingRef(car)))
            {
                ReleaseAbandonedReferences(car);
                return true;
            }

            var id = car.GetInstanceID();
            if (PendingIds.Add(id))
            {
                Pending.Add(car);
                _queueDepth = Pending.Count;
                _peakQueueDepth = Math.Max(_peakQueueDepth, _queueDepth);
            }

            return false;
        }

        internal static void Update()
        {
            if (!Available || Pending.Count == 0)
            {
                _queueDepth = Pending.Count;
                return;
            }

            PrepareNearestFirstQueue();

            var loading = FuseLoadingScreen.IsShowing;
            var sceneryBusy =
                FuseSceneryLoadThrottlePatch.QueueDepth > 0 ||
                FuseSceneryLoadThrottlePatch.InFlightLoads > 0;
            var budget = loading
                ? FuseEquipmentRetentionPolicy.LoadingCompletionBudgetMilliseconds
                : FuseEquipmentRetentionPolicy.GameplayCompletionBudgetMilliseconds;
            var maximum =
                FuseEquipmentRetentionPolicy.CompletionMaximum(
                    loading,
                    sceneryBusy);
            if (sceneryBusy)
            {
                Interlocked.Increment(ref _sceneryYieldFrames);
            }
            var stopwatch = Stopwatch.StartNew();
            var completedThisFrame = 0;

            while (CompletionCandidates.Count > 0 &&
                   FuseEquipmentRetentionPolicy.CanCompleteAnother(
                       completedThisFrame,
                       stopwatch.Elapsed.TotalMilliseconds,
                       budget,
                       maximum))
            {
                var tail = CompletionCandidates.Count - 1;
                var candidate = CompletionCandidates[tail];
                CompletionCandidates.RemoveAt(tail);
                Pending.Remove(candidate.Car);

                var car = candidate.Car;
                if (car == null)
                {
                    continue;
                }

                PendingIds.Remove(car.GetInstanceID());
                if (FuseEquipmentRetentionPolicy.ShouldReleaseAbandonedModel(
                        ModelLoadPendingRef(car)))
                {
                    ReleaseAbandonedReferences(car);
                }

                InvokeOriginal(car);
                completedThisFrame++;
                Interlocked.Increment(ref _completedModels);
            }

            CompletionCandidates.Clear();
            _queueDepth = Pending.Count;
            if (!_activationLogged)
            {
                _activationLogged = true;
                FuseLog.Info(
                    "FUSE bounded asynchronous equipment-model completions: " +
                    $"gameplayBudgetMs={FuseEquipmentRetentionPolicy.GameplayCompletionBudgetMilliseconds:0.#}, " +
                    $"gameplayCap={FuseEquipmentRetentionPolicy.GameplayMaxCompletionsPerFrame}, " +
                    $"loadingBudgetMs={FuseEquipmentRetentionPolicy.LoadingCompletionBudgetMilliseconds:0.#}, " +
                    $"sceneryBusyCap={FuseEquipmentRetentionPolicy.SceneryBusyMaxCompletionsPerFrame}. " +
                    "Visible/nearest equipment completes first.");
            }
        }

        internal static void Shutdown()
        {
            // Harmony is removed before this is called, so invoking the current
            // game method drains any completion that FUSE had already deferred.
            for (var index = 0; index < Pending.Count; index++)
            {
                var car = Pending[index];
                if (car == null)
                {
                    continue;
                }

                try
                {
                    HandleModelsLoadedMethod?.Invoke(car, Array.Empty<object>());
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        "FUSE could not flush a deferred equipment-model completion during shutdown",
                        ex);
                }
            }

            Pending.Clear();
            PendingIds.Clear();
            CompletionCandidates.Clear();
            _allowOriginalFor = null;
            _releasedReferences = 0;
            _completedModels = 0;
            _sceneryYieldFrames = 0;
            _queueDepth = 0;
            _peakQueueDepth = 0;
            _releaseFailureLogged = 0;
            _completionFailureLogged = 0;
            _activationLogged = false;
        }

        private static void InvokeOriginal(Car car)
        {
            try
            {
                _allowOriginalFor = car;
                HandleModelsLoadedMethod.Invoke(car, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _completionFailureLogged, 1) == 0)
                {
                    FuseLog.Exception(
                        "FUSE could not complete a deferred car model; later failures will be suppressed",
                        ex);
                }
            }
            finally
            {
                _allowOriginalFor = null;
            }
        }

        private static void ReleaseAbandonedReferences(Car car)
        {
            var tasks = ModelLoadTasksRef(car);
            if (tasks == null || tasks.Count == 0)
            {
                return;
            }

            try
            {
                foreach (var task in tasks.Values)
                {
                    if (task == null || task.Status != TaskStatus.RanToCompletion)
                    {
                        continue;
                    }

                    var loadedReference = task.Result;
                    if (loadedReference == null)
                    {
                        continue;
                    }

                    loadedReference.Dispose();
                    Interlocked.Increment(ref _releasedReferences);
                }

                tasks.Clear();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _releaseFailureLogged, 1) == 0)
                {
                    FuseLog.Exception(
                        "FUSE could not release an abandoned car-model asset reference; " +
                        "later failures will be suppressed",
                        ex);
                }
            }
        }

        private static void PrepareNearestFirstQueue()
        {
            CompletionCandidates.Clear();
            if (CompletionCandidates.Capacity < Pending.Count)
            {
                CompletionCandidates.Capacity = Pending.Count;
            }

            var camera = FuseSceneryCameraRef.Resolve();
            var hasCamera = camera != null;
            var cameraPosition = hasCamera
                ? camera.transform.position
                : Vector3.zero;

            for (var index = Pending.Count - 1; index >= 0; index--)
            {
                var car = Pending[index];
                if (car == null)
                {
                    Pending.RemoveAt(index);
                    continue;
                }

                var distanceSqr = hasCamera
                    ? (car.transform.position - cameraPosition).sqrMagnitude
                    : 0f;
                CompletionCandidates.Add(
                    new CompletionCandidate(
                        car,
                        car.IsVisible,
                        distanceSqr,
                        index));
            }

            CompletionCandidates.Sort(CompletionCandidateComparer.Instance);
        }

        private static AccessTools.FieldRef<Car, TField> BindField<TField>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<Car, TField>(name);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE equipment retention fix could not bind Car.{name}; " +
                    "the stock path will remain active",
                    ex);
                return null;
            }
        }

        private readonly struct CompletionCandidate
        {
            internal CompletionCandidate(
                Car car,
                bool visible,
                float distanceSqr,
                int sequence)
            {
                Car = car;
                Visible = visible;
                DistanceSqr = distanceSqr;
                Sequence = sequence;
            }

            internal Car Car { get; }
            internal bool Visible { get; }
            internal float DistanceSqr { get; }
            internal int Sequence { get; }
        }

        private sealed class CompletionCandidateComparer :
            IComparer<CompletionCandidate>
        {
            internal static readonly CompletionCandidateComparer Instance =
                new CompletionCandidateComparer();

            public int Compare(
                CompletionCandidate left,
                CompletionCandidate right)
            {
                return FuseEquipmentRetentionPolicy.CompareCompletionPriority(
                    left.Visible,
                    left.DistanceSqr,
                    left.Sequence,
                    right.Visible,
                    right.DistanceSqr,
                    right.Sequence);
            }
        }
    }

    [HarmonyPatch(typeof(Car), "HandleModelsLoaded")]
    internal static class FuseCarModelCompletionPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Car __instance)
        {
            return FuseCarModelCompletionScheduler.ShouldRunOriginal(__instance);
        }
    }

    /// <summary>
    /// Destroys the runtime material cloned for an aggregate cargo mesh.
    /// Stock searches only the root object for a MeshRenderer, while both
    /// renderers live on LOD child objects, so the clone otherwise survives
    /// every cargo-model teardown.
    /// </summary>
    [HarmonyPatch(typeof(AggregateLoadModelController), "DestroyMeshGameObject")]
    internal static class FuseAggregateLoadMaterialReleasePatch
    {
        private static readonly AccessTools.FieldRef<
            AggregateLoadModelController,
            GameObject> MeshGameObjectRef = BindMeshGameObject();

        private static long _releasedMaterials;
        private static int _releaseFailureLogged;

        internal static long ReleasedMaterials => Interlocked.Read(ref _releasedMaterials);

        internal static bool Available => MeshGameObjectRef != null;

        private static void Prefix(AggregateLoadModelController __instance)
        {
            if (__instance == null || MeshGameObjectRef == null)
            {
                return;
            }

            var root = MeshGameObjectRef(__instance);
            if (root == null)
            {
                return;
            }

            try
            {
                var renderers = root.GetComponentsInChildren<MeshRenderer>(
                    includeInactive: true);
                var released = new HashSet<Material>();
                for (var index = 0; index < renderers.Length; index++)
                {
                    var materials = renderers[index].sharedMaterials;
                    for (var materialIndex = 0;
                         materialIndex < materials.Length;
                         materialIndex++)
                    {
                        var material = materials[materialIndex];
                        if (material != null && released.Add(material))
                        {
                            UnityEngine.Object.Destroy(material);
                            Interlocked.Increment(ref _releasedMaterials);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _releaseFailureLogged, 1) == 0)
                {
                    FuseLog.Exception(
                        "FUSE could not release an aggregate-load runtime material; " +
                        "the stock teardown will continue and later failures will be suppressed",
                        ex);
                }
            }
        }

        private static AccessTools.FieldRef<
            AggregateLoadModelController,
            GameObject> BindMeshGameObject()
        {
            try
            {
                return AccessTools.FieldRefAccess<
                    AggregateLoadModelController,
                    GameObject>("_meshGameObject");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE aggregate-load material cleanup could not bind " +
                    "AggregateLoadModelController._meshGameObject; the stock path will remain active",
                    ex);
                return null;
            }
        }
    }
}
