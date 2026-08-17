using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Fuse.Core.Bridge;
using FUSE.Loading;
using UnityEngine;

namespace FUSE.LiveBridge
{
    /// <summary>
    /// Watches the Mods folder for the editor's <c>.fuse-bridge/bridge_command.json</c>
    /// (recursive FileSystemWatcher), then — on the Unity main thread, debounced —
    /// calls <see cref="FuseLiveReloadApi.ReloadAllFromDisk"/>. Publishes a ~1s
    /// heartbeat to its own mod folder so the editor can show connection status.
    /// </summary>
    public sealed class FuseLiveBridgeBehaviour : MonoBehaviour
    {
        private const double DebounceSeconds = 0.4;

        private string _modsDir;
        private string _ownDir;
        private FileSystemWatcher _watcher;
        private readonly object _lock = new object();
        private bool _pending;
        private DateTime _lastEventUtc;
        private string _lastCommandPath;
        private float _heartbeatTimer;
        private int _heartbeatWriteInFlight;
        private string _heartbeatWriteError;

        private string _lastRequestId;
        private string _lastReloadUtc;
        private int _appliedCount;
        private bool _lastOk = true;
        private string _lastError;
        private int _pid;

        public void Configure(string modPath)
        {
            _ownDir = modPath;
            _pid = Process.GetCurrentProcess().Id; // invariant; resolve once (avoids leaking a Process handle per heartbeat)
            try
            {
                _modsDir = Directory.GetParent(modPath)?.FullName ?? modPath;
            }
            catch
            {
                _modsDir = modPath;
            }

            SetupWatcher();
            WriteHeartbeat();
        }

        private void SetupWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(_modsDir, BridgeProtocol.CommandFileName)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                };
                _watcher.Changed += OnCommandFileEvent;
                _watcher.Created += OnCommandFileEvent;
                _watcher.Renamed += OnCommandFileRenamed;
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Warning("FUSE.LiveBridge watcher setup failed: " + ex.Message);
            }
        }

        // The recursive watcher's internal buffer can overflow; when it does the Error
        // event fires and delivery can stop. Log it and re-arm the watcher so reload survives.
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Main.ModEntry?.Logger.Warning("FUSE.LiveBridge watcher error (re-arming): " + e.GetException()?.Message);
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

        private void OnCommandFileEvent(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

        private void OnCommandFileRenamed(object sender, RenamedEventArgs e) => Enqueue(e.FullPath);

        private void Enqueue(string path)
        {
            // FileSystemWatcher fires on a thread-pool thread; defer the actual
            // reload to Update() on the Unity main thread.
            lock (_lock)
            {
                _pending = true;
                _lastEventUtc = DateTime.UtcNow;
                _lastCommandPath = path;
            }
        }

        private void Update()
        {
            var heartbeatError = Interlocked.Exchange(ref _heartbeatWriteError, null);
            if (!string.IsNullOrEmpty(heartbeatError))
            {
                Main.ModEntry?.Logger.Warning("FUSE.LiveBridge heartbeat write failed: " + heartbeatError);
            }

            var reload = false;
            string commandPath = null;
            lock (_lock)
            {
                if (_pending && (DateTime.UtcNow - _lastEventUtc).TotalSeconds >= DebounceSeconds)
                {
                    reload = true;
                    _pending = false;
                    commandPath = _lastCommandPath;
                }
            }

            if (reload)
            {
                DoReload(commandPath);
            }

            _heartbeatTimer += Time.unscaledDeltaTime;
            if (_heartbeatTimer >= 1.0f)
            {
                _heartbeatTimer = 0f;
                WriteHeartbeat();
            }
        }

        private void DoReload(string commandPath)
        {
            try
            {
                var command = commandPath != null ? BridgeIo.TryRead<BridgeCommand>(commandPath) : null;
                var reason = command?.Reason ?? "live-bridge reload";
                _appliedCount = FuseLiveReloadApi.ReloadAllFromDisk(reason);
                _lastRequestId = command?.RequestId;
                _lastReloadUtc = DateTime.UtcNow.ToString("o");
                _lastOk = true;
                _lastError = null;
                Main.ModEntry?.Logger.Log($"FUSE.LiveBridge reload applied requestId={_lastRequestId ?? "?"} applied={_appliedCount}");
            }
            catch (Exception ex)
            {
                _lastOk = false;
                _lastError = ex.GetBaseException().Message;
                Main.ModEntry?.Logger.Error("FUSE.LiveBridge reload failed: " + ex);
            }

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
                    MapLoaded = true,
                    CanApply = FuseLiveReloadApi.CanApplyWorldMutations("heartbeat"),
                    MultiplayerRole = FuseLiveReloadApi.DescribeMultiplayer("heartbeat"),
                    LastRequestId = _lastRequestId,
                    LastReloadUtc = _lastReloadUtc,
                    AppliedCount = _appliedCount,
                    Ok = _lastOk,
                    Error = _lastError,
                };
                var path = Path.Combine(_ownDir, BridgeProtocol.StateFileName);
                if (Interlocked.CompareExchange(ref _heartbeatWriteInFlight, 1, 0) != 0)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(
                    _ =>
                    {
                        try
                        {
                            BridgeIo.WriteAtomic(path, state);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Exchange(ref _heartbeatWriteError, ex.GetBaseException().Message);
                        }
                        finally
                        {
                            Volatile.Write(ref _heartbeatWriteInFlight, 0);
                        }
                    });
            }
            catch (Exception ex)
            {
                Main.ModEntry?.Logger.Warning("FUSE.LiveBridge heartbeat write failed: " + ex.Message);
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
