using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FUSE.Infrastructure;
using FUSE.Runtime.API;
using HarmonyLib;
using Helpers;
using Helpers.Culling;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Railroader iterates CullingManager._needsUpdate directly. A handler may
    /// add or remove a token during CullingSphereStateChanged, invalidating the
    /// live HashSet enumerator and sending Unity through its synchronous crash
    /// reporter. Drain a reusable snapshot instead; changes raised by handlers
    /// remain in the set for the next frame.
    /// </summary>
    [HarmonyPatch(typeof(CullingManager), "Update")]
    internal static class FuseCullingManagerUpdateRacePatch
    {
        internal const float DestinationCameraJumpDistance = 100f;
        internal const int DestinationReconciliationPasses = 3;
        internal const int ReconciliationBatchSize = 32;

        private const float DestinationCameraJumpDistanceSqr =
            DestinationCameraJumpDistance * DestinationCameraJumpDistance;
        private const float ImmediateDestinationDistanceSqr =
            FuseSceneryLoadThrottlePatch.ImmediateSceneryPriorityDistance *
            FuseSceneryLoadThrottlePatch.ImmediateSceneryPriorityDistance;
        private const int FirstFollowupDelayFrames = 15;
        private const int FinalFollowupDelayFrames = 45;

        private sealed class SnapshotState
        {
            internal readonly List<CullingManager.Token> Tokens =
                new List<CullingManager.Token>(32);

            internal readonly List<ReconciliationCandidate>
                ReconciliationCandidates =
                    new List<ReconciliationCandidate>(128);

            internal int ReconciliationCandidateIndex;
            internal int ReconciliationPassesRemaining;
            internal int NextReconciliationFrame;
            internal bool HasCameraPosition;
            internal Vector3 CameraPosition;
        }

        private static ConditionalWeakTable<CullingManager, SnapshotState>
            _snapshotStates =
                new ConditionalWeakTable<CullingManager, SnapshotState>();

        private static readonly AccessTools.FieldRef<
            CullingManager,
            CullingGroup> CullingGroupRef =
                BindField<CullingGroup>("_cullingGroup");

        private static readonly AccessTools.FieldRef<
            CullingManager,
            BoundingSphere[]> SpheresRef =
                BindField<BoundingSphere[]>("_spheres");

        private static readonly AccessTools.FieldRef<
            CullingManager,
            float[]> DistancesRef =
                BindField<float[]>("_distances");

        private static readonly AccessTools.FieldRef<
            CullingManager,
            HashSet<CullingManager.Token>> NeedsUpdateRef =
                BindField<HashSet<CullingManager.Token>>("_needsUpdate");

        private static readonly AccessTools.FieldRef<
            CullingManager,
            List<CullingManager.Token>> ManagerTokensRef =
                BindField<List<CullingManager.Token>>("_tokens");

        private static long _destinationReconciliationRequests;
        private static long _destinationReconciliationCallbacks;
        private static bool _loggedCandidateFailure;

        internal static bool Available =>
            CullingGroupRef != null &&
            SpheresRef != null &&
            DistancesRef != null &&
            NeedsUpdateRef != null;

        internal static bool DestinationReconciliationAvailable =>
            Available && ManagerTokensRef != null;

        internal static long DestinationReconciliationRequests =>
            _destinationReconciliationRequests;

        internal static long DestinationReconciliationCallbacks =>
            _destinationReconciliationCallbacks;

        private static bool Prefix(CullingManager __instance)
        {
            if (!Available || __instance == null)
            {
                return true;
            }

            var cullingGroup = CullingGroupRef(__instance);
            if (cullingGroup == null)
            {
                return false;
            }

            var needsUpdate = NeedsUpdateRef(__instance);
            var spheres = SpheresRef(__instance);
            var distances = DistancesRef(__instance);
            if (needsUpdate == null || spheres == null || distances == null)
            {
                return false;
            }

            var state = _snapshotStates.GetOrCreateValue(__instance);
            if (IsSceneryManager(__instance))
            {
                ObserveDestinationCamera(
                    __instance,
                    state,
                    cullingGroup);
                QueueDestinationReconciliation(
                    __instance,
                    state,
                    cullingGroup,
                    spheres,
                    distances,
                    needsUpdate);
            }

            if (needsUpdate.Count == 0)
            {
                return false;
            }

            var snapshot = state.Tokens;
            snapshot.Clear();
            snapshot.AddRange(needsUpdate);
            needsUpdate.Clear();

            for (var index = 0; index < snapshot.Count; index++)
            {
                var token = snapshot[index];
                var handler = token?.Handler;
                if (handler == null ||
                    token.Index < 0 ||
                    token.Index >= spheres.Length)
                {
                    continue;
                }

                var distanceBand = cullingGroup.CalculateDistanceBand(
                    spheres[token.Index].position,
                    distances);
                var isVisible = cullingGroup.IsVisible(token.Index);
                handler.CullingSphereStateChanged(
                    isVisible,
                    distanceBand);
            }

            snapshot.Clear();
            return false;
        }

        /// <summary>
        /// Pure threshold decision used by tests. The first live camera requires a
        /// pass, as does a one-frame jump of at least
        /// <see cref="DestinationCameraJumpDistance"/>.
        /// </summary>
        internal static bool ShouldRequestDestinationReconciliation(
            bool hasPreviousCameraPosition,
            float cameraMovementSqr)
        {
            return !hasPreviousCameraPosition ||
                   cameraMovementSqr >= DestinationCameraJumpDistanceSqr;
        }

        /// <summary>
        /// Pure eligibility decision used by tests and the reconciliation scan.
        /// Band 2 is the game's outermost loaded scenery band.
        /// </summary>
        internal static bool ShouldReconcileScenery(
            bool isActiveAndEnabled,
            int distanceBand)
        {
            return isActiveAndEnabled && distanceBand <= 2;
        }

        internal static void RequestDestinationReconciliation(
            CullingManager manager)
        {
            if (!DestinationReconciliationAvailable ||
                manager == null ||
                !IsSceneryManager(manager))
            {
                return;
            }

            var state = _snapshotStates.GetOrCreateValue(manager);
            state.ReconciliationCandidates.Clear();
            state.ReconciliationCandidateIndex = 0;
            state.ReconciliationPassesRemaining =
                DestinationReconciliationPasses;
            state.NextReconciliationFrame = Time.frameCount;
            _destinationReconciliationRequests++;
        }

        internal static void ResetStats()
        {
            _destinationReconciliationRequests = 0;
            _destinationReconciliationCallbacks = 0;
            _loggedCandidateFailure = false;
            _snapshotStates =
                new ConditionalWeakTable<CullingManager, SnapshotState>();
        }

        private static void ObserveDestinationCamera(
            CullingManager manager,
            SnapshotState state,
            CullingGroup cullingGroup)
        {
            var camera = cullingGroup.targetCamera;
            if (camera == null)
            {
                return;
            }

            var cameraPosition = camera.transform.position;
            var movementSqr = state.HasCameraPosition
                ? (cameraPosition - state.CameraPosition).sqrMagnitude
                : 0f;
            var shouldRequest =
                ShouldRequestDestinationReconciliation(
                    state.HasCameraPosition,
                    movementSqr);

            state.HasCameraPosition = true;
            state.CameraPosition = cameraPosition;
            if (shouldRequest)
            {
                RequestDestinationReconciliation(manager);
            }
        }

        private static void QueueDestinationReconciliation(
            CullingManager manager,
            SnapshotState state,
            CullingGroup cullingGroup,
            BoundingSphere[] spheres,
            float[] distances,
            HashSet<CullingManager.Token> needsUpdate)
        {
            if (!DestinationReconciliationAvailable)
            {
                return;
            }

            if (state.ReconciliationCandidateIndex >=
                state.ReconciliationCandidates.Count)
            {
                state.ReconciliationCandidates.Clear();
                state.ReconciliationCandidateIndex = 0;

                if (state.ReconciliationPassesRemaining <= 0 ||
                    Time.frameCount < state.NextReconciliationFrame)
                {
                    return;
                }

                BuildReconciliationCandidates(
                    manager,
                    state,
                    cullingGroup,
                    spheres,
                    distances);
                state.ReconciliationPassesRemaining--;
                state.NextReconciliationFrame =
                    Time.frameCount +
                    (state.ReconciliationPassesRemaining == 1
                        ? FinalFollowupDelayFrames
                        : FirstFollowupDelayFrames);
            }

            var added = 0;
            while (state.ReconciliationCandidateIndex <
                   state.ReconciliationCandidates.Count)
            {
                var candidate =
                    state.ReconciliationCandidates[
                        state.ReconciliationCandidateIndex];
                if (added >= ReconciliationBatchSize &&
                    candidate.DistanceSqr >
                    ImmediateDestinationDistanceSqr)
                {
                    break;
                }

                state.ReconciliationCandidateIndex++;
                if (candidate.Token?.Handler == null)
                {
                    continue;
                }

                if (needsUpdate.Add(candidate.Token))
                {
                    added++;
                }
            }

            _destinationReconciliationCallbacks += added;
        }

        private static void BuildReconciliationCandidates(
            CullingManager manager,
            SnapshotState state,
            CullingGroup cullingGroup,
            BoundingSphere[] spheres,
            float[] distances)
        {
            var managerTokens = ManagerTokensRef(manager);
            var camera = cullingGroup.targetCamera;
            if (managerTokens == null || camera == null)
            {
                return;
            }

            var cameraPosition = camera.transform.position;
            for (var index = 0; index < managerTokens.Count; index++)
            {
                var token = managerTokens[index];
                var instance = token?.Handler as SceneryAssetInstance;
                if (instance == null ||
                    token.Index < 0 ||
                    token.Index >= spheres.Length)
                {
                    continue;
                }

                try
                {
                    if (instance.GetComponent<
                            SceneryAPI.FuseSceneryMarker>() == null)
                    {
                        continue;
                    }

                    var spherePosition = spheres[token.Index].position;
                    var distanceBand =
                        cullingGroup.CalculateDistanceBand(
                            spherePosition,
                            distances);
                    if (!ShouldReconcileScenery(
                            instance.isActiveAndEnabled,
                            distanceBand))
                    {
                        continue;
                    }

                    state.ReconciliationCandidates.Add(
                        new ReconciliationCandidate(
                            token,
                            (spherePosition - cameraPosition)
                                .sqrMagnitude));
                }
                catch (System.Exception ex)
                {
                    // A scenery object can be destroyed while its token is being
                    // retired. Skip that one; the normal token cleanup owns it.
                    // Log only the first unexpected failure so a teardown wave
                    // cannot turn diagnostics into its own stutter.
                    if (!_loggedCandidateFailure)
                    {
                        _loggedCandidateFailure = true;
                        FuseLog.Exception(
                            "FUSE scenery destination reconciliation skipped a token",
                            ex);
                    }
                }
            }

            state.ReconciliationCandidates.Sort(
                ReconciliationCandidateComparer.Instance);
        }

        private static bool IsSceneryManager(CullingManager manager)
        {
            try
            {
                return ReferenceEquals(manager, CullingManager.Scenery);
            }
            catch
            {
                return false;
            }
        }

        private static AccessTools.FieldRef<CullingManager, T> BindField<T>(
            string fieldName)
        {
            try
            {
                var field = AccessTools.Field(
                    typeof(CullingManager),
                    fieldName);
                return field == null
                    ? null
                    : AccessTools.FieldRefAccess<CullingManager, T>(field);
            }
            catch
            {
                return null;
            }
        }

        private readonly struct ReconciliationCandidate
        {
            internal ReconciliationCandidate(
                CullingManager.Token token,
                float distanceSqr)
            {
                Token = token;
                DistanceSqr = distanceSqr;
            }

            internal CullingManager.Token Token { get; }

            internal float DistanceSqr { get; }
        }

        private sealed class ReconciliationCandidateComparer
            : IComparer<ReconciliationCandidate>
        {
            internal static readonly ReconciliationCandidateComparer Instance =
                new ReconciliationCandidateComparer();

            public int Compare(
                ReconciliationCandidate left,
                ReconciliationCandidate right)
            {
                return left.DistanceSqr.CompareTo(right.DistanceSqr);
            }
        }
    }

    /// <summary>
    /// Railroader's world-shift callback refreshes every culling sphere position but
    /// does not add those tokens to CullingManager._needsUpdate. Request an exact,
    /// bounded scenery-state reconciliation after the positions have moved.
    /// </summary>
    [HarmonyPatch(
        typeof(CullingManager),
        "OnWorldDidMove",
        new[] { typeof(Vector3) })]
    internal static class FuseSceneryWorldMoveReconciliationPatch
    {
        private static void Postfix(CullingManager __instance)
        {
            FuseCullingManagerUpdateRacePatch
                .RequestDestinationReconciliation(__instance);
        }
    }
}
