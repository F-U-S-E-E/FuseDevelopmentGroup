using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FUSE.API;
using FUSE.Cache;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Patches;
using FUSE.Registry;
using FUSE.Validation;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Track;
using UnityEngine;
using UI.Console;
using Object = UnityEngine.Object;

namespace FUSE.Console
{
    internal static class FuseConsoleCommands
    {
        public static IList<IConsoleCommand> CreateAll()
        {
            return new List<IConsoleCommand>
            {
                new FuseReportCommand(),
                new FuseLoadedCommand(),
                new FuseAssetsCommand(),
                new FuseGraphCommand(),
                new FuseProgressionsCommand(),
                new FuseOperationsCommand(),
                new FuseDumpGraphCommand(),
                new FuseDumpMandelasCommand(),
                new FuseGroupsCommand(),
                new FuseValidateCommand(),
                new FuseConflictsCommand(),
                new FuseSuppressionsCommand(),
                new FusePatchesCommand(),
                new FuseReapplyCommand(),
                new FuseRestoreCommand()
            };
        }

        internal static bool IsInSession()
        {
            // Best-effort: a populated graph means a map is loaded and gameplay
            // is in or near runtime. Refuse destructive console actions then.
            try
            {
                return Graph.Shared != null && Graph.Shared.HasPopulatedCollections;
            }
            catch
            {
                return false;
            }
        }

        internal static string SessionGuardMessage(string commandName, string[] components)
        {
            var hasForce = components != null && components.Any(arg =>
                string.Equals(arg, "--force", StringComparison.OrdinalIgnoreCase));
            if (!IsInSession() || hasForce)
            {
                return null;
            }

            return $"{commandName} refused: a map is currently loaded. Pass --force to override " +
                   "(may destabilize the running save).";
        }

