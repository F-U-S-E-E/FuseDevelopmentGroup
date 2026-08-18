using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Moves TimeSync Mod's periodic synchronization onto Unity's main thread.
    /// TimeSync 1.0.26128.1713 uses <see cref="System.Threading.Timer"/> and calls
    /// StateManager.ApplyLocal directly from its worker callback. That call logs
    /// to the in-game console, which creates TextMeshPro objects and native Mesh
    /// instances; Unity crashes when that work runs off the main thread.
    ///
    /// The Harmony prefix is intentionally tiny on the timer thread: it queues
    /// the original receiver and argument, then suppresses that invocation. The
    /// always-on FUSE runtime pump replays the method on the next frame. Harmony
    /// re-enters this prefix there, recognizes the recorded main thread, and lets
    /// the original method execute unchanged.
    /// </summary>
    internal static class FuseTimeSyncMainThreadGuardPatches
    {
        private static readonly ConcurrentQueue<PendingSync> Pending =
            new ConcurrentQueue<PendingSync>();

        private static bool _syncTimesPatched;
        private static MethodInfo _syncTimesMethod;
        private static int _mainThreadId;

        internal static bool Installed => _syncTimesPatched;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_syncTimesPatched)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var timeSyncType = AccessTools.TypeByName("TimeSyncMod.TimeSyncMod");
            if (timeSyncType == null)
            {
                return "idle (not present)";
            }

            var syncTimes = AccessTools.DeclaredMethod(
                timeSyncType,
                "SyncTimes",
                new[] { typeof(object) });
            if (syncTimes == null)
            {
                return "idle (surface changed)";
            }

            _syncTimesMethod = syncTimes;
            harmony.Patch(
                syncTimes,
                prefix: new HarmonyMethod(
                    typeof(FuseTimeSyncMainThreadGuardPatches),
                    nameof(SyncTimesPrefix)));
            _syncTimesPatched = true;
            return "installed";
        }

        internal static bool ShouldDefer(int currentThreadId, int mainThreadId)
        {
            return mainThreadId == 0 || currentThreadId != mainThreadId;
        }

        private static bool SyncTimesPrefix(object __instance, object __0)
        {
            if (!ShouldDefer(
                    Environment.CurrentManagedThreadId,
                    Volatile.Read(ref _mainThreadId)))
            {
                return true;
            }

            Pending.Enqueue(new PendingSync(__instance, __0));
            return false;
        }

        /// <summary>
        /// Replays queued timer callbacks. Called once per frame by
        /// <c>FuseRuntimePump</c>, so this method also records the authoritative
        /// Unity main-thread id before invoking any queued work.
        /// </summary>
        internal static void DrainPending()
        {
            Volatile.Write(ref _mainThreadId, Environment.CurrentManagedThreadId);

            while (Pending.TryDequeue(out var pending))
            {
                try
                {
                    _syncTimesMethod?.Invoke(
                        pending.Instance,
                        new[] { pending.StateInfo });

                    var marshaled = FuseRuntimeGuardCounters.RecordTimeSyncMainThreadMarshaled();
                    if (FuseGuardLog.ShouldLog(marshaled))
                    {
                        FuseLog.Warning(
                            $"FUSE moved TimeSync Mod timer callback #{marshaled} onto Unity's main thread " +
                            "to prevent its background-thread console/UI crash.");
                    }
                }
                catch (TargetInvocationException ex)
                {
                    FuseLog.Exception(
                        "FUSE TimeSync main-thread callback failed",
                        ex.InnerException ?? ex);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE TimeSync main-thread callback failed", ex);
                }
            }
        }

        private sealed class PendingSync
        {
            internal PendingSync(object instance, object stateInfo)
            {
                Instance = instance;
                StateInfo = stateInfo;
            }

            internal object Instance { get; }

            internal object StateInfo { get; }
        }
    }
}
