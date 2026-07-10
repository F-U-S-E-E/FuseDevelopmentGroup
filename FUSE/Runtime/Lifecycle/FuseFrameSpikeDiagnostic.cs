using System;
using System.Diagnostics;
using FUSE.Infrastructure;
using FUSE.Interface;
using FUSE.Patches;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Setting-gated frame-hitch logger for stutter attribution.
    ///
    /// A fixed millisecond threshold produces false positives when the game's
    /// sustained frame rate is low. The runner therefore compares each frame
    /// with both the configured absolute floor and an allocation-free rolling
    /// median. Logged hitches carry the baseline and effective threshold so a
    /// field log explains why each frame was classified.
    ///
    /// Once a minute the runner also records managed, Unity-native, graphics,
    /// and process memory alongside scene population and FUSE queue depths.
    /// Expensive object censuses are staged one scan per frame, use the
    /// unsorted Unity API, and report their own cost. Their frames are excluded
    /// from hitch classification so the diagnostic does not blame the game for
    /// work it introduced itself.
    /// </summary>
    internal static class FuseFrameSpikeDiagnostic
    {
        private static GameObject _host;

        /// <summary>Frames over the adaptive threshold since startup.</summary>
        internal static long SpikeCount => FuseRuntimeGuardCounters.FrameSpikes;

        /// <summary>Worst frame observed over threshold, in milliseconds.</summary>
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

        /// <summary>
        /// Stops the diagnostic and releases its process handle on mod unload.
        /// Counters remain session-cumulative by design.
        /// </summary>
        internal static void Shutdown()
        {
            if (_host == null)
            {
                return;
            }

            try
            {
                _host.GetComponent<FuseFrameSpikeRunner>()?.DisposeResources();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE frame-spike diagnostic cleanup failed", ex);
            }

            UnityEngine.Object.Destroy(_host);
            _host = null;
        }

        // Scene loads, window drags, and alt-tab device stalls are not the
        // gameplay hitches this exists to attribute.
        private const float StallCutoffMs = 5000f;

        // First few hitches individually (enough to align with nearby logs),
        // then a heartbeat so persistent stutter stays visible without a line
        // for every slow frame.
        private static bool ShouldLog(long count)
        {
            return count <= 10 || count % 25 == 0;
        }

        private sealed class FuseFrameSpikeRunner : MonoBehaviour
        {
            private const float CensusIntervalSeconds = 60f;
            private const double BytesPerMegabyte = 1024d * 1024d;

            private readonly AdaptiveFrameHitchDetector _hitchDetector =
                new AdaptiveFrameHitchDetector();
            private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

            private Process _process;
            private bool _measurementActive;
            private bool _hasGcBaseline;
            private bool _skipSpikeThisFrame;
            private int _gen0;
            private int _gen1;
            private int _gen2;

            private float _lastCensusAt;
            private long _lastCensusFrame;
            private CensusPhase _censusPhase;
            private float _censusWindowSeconds;
            private long _censusWindowFrames;
            private long _censusFrame;
            private int _gameObjects;
            private int _renderers;
            private int _sceneryInstances;
            private double _gameObjectScanMs;
            private double _rendererScanMs;
            private double _sceneryScanMs;

            private bool _frameTimingCapabilityChecked;
            private bool _frameTimingAvailable;
            private bool _frameTimingFailed;
            private double _cpuFrameMsTotal;
            private double _cpuMainThreadMsTotal;
            private double _cpuRenderThreadMsTotal;
            private double _gpuFrameMsTotal;
            private int _cpuTimingSamples;
            private int _gpuTimingSamples;

            private enum CensusPhase
            {
                Idle,
                GameObjects,
                Renderers,
                Scenery,
                Log
            }

            private void Awake()
            {
                TryAcquireProcess();
            }

            private void Update()
            {
                if (!FuseSettings.EnableFrameSpikeDiagnostics || FuseLoadingScreen.IsShowing)
                {
                    ResetMeasurementSession();
                    return;
                }

                if (!_measurementActive)
                {
                    BeginMeasurementSession();
                }

                // The flag still describes the frame whose timing Unity will
                // return here. Keep capturing through census frames, but do not
                // mix diagnostic self-cost into the CPU/GPU gameplay averages.
                SampleFrameTiming(includeSample: !_skipSpikeThisFrame);

                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);
                if (!_hasGcBaseline)
                {
                    // The first delta after enable/loading spans an unmeasured
                    // gap. Capture counters now and seed frame timing on the
                    // next ordinary gameplay frame.
                    _hasGcBaseline = true;
                    _gen0 = gen0;
                    _gen1 = gen1;
                    _gen2 = gen2;
                    return;
                }

                var frameMs = Time.unscaledDeltaTime * 1000f;
                var absoluteFloorMs = Mathf.Max(20f, FuseSettings.FrameSpikeThresholdMs);
                if (_skipSpikeThisFrame)
                {
                    // Time.unscaledDeltaTime describes the frame that just ran.
                    // A staged census scan ran in that frame, so neither classify
                    // it nor let its self-cost raise the rolling baseline.
                    _skipSpikeThisFrame = false;
                }
                else if (frameMs < StallCutoffMs)
                {
                    var observation = _hitchDetector.Observe(frameMs, absoluteFloorMs);
                    if (observation.IsHitch)
                    {
                        var spikeCount = FuseRuntimeGuardCounters.RecordFrameSpike(frameMs);
                        if (ShouldLog(spikeCount))
                        {
                            FuseLog.Warning(
                                $"FUSE frame spike #{spikeCount}: {frameMs:F0}ms " +
                                $"(adaptive baseline {observation.BaselineMs:F1}ms, absolute floor {absoluteFloorMs:F0}ms, " +
                                $"effective threshold {observation.EffectiveThresholdMs:F1}ms, " +
                                $"worst {FuseRuntimeGuardCounters.FrameSpikeWorstMs:F0}ms) frame={Time.frameCount} " +
                                $"gcDelta0={gen0 - _gen0} gcDelta1={gen1 - _gen1} gcDelta2={gen2 - _gen2}. " +
                                "Correlate this timestamp with surrounding FUSE.log/Player.log activity; " +
                                "a positive GC delta with no nearby activity points at allocation pressure.");
                        }
                    }
                }

                _gen0 = gen0;
                _gen1 = gen1;
                _gen2 = gen2;

                if (_censusPhase != CensusPhase.Idle)
                {
                    AdvanceCensus();
                }
                else if (Time.unscaledTime - _lastCensusAt >= CensusIntervalSeconds)
                {
                    BeginCensus();
                    AdvanceCensus();
                }
            }

            private void BeginMeasurementSession()
            {
                _measurementActive = true;
                _hasGcBaseline = false;
                _skipSpikeThisFrame = false;
                _hitchDetector.Reset();
                _censusPhase = CensusPhase.Idle;
                _lastCensusAt = Time.unscaledTime;
                _lastCensusFrame = Time.frameCount;
                ResetFrameTimingSession();

                if (_process == null)
                {
                    TryAcquireProcess();
                }
            }

            private void ResetMeasurementSession()
            {
                if (!_measurementActive)
                {
                    return;
                }

                _measurementActive = false;
                _hasGcBaseline = false;
                _skipSpikeThisFrame = false;
                _hitchDetector.Reset();
                _censusPhase = CensusPhase.Idle;
                ResetFrameTimingSession();
            }

            private void BeginCensus()
            {
                var now = Time.unscaledTime;
                var frame = (long)Time.frameCount;
                _censusWindowSeconds = now - _lastCensusAt;
                _censusWindowFrames = frame - _lastCensusFrame;
                _censusFrame = frame;
                _gameObjects = -1;
                _renderers = -1;
                _sceneryInstances = -1;
                _gameObjectScanMs = 0d;
                _rendererScanMs = 0d;
                _sceneryScanMs = 0d;
                _censusPhase = CensusPhase.GameObjects;
            }

            private void AdvanceCensus()
            {
                switch (_censusPhase)
                {
                    case CensusPhase.GameObjects:
                        ScanGameObjects();
                        _censusPhase = CensusPhase.Renderers;
                        break;
                    case CensusPhase.Renderers:
                        ScanRenderers();
                        _censusPhase = CensusPhase.Scenery;
                        break;
                    case CensusPhase.Scenery:
                        ScanSceneryInstances();
                        _censusPhase = CensusPhase.Log;
                        break;
                    case CensusPhase.Log:
                        try
                        {
                            LogCensus();
                        }
                        catch (Exception ex)
                        {
                            FuseLog.Exception("FUSE runtime census logging failed", ex);
                        }
                        finally
                        {
                            ResetFrameTimingAccumulators();
                            _skipSpikeThisFrame = true;
                            _lastCensusAt = Time.unscaledTime;
                            _lastCensusFrame = Time.frameCount;
                            _censusPhase = CensusPhase.Idle;
                        }
                        break;
                }
            }

            private void ScanGameObjects()
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    _gameObjects = UnityEngine.Object
                        .FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                        .Length;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census GameObject scan failed", ex);
                }
                finally
                {
                    _gameObjectScanMs = ElapsedMilliseconds(started);
                    _skipSpikeThisFrame = true;
                }
            }

            private void ScanRenderers()
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    _renderers = UnityEngine.Object
                        .FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                        .Length;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census Renderer scan failed", ex);
                }
                finally
                {
                    _rendererScanMs = ElapsedMilliseconds(started);
                    _skipSpikeThisFrame = true;
                }
            }

            private void ScanSceneryInstances()
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    _sceneryInstances = UnityEngine.Object
                        .FindObjectsByType<Helpers.SceneryAssetInstance>(FindObjectsSortMode.None)
                        .Length;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census scenery scan failed", ex);
                }
                finally
                {
                    _sceneryScanMs = ElapsedMilliseconds(started);
                    _skipSpikeThisFrame = true;
                }
            }

            private void LogCensus()
            {
                var averageFps = _censusWindowSeconds > 0f
                    ? _censusWindowFrames / _censusWindowSeconds
                    : 0f;
                var cars = ReadCarCount();
                var managedBytes = GC.GetTotalMemory(forceFullCollection: false);

                var unityAllocatedBytes = -1L;
                var unityReservedBytes = -1L;
                var graphicsDriverBytes = -1L;
                try
                {
                    unityAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                    unityReservedBytes = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                    graphicsDriverBytes = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census Unity memory read failed", ex);
                }

                ReadProcessMemory(out var privateBytes, out var workingSetBytes);
                var decalRegistry = FuseDecalCullingScrubPatch.TryGetRegistryCount(out var registryCount)
                    ? registryCount
                    : -1;

                var cpuFrameAverageMs = _cpuTimingSamples > 0
                    ? _cpuFrameMsTotal / _cpuTimingSamples
                    : -1d;
                var cpuMainAverageMs = _cpuTimingSamples > 0
                    ? _cpuMainThreadMsTotal / _cpuTimingSamples
                    : -1d;
                var cpuRenderAverageMs = _cpuTimingSamples > 0
                    ? _cpuRenderThreadMsTotal / _cpuTimingSamples
                    : -1d;
                var gpuFrameAverageMs = _gpuTimingSamples > 0
                    ? _gpuFrameMsTotal / _gpuTimingSamples
                    : -1d;
                var frameTimingState = _frameTimingFailed
                    ? "failed"
                    : _frameTimingAvailable ? "enabled" : "unavailable";

                var scanTotalMs = _gameObjectScanMs + _rendererScanMs + _sceneryScanMs;
                var effectiveThresholdMs = Math.Max(
                    Mathf.Max(20f, FuseSettings.FrameSpikeThresholdMs),
                    _hitchDetector.BaselineMs * AdaptiveFrameHitchDetector.DefaultBaselineMultiplier);

                FuseLog.Info(
                    $"FUSE runtime census: avgFps={averageFps:F1} frame={_censusFrame} " +
                    $"gameObjects={_gameObjects} renderers={_renderers} sceneryInstances={_sceneryInstances} " +
                    $"cars={cars} managedMB={ToMegabytes(managedBytes):F1} " +
                    $"unityAllocMB={ToMegabytes(unityAllocatedBytes):F1} " +
                    $"unityReservedMB={ToMegabytes(unityReservedBytes):F1} " +
                    $"gfxDriverMB={ToMegabytes(graphicsDriverBytes):F1} " +
                    $"processPrivateMB={ToMegabytes(privateBytes):F1} " +
                    $"processWorkingSetMB={ToMegabytes(workingSetBytes):F1} " +
                    $"nativeLeakMode={FuseNativeLeakDiagnostic.ModeLabel} " +
                    $"decalRegistry={decalRegistry} " +
                    $"sceneryLoadQueue={FuseSceneryLoadThrottlePatch.QueueDepth} " +
                    $"sceneryLoadQueuePeak={FuseSceneryLoadThrottlePatch.PeakQueueDepth} " +
                    $"deferredScenery={FuseDeferredSceneryActivator.PendingCount} " +
                    $"frameTiming={frameTimingState} cpuFrameAvgMs={cpuFrameAverageMs:F2} " +
                    $"cpuMainAvgMs={cpuMainAverageMs:F2} cpuRenderAvgMs={cpuRenderAverageMs:F2} " +
                    $"gpuFrameAvgMs={gpuFrameAverageMs:F2} cpuTimingSamples={_cpuTimingSamples} " +
                    $"gpuTimingSamples={_gpuTimingSamples} " +
                    $"hitchBaselineMs={_hitchDetector.BaselineMs:F1} " +
                    $"hitchThresholdMs={effectiveThresholdMs:F1} " +
                    $"censusScanMs={scanTotalMs:F1} " +
                    $"(gameObjects={_gameObjectScanMs:F1}, renderers={_rendererScanMs:F1}, " +
                    $"scenery={_sceneryScanMs:F1}) " +
                    $"spikes={FuseRuntimeGuardCounters.FrameSpikes} " +
                    $"worstMs={FuseRuntimeGuardCounters.FrameSpikeWorstMs:F0}. " +
                    "A metric that climbs while average FPS falls identifies the likely accumulator; " +
                    "censusScanMs is diagnostic self-cost excluded from hitch classification.");

            }

            private void SampleFrameTiming(bool includeSample)
            {
                if (!_frameTimingCapabilityChecked)
                {
                    _frameTimingCapabilityChecked = true;
                    try
                    {
                        _frameTimingAvailable = FrameTimingManager.IsFeatureEnabled();
                        if (_frameTimingAvailable)
                        {
                            FrameTimingManager.CaptureFrameTimings();
                        }
                    }
                    catch (Exception ex)
                    {
                        DisableFrameTiming(ex);
                    }

                    return;
                }

                if (!_frameTimingAvailable)
                {
                    return;
                }

                try
                {
                    var count = FrameTimingManager.GetLatestTimings(1u, _frameTimings);
                    if (count > 0u && includeSample)
                    {
                        var timing = _frameTimings[0];
                        if (IsFinitePositive(timing.cpuFrameTime))
                        {
                            _cpuFrameMsTotal += timing.cpuFrameTime;
                            _cpuMainThreadMsTotal += FiniteNonNegative(timing.cpuMainThreadFrameTime);
                            _cpuRenderThreadMsTotal += FiniteNonNegative(timing.cpuRenderThreadFrameTime);
                            _cpuTimingSamples++;
                        }

                        if (IsFinitePositive(timing.gpuFrameTime))
                        {
                            _gpuFrameMsTotal += timing.gpuFrameTime;
                            _gpuTimingSamples++;
                        }
                    }

                    FrameTimingManager.CaptureFrameTimings();
                }
                catch (Exception ex)
                {
                    DisableFrameTiming(ex);
                }
            }

            private void DisableFrameTiming(Exception ex)
            {
                _frameTimingAvailable = false;
                _frameTimingFailed = true;
                FuseLog.Exception(
                    "FUSE FrameTimingManager sampling failed; CPU/GPU timing will be unavailable for this diagnostic session",
                    ex);
            }

            private void ResetFrameTimingSession()
            {
                _frameTimingCapabilityChecked = false;
                _frameTimingAvailable = false;
                _frameTimingFailed = false;
                ResetFrameTimingAccumulators();
            }

            private void ResetFrameTimingAccumulators()
            {
                _cpuFrameMsTotal = 0d;
                _cpuMainThreadMsTotal = 0d;
                _cpuRenderThreadMsTotal = 0d;
                _gpuFrameMsTotal = 0d;
                _cpuTimingSamples = 0;
                _gpuTimingSamples = 0;
            }

            private static int ReadCarCount()
            {
                try
                {
                    var controller = TrainController.Shared;
                    var cars = controller != null ? controller.Cars : null;
                    return cars != null ? cars.Count : 0;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census car-count read failed", ex);
                    return -1;
                }
            }

            private void TryAcquireProcess()
            {
                try
                {
                    _process = Process.GetCurrentProcess();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime diagnostic could not open the current process", ex);
                }
            }

            private void ReadProcessMemory(out long privateBytes, out long workingSetBytes)
            {
                privateBytes = -1L;
                workingSetBytes = -1L;
                try
                {
                    if (_process == null)
                    {
                        TryAcquireProcess();
                    }

                    if (_process == null)
                    {
                        return;
                    }

                    _process.Refresh();
                    privateBytes = _process.PrivateMemorySize64;
                    workingSetBytes = _process.WorkingSet64;
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime census process-memory read failed", ex);
                }
            }

            internal void DisposeResources()
            {
                var process = _process;
                _process = null;
                if (process == null)
                {
                    return;
                }

                try
                {
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE runtime diagnostic process-handle cleanup failed", ex);
                }
            }

            private void OnDestroy()
            {
                DisposeResources();
            }

            private static double ElapsedMilliseconds(long started)
            {
                return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            }

            private static double ToMegabytes(long bytes)
            {
                return bytes >= 0L ? bytes / BytesPerMegabyte : -1d;
            }

            private static bool IsFinitePositive(double value)
            {
                return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
            }

            private static double FiniteNonNegative(double value)
            {
                return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value)
                    ? value
                    : 0d;
            }
        }
    }
}