        internal static string GetRailroaderRootFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Application.dataPath))
                {
                    var directory = Directory.GetParent(Application.dataPath);
                    if (directory != null && directory.Exists)
                    {
                        return directory.FullName;
                    }
                }
            }
            catch
            {
                // Fall back below.
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        internal static string WriteJsonToRailroaderRoot(string fileName, object data)
        {
            var root = GetRailroaderRootFolder();
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented, JsonSettings()));
            return path;
        }

        internal static JsonSerializerSettings JsonSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        }

        internal static object Vector(Vector3 value)
        {
            return new
            {
                x = Math.Round(value.x, 6),
                y = Math.Round(value.y, 6),
                z = Math.Round(value.z, 6)
            };
        }

        internal static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            var cursor = transform;
            while (cursor != null)
            {
                names.Push(cursor.name);
                cursor = cursor.parent;
            }

            return string.Join("/", names.ToArray());
        }
    }

    [ConsoleCommand("/fuse.dumpgraph", "Dump FUSE's captured original Railroader track graph to FUSE-original-graph.json in the Railroader folder.")]
    public sealed class FuseDumpGraphCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var snapshot = TrackAPI.GetBaseGraphSnapshotDefinition();
                if (snapshot == null)
                {
                    return "FUSE dumpgraph: original/base graph snapshot is not available yet. Load a map with FUSE active, then run the command after map load.";
                }

                var data = new
                {
                    tool = "FUSE",
                    dumpType = "originalGraph",
                    createdLocal = DateTime.Now.ToString("O"),
                    source = "Captured from Railroader runtime graph before FUSE track mutations; no AMM graph-original.json was used.",
                    counts = new
                    {
                        nodes = snapshot.Nodes?.Count ?? 0,
                        segments = snapshot.Segments?.Count ?? 0,
                        spans = snapshot.Spans?.Count ?? 0
                    },
                    tracks = snapshot
                };

                var path = FuseConsoleCommands.WriteJsonToRailroaderRoot("FUSE-original-graph.json", data);
                return $"FUSE dumpgraph wrote '{path}' nodes={snapshot.Nodes?.Count ?? 0} segments={snapshot.Segments?.Count ?? 0} spans={snapshot.Spans?.Count ?? 0}.";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE dumpgraph console command failed.", ex);
                return $"FUSE dumpgraph failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.dumpmandelas", "Dump loaded scene-clone definitions and current World scene paths to FUSE-mandelas.json in the Railroader folder.")]
    public sealed class FuseDumpMandelasCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var definitions = FuseModLoader.GetLoadedModsInOrder()
                    .Where(loaded => loaded?.Definition?.World?.SceneClones != null && loaded.Definition.World.SceneClones.Count > 0)
                    .Select(loaded => new
                    {
                        packageId = loaded.Definition.Id,
                        sceneClones = loaded.Definition.World.SceneClones
                            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(item => new
                            {
                                id = item.Key,
                                item.Value.TargetPath,
                                item.Value.Source,
                                item.Value.Enabled,
                                item.Value.LocalPosition,
                                item.Value.LocalRotation,
                                item.Value.LocalScale
                            })
                            .ToArray()
                    })
                    .ToArray();

                var runtimeClones = SceneCloneAPI.GetAllSceneClones()
                    .OrderBy(go => FuseConsoleCommands.GetTransformPath(go.transform), StringComparer.OrdinalIgnoreCase)
                    .Select(go => new
                    {
                        path = FuseConsoleCommands.GetTransformPath(go.transform),
                        definition = SceneCloneAPI.GetDefinition(go),
                        activeSelf = go.activeSelf,
                        activeInHierarchy = go.activeInHierarchy,
                        position = FuseConsoleCommands.Vector(go.transform.position),
                        localPosition = FuseConsoleCommands.Vector(go.transform.localPosition)
                    })
                    .ToArray();

                var worldObjects = EnumerateWorldObjects().ToArray();
                var data = new
                {
                    tool = "FUSE",
                    dumpType = "mandelas",
                    createdLocal = DateTime.Now.ToString("O"),
                    notes = "Mandelas are represented in FUSE as world.sceneClones. sceneObjects lists current World hierarchy paths that may be useful vanilla:// clone sources.",
                    counts = new
                    {
                        packagesWithSceneClones = definitions.Length,
                        runtimeSceneClones = runtimeClones.Length,
                        sceneObjects = worldObjects.Length
                    },
                    loadedDefinitions = definitions,
                    runtimeClones,
                    sceneObjects = worldObjects
                };

                var path = FuseConsoleCommands.WriteJsonToRailroaderRoot("FUSE-mandelas.json", data);
                return $"FUSE dumpmandelas wrote '{path}' packages={definitions.Length} runtimeClones={runtimeClones.Length} sceneObjects={worldObjects.Length}.";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE dumpmandelas console command failed.", ex);
                return $"FUSE dumpmandelas failed: {ex.Message}";
            }
        }

        private static IEnumerable<object> EnumerateWorldObjects()
        {
            var roots = Object.FindObjectsOfType<Transform>(true)
                .Where(transform => transform != null && transform.parent == null && string.Equals(transform.name, "World", StringComparison.OrdinalIgnoreCase))
                .OrderBy(transform => transform.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var root in roots)
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == null)
                    {
                        continue;
                    }

                    var gameObject = transform.gameObject;
                    var renderers = gameObject.GetComponentsInChildren<Renderer>(true);
                    var lodGroups = gameObject.GetComponentsInChildren<LODGroup>(true);
                    var components = gameObject.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().FullName ?? component.GetType().Name)
                        .ToArray();

                    yield return new
                    {
                        path = FuseConsoleCommands.GetTransformPath(transform),
                        name = gameObject.name,
                        activeSelf = gameObject.activeSelf,
                        activeInHierarchy = gameObject.activeInHierarchy,
                        localPosition = FuseConsoleCommands.Vector(transform.localPosition),
                        localRotation = FuseConsoleCommands.Vector(transform.localEulerAngles),
                        localScale = FuseConsoleCommands.Vector(transform.localScale),
                        rendererCount = renderers.Length,
                        enabledRendererCount = renderers.Count(renderer => renderer != null && renderer.enabled),
                        lodGroupCount = lodGroups.Length,
                        mapMaskCount = CountComponentsByName(gameObject, "MapMask"),
                        sceneryAssetIdentifier = TryGetSceneryAssetIdentifier(gameObject),
                        components
                    };
                }
            }
        }

        private static int CountComponentsByName(GameObject gameObject, string typeNameContains)
        {
            return gameObject.GetComponentsInChildren<Component>(true)
                .Count(component => component != null &&
                                    component.GetType().Name.IndexOf(typeNameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string TryGetSceneryAssetIdentifier(GameObject gameObject)
        {
            var component = gameObject.GetComponents<Component>()
                .FirstOrDefault(item => item != null &&
                                        item.GetType().Name.IndexOf("SceneryAssetInstance", StringComparison.OrdinalIgnoreCase) >= 0);
            if (component == null)
            {
                return null;
            }

            foreach (var name in new[] { "identifier", "Identifier", "assetIdentifier", "AssetIdentifier" })
            {
                var field = component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(component) as string;
                }

                var property = component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanRead)
                {
                    return property.GetValue(component, null) as string;
                }
            }

            return null;
        }
    }

    [ConsoleCommand("/fuse.assets", "List FUSE asset pack folders discovered for direct PrefabStore loading.")]
    public sealed class FuseAssetsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var folders = FuseAssetPackRegistry.EnumerateAvailableAssetPackFolders()
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"FUSE asset packs: discovered={folders.Length}; mirrorToLocalLow={FuseSettings.MirrorAssetPacksToLocalLow}.");
                foreach (var folder in folders)
                {
                    sb.AppendLine("  " + folder);
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE assets console command failed.", ex);
                return $"FUSE assets failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.graph", "Summarize the active Railroader graph and FUSE track definitions.")]
    public sealed class FuseGraphCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var graph = Graph.Shared;
                var graphReady = graph != null && graph.HasPopulatedCollections;
                var runtimeNodes = TrackAPI.GetAllNodes().Count();
                var runtimeSegments = TrackAPI.GetAllSegments().Count();
                var runtimeSpans = TrackAPI.GetAllSpans().Count();
                var runtimeAreas = TrackAPI.GetAllAreas().Count();

                var definitionNodes = 0;
                var definitionSegments = 0;
                var definitionSpans = 0;
                var definitionAreas = 0;
                var removalsNodes = 0;
                var removalsSegments = 0;
                var removalsSpans = 0;

                foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
                {
                    var tracks = loaded?.Definition?.Tracks;
                    if (tracks == null)
                    {
                        continue;
                    }

                    definitionNodes += tracks.Nodes?.Count ?? 0;
                    definitionSegments += tracks.Segments?.Count ?? 0;
                    definitionSpans += tracks.Spans?.Count ?? 0;
                    definitionAreas += tracks.Areas?.Count ?? 0;
                    removalsNodes += tracks.Removals?.Nodes?.Length ?? 0;
                    removalsSegments += tracks.Removals?.Segments?.Length ?? 0;
                    removalsSpans += tracks.Removals?.Spans?.Length ?? 0;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"FUSE graph: populated={graphReady}; runtime nodes={runtimeNodes} segments={runtimeSegments} spans={runtimeSpans} areas={runtimeAreas}.");
                sb.AppendLine($"FUSE graph definitions: nodes={definitionNodes} segments={definitionSegments} spans={definitionSpans} areas={definitionAreas}.");
                sb.AppendLine($"FUSE graph removals: nodes={removalsNodes} segments={removalsSegments} spans={removalsSpans}.");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE graph console command failed.", ex);
                return $"FUSE graph failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.progressions", "Summarize FUSE progression sections, map features, and delivery phases.")]
    public sealed class FuseProgressionsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var sb = new StringBuilder();
                var totalSections = 0;
                var totalMapFeatures = 0;
                var totalDeliveryPhases = 0;
                var totalDeliveries = 0;

                foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
                {
                    var definition = loaded?.Definition;
                    var progression = definition?.Progression;
                    if (progression == null)
                    {
                        continue;
                    }

                    var sections = CountProgressionSections(progression);
                    var mapFeatures = progression.MapFeatures?.Count ?? 0;
                    var deliveryPhases = CountDeliveryPhases(progression);
                    var deliveries = CountDeliveries(progression);
                    if (sections == 0 && mapFeatures == 0 && deliveryPhases == 0 && deliveries == 0)
                    {
                        continue;
                    }

                    totalSections += sections;
                    totalMapFeatures += mapFeatures;
                    totalDeliveryPhases += deliveryPhases;
                    totalDeliveries += deliveries;
                    sb.AppendLine($"  {definition.Id}: sections={sections} mapFeatures={mapFeatures} deliveryPhases={deliveryPhases} deliveries={deliveries}");
                }

                sb.Insert(0, $"FUSE progressions: sections={totalSections} mapFeatures={totalMapFeatures} deliveryPhases={totalDeliveryPhases} deliveries={totalDeliveries}.{Environment.NewLine}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE progressions console command failed.", ex);
                return $"FUSE progressions failed: {ex.Message}";
            }
        }

        private static int CountProgressionSections(Data.FuseProgressionRoot progression)
        {
            return (progression.Sections?.Length ?? 0) +
                   (progression.Progressions?.Values.Sum(value => value?.Sections?.Count ?? 0) ?? 0);
        }

        private static int CountDeliveryPhases(Data.FuseProgressionRoot progression)
        {
            return EnumerateSections(progression).Sum(section => section?.DeliveryPhases?.Length ?? 0);
        }

        private static int CountDeliveries(Data.FuseProgressionRoot progression)
        {
            return EnumerateSections(progression)
                .SelectMany(section => section?.DeliveryPhases ?? Enumerable.Empty<Data.FuseDeliveryPhase>())
                .Sum(phase => phase?.Deliveries?.Length ?? 0);
        }

        private static IEnumerable<Data.FuseSection> EnumerateSections(Data.FuseProgressionRoot progression)
        {
            return (progression.Sections ?? Enumerable.Empty<Data.FuseSection>())
                .Concat((progression.Progressions?.Values ?? Enumerable.Empty<Data.FuseProgression>())
                    .SelectMany(value => value?.Sections?.Values ?? Enumerable.Empty<Data.FuseSection>()));
        }
    }

    [ConsoleCommand("/fuse.operations", "Summarize FUSE loads, industries, components, loaders, stations, and turntables.")]
    public sealed class FuseOperationsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var sb = new StringBuilder();
                var totalLoads = 0;
                var totalIndustries = 0;
                var totalComponents = 0;
                var totalLoaders = 0;
                var totalStations = 0;
                var totalTurntables = 0;
                var componentTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var loaded in FuseModLoader.GetLoadedModsInOrder())
                {
                    var definition = loaded?.Definition;
                    var operations = definition?.Operations;
                    if (operations == null)
                    {
                        continue;
                    }

                    var loads = operations.Loads?.Count ?? 0;
                    var industries = operations.Industries?.Count ?? 0;
                    var componentCount = operations.Industries?.Values.Sum(industry => industry?.Components?.Count ?? 0) ?? 0;
                    var loaders = operations.Loaders?.Count ?? 0;
                    var stations = operations.Stations?.Count ?? 0;
                    var turntables = operations.Turntables?.Count ?? 0;
                    if (loads == 0 && industries == 0 && componentCount == 0 && loaders == 0 && stations == 0 && turntables == 0)
                    {
                        continue;
                    }

                    totalLoads += loads;
                    totalIndustries += industries;
                    totalComponents += componentCount;
                    totalLoaders += loaders;
                    totalStations += stations;
                    totalTurntables += turntables;
                    foreach (var component in operations.Industries?.Values
                                 .SelectMany(industry => industry?.Components?.Values ?? Enumerable.Empty<Data.FuseIndustryComponent>())
                             ?? Enumerable.Empty<Data.FuseIndustryComponent>())
                    {
                        var type = string.IsNullOrWhiteSpace(component?.Type) ? "<blank>" : component.Type.Trim();
                        componentTypes[type] = componentTypes.TryGetValue(type, out var count) ? count + 1 : 1;
                    }

                    sb.AppendLine($"  {definition.Id}: loads={loads} industries={industries} components={componentCount} loaders={loaders} stations={stations} turntables={turntables}");
                }

                sb.Insert(0, $"FUSE operations: loads={totalLoads} industries={totalIndustries} components={totalComponents} loaders={totalLoaders} stations={totalStations} turntables={totalTurntables}.{Environment.NewLine}");
                if (componentTypes.Count > 0)
                {
                    sb.AppendLine("Component types:");
                    foreach (var entry in componentTypes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"  {entry.Key}: {entry.Value}");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE operations console command failed.", ex);
                return $"FUSE operations failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.report", "Show the last human-readable FUSE map-load report.")]
    public sealed class FuseReportCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            return FuseLoadReport.GetLastDetailReport();
        }
    }

    [ConsoleCommand("/fuse.loaded", "List loaded FUSE packages and their applied/faulted state.")]
    public sealed class FuseLoadedCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            var faulted = FusePackageFaultRegistry.GetFaultedPackageIds();
            var ids = FuseModLoader.GetLoadedMods()
                .Concat(faulted)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            sb.AppendLine($"FUSE loaded packages: {ids.Length}");
            foreach (var id in ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var statuses = new List<string>();
                statuses.Add(FuseModLoader.IsApplied(id) ? "applied" : "loaded-not-applied");
                if (FusePackageFaultRegistry.IsFaulted(id))
                {
                    statuses.Add("faulted");
                }

                var status = string.Join(", ", statuses.ToArray());
                sb.AppendLine($"  {id}  [{status}]");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/fuse.groups", "List runtime track groups discovered on the active graph.")]
    public sealed class FuseGroupsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            try
            {
                var graph = Graph.Shared;
                if (graph == null || !graph.HasPopulatedCollections)
                {
                    return "FUSE groups: track graph is not populated yet.";
                }

                var groups = graph.Segments
                    .Where(seg => seg != null && !string.IsNullOrWhiteSpace(seg.groupId))
                    .GroupBy(seg => seg.groupId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var sb = new StringBuilder();
                sb.AppendLine($"FUSE track groups: {groups.Length} (segments-with-group / total {graph.Segments.Count()}).");
                foreach (var group in groups)
                {
                    sb.AppendLine($"  {group.Key}  segments={group.Count()}");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"FUSE groups failed: {ex.Message}";
            }
        }
    }

    [ConsoleCommand("/fuse.validate", "Re-run the FUSE validator for a loaded mod id.")]
    public sealed class FuseValidateCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var modId = components != null && components.Length > 0 ? components[0] : null;
            if (string.IsNullOrWhiteSpace(modId))
            {
                return "Usage: /fuse.validate <modId>";
            }

            var definition = FuseModLoader.GetLoadedDefinition(modId);
            if (definition == null)
            {
                return $"FUSE validate: mod '{modId}' is not loaded.";
            }

            var result = new FuseDefinitionValidator().Validate(definition);
            var sb = new StringBuilder();
            sb.AppendLine($"FUSE validate '{modId}': errors={result.Errors.Count} warnings={result.Warnings.Count}");
            foreach (var error in result.Errors)
            {
                sb.AppendLine($"  [error] {error.Field}: {error.Message} ({error.Code ?? string.Empty})");
            }

            foreach (var warning in result.Warnings)
            {
                sb.AppendLine($"  [warn ] {warning.Field}: {warning.Message} ({warning.Code ?? string.Empty})");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/fuse.conflicts", "List FUSE registry conflicts (recorded ownership collisions).")]
    public sealed class FuseConflictsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var conflicts = FuseRegistry.Conflicts;
            var sb = new StringBuilder();
            sb.AppendLine(
                $"FUSE registry: exclusive={FuseRegistry.ExclusiveClaimCount} shared={FuseRegistry.SharedClaimCount} " +
                $"conflicts={conflicts.Count}");
            foreach (var conflict in conflicts.OrderByDescending(c => c.AtUtc))
            {
                sb.AppendLine(
                    $"  target='{conflict.Target ?? conflict.Kind.ToString()}' kind='{conflict.Kind}' id='{conflict.Id}': " +
                    $"owner='{conflict.OwnerPackageId}' attempted='{conflict.AttemptedPackageId}' " +
                    $"resolution='{conflict.Resolution ?? "claim skipped"}' at={conflict.AtUtc:HH:mm:ss}Z");
            }

            return sb.ToString();
        }
    }

    [ConsoleCommand("/fuse.suppressions", "List active FUSE world suppressions.")]
    public sealed class FuseSuppressionsCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var scenePaths = FuseWorldSuppressor.GetActiveScenePathSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var groups = FuseWorldSuppressor.GetActiveTrackGroupSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var areas = FuseWorldSuppressor.GetActiveAreaSuppressions()
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine(
                $"FUSE suppressions: scenePaths={scenePaths.Length} trackGroups={groups.Length} areas={areas.Length}.");
            AppendSuppressionList(sb, "scene paths", scenePaths);
            AppendSuppressionList(sb, "track groups", groups);
            AppendSuppressionList(sb, "areas", areas);
            return sb.ToString();
        }

        private static void AppendSuppressionList(StringBuilder sb, string label, IEnumerable<string> values)
        {
            var items = (values ?? Enumerable.Empty<string>()).ToArray();
            if (items.Length == 0)
            {
                return;
            }

            sb.AppendLine("  " + label + ":");
            foreach (var item in items)
            {
                sb.AppendLine("    " + item);
            }
        }
    }

    [ConsoleCommand("/fuse.patches", "List Harmony patch classes applied or skipped by FUSE.")]
    public sealed class FusePatchesCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FUSE Harmony patches: applied={FusePatchResilience.Applied.Count} failed={FusePatchResilience.Failed.Count}");
            foreach (var info in FusePatchResilience.Applied.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [ok  ] {info.TypeName}");
            }

            foreach (var info in FusePatchResilience.Failed.OrderBy(p => p.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  [fail] {info.TypeName}: {info.FailureReason}");
            }

            return sb.ToString();
        }
    }

    [Experimental("Mid-session reapply may destabilize a running save; gated by --force.")]
    [ConsoleCommand("/fuse.reapply", "[experimental] Re-apply loaded FUSE definitions. Refused while a map is loaded unless --force is passed.")]
    public sealed class FuseReapplyCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Console./fuse.reapply",
                "mid-session reapply via console");

            var guard = FuseConsoleCommands.SessionGuardMessage("/fuse.reapply", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                FuseCacheRegistry.RebuildAll();
                var applied = FuseDataPackageDiscovery.ApplyLoadedPackages("fuse.reapply console");
                return $"FUSE reapply: applied={applied} resident definition(s).";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE reapply console command failed.", ex);
                return $"FUSE reapply failed: {ex.Message}";
            }
        }
    }

    [Experimental("Full unload + disk reload + reapply; not safe mid-session, gated by --force.")]
    [ConsoleCommand("/fuse.restore", "[experimental] Reload FUSE packages from disk and reapply. Refused while a map is loaded unless --force is passed.")]
    public sealed class FuseRestoreCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            FuseExperimentalLog.WarnFirstUse(
                "FUSE.Console./fuse.restore",
                "mid-session full restore via console");

            var guard = FuseConsoleCommands.SessionGuardMessage("/fuse.restore", components);
            if (guard != null)
            {
                return guard;
            }

            try
            {
                FuseModLoader.UnloadAll();
                FuseCacheRegistry.ClearAll();
                var loaded = FuseDataPackageDiscovery.LoadPackagesFromDisk(true);
                FuseCacheRegistry.RebuildAll();
                var applied = FuseDataPackageDiscovery.ApplyLoadedPackages("fuse.restore console");
                return $"FUSE restore: loadedFromDisk={loaded} appliedToRuntime={applied}.";
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE restore console command failed.", ex);
                return $"FUSE restore failed: {ex.Message}";
            }
        }
    }
}
