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

        // --- Dev-only test bridge (FUSE.TestBridge mod) ---
        // A point-to-point RPC channel: the driver (CLI) drops a request into the
        // test bridge mod's own folder, the in-game mod executes it on the Unity
        // main thread and writes a result alongside, plus a ~1s heartbeat.

        /// <summary>The dev-only test bridge mod's folder under <c>Mods/</c>; request/result/heartbeat files live here.</summary>
        public const string TestBridgeModFolderName = "FUSE.TestBridge";
        // Per-request files (correlated by RequestId) so a slow/aborted request never clobbers the next.
        public const string TestRequestPrefix = "test_request_";
        public const string TestResultPrefix = "test_result_";
        public const string TestRequestPattern = "test_request_*.json";
        public const string TestStateFileName = "test_state.json";

        // Test bridge verbs (TestRequest.Verb).
        public const string TestVerbReload = "reload";
        public const string TestVerbReloadTerrain = "reloadTerrain";
        public const string TestVerbConsole = "console";
        public const string TestVerbReport = "report";
        public const string TestVerbScreenshot = "screenshot";
        public const string TestVerbDump = "dump";
        public const string TestVerbLoadSave = "loadSave";
        public const string TestVerbSaves = "saves";
        public const string TestVerbSave = "save";
        public const string TestVerbUmm = "umm";
        public const string TestVerbNewGame = "newGame";
        public const string TestVerbCleanup = "cleanup";
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

        /// <summary>Absolute path to the active FUSE.log (test bridge only; lets the driver tail it without a round trip).</summary>
        public string LogPath { get; set; }
    }

    /// <summary>Driver → game: a single test-bridge RPC. <see cref="Verb"/> selects the action;
    /// <see cref="CommandLine"/> carries the console command for <c>console</c>, <see cref="Arg"/> a generic argument.</summary>
    public sealed class TestRequest
    {
        public int Schema { get; set; } = BridgeProtocol.Schema;
        public string RequestId { get; set; }
        public string Verb { get; set; }
        public string CommandLine { get; set; }
        public string Arg { get; set; }
        public string Reason { get; set; }
        public string IssuedUtc { get; set; }
    }

    /// <summary>Game → driver: the result of a single <see cref="TestRequest"/>, correlated by <see cref="RequestId"/>.</summary>
    public sealed class TestResult
    {
        public int Schema { get; set; } = BridgeProtocol.Schema;
        public string RequestId { get; set; }
        public bool Ok { get; set; }
        public string Text { get; set; }
        public string Error { get; set; }
        public string ArtifactPath { get; set; }
        public string CompletedUtc { get; set; }
    }
}
