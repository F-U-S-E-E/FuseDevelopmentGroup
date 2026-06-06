using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Fuse.Core.Bridge;
using Fuse.LiveHarness.Bridge;
using Fuse.LiveHarness.Fixtures;

namespace Fuse.TestCli;

/// <summary>
/// Dev-only command-line driver for the in-game <c>FUSE.TestBridge</c> mod. It uses the shared
/// <see cref="BridgeClient"/> to write a <see cref="TestRequest"/>, poll for the correlated
/// <see cref="TestResult"/>, and print it (errors to stderr, non-zero exit). Claude calls this via the
/// Bash tool to drive FUSE against a live game session.
///
/// The game Mods directory comes from <c>--mods &lt;dir&gt;</c> or the <c>FUSE_GAME_MODS</c> env var.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fuse-test: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var options = Options.Parse(args);
        var client = new BridgeClient(options.ModsDir, options.TimeoutSeconds, options.PollMilliseconds, options.StaleSeconds);

        // Local commands (read files directly / orchestrate) — no single round trip.
        switch (options.Command)
        {
            case "status":
                return Status(client);
            case "tail-log":
                return TailLog(client, options.Positional);
            case "ready":
                return await client.WaitReadyAsync() ? Ok("ready") : Fail(
                    $"not ready within {options.TimeoutSeconds}s (need connected + mapLoaded + canApply, settled).");
            case "run-fixture":
                return await RunFixtureAsync(options);
        }

        var request = options.Command switch
        {
            "reload" => new TestRequest { Verb = BridgeProtocol.TestVerbReload, Reason = options.Positional ?? "fuse-test reload" },
            "reload-terrain" => new TestRequest { Verb = BridgeProtocol.TestVerbReloadTerrain, Reason = options.Positional ?? "fuse-test reload-terrain" },
            "report" => new TestRequest { Verb = BridgeProtocol.TestVerbReport, Arg = options.Positional ?? "json" },
            "query" => new TestRequest { Verb = BridgeProtocol.TestVerbConsole, CommandLine = options.Positional },
            "dump" => new TestRequest { Verb = BridgeProtocol.TestVerbDump, Arg = options.Positional },
            "screenshot" => new TestRequest { Verb = BridgeProtocol.TestVerbScreenshot, Arg = options.Positional },
            "load" => new TestRequest { Verb = BridgeProtocol.TestVerbLoadSave, Arg = options.Positional },
            "saves" => new TestRequest { Verb = BridgeProtocol.TestVerbSaves },
            "save" => new TestRequest { Verb = BridgeProtocol.TestVerbSave, Arg = options.Positional },
            "umm" => new TestRequest { Verb = BridgeProtocol.TestVerbUmm, Arg = options.Positional ?? "close" },
            "newgame" => new TestRequest { Verb = BridgeProtocol.TestVerbNewGame, Arg = options.Positional },
            "cleanup" => new TestRequest { Verb = BridgeProtocol.TestVerbCleanup },
            _ => null,
        };

        if (request is null)
        {
            return Fail($"unknown command '{options.Command}'. Run with --help.");
        }

        if (options.Command == "query" && string.IsNullOrWhiteSpace(request.CommandLine))
        {
            return Fail("'query' needs a console command, e.g. query \"/fuse.report json\".");
        }

        if (options.Command == "dump" && string.IsNullOrWhiteSpace(request.Arg))
        {
            return Fail("'dump' needs a target: graph | runtimegraph | mandelas | progression.");
        }

        if (options.Command == "load" && string.IsNullOrWhiteSpace(request.Arg))
        {
            return Fail("'load' needs a save name. Use 'saves' to list available saves.");
        }

        if (options.Command == "save" && string.IsNullOrWhiteSpace(request.Arg))
        {
            return Fail("'save' needs a name, e.g. save \"fuse-test-baseline\".");
        }

        return await SendAndPrintAsync(client, request);
    }

    private static async Task<int> SendAndPrintAsync(BridgeClient client, TestRequest request)
    {
        TestResult result;
        try
        {
            result = await client.SendAsync(request);
        }
        catch (TimeoutException ex)
        {
            return Fail(ex.Message + " Is the game running with FUSE.TestBridge enabled, and (for session verbs) a map loaded?");
        }

        if (!result.Ok)
        {
            return Fail(result.Error ?? "command failed.");
        }

        if (!string.IsNullOrEmpty(result.Text))
        {
            Console.WriteLine(result.Text);
        }

        if (!string.IsNullOrEmpty(result.ArtifactPath))
        {
            Console.Error.WriteLine("artifact: " + result.ArtifactPath);
        }

        return 0;
    }

    private static int Status(BridgeClient client)
    {
        var state = client.ReadState();
        if (state is null)
        {
            Console.WriteLine(
                $"disconnected — no heartbeat in {client.ChannelDir}. " +
                "Is the game running with FUSE.TestBridge enabled (FUSE_TEST_BRIDGE=1)?");
            return 1;
        }

        var connection = client.Classify(state, DateTime.UtcNow);
        Console.WriteLine($"connection  : {connection}");
        Console.WriteLine($"pid         : {state.Pid}");
        Console.WriteLine($"gameVersion : {state.GameVersion}");
        Console.WriteLine($"mapLoaded   : {state.MapLoaded}");
        Console.WriteLine($"canApply    : {state.CanApply}");
        Console.WriteLine($"mpRole      : {state.MultiplayerRole}");
        Console.WriteLine($"heartbeatUtc: {state.HeartbeatUtc}");
        Console.WriteLine($"lastReload  : {state.LastReloadUtc} (applied={state.AppliedCount}, ok={state.Ok})");
        if (!string.IsNullOrEmpty(state.Error))
        {
            Console.WriteLine($"lastError   : {state.Error}");
        }

        return connection == BridgeConnection.Connected ? 0 : 1;
    }

    private static int TailLog(BridgeClient client, string? positional)
    {
        var logPath = client.ReadState()?.LogPath;
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
        {
            return Fail($"no FUSE.log available (heartbeat LogPath='{logPath}'). Is the game running with FUSE.TestBridge enabled?");
        }

        var lines = 200;
        if (!string.IsNullOrWhiteSpace(positional)
            && int.TryParse(positional, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            lines = parsed;
        }

        foreach (var line in ReadLastLines(logPath, lines))
        {
            Console.WriteLine(line);
        }

        return 0;
    }

    private static LinkedList<string> ReadLastLines(string path, int count)
    {
        // Open shared so we never fight the game's appender or its startup log rotation.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var tail = new LinkedList<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            tail.AddLast(line);
            if (tail.Count > count)
            {
                tail.RemoveFirst();
            }
        }

        return tail;
    }

    private static async Task<int> RunFixtureAsync(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.Positional))
        {
            return Fail("run-fixture needs a fixture id or directory.");
        }

        var fixtureDir = ResolveFixtureDir(options.Positional);
        if (!Directory.Exists(fixtureDir))
        {
            return Fail($"fixture directory not found: {fixtureDir}");
        }

        // Fixture ops — reload especially — can take minutes on large installs; give generous headroom.
        var client = new BridgeClient(options.ModsDir, Math.Max(options.TimeoutSeconds, 300), options.PollMilliseconds, options.StaleSeconds);
        var result = await new FixtureRunner(client).RunAsync(fixtureDir, options.Update);

        if (result.WasSkipped)
        {
            Console.WriteLine("SKIP: " + result.Message);
            return 0;
        }

        if (result.Captures.Count == 0 && result.Message is not null)
        {
            return Fail(result.Message);
        }

        foreach (var capture in result.Captures)
        {
            Console.WriteLine($"{(capture.Ok ? "ok  " : "FAIL")} {capture.Name}: {capture.Note}");
            foreach (var delta in capture.Deltas.Take(50))
            {
                Console.WriteLine($"     {delta.Kind} {delta.Path}: {delta.Left} -> {delta.Right}");
            }
        }

        if (result.Updated)
        {
            Console.WriteLine("(baselines written — review and commit them)");
        }

        return result.Success ? 0 : 1;
    }

    private static string ResolveFixtureDir(string idOrDir)
    {
        if (Directory.Exists(idOrDir))
        {
            return idOrDir;
        }

        var root = Environment.GetEnvironmentVariable("FUSE_FIXTURES_DIR")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "tests", "live", "fixtures");
        return Path.Combine(root, idOrDir);
    }

    private static int Ok(string message)
    {
        Console.WriteLine(message);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("fuse-test: " + message);
        return 1;
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine(
@"fuse-test — drive the live Railroader game via the FUSE.TestBridge mod.

Usage:
  dotnet run --project FUSE.TestCli -- <command> [args] [--mods <dir>] [--timeout <sec>] [--poll <ms>]

Commands:
  status                      Read the bridge heartbeat (connection, mapLoaded, canApply). No round trip.
  ready                       Wait until connected, a map is loaded, and FUSE has settled (heartbeat steady).
  reload [reason]             Re-read FUSE packages from disk and re-apply. Prints the applied count.
  reload-terrain [reason]     Rebuild terrain via the game's MapManager.
  report [json|detail]        Print the FUSE load report (default: json).
  query ""<console command>""   Run a /fuse.* command, e.g. query ""/fuse.report json"".
  dump <which>                Write a dump JSON and print its path. which: graph | runtimegraph | mandelas | progression.
  screenshot [name]           Capture a screenshot; prints the saved PNG path.
  umm [open|close]            Open/close the Unity Mod Manager overlay (default close) so it doesn't block screenshots.
  tail-log [n]                Print the last n lines of FUSE.log (default 200). No round trip.
  newgame [name]              Start a fresh sandbox game from the menu (deletes old fuse-test-* saves first).
  load <save>                 Load a save (cold-boot from the menu, or in-session swap when a map is loaded).
  save <name>                 Save the running session (host). Test saves should start with 'fuse-test-'.
  saves                       List available save names.
  cleanup                     Delete the harness's fuse-test-* saves (never touches your real saves).
  run-fixture <id> [--update] Load the fixture's save, reload, capture, and golden-master diff (--update writes baselines).

The game Mods directory comes from --mods or the FUSE_GAME_MODS environment variable.");
    }

    private sealed class Options
    {
        public string Command { get; private set; } = string.Empty;
        public string? Positional { get; private set; }
        public string ModsDir { get; private set; } = string.Empty;
        public int TimeoutSeconds { get; private set; } = 30;
        public int PollMilliseconds { get; private set; } = 100;
        public double StaleSeconds { get; private set; } = 5.0;
        public bool Update { get; private set; }

        public static Options Parse(string[] args)
        {
            var options = new Options();
            var positionals = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--mods":
                        options.ModsDir = Next(args, ref i);
                        break;
                    case "--timeout":
                        options.TimeoutSeconds = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--poll":
                        options.PollMilliseconds = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--update":
                        options.Update = true;
                        break;
                    default:
                        positionals.Add(args[i]);
                        break;
                }
            }

            if (positionals.Count > 0)
            {
                options.Command = positionals[0];
            }

            if (positionals.Count > 1)
            {
                options.Positional = string.Join(" ", positionals.GetRange(1, positionals.Count - 1));
            }

            if (string.IsNullOrEmpty(options.ModsDir))
            {
                options.ModsDir = Environment.GetEnvironmentVariable("FUSE_GAME_MODS") ?? string.Empty;
            }

            if (string.IsNullOrEmpty(options.ModsDir))
            {
                throw new InvalidOperationException("No game Mods directory. Pass --mods <dir> or set FUSE_GAME_MODS.");
            }

            return options;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Missing value for {args[i]}.");
            }

            return args[++i];
        }
    }
}
