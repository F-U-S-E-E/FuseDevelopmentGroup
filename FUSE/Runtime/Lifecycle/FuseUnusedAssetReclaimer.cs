using System;
using System.Threading;
using FUSE.Infrastructure;
using FUSE.Patches;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Coalesces zero-reference asset request evictions into an unused-asset
    /// sweep after scenery streaming has gone quiet.
    /// </summary>
    internal static class FuseUnusedAssetReclaimer
    {
        internal const int MinimumEvictionsPerSweep = 128;
        internal const long EmergencyTextureMemoryBytes = 7L * 1024L * 1024L * 1024L;
        private const float QuietSeconds = 10f;
        private const float MinimumSweepIntervalSeconds = 300f;
        private const double BytesPerMegabyte = 1024d * 1024d;

        private static int _pendingEvictions;
        private static long _totalEvictions;
        private static long _completedSweeps;
        private static int _lastObservedPending;
        private static float _lastPendingChangeRealtime;
        private static float _lastSweepRealtime = float.NegativeInfinity;
        private static float _activeSweepStartedRealtime;
        private static AsyncOperation _activeSweep;

        internal static int PendingEvictions => Volatile.Read(ref _pendingEvictions);

        internal static long TotalEvictions => Interlocked.Read(ref _totalEvictions);

        internal static long CompletedSweeps => Interlocked.Read(ref _completedSweeps);

        internal static bool SweepInProgress => _activeSweep != null;

        internal static void RecordEviction()
        {
            Interlocked.Increment(ref _pendingEvictions);
            Interlocked.Increment(ref _totalEvictions);
        }

        internal static void Update()
        {
            if (!FuseConstrainedTextureMemoryPolicy.IsApplied)
            {
                return;
            }

            if (_activeSweep != null)
            {
                if (_activeSweep.isDone)
                {
                    var durationSeconds =
                        Time.realtimeSinceStartup - _activeSweepStartedRealtime;
                    var textureAfterMb =
                        Texture.currentTextureMemory / BytesPerMegabyte;
                    _activeSweep = null;
                    Interlocked.Increment(ref _completedSweeps);
                    FuseLog.Info(
                        "FUSE unused-asset sweep completed: " +
                        $"durationSeconds={durationSeconds:F1} " +
                        $"textureCurrentAfterMB={textureAfterMb:F1}.");
                }

                return;
            }

            var pending = PendingEvictions;
            if (pending != _lastObservedPending)
            {
                _lastObservedPending = pending;
                _lastPendingChangeRealtime = Time.realtimeSinceStartup;
                return;
            }

            var textureCurrentBytes = ToSignedBytes(Texture.currentTextureMemory);
            if (!HasEnoughPressureToSweep(pending, textureCurrentBytes) ||
                Time.realtimeSinceStartup - _lastPendingChangeRealtime < QuietSeconds ||
                Time.realtimeSinceStartup - _lastSweepRealtime < MinimumSweepIntervalSeconds ||
                FuseSceneryLoadThrottlePatch.QueueDepth > 0 ||
                FuseSceneryLoadThrottlePatch.InFlightLoads > 0 ||
                FuseTrackRebuilderQueueProcessor.BuildQueueDepth > 0 ||
                FuseTrackRebuilderQueueProcessor.DestroyQueueDepth > 0 ||
                FuseCarCullerPendingProcessor.QueueDepth > 0 ||
                FuseCarModelCompletionScheduler.QueueDepth > 0 ||
                FuseDeferredSceneryActivator.PendingCount > 0)
            {
                return;
            }

            var claimed = Interlocked.Exchange(ref _pendingEvictions, 0);
            _lastObservedPending = 0;
            if (claimed <= 0)
            {
                return;
            }

            var textureBeforeMb = Texture.currentTextureMemory / BytesPerMegabyte;
            try
            {
                _activeSweep = Resources.UnloadUnusedAssets();
                _lastSweepRealtime = Time.realtimeSinceStartup;
                _activeSweepStartedRealtime = _lastSweepRealtime;
                FuseLog.Info(
                    "FUSE pressure-triggered unused-asset sweep started after a quiet streaming window: " +
                    $"evictedRequests={claimed} textureCurrentBeforeMB={textureBeforeMb:F1}.");
            }
            catch (Exception ex)
            {
                Interlocked.Add(ref _pendingEvictions, claimed);
                FuseLog.Exception("FUSE unused-asset sweep could not start", ex);
            }
        }

        internal static void Reset()
        {
            Interlocked.Exchange(ref _pendingEvictions, 0);
            Interlocked.Exchange(ref _totalEvictions, 0L);
            Interlocked.Exchange(ref _completedSweeps, 0L);
            _lastObservedPending = 0;
            _lastPendingChangeRealtime = 0f;
            _lastSweepRealtime = float.NegativeInfinity;
            _activeSweepStartedRealtime = 0f;
            _activeSweep = null;
        }

        internal static bool HasEnoughPressureToSweep(
            int pendingEvictions,
            long textureCurrentBytes)
        {
            return pendingEvictions >= MinimumEvictionsPerSweep ||
                   (pendingEvictions > 0 &&
                    textureCurrentBytes >= EmergencyTextureMemoryBytes);
        }

        private static long ToSignedBytes(ulong value)
        {
            return value <= long.MaxValue ? (long)value : long.MaxValue;
        }
    }
}
