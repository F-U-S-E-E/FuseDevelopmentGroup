using System;
using System.IO;
using Fuse.Core.Bridge;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Editor-side live-bridge channel: command write, heartbeat read, status, push.</summary>
public class BridgeTests
{
    [Fact]
    public void WriteReloadCommand_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fuse-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            new LiveBridgeService().WriteReloadCommand(dir, "my.pkg", "test reason");

            var cmd = BridgeIo.TryRead<BridgeCommand>(BridgeIo.CommandPath(dir));
            Assert.NotNull(cmd);
            Assert.Equal(BridgeProtocol.ReloadCommand, cmd!.Command);
            Assert.Equal("my.pkg", cmd.PackageId);
            Assert.Equal("test reason", cmd.Reason);
            Assert.False(string.IsNullOrEmpty(cmd.RequestId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadHeartbeat_And_Classify_By_Freshness()
    {
        var mods = Path.Combine(Path.GetTempPath(), "fuse-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mods);
        try
        {
            var service = new LiveBridgeService();
            Assert.Null(service.ReadHeartbeat(mods));
            Assert.Equal(BridgeConnection.Disconnected, service.Classify(null, DateTime.UtcNow));

            var now = DateTime.UtcNow;
            BridgeIo.WriteAtomic(BridgeIo.HeartbeatPath(mods), new BridgeState
            {
                HeartbeatUtc = now.ToString("o"),
                AppliedCount = 3,
                CanApply = true,
            });

            var state = service.ReadHeartbeat(mods);
            Assert.NotNull(state);
            Assert.Equal(3, state!.AppliedCount);
            Assert.Equal(BridgeConnection.Connected, service.Classify(state, now.AddSeconds(1)));
            Assert.Equal(BridgeConnection.Stale, service.Classify(state, now.AddSeconds(30)));
        }
        finally
        {
            Directory.Delete(mods, recursive: true);
        }
    }

    [Fact]
    public void PushToGame_Writes_Package_And_Reload_Command()
    {
        var mods = Path.Combine(Path.GetTempPath(), "fuse-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mods);
        try
        {
            var vm = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), new Fuse.Core.Authoring.UndoService())
            {
                GameModsPath = mods,
            };
            vm.AddNodeCommand.Execute(null);

            vm.PushToGameCommand.Execute(null);

            var packageDir = Path.Combine(mods, "untitled");
            Assert.True(File.Exists(Path.Combine(packageDir, "untitled.fuse.json")));
            Assert.True(File.Exists(BridgeIo.CommandPath(packageDir)));
            Assert.Contains("Pushed", vm.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(mods, recursive: true);
        }
    }
}
