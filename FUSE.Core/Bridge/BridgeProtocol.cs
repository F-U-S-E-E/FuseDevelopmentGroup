namespace Fuse.Core.Bridge
{
    /// <summary>
    /// File-based live-reload protocol shared by the editor (net10) and the
    /// in-game <c>FUSE.LiveBridge</c> mod (net48). The editor writes a command
    /// into a package's <c>.fuse-bridge/</c> folder; the in-game watcher reloads
    /// and publishes a heartbeat under <c>Mods/FUSE.LiveBridge/</c>. Net10↔net48
    /// can only talk via files, so both sides (de)serialize these plain DTOs.
    /// </summary>
    public static class BridgeProtocol
    {
        public const int Schema = 1;
        public const string BridgeSubfolder = ".fuse-bridge";
        public const string CommandFileName = "bridge_command.json";
        public const string StateFileName = "bridge_state.json";

        /// <summary>The in-game bridge mod's folder name under <c>Mods/</c>; its heartbeat lives here.</summary>
        public const string BridgeModFolderName = "FUSE.LiveBridge";

        public const string ReloadCommand = "reload";
        public const string ReloadTerrainCommand = "reloadTerrain";
    }

    /// <summary>Editor → game: "re-read packages from disk and re-apply".</summary>
    public sealed class BridgeCommand
    {
        public int Schema { get; set; } = BridgeProtocol.Schema;
        public string RequestId { get; set; }
        public string Command { get; set; } = BridgeProtocol.ReloadCommand;
        public string Reason { get; set; }
        public string PackageId { get; set; }
        public string IssuedUtc { get; set; }
    }

    /// <summary>Game → editor: heartbeat + last-reload result (connection + status).</summary>
    public sealed class BridgeState
    {
        public int Schema { get; set; } = BridgeProtocol.Schema;
        public int Pid { get; set; }
        public string GameVersion { get; set; }
        public string FuseVersion { get; set; }
        public string HeartbeatUtc { get; set; }
        public bool MapLoaded { get; set; }
        public string MultiplayerRole { get; set; }
        public bool CanApply { get; set; }
        public string LastRequestId { get; set; }
        public string LastReloadUtc { get; set; }
        public int AppliedCount { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
    }
}
