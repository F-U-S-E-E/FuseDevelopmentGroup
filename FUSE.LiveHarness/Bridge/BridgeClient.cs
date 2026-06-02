using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fuse.Core.Bridge;

namespace Fuse.LiveHarness.Bridge;

/// <summary>Bridge connection state derived from heartbeat freshness.</summary>
public enum BridgeConnection
{
    Disconnected,
    Stale,
    Connected,
}

/// <summary>
/// Client side of the FUSE.TestBridge channel. Each request is written to a unique
/// <c>test_request_&lt;id&gt;.json</c> and the matching <c>test_result_&lt;id&gt;.json</c> is polled for, so a
/// slow or aborted request never clobbers the next. Also reads the heartbeat and provides a
/// settle-aware readiness wait. Shared by the CLI and the fixture runner.
/// </summary>
public sealed class BridgeClient
{
    private readonly string _modsDir;
    private readonly int _timeoutSeconds;
    private readonly int _pollMilliseconds;
    private readonly double _staleSeconds;

    public BridgeClient(string modsDir, int timeoutSeconds = 30, int pollMilliseconds = 100, double staleSeconds = 5.0)
    {
        _modsDir = modsDir;
        _timeoutSeconds = timeoutSeconds;
        _pollMilliseconds = pollMilliseconds;
        _staleSeconds = staleSeconds;
    }

    public string ChannelDir => BridgeIo.TestChannelDir(_modsDir);

    public bool ChannelExists => Directory.Exists(ChannelDir);

    public BridgeState? ReadState() => BridgeIo.TryRead<BridgeState>(BridgeIo.TestStatePath(_modsDir));

    public BridgeConnection Classify(BridgeState? state, DateTime nowUtc)
    {
        if (state?.HeartbeatUtc is null
            || !DateTime.TryParse(state.HeartbeatUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var beat))
        {
            return BridgeConnection.Disconnected;
        }

        return (nowUtc - beat.ToUniversalTime()).TotalSeconds <= _staleSeconds
            ? BridgeConnection.Connected
            : BridgeConnection.Stale;
    }

    public async Task<TestResult> SendAsync(TestRequest request, CancellationToken ct = default)
    {
        request.RequestId = Guid.NewGuid().ToString("N");
        request.IssuedUtc = DateTime.UtcNow.ToString("o");
        var requestPath = BridgeIo.TestRequestPath(_modsDir, request.RequestId);
        var resultPath = BridgeIo.TestResultPath(_modsDir, request.RequestId);
        BridgeIo.WriteAtomic(requestPath, request);

        var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var result = BridgeIo.TryRead<TestResult>(resultPath);
            if (result is not null && string.Equals(result.RequestId, request.RequestId, StringComparison.Ordinal))
            {
                TryDelete(resultPath);
                return result;
            }

            await Task.Delay(_pollMilliseconds, ct);
        }

        // Give up: remove our request so the bridge doesn't run dead work.
        TryDelete(requestPath);
        throw new TimeoutException($"No bridge result within {_timeoutSeconds}s for verb '{request.Verb}'.");
    }

    /// <summary>
    /// Wait until the bridge is genuinely settled — connected, a map loaded, world-apply allowed, AND
    /// the heartbeat has advanced several times without stalling. FUSE's post-load apply blocks the
    /// single-threaded bridge (freezing the heartbeat), so requiring sustained fresh beats avoids the
    /// "ready, then the next command times out because FUSE is still applying" trap.
    /// </summary>
    public async Task<bool> WaitReadyAsync(int requiredBeats = 3, CancellationToken ct = default)
    {
        var beats = new HashSet<string>(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var state = ReadState();
            if (state is not null
                && Classify(state, DateTime.UtcNow) == BridgeConnection.Connected
                && state.MapLoaded
                && state.CanApply
                && state.HeartbeatUtc is not null)
            {
                beats.Add(state.HeartbeatUtc);
                if (beats.Count >= requiredBeats)
                {
                    return true;
                }
            }
            else
            {
                beats.Clear(); // a stall/disconnect resets the streak
            }

            await Task.Delay(_pollMilliseconds, ct);
        }

        return false;
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
            // best-effort cleanup
        }
    }
}
