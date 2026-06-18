using System;
using System.Globalization;
using Fuse.Core.Bridge;

namespace Fuse.ExternalEditor.Services;

/// <summary>Live-bridge connection state derived from the heartbeat freshness.</summary>
public enum BridgeConnection
{
    Disconnected,
    Stale,
    Connected,
}

/// <summary>Editor side of the live-reload bridge: writes reload commands and reads the in-game heartbeat.</summary>
public interface ILiveBridgeService
{
    void WriteReloadCommand(string packageDir, string packageId, string reason);

    BridgeState? ReadHeartbeat(string gameModsDir);

    BridgeConnection Classify(BridgeState? state, DateTime nowUtc, double staleSeconds = 5.0);
}

public sealed class LiveBridgeService : ILiveBridgeService
{
    public void WriteReloadCommand(string packageDir, string packageId, string reason)
    {
        var command = new BridgeCommand
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Command = BridgeProtocol.ReloadCommand,
            Reason = reason,
            PackageId = packageId,
            IssuedUtc = DateTime.UtcNow.ToString("o"),
        };
        BridgeIo.WriteAtomic(BridgeIo.CommandPath(packageDir), command);
    }

    public BridgeState? ReadHeartbeat(string gameModsDir) =>
        BridgeIo.TryRead<BridgeState>(BridgeIo.HeartbeatPath(gameModsDir));

    public BridgeConnection Classify(BridgeState? state, DateTime nowUtc, double staleSeconds = 5.0)
    {
        if (state?.HeartbeatUtc is null
            || !DateTime.TryParse(state.HeartbeatUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var beat))
        {
            return BridgeConnection.Disconnected;
        }

        return (nowUtc - beat.ToUniversalTime()).TotalSeconds <= staleSeconds
            ? BridgeConnection.Connected
            : BridgeConnection.Stale;
    }
}
