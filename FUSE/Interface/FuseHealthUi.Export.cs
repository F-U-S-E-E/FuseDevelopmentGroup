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

        private static string ExportHealthReportJson()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-health-report.json");
            File.WriteAllText(path, FuseLoadReport.GetLastJsonReport());
            return "Exported FUSE health JSON report: " + path;
        }

        private string ExportDebugBundle()
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
                    ["fps"] = _fpsAverage,
                    ["frameMilliseconds"] = _frameMilliseconds,
                    ["managedMemoryBytes"] = _managedMemoryBytes,
                    ["unityAllocatedBytes"] = _unityAllocatedBytes,
                    ["unityReservedBytes"] = _unityReservedBytes,
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
                    ["mode"] = AssetPackModeText(),
                    ["storesScanned"] = diagnostics.StoreFolders?.Length ?? 0,
                    ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                    ["duplicateKeys"] = diagnostics.DuplicateKeys?.Length ?? 0,
                    ["failedDefinitions"] = diagnostics.FailedDefinitionLoads?.Length ?? 0
                },
                ["lastFuseLogLines"] = new JArray(ReadLastLogLines(80))
            };

            File.WriteAllText(path, bundle.ToString(Newtonsoft.Json.Formatting.Indented));
            return "Exported FUSE debug bundle: " + path;
        }
    }
}
