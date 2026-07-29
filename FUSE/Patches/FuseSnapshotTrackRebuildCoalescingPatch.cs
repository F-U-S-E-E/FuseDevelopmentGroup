using System;
using System.Diagnostics;
using System.Reflection;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using Game.Progression;
using HarmonyLib;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// During snapshot restore, MapFeatureManager schedules a full track mesh
    /// rebuild for the next frame. That frame is also where completed car and
    /// scenery asset tasks run, producing the repeatable post-load mega-frame.
    ///
    /// FUSE keeps MapFeatureManager's coalescing semantics, cancels only that
    /// snapshot-scoped delayed coroutine, and performs the same rebuild once at
    /// the successful end of StateManager.PopulateFromRemoteSnapshot. All feature
    /// changes have settled by then, while queued asset continuations have not yet
    /// reached the next Unity frame.
    /// </summary>
    internal static class FuseSnapshotTrackRebuildCoordinator
    {
        private static bool _pending;
        private static MapFeatureManager _owner;
        private static Coroutine _scheduled;

        internal static void Request(
            MapFeatureManager owner,
            Coroutine scheduled)
        {
            _pending = true;
            _owner = owner;
            _scheduled = scheduled;
        }

        internal static void Flush()
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
            var manager = TrackObjectManager.Instance;
            if (manager == null)
            {
                FuseLog.Warning(
                    "FUSE could not flush the coalesced snapshot track rebuild " +
                    "because TrackObjectManager.Instance was unavailable.");
                ClearReferences();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                manager.Rebuild();

                // Cancel the stock next-frame invocation only after the equivalent
                // immediate rebuild succeeds. If it throws, the untouched coroutine
                // remains Railroader's fail-open retry.
                if (_owner != null && _scheduled != null)
                {
                    _owner.StopCoroutine(_scheduled);
                    var current =
                        FuseMapFeatureSnapshotTrackRebuildPatch
                            .ScheduledRebuildTrackField
                            ?.GetValue(_owner);
                    if (ReferenceEquals(current, _scheduled))
                    {
                        FuseMapFeatureSnapshotTrackRebuildPatch
                            .ScheduledRebuildTrackField
                            .SetValue(_owner, null);
                    }
                }

                FuseLog.Info(
                    "FUSE coalesced Railroader's delayed snapshot track rebuild into " +
                    $"the snapshot transaction elapsedMs={stopwatch.ElapsedMilliseconds}.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE immediate snapshot track rebuild failed; Railroader's " +
                    "original delayed rebuild remains scheduled",
                    ex);
            }
            finally
            {
                ClearReferences();
            }
        }

        internal static void Cancel()
        {
            _pending = false;
            ClearReferences();
        }

        private static void ClearReferences()
        {
            _owner = null;
            _scheduled = null;
        }
    }

    [HarmonyPatch]
    internal static class FuseMapFeatureSnapshotTrackRebuildPatch
    {
        internal static readonly FieldInfo ScheduledRebuildTrackField =
            AccessTools.Field(
                typeof(MapFeatureManager),
                "_scheduledRebuildTrack");

        private static MethodInfo TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapFeatureManager),
                "HandleFeatureEnablesChanged");
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MapFeatureManager __instance)
        {
            if (__instance == null ||
                !FuseRuntimeRebindService.IsSnapshotRestoreInProgress ||
                ScheduledRebuildTrackField == null)
            {
                return;
            }

            var scheduled =
                ScheduledRebuildTrackField.GetValue(__instance) as Coroutine;
            if (scheduled == null)
            {
                return;
            }

            // StartCoroutine has already advanced DelayedRebuildTrack to its
            // first yield. Retain its exact handle as a fail-open fallback; the
            // transaction postfix cancels it only after an immediate rebuild
            // succeeds.
            FuseSnapshotTrackRebuildCoordinator.Request(
                __instance,
                scheduled);
        }
    }
}
