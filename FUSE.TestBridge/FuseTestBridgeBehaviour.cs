using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Fuse.Core.Bridge;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Testing;
using UnityEngine;

namespace FUSE.TestBridge
{
    /// <summary>
    /// Persistent (DontDestroyOnLoad) host for the dev-only test bridge. A
    /// <see cref="FileSystemWatcher"/> on this mod's own folder detects driver-written
    /// <c>test_request_&lt;id&gt;.json</c> files; each is read and executed on the Unity main thread in
    /// <see cref="Update"/> (the only place FUSE/Unity calls are legal), then a
    /// <c>test_result_&lt;id&gt;.json</c> is written and the request file deleted. Per-request files mean a
    /// slow or aborted request never clobbers the next. A ~1s heartbeat to <c>test_state.json</c> lets the
    /// driver see liveness/readiness (and the FUSE.log path) without a round trip; the heartbeat
    /// payload is sampled on the Unity main thread, then the atomic file write is serialized on a
    /// background worker so disk/antivirus latency cannot hitch the frame loop.
    ///
    /// The watcher fires on a thread-pool thread and only flags work; all execution is on the main
    /// thread. The <c>screenshot</c> verb is deferred — Unity writes the PNG a frame later, so its result
    /// is held until the file exists.
    /// </summary>
    public sealed class FuseTestBridgeBehaviour : MonoBehaviour
    {
        private const float ScreenshotTimeoutSeconds = 15f;

        private string _channelDir;
        private string _statePath;
        private FileSystemWatcher _watcher;
        private readonly object _lock = new object();
        private readonly object _heartbeatLock = new object();
        private bool _pending;
        private float _heartbeatTimer;
        private int _pid;
        private BridgeState _pendingHeartbeatState;
        private bool _heartbeatWriteInFlight;
        private string _heartbeatWriteError;

        private string _lastRequestId;
        private string _lastCompletedUtc;
        private bool _lastOk = true;
        private string _lastError;
        private int _lastAppliedCount;

        // Deferred screenshot completion (the PNG appears a frame or two after the capture call).
        private string _pendingScreenshotRequestId;
        private string _pendingScreenshotRequestPath;
        private string _pendingScreenshotPath;
        private float _pendingScreenshotElapsed;

        public void Configure(string modPath)
        {
            _channelDir = modPath;
            _statePath = Path.Combine(modPath, BridgeProtocol.TestStateFileName);
            _pid = Process.GetCurrentProcess().Id; // invariant; resolve once (avoids leaking a Process handle per heartbeat)

            CleanStaleChannelFiles();
            SetupWatcher();
            WriteHeartbeat();
        }

