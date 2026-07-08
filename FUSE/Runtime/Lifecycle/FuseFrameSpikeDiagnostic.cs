using System;
using FUSE.Infrastructure;
using FUSE.Interface;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Setting-gated frame-spike logger for stutter attribution.
    ///
    /// Field reports of the form "the game freezes for a few frames every second"
    /// are undiagnosable from Player.log/FUSE.log alone: the logs record faults,
    /// not frame times, so there is nothing to correlate a felt hitch against.
    /// With <c>EnableFrameSpikeDiagnostics</c> on, every frame whose unscaled
    /// delta exceeds <c>FrameSpikeThresholdMs</c> writes one timestamped FUSE.log
    /// line carrying the frame duration and the number of GC collections that
    /// landed on that frame. Lining those timestamps up against the surrounding
    /// FUSE.log/Player.log activity (scenery streaming, mod exceptions, saves,
    /// KV churn) attributes the stutter to a producer instead of guessing —
    /// and a spike line with a positive GC delta and no neighbouring activity
    /// points at allocation pressure rather than any single event.
    ///
    /// Deliberately quiet by default: one bool check per frame while disabled.
    /// While the FUSE loading screen is up (or after any &gt;5 s stall, i.e.
    /// scene switches and alt-tab device resets) frames are not counted — the
    /// synchronous load phases block the main thread by design and would
    /// otherwise exhaust the individually-logged budget before gameplay starts.
    /// </summary>
    internal static class FuseFrameSpikeDiagnostic
    {
        private static GameObject _host;

        /// <summary>Frames over threshold since startup (diagnostics).</summary>
        internal static long SpikeCount => FuseRuntimeGuardCounters.FrameSpikes;

        /// <summary>Worst frame observed over threshold, in milliseconds (diagnostics).</summary>
        internal static float WorstMs => FuseRuntimeGuardCounters.FrameSpikeWorstMs;

        internal static void EnsureStarted()
        {
            if (_host != null)
            {
                return;
            }

            try
            {
                _host = new GameObject("FUSE.FrameSpikeDiagnostic");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _host.hideFlags = HideFlags.HideAndDontSave;
                _host.AddComponent<FuseFrameSpikeRunner>();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE frame-spike diagnostic host creation failed", ex);
            }
        }

        // Ignore frames longer than this outright: scene loads, window drags, and
        // alt-tab GPU device stalls are not the gameplay hitches this exists to
        // attribute, and counting them would drown the useful spikes.
        private const float StallCutoffMs = 5000f;

        // First few spikes individually (enough to line up against the logs),
        // then a heartbeat so a persistent stutter keeps a visible pulse without
        // writing a line per hitch.
        private static bool ShouldLog(long count)
        {
            return count <= 10 || count % 25 == 0;
        }

        private sealed class FuseFrameSpikeRunner : MonoBehaviour
        {
            private bool _hasBaseline;
            private int _gen0;
            private int _gen1;
            private int _gen2;

            private void Update()
            {
                if (!FuseSettings.EnableFrameSpikeDiagnostics || FuseLoadingScreen.IsShowing)
                {
                    _hasBaseline = false;
                    return;
                }

                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);
                if (!_hasBaseline)
                {
                    // First measured frame after enable/loading: its delta spans the
                    // gap, so only capture the GC baseline and start on the next one.
                    _hasBaseline = true;
                    _gen0 = gen0;
                    _gen1 = gen1;
                    _gen2 = gen2;
                    return;
                }

                var frameMs = Time.unscaledDeltaTime * 1000f;
                var thresholdMs = Mathf.Max(20f, FuseSettings.FrameSpikeThresholdMs);
                if (frameMs >= thresholdMs && frameMs < StallCutoffMs)
                {
                    var spikeCount = FuseRuntimeGuardCounters.RecordFrameSpike(frameMs);

                    if (ShouldLog(spikeCount))
                    {
                        FuseLog.Warning(
                            $"FUSE frame spike #{spikeCount}: {frameMs:F0}ms " +
                            $"(threshold {thresholdMs:F0}ms, worst {FuseRuntimeGuardCounters.FrameSpikeWorstMs:F0}ms) frame={Time.frameCount} " +
                            $"gcDelta0={gen0 - _gen0} gcDelta1={gen1 - _gen1} gcDelta2={gen2 - _gen2}. " +
                            "Correlate this timestamp with surrounding FUSE.log/Player.log activity to attribute the hitch; " +
                            "a positive gcDelta with no nearby activity points at allocation pressure.");
                    }
                }

                _gen0 = gen0;
                _gen1 = gen1;
                _gen2 = gen2;
            }
        }
    }
}
