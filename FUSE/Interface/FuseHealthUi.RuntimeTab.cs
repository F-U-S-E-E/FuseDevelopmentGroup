using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed partial class FuseHealthUi : MonoBehaviour
    {

        private static void BuildRuntimeContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Runtime Objects");
            AddValueField(builder, "Track Nodes", SafeCount(() => TrackAPI.GetAllNodes().Count()).ToString());
            AddValueField(builder, "Track Segments", SafeCount(() => TrackAPI.GetAllSegments().Count()).ToString());
            AddValueField(builder, "Track Spans", SafeCount(() => TrackAPI.GetAllSpans().Count()).ToString());
            AddValueField(builder, "Areas", SafeCount(() => TrackAPI.GetAllAreas().Count()).ToString());
            AddValueField(builder, "Loads", SafeCount(() => LoadAPI.GetAllLoads().Count()).ToString());
            AddValueField(builder, "Industries", SafeCount(() => IndustryAPI.GetAllIndustries().Count()).ToString());
            AddValueField(builder, "Loaders", SafeCount(() => LoaderAPI.GetAllLoaders().Count()).ToString());
            AddValueField(builder, "Stations", SafeCount(() => StationAPI.GetAllStationAgents().Count()).ToString());
            AddValueField(builder, "Passenger Stops", SafeCount(() => StationAPI.GetAllPassengerStops().Count()).ToString());
            AddValueField(builder, "Turntables", SafeCount(() => TurntableAPI.GetAllTurntables().Count()).ToString());
            AddValueField(builder, "Scenery", SafeCount(() => SceneryAPI.GetAllScenery().Count()).ToString());
            AddValueField(builder, "Scene Clones", SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()).ToString());
            AddValueField(builder, "Splineys", SafeCount(() => SplineyAPI.GetAllSplineys().Count()).ToString());
            AddValueField(builder, "Map Labels", SafeCount(() => MapAPI.GetAllMapLabels().Count()).ToString());
            AddValueField(builder, "Map Masks", SafeCount(() => MapAPI.GetAllMapMasks().Count()).ToString());
            AddValueField(builder, "Progressions", SafeCount(() => ProgressionAPI.GetAllProgressions().Count()).ToString());
            AddValueField(builder, "Map Features", SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count()).ToString());
            builder.Spacer(6f);

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                builder.AddSection("Registry");
                AddValueField(builder, "Exclusive Claims", FUSE.Runtime.Registry.FuseRegistry.ExclusiveClaimCount.ToString());
                AddValueField(builder, "Shared Claims", FUSE.Runtime.Registry.FuseRegistry.SharedClaimCount.ToString());
                AddValueField(builder, "Conflicts", FUSE.Runtime.Registry.FuseRegistry.Conflicts.Count.ToString());
            }
            else
            {
                AddWrappedField(
                    builder,
                    "Advanced",
                    "Enable Advanced Details in Settings to show registry claim counts and lower-level runtime diagnostics.",
                    44f);
            }
            builder.Spacer(8f);
        }

        private void AddUnityRuntimeFields(UIPanelBuilder builder)
        {
            builder.AddField("FPS", () => _fpsAverage <= 0f ? "warming up" : _fpsAverage.ToString("0.0"), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Frame Time", () => _frameMilliseconds <= 0f ? "warming up" : _frameMilliseconds.ToString("0.0") + " ms", UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Managed Memory", () => FormatBytes(_managedMemoryBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Allocated", () => FormatBytes(_unityAllocatedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Reserved", () => FormatBytes(_unityReservedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            AddValueField(builder, "Active Scene", ActiveSceneName());
            AddValueField(builder, "Loaded Scenes", LoadedSceneSummary());
            AddValueField(builder, "Scene Roots", SafeCount(CountSceneRootObjects).ToString());
            AddValueField(builder, "GameObjects", SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length).ToString());
        }

        private static void AddRailroaderRuntimeFields(UIPanelBuilder builder)
        {
            AddValueField(builder, "Track Nodes", SafeCount(() => TrackAPI.GetAllNodes().Count()).ToString());
            AddValueField(builder, "Track Segments", SafeCount(() => TrackAPI.GetAllSegments().Count()).ToString());
            AddValueField(builder, "Track Spans", SafeCount(() => TrackAPI.GetAllSpans().Count()).ToString());
            AddValueField(builder, "Areas", SafeCount(() => TrackAPI.GetAllAreas().Count()).ToString());
            AddValueField(builder, "Loads", SafeCount(() => LoadAPI.GetAllLoads().Count()).ToString());
            AddValueField(builder, "Industries", SafeCount(() => IndustryAPI.GetAllIndustries().Count()).ToString());
            AddValueField(builder, "Loaders", SafeCount(() => LoaderAPI.GetAllLoaders().Count()).ToString());
            AddValueField(builder, "Stations", SafeCount(() => StationAPI.GetAllStationAgents().Count()).ToString());
            AddValueField(builder, "Passenger Stops", SafeCount(() => StationAPI.GetAllPassengerStops().Count()).ToString());
            AddValueField(builder, "Turntables", SafeCount(() => TurntableAPI.GetAllTurntables().Count()).ToString());
            AddValueField(builder, "Scenery", SafeCount(() => SceneryAPI.GetAllScenery().Count()).ToString());
            AddValueField(builder, "Scene Clones", SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()).ToString());
            AddValueField(builder, "Splineys", SafeCount(() => SplineyAPI.GetAllSplineys().Count()).ToString());
            AddValueField(builder, "Map Labels", SafeCount(() => MapAPI.GetAllMapLabels().Count()).ToString());
            AddValueField(builder, "Map Masks", SafeCount(() => MapAPI.GetAllMapMasks().Count()).ToString());
            AddValueField(builder, "Progressions", SafeCount(() => ProgressionAPI.GetAllProgressions().Count()).ToString());
            AddValueField(builder, "Map Features", SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count()).ToString());
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

        private static string BuildStreamReadinessReport(JObject report)
        {
            var counts = report?["counts"] as JObject ?? new JObject();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Readiness");
            builder.AppendLine("State: " + (ReadBool(report?["hasProblems"], false) ? "Needs Attention" : "Ready"));
            builder.AppendLine("Summary: " + ReadString(report?["summary"], FuseLoadReport.LastSummary));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.AppendLine("Profile: " + FuseModSetService.ActiveSetName);
            builder.AppendLine("Profile Hash: " + FuseModSetService.GetActiveSetFingerprint());
            builder.AppendLine("Loaded Packages: " + ReadInt(counts["loadedPackages"]));
            builder.AppendLine("Applied Packages: " + ReadInt(counts["appliedPackages"]));
            builder.AppendLine("Faults: " + ReadInt(counts["faultedPackages"]));
            builder.AppendLine("Conflicts: " + ReadInt(counts["conflicts"]));
            builder.AppendLine("Unknown Assets: " + ReadInt(counts["unknownSceneryAssets"]));
            builder.AppendLine("Graph Issues: " + ReadInt(counts["graphIssues"]));
            builder.AppendLine("Transfer Skips: " + ReadInt(counts["progressionTransferSkips"]));
            builder.AppendLine("Suppressions: " + ReadInt(counts["suppressions"]));
            builder.AppendLine("Runtime Guards: " + FuseRuntimeGuardCounters.FormatSummary());
            builder.AppendLine("Map Load: " + FusePerformanceMetrics.FormatTiming("map load total"));
            builder.AppendLine("Runtime Apply: " + FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            return builder.ToString().TrimEnd();
        }

        private string BuildRuntimeSnapshotText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Runtime Snapshot");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion);
            builder.AppendLine();
            builder.AppendLine("Unity");
            builder.AppendLine("FPS: " + (_fpsAverage <= 0f ? "warming up" : _fpsAverage.ToString("0.0")));
            builder.AppendLine("Frame Time: " + (_frameMilliseconds <= 0f ? "warming up" : _frameMilliseconds.ToString("0.0") + " ms"));
            builder.AppendLine("Managed Memory: " + FormatBytes(_managedMemoryBytes));
            builder.AppendLine("Unity Allocated: " + FormatBytes(_unityAllocatedBytes));
            builder.AppendLine("Unity Reserved: " + FormatBytes(_unityReservedBytes));
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
    }
}