        // Remove leftover request/result files from a previous game session so we start clean.
        private void CleanStaleChannelFiles()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_channelDir, BridgeProtocol.TestRequestPattern))
                {
                    TryDelete(file);
                }

                foreach (var file in Directory.GetFiles(_channelDir, BridgeProtocol.TestResultPrefix + "*.json"))
                {
                    TryDelete(file);
                }
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Warning("FUSE.TestBridge stale-file cleanup failed: " + ex.Message);
            }
        }

        private void SetupWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(_channelDir, BridgeProtocol.TestRequestPattern)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                };
                _watcher.Changed += OnRequestEvent;
                _watcher.Created += OnRequestEvent;
                _watcher.Renamed += OnRequestRenamed;
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Warning("FUSE.TestBridge watcher setup failed: " + ex.Message);
            }
        }

        // The watcher's internal buffer can overflow; when it does the Error event fires and delivery
        // can stop. Log it and re-arm so request delivery survives.
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Main.ModEntry?.Logger.Warning("FUSE.TestBridge watcher error (re-arming): " + e.GetException()?.Message);
            try
            {
                _watcher?.Dispose();
            }
            catch
            {
                // ignore
            }

            SetupWatcher();
        }

        private void OnRequestEvent(object sender, FileSystemEventArgs e) => Flag();

        private void OnRequestRenamed(object sender, RenamedEventArgs e) => Flag();

        private void Flag()
        {
            lock (_lock)
            {
                _pending = true;
            }
        }

        private void Update()
        {
            // While a screenshot is in flight, finish it before accepting new work.
            if (_pendingScreenshotRequestId != null)
            {
                AdvancePendingScreenshot();
            }
            else
            {
                bool pending;
                lock (_lock)
                {
                    pending = _pending;
                    _pending = false;
                }

                if (pending)
                {
                    ProcessOldestRequest();
                }
            }

            _heartbeatTimer += Time.unscaledDeltaTime;
            if (_heartbeatTimer >= 1f)
            {
                _heartbeatTimer = 0f;
                WriteHeartbeat();
            }

            ReportHeartbeatWriteError();
        }

        private void ProcessOldestRequest()
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(_channelDir, BridgeProtocol.TestRequestPattern);
            }
            catch
            {
                return;
            }

            if (files.Length == 0)
            {
                return;
            }

            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
            var requestPath = files[0];
            var request = BridgeIo.TryRead<TestRequest>(requestPath);

            if (request == null || string.IsNullOrEmpty(request.RequestId))
            {
                TryDelete(requestPath);
            }
            else if (string.Equals(request.Verb, BridgeProtocol.TestVerbScreenshot, StringComparison.Ordinal))
            {
                BeginScreenshot(request, requestPath);
                return; // hold further processing until the screenshot completes
            }
            else
            {
                CompleteRequest(Execute(request), requestPath);
            }

            // Re-arm so any remaining queued requests are processed on subsequent frames.
            if (files.Length > 1)
            {
                Flag();
            }
        }

        private TestResult Execute(TestRequest request)
        {
            var result = new TestResult
            {
                RequestId = request.RequestId,
                CompletedUtc = DateTime.UtcNow.ToString("o"),
                Ok = true,
            };

            try
            {
                var reason = string.IsNullOrWhiteSpace(request.Reason) ? "test-bridge" : request.Reason;
                switch (request.Verb)
                {
                    case BridgeProtocol.TestVerbReload:
                        _lastAppliedCount = FuseTestApi.Reload(reason);
                        result.Text = _lastAppliedCount.ToString();
                        break;
                    case BridgeProtocol.TestVerbReloadTerrain:
                        result.Text = FuseTestApi.ReloadTerrain(reason) ? "true" : "false";
                        break;
                    case BridgeProtocol.TestVerbReport:
                        result.Text = string.Equals(request.Arg, "detail", StringComparison.OrdinalIgnoreCase)
                            ? FuseTestApi.GetLoadReportDetail()
                            : FuseTestApi.GetLoadReportJson();
                        break;
                    case BridgeProtocol.TestVerbConsole:
                        result.Text = FuseTestApi.RunConsoleCommand(request.CommandLine);
                        break;
                    case BridgeProtocol.TestVerbDump:
                        result.Text = FuseTestApi.Dump(request.Arg, out var dumpArtifact);
                        result.ArtifactPath = dumpArtifact;
                        break;
                    case BridgeProtocol.TestVerbLoadSave:
                        result.Text = FuseTestApi.LoadSave(request.Arg);
                        break;
                    case BridgeProtocol.TestVerbSaves:
                        result.Text = FuseTestApi.ListSaves();
                        break;
                    case BridgeProtocol.TestVerbSave:
                        result.Text = FuseTestApi.SaveGame(request.Arg);
                        break;
                    case BridgeProtocol.TestVerbUmm:
                        result.Text = FuseTestApi.SetUmmWindow(
                            string.Equals(request.Arg, "open", StringComparison.OrdinalIgnoreCase));
                        break;
                    case BridgeProtocol.TestVerbNewGame:
                        result.Text = FuseTestApi.NewGame(request.Arg);
                        break;
                    case BridgeProtocol.TestVerbCleanup:
                        result.Text = FuseTestApi.Cleanup();
                        break;
                    default:
                        result.Ok = false;
                        result.Error = $"Unknown verb '{request.Verb}'.";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.GetBaseException().Message;
                Main.ModEntry?.Logger.Error($"FUSE.TestBridge verb '{request.Verb}' failed: " + ex);
            }

            return result;
        }

        private void BeginScreenshot(TestRequest request, string requestPath)
        {
            try
            {
                _pendingScreenshotPath = FuseTestApi.CaptureScreenshot(request.Arg);
                _pendingScreenshotRequestId = request.RequestId;
                _pendingScreenshotRequestPath = requestPath;
                _pendingScreenshotElapsed = 0f;
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Error("FUSE.TestBridge screenshot failed: " + ex);
                CompleteRequest(
                    new TestResult
                    {
                        RequestId = request.RequestId,
                        CompletedUtc = DateTime.UtcNow.ToString("o"),
                        Ok = false,
                        Error = ex.GetBaseException().Message,
                    },
                    requestPath);
                Flag(); // re-arm: ProcessOldestRequest returned early, so pick up anything queued behind this
            }
        }

        private void AdvancePendingScreenshot()
        {
            _pendingScreenshotElapsed += Time.unscaledDeltaTime;

            var exists = false;
            try
            {
                exists = File.Exists(_pendingScreenshotPath);
            }
            catch
            {
                // treat as not-yet-present
            }

            if (exists)
            {
                CompleteScreenshot(new TestResult
                {
                    RequestId = _pendingScreenshotRequestId,
                    CompletedUtc = DateTime.UtcNow.ToString("o"),
                    Ok = true,
                    Text = _pendingScreenshotPath,
                    ArtifactPath = _pendingScreenshotPath,
                });
            }
            else if (_pendingScreenshotElapsed >= ScreenshotTimeoutSeconds)
            {
                CompleteScreenshot(new TestResult
                {
                    RequestId = _pendingScreenshotRequestId,
                    CompletedUtc = DateTime.UtcNow.ToString("o"),
                    Ok = false,
                    Error = $"Screenshot file did not appear within {ScreenshotTimeoutSeconds}s: {_pendingScreenshotPath}",
                });
            }
        }

        private void CompleteScreenshot(TestResult result)
        {
            var requestPath = _pendingScreenshotRequestPath;
            _pendingScreenshotRequestId = null;
            _pendingScreenshotRequestPath = null;
            _pendingScreenshotPath = null;
            _pendingScreenshotElapsed = 0f;
            CompleteRequest(result, requestPath);
            Flag(); // process anything queued behind the screenshot
        }

        private void CompleteRequest(TestResult result, string requestPath)
        {
            _lastRequestId = result.RequestId;
            _lastCompletedUtc = result.CompletedUtc;
            _lastOk = result.Ok;
            _lastError = result.Error;

            try
            {
                BridgeIo.WriteAtomic(Path.Combine(_channelDir, BridgeProtocol.TestResultPrefix + result.RequestId + ".json"), result);
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Error("FUSE.TestBridge failed to write result: " + ex);
            }

            TryDelete(requestPath);
            WriteHeartbeat();
        }

        private void WriteHeartbeat()
        {
            try
            {
                var state = new BridgeState
                {
                    Pid = _pid,
                    GameVersion = Application.version,
                    FuseVersion = string.Empty,
                    HeartbeatUtc = DateTime.UtcNow.ToString("o"),
                    MapLoaded = FuseTestApi.IsMapLoaded(),
                    CanApply = FuseLiveReloadApi.CanApplyWorldMutations("test-bridge heartbeat"),
                    MultiplayerRole = FuseLiveReloadApi.DescribeMultiplayer("test-bridge heartbeat"),
                    LastRequestId = _lastRequestId,
                    LastReloadUtc = _lastCompletedUtc,
                    AppliedCount = _lastAppliedCount,
                    Ok = _lastOk,
                    Error = _lastError,
                    LogPath = FuseLog.LogFilePath,
                };

                QueueHeartbeatWrite(state);
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Warning("FUSE.TestBridge heartbeat sample failed: " + ex.Message);
            }
        }

        private void QueueHeartbeatWrite(BridgeState state)
        {
            lock (_heartbeatLock)
            {
                _pendingHeartbeatState = state;
                if (_heartbeatWriteInFlight)
                {
                    return;
                }

                _heartbeatWriteInFlight = true;
            }

            ThreadPool.QueueUserWorkItem(_ => DrainHeartbeatWrites());
        }

        private void DrainHeartbeatWrites()
        {
            while (true)
            {
                BridgeState state;
                lock (_heartbeatLock)
                {
                    state = _pendingHeartbeatState;
                    _pendingHeartbeatState = null;
                    if (state == null)
                    {
                        _heartbeatWriteInFlight = false;
                        return;
                    }
                }

                try
                {
                    BridgeIo.WriteAtomic(_statePath, state);
                }
                catch (Exception ex)
                {
                    lock (_heartbeatLock)
                    {
                        _heartbeatWriteError = ex.Message;
                    }
                }
            }
        }

        private void ReportHeartbeatWriteError()
        {
            string error;
            lock (_heartbeatLock)
            {
                error = _heartbeatWriteError;
                _heartbeatWriteError = null;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Main.ModEntry?.Logger.Warning("FUSE.TestBridge heartbeat write failed: " + error);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore
            }
        }

        private void OnDestroy()
        {
            try
            {
                _watcher?.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }
}
