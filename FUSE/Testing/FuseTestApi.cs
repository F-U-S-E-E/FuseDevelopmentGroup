using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Interface.Console;
using FUSE.Loading;
using FUSE.Runtime.Lifecycle;
using Game.Persistence;
using Game.State;
using Track;
using UI.Console;
using UnityEngine;

namespace FUSE.Testing
{
    /// <summary>
    /// Public, dev-facing facade the optional <c>FUSE.TestBridge</c> mod calls to drive
    /// FUSE for automated live-game testing. It re-exposes the reload entry points, the
    /// structured load report, and the <c>/fuse.*</c> console commands — several of which
    /// live on <c>internal</c> types that a separate mod assembly cannot reach. This mirrors
    /// the existing <see cref="FuseLiveReloadApi"/> precedent: a thin public seam inside the
    /// FUSE assembly rather than widening internals to the mod.
    ///
    /// Every method assumes the Unity main thread; the test bridge host guarantees that by
    /// only invoking this from its <c>Update()</c> pump.
    /// </summary>
    public static class FuseTestApi
    {
        private static readonly char[] WhitespaceSeparators = { ' ', '\t' };

        private static readonly Dictionary<string, (string Command, string File)> DumpTargets =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["graph"] = ("/fuse.dumpgraph", "FUSE-original-graph.json"),
                ["runtimegraph"] = ("/fuse.dumpruntimegraph", "FUSE-runtime-graph.json"),
                ["mandelas"] = ("/fuse.dumpmandelas", "FUSE-mandelas.json"),
                ["progression"] = ("/fuse.dumpprogression", "FUSE-progression.json"),
            };

        private static readonly Type[] StartGameSinglePlayerParams = { typeof(GameSetup) };

        // Reserved prefix for harness-created saves. Cleanup ONLY ever deletes saves with this
        // prefix — never the user's real saves. Defined here so FUSE.dll keeps no FUSE.Core dependency.
        private const string TestSavePrefix = "fuse-test-";

        private static Dictionary<string, IConsoleCommand> _commandsByName;

        /// <summary>Re-read every FUSE package from disk and re-apply to the running world. Returns the number of definitions applied.</summary>
        public static int Reload(string reason) => FuseLiveReloadApi.ReloadAllFromDisk(reason);

        /// <summary>Rebuild terrain via the game's MapManager. Returns whether the rebuild ran.</summary>
        public static bool ReloadTerrain(string reason) => FuseRuntimeReloadService.ReloadTerrain(reason);

        /// <summary>The last FUSE map-load report as machine-readable JSON (re-snapshots registries on demand).</summary>
        public static string GetLoadReportJson() => FuseLoadReport.GetLastJsonReport();

        /// <summary>The last FUSE map-load report as human-readable text.</summary>
        public static string GetLoadReportDetail() => FuseLoadReport.GetLastDetailReport();

