using FUSE.Authoring.Migrations;
using FUSE.Infrastructure;
using FUSE.Interface.Console;
using FUSE.Loading;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Runtime.Lifecycle;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct RuntimeActionsToolPage
    {
        private static string _lastAction = "No runtime action has been run yet this session.";

        public static void Build(UIPanelBuilder builder)
        {
            builder.AddTitle("Runtime Actions", "");

            builder.AddLabel("Apply-in-place reloads and diagnostics exports for pack authors and FUSE debugging.");

            builder.Spacer(8f);

            builder.AddSection("Reload");
            builder.AddLabel("Re-apply resident FUSE definitions to the live world without leaving the session.");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Reload Track/Data", () => RunAction(builder, "reload track and data", () =>
                {
                    var applied = FuseRuntimeReloadService.ReloadTrackAndData("FUSE tools page reload track/data");
                    return $"Reload Track/Data complete. Applied {applied} resident definition(s).";
                }));
                row.AddButtonCompact("Reload Terrain", () => RunAction(builder, "reload terrain", () =>
                    FuseRuntimeReloadService.ReloadTerrain("FUSE tools page reload terrain")
                        ? "Reload Terrain complete."
                        : "Reload Terrain skipped or failed. See FUSE.log."));
                row.AddButtonCompact("Rebuild Caches", () => RunAction(builder, "rebuild caches", () =>
                {
                    FuseCacheRegistry.RebuildAll();
                    return "Rebuilt FUSE runtime caches.";
                }));
            }, 6f).Height(32f);

            builder.Spacer(8f);

            AddMapsSection(builder);

            builder.Spacer(8f);

            builder.AddSection("Diagnostics Export");
            builder.AddLabel("Attach these to bug reports: the snapshot is a quick paste, the bundle is the full machine-readable state.");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Runtime Snapshot", () => RunAction(builder, "copy runtime snapshot", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildRuntimeSnapshotText();
                    return "Copied FUSE runtime snapshot to clipboard.";
                }));
                row.AddButtonCompact("Export Debug Bundle", () => RunAction(builder, "export debug bundle", () =>
                    ExportDebugBundle(openFolder: true)));
            }, 6f).Height(32f);

            builder.Spacer(8f);

            AddWrappedField(builder, "Last Action", _lastAction, 44f);
        }

        private static void AddMapsSection(UIPanelBuilder builder)
        {
            var maps = FuseMapPackageRegistry.GetRegisteredMaps();
            if (maps.Count == 0)
            {
                return;
            }

            builder.AddSection("Maps");
            var activeMapId = FuseMapSession.ActiveMapId;
            builder.AddLabel(string.IsNullOrEmpty(activeMapId)
                ? "Launch a new sandbox session on a FUSE map. Only available from the main menu."
                : $"Active session map: {activeMapId}. Return to the main menu to launch a different map.");

            foreach (var map in maps)
            {
                var captured = map;
                builder.HStack(row =>
                {
                    if (captured.IsValid)
                    {
                        row.AddButtonCompact($"Launch {captured.DisplayName}", () => RunAction(builder, "launch map", () =>
                        {
                            if (FuseConsoleCommands.IsInSession())
                            {
                                return "Map launch refused: a session is already running. Return to the main menu first.";
                            }

                            return FuseMapLauncher.TryLaunchMap(captured.MapId, null, null, out var error)
                                ? $"Launching map '{captured.DisplayName}'…"
                                : $"Map launch failed: {error}";
                        }));
                    }
                    else
                    {
                        row.AddLabel($"{captured.DisplayName}: {captured.FaultReason}");
                    }
                }, 6f).Height(32f);
            }
        }

        private static void RunAction(UIPanelBuilder builder, string actionName, Func<string> action)
        {
            try
            {
                _lastAction = action();
            }
            catch (Exception ex)
            {
                _lastAction = $"FUSE {actionName} failed: {ex.GetBaseException().Message}";
                FuseLog.Exception($"FUSE tools page action failed operation='{actionName}'", ex);
            }

            Toast.Present(_lastAction);
            builder.Rebuild();
        }

        // The retired Health window averaged FPS in its Update loop; this page has
        // no per-frame host, so snapshots sample Unity's own smoothed frame time
        // at the moment the button is pressed.
        private static float SmoothedFps()
        {
            var delta = Time.smoothDeltaTime;
            return delta <= 0f ? 0f : 1f / delta;
        }

        private static string BuildRuntimeSnapshotText()
        {
            var fps = SmoothedFps();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Runtime Snapshot");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion);
            builder.AppendLine();
            builder.AppendLine("Unity");
            builder.AppendLine("FPS: " + (fps <= 0f ? "n/a" : fps.ToString("0.0")));
            builder.AppendLine("Frame Time: " + (fps <= 0f ? "n/a" : (Time.smoothDeltaTime * 1000f).ToString("0.0") + " ms"));
            builder.AppendLine("Managed Memory: " + FormatBytes(GC.GetTotalMemory(false)));
            builder.AppendLine("Unity Allocated: " + FormatBytes(ReadProfilerMetric(Profiler.GetTotalAllocatedMemoryLong)));
            builder.AppendLine("Unity Reserved: " + FormatBytes(ReadProfilerMetric(Profiler.GetTotalReservedMemoryLong)));
            builder.AppendLine("Active Scene: " + ActiveSceneName());
            builder.AppendLine("Loaded Scenes: " + LoadedSceneSummary());
            builder.AppendLine("Scene Roots: " + SafeCount(CountSceneRootObjects));
            builder.AppendLine("GameObjects: " + SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length));
            builder.AppendLine();
            builder.AppendLine("Railroader");
            builder.AppendLine("Track Nodes: " + SafeCount(() => TrackAPI.GetAllNodes().Count()));
            builder.AppendLine("Track Segments: " + SafeCount(() => TrackAPI.GetAllSegments().Count()));
            builder.AppendLine("Track Spans: " + SafeCount(() => TrackAPI.GetAllSpans().Count()));
            builder.AppendLine("Areas: " + SafeCount(() => TrackAPI.GetAllAreas().Count()));
            builder.AppendLine("Loads: " + SafeCount(() => LoadAPI.GetAllLoads().Count()));
            builder.AppendLine("Industries: " + SafeCount(() => IndustryAPI.GetAllIndustries().Count()));
            builder.AppendLine("Loaders: " + SafeCount(() => LoaderAPI.GetAllLoaders().Count()));
            builder.AppendLine("Stations: " + SafeCount(() => StationAPI.GetAllStationAgents().Count()));
            builder.AppendLine("Passenger Stops: " + SafeCount(() => StationAPI.GetAllPassengerStops().Count()));
            builder.AppendLine("Scenery: " + SafeCount(() => SceneryAPI.GetAllScenery().Count()));
            builder.AppendLine();
            builder.AppendLine("FUSE Registry");
            builder.AppendLine("Exclusive Claims: " + FUSE.Runtime.Registry.FuseRegistry.ExclusiveClaimCount);
            builder.AppendLine("Shared Claims: " + FUSE.Runtime.Registry.FuseRegistry.SharedClaimCount);
            builder.AppendLine("Conflicts: " + FUSE.Runtime.Registry.FuseRegistry.Conflicts.Count);
            return builder.ToString().TrimEnd();
        }

        private static string ExportDebugBundle(bool openFolder)
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-debug-bundle.json");
            var diagnostics = FuseAssetPackRegistry.GetDiagnostics();
            var loadedScenes = new JArray();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded)
                {
                    loadedScenes.Add(new JObject
                    {
                        ["name"] = scene.name ?? string.Empty,
                        ["rootObjects"] = SafeCount(() => scene.GetRootGameObjects().Length)
                    });
                }
            }

            var fps = SmoothedFps();
            var bundle = new JObject
            {
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["version"] = ReadVersion(),
                ["schema"] = FuseMigration.CurrentVersion.ToString(),
                ["profile"] = FuseModSetService.ActiveSetName,
                ["profileHash"] = FuseModSetService.GetActiveSetFingerprint(),
                ["health"] = JObject.Parse(FuseLoadReport.GetLastJsonReport()),
                ["unity"] = new JObject
                {
                    ["fps"] = fps,
                    ["frameMilliseconds"] = fps <= 0f ? 0f : Time.smoothDeltaTime * 1000f,
                    ["managedMemoryBytes"] = GC.GetTotalMemory(false),
                    ["unityAllocatedBytes"] = ReadProfilerMetric(Profiler.GetTotalAllocatedMemoryLong),
                    ["unityReservedBytes"] = ReadProfilerMetric(Profiler.GetTotalReservedMemoryLong),
                    ["activeScene"] = ActiveSceneName(),
                    ["loadedScenes"] = loadedScenes,
                    ["sceneRootObjects"] = SafeCount(CountSceneRootObjects),
                    ["gameObjects"] = SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length)
                },
                ["railroader"] = new JObject
                {
                    ["trackNodes"] = SafeCount(() => TrackAPI.GetAllNodes().Count()),
                    ["trackSegments"] = SafeCount(() => TrackAPI.GetAllSegments().Count()),
                    ["trackSpans"] = SafeCount(() => TrackAPI.GetAllSpans().Count()),
                    ["areas"] = SafeCount(() => TrackAPI.GetAllAreas().Count()),
                    ["loads"] = SafeCount(() => LoadAPI.GetAllLoads().Count()),
                    ["industries"] = SafeCount(() => IndustryAPI.GetAllIndustries().Count()),
                    ["loaders"] = SafeCount(() => LoaderAPI.GetAllLoaders().Count()),
                    ["stations"] = SafeCount(() => StationAPI.GetAllStationAgents().Count()),
                    ["passengerStops"] = SafeCount(() => StationAPI.GetAllPassengerStops().Count()),
                    ["turntables"] = SafeCount(() => TurntableAPI.GetAllTurntables().Count()),
                    ["scenery"] = SafeCount(() => SceneryAPI.GetAllScenery().Count()),
                    ["sceneClones"] = SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()),
                    ["splineys"] = SafeCount(() => SplineyAPI.GetAllSplineys().Count()),
                    ["mapLabels"] = SafeCount(() => MapAPI.GetAllMapLabels().Count()),
                    ["mapMasks"] = SafeCount(() => MapAPI.GetAllMapMasks().Count()),
                    ["progressions"] = SafeCount(() => ProgressionAPI.GetAllProgressions().Count()),
                    ["mapFeatures"] = SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count())
                },
                ["registry"] = new JObject
                {
                    ["exclusiveClaims"] = FUSE.Runtime.Registry.FuseRegistry.ExclusiveClaimCount,
                    ["sharedClaims"] = FUSE.Runtime.Registry.FuseRegistry.SharedClaimCount,
                    ["conflicts"] = FUSE.Runtime.Registry.FuseRegistry.Conflicts.Count
                },
                ["assets"] = new JObject
                {
                    ["mode"] = FuseSettings.MirrorAssetPacksToLocalLow ? "LocalLow mirror fallback" : "Direct stores",
                    ["storesScanned"] = diagnostics.StoreFolders?.Length ?? 0,
                    ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                    ["duplicateKeys"] = diagnostics.DuplicateKeys?.Length ?? 0,
                    ["failedDefinitions"] = diagnostics.FailedDefinitionLoads?.Length ?? 0
                },
                ["lastFuseLogLines"] = new JArray(ReadLastLogLines(80))
            };

            File.WriteAllText(path, bundle.ToString(Newtonsoft.Json.Formatting.Indented));
            if (openFolder)
            {
                Application.OpenURL(Path.GetDirectoryName(path));
            }

            return "Exported FUSE debug bundle: " + path;
        }

        private static long ReadProfilerMetric(Func<long> metric)
        {
            try
            {
                return metric();
            }
            catch
            {
                return 0L;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0L)
            {
                return "n/a";
            }

            return (bytes / 1048576f).ToString("0.0") + " MB";
        }

        private static string ActiveSceneName()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                return scene.IsValid() ? BlankAs(scene.name, "(unnamed)") : "none";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string LoadedSceneSummary()
        {
            try
            {
                var names = new List<string>();
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        names.Add(BlankAs(scene.name, "(unnamed)"));
                    }
                }

                return names.Count == 0
                    ? "0"
                    : names.Count + " | " + string.Join(", ", names.Take(3).ToArray()) + (names.Count > 3 ? " +" + (names.Count - 3) : string.Empty);
            }
            catch
            {
                return "unknown";
            }
        }

        private static int CountSceneRootObjects()
        {
            var total = 0;
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                total += roots == null ? 0 : roots.Length;
            }

            return total;
        }

        private static string ReadVersion()
        {
            try
            {
                var infoPath = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "Info.json");
                if (!File.Exists(infoPath))
                {
                    return "unknown";
                }

                var info = JObject.Parse(File.ReadAllText(infoPath));
                var value = info["Version"]?.ToString();
                return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
            }
            catch
            {
                return "unknown";
            }
        }

        private static string[] ReadLastLogLines(int maxLines)
        {
            try
            {
                var path = FuseLog.LogFilePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return Array.Empty<string>();
                }

                // FuseLog holds the log file open for writing for the whole
                // session, so a default read (FileShare.Read) would hit a
                // sharing violation. Open with FileShare.ReadWrite to read
                // alongside the live writer.
                var lines = ReadAllLinesShared(path);
                return lines.Skip(Math.Max(0, lines.Length - Math.Max(1, maxLines))).ToArray();
            }
            catch (Exception ex)
            {
                return new[] { "Could not read FUSE.log: " + ex.GetBaseException().Message };
            }
        }

        private static string[] ReadAllLinesShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var lines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }

                return lines.ToArray();
            }
        }
    }
}