        /// <summary>Whether a map is loaded and the runtime track graph is populated.</summary>
        public static bool IsMapLoaded()
        {
            try
            {
                return Graph.Shared != null && Graph.Shared.HasPopulatedCollections;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Run a FUSE console command by its full command line (e.g. <c>/fuse.report json</c>) and
        /// return the same text the in-game console would show. Reuses the exact
        /// <see cref="IConsoleCommand"/> instances FUSE registers with the game, so every
        /// <c>/fuse.*</c> command is drivable without re-implementing any of them.
        /// </summary>
        public static string RunConsoleCommand(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return "FUSE test bridge: empty console command.";
            }

            var tokens = commandLine.Split(WhitespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            var commands = Commands();
            if (!commands.TryGetValue(Normalize(tokens[0]), out var command))
            {
                var known = string.Join(", ", commands.Keys.OrderBy(k => k, StringComparer.Ordinal));
                return $"FUSE test bridge: unknown console command '{tokens[0]}'. Known: {known}.";
            }

            // The game passes components[0] == the command name; the commands scan the
            // remaining tokens for flags/args. Pass the full token array unchanged.
            return command.Execute(tokens) ?? string.Empty;
        }

        /// <summary>
        /// Capture a screenshot to <c>persistentDataPath/FUSE-test-shots/&lt;name&gt;.png</c> and return the
        /// absolute path. Unity writes the PNG asynchronously (ready on a later frame), so the test bridge
        /// host waits for the file to exist before reporting completion. ScreenCapture is resolved by
        /// reflection so FUSE keeps no compile-time dependency on the screen-capture module.
        /// </summary>
        public static string CaptureScreenshot(string name)
        {
            var dir = Path.Combine(Application.persistentDataPath, "FUSE-test-shots");
            Directory.CreateDirectory(dir);

            var stem = SanitizeStem(name);
            var absolutePath = Path.Combine(dir, stem + ".png");

            // Remove any stale same-named file so the host's File.Exists poll observes the NEW
            // capture, not a prior one (Unity writes the PNG a frame later).
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
            catch
            {
                // best-effort; if it can't be removed the host times out rather than returning stale data
            }

            var screenCapture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")
                ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule");
            var capture = screenCapture?.GetMethod("CaptureScreenshot", new[] { typeof(string) });
            if (capture == null)
            {
                throw new InvalidOperationException("UnityEngine.ScreenCapture.CaptureScreenshot(string) could not be resolved.");
            }

            // Pass an ABSOLUTE path: Unity resolves a *relative* screenshot path against the
            // executable's working directory (the game root), not persistentDataPath, so a
            // relative path lands somewhere the host isn't watching (or fails to create the subdir).
            capture.Invoke(null, new object[] { absolutePath });
            return absolutePath;
        }

        /// <summary>
        /// Run one of FUSE's dump console commands (<c>graph</c>, <c>runtimegraph</c>, <c>mandelas</c>,
        /// <c>progression</c>), returning its summary text and the absolute path of the JSON file it wrote.
        /// </summary>
        public static string Dump(string which, out string artifactPath)
        {
            artifactPath = null;
            if (string.IsNullOrWhiteSpace(which) || !DumpTargets.TryGetValue(which.Trim(), out var target))
            {
                return $"FUSE test bridge: unknown dump '{which}'. Known: {string.Join(", ", DumpTargets.Keys)}.";
            }

            var summary = RunConsoleCommand(target.Command);
            artifactPath = Path.Combine(FuseConsoleCommands.GetRailroaderRootFolder(), target.File);
            return summary;
        }

        /// <summary>
        /// Load a save into the running session by name. This is the in-session loader — it swaps
        /// the current world for another save on the same map; it does NOT boot from the main menu.
        /// Host-only and requires a session already loaded. The actual load proceeds over subsequent
        /// frames; poll <see cref="IsMapLoaded"/>/the load report for completion.
        /// </summary>
        public static string LoadSave(string saveName)
        {
            if (string.IsNullOrWhiteSpace(saveName))
            {
                return "FUSE test bridge: loadSave needs a save name.";
            }

            var name = saveName.Trim();

            // In a running session: swap to another save on the same map (host-only).
            if (IsMapLoaded())
            {
                if (StateManager.Shared == null)
                {
                    return "FUSE test bridge: no active session state.";
                }

                if (!StateManager.IsHost)
                {
                    return "FUSE test bridge: loadSave is host-only.";
                }

                StateManager.Shared.SaveManager.Load(name);
                return $"Loading save '{name}' into the running session.";
            }

            // At the main menu: cold-boot the save exactly as the Load Game menu does.
            return ColdBootSave(name);
        }

        // Drives the same entry point the Load Game menu uses:
        // MenuManager.StartGameSinglePlayer(new GameSetup(saveName)). The method is private,
        // so it is reflected by name (canaried in RailroaderReflectionSurfaceTests); the load
        // proceeds asynchronously — poll IsMapLoaded()/the load report for completion.
        private static string ColdBootSave(string name)
        {
            var menu = UnityEngine.Object.FindObjectOfType<UI.Menu.MenuManager>();
            if (menu == null)
            {
                return "FUSE test bridge: not in a session and the main menu (MenuManager) is not present — " +
                       "wait for the main menu, then retry.";
            }

            var start = typeof(UI.Menu.MenuManager).GetMethod(
                "StartGameSinglePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: StartGameSinglePlayerParams,
                modifiers: null);
            if (start == null)
            {
                return "FUSE test bridge: UI.Menu.MenuManager.StartGameSinglePlayer(GameSetup) not found (game changed).";
            }

            start.Invoke(menu, new object[] { new GameSetup(name) });
            return $"Booting save '{name}' from the main menu.";
        }

        /// <summary>List the available save names on disk (newest first).</summary>
        public static string ListSaves()
        {
            var infos = WorldStore.FindSaveInfos();
            return infos == null || infos.Count == 0
                ? "(no saves found)"
                : string.Join(Environment.NewLine, infos.Select(info => info.Name));
        }

        /// <summary>Save the running session to a named save (host-only). Lets a test snapshot a known state for reuse.</summary>
        public static string SaveGame(string saveName)
        {
            if (string.IsNullOrWhiteSpace(saveName))
            {
                return "FUSE test bridge: save needs a name.";
            }

            if (!IsMapLoaded() || StateManager.Shared == null)
            {
                return "FUSE test bridge: save requires a running session.";
            }

            if (!StateManager.IsHost)
            {
                return "FUSE test bridge: save is host-only.";
            }

            var name = saveName.Trim();
            StateManager.Shared.SaveManager.Save(name);
            return $"Saved session as '{name}'.";
        }

        /// <summary>Open or close the Unity Mod Manager overlay window so it doesn't block screenshots.</summary>
        public static string SetUmmWindow(bool open)
        {
            // Reflected, not referenced: UMM's UI derives from MonoBehaviour in the monolithic
            // UnityEngine assembly, which FUSE does not reference (it uses the split modules), so a
            // direct compile-time call won't resolve. Reflection sidesteps that and stays graceful.
            var uiType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityModManagerNet.UnityModManager+UI", throwOnError: false))
                .FirstOrDefault(type => type != null);
            var instance = uiType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            if (instance == null)
            {
                return "FUSE test bridge: UMM UI instance not available.";
            }

            var toggle = uiType.GetMethod("ToggleWindow", new[] { typeof(bool) });
            if (toggle == null)
            {
                return "FUSE test bridge: UMM UI.ToggleWindow(bool) not found.";
            }

            toggle.Invoke(instance, new object[] { open });
            return open ? "UMM window opened." : "UMM window closed.";
        }

        /// <summary>
        /// Start a fresh sandbox game from the main menu, first deleting prior harness test saves
        /// (only those named with the reserved <c>fuse-test-</c> prefix — never the user's real saves).
        /// Requires the main menu (MenuManager); the new session's save name carries the test prefix.
        /// </summary>
        public static string NewGame(string name)
        {
            var saveName = NormalizeTestSaveName(name);
            var removed = CleanupTestSaves(saveName);

            var menu = UnityEngine.Object.FindObjectOfType<UI.Menu.MenuManager>();
            if (menu == null)
            {
                return "FUSE test bridge: newGame requires the main menu (quit to the menu first).";
            }

            var start = typeof(UI.Menu.MenuManager).GetMethod(
                "StartGameSinglePlayer",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: StartGameSinglePlayerParams,
                modifiers: null);
            if (start == null)
            {
                return "FUSE test bridge: UI.Menu.MenuManager.StartGameSinglePlayer(GameSetup) not found (game changed).";
            }

            var setup = new GameSetup(saveName, new NewGameSetup("FUSE Test", "FUSE", GameMode.Sandbox, null, null));
            start.Invoke(menu, new object[] { setup });
            return $"Starting fresh sandbox game '{saveName}' (removed {removed} old test save(s)).";
        }

        /// <summary>Delete harness test saves (names starting with the reserved <c>fuse-test-</c> prefix). Never touches other saves.</summary>
        public static string Cleanup()
        {
            var removed = CleanupTestSaves(null);
            return $"Removed {removed} test save(s).";
        }

        private static int CleanupTestSaves(string keepName)
        {
            var infos = WorldStore.FindSaveInfos();
            if (infos == null)
            {
                return 0;
            }

            var removed = 0;
            foreach (var info in infos)
            {
                var saveName = info.Name;
                if (string.IsNullOrEmpty(saveName)
                    || !saveName.StartsWith(TestSavePrefix, StringComparison.Ordinal)
                    || string.Equals(saveName, keepName, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    WorldStore.Clear(saveName);
                    removed++;
                }
                catch
                {
                    // skip a save we couldn't delete
                }
            }

            return removed;
        }

        private static string NormalizeTestSaveName(string name)
        {
            var trimmed = string.IsNullOrWhiteSpace(name) ? "clean" : name.Trim();
            return trimmed.StartsWith(TestSavePrefix, StringComparison.Ordinal)
                ? trimmed
                : TestSavePrefix + trimmed;
        }

        private static string SanitizeStem(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "shot";
            }

            var stem = name.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                stem = stem.Replace(invalid, '_');
            }

            return stem.Length == 0 ? "shot" : stem;
        }

        private static Dictionary<string, IConsoleCommand> Commands()
        {
            if (_commandsByName != null)
            {
                return _commandsByName;
            }

            var map = new Dictionary<string, IConsoleCommand>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in FuseConsoleCommands.CreateAll())
            {
                var name = Normalize(GetCommandName(command));
                if (!string.IsNullOrEmpty(name))
                {
                    map[name] = command;
                }
            }

            _commandsByName = map;
            return _commandsByName;
        }

        // Read the [ConsoleCommand("/fuse.xxx", ...)] verb off each command via attribute
        // data so we don't depend on the game attribute's property names.
        private static string GetCommandName(IConsoleCommand command)
        {
            foreach (var data in CustomAttributeData.GetCustomAttributes(command.GetType()))
            {
                if (data.AttributeType.FullName == "UI.Console.ConsoleCommandAttribute"
                    && data.ConstructorArguments.Count > 0)
                {
                    return data.ConstructorArguments[0].Value as string;
                }
            }

            return null;
        }

        private static string Normalize(string name) => name?.TrimStart('/').Trim();
    }
}
