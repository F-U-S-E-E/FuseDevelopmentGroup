using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Runtime.Cache;
using FUSE.Infrastructure;
using GalaSoft.MvvmLight.Messaging;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FUSE.Loading
{
    /// <summary>
    /// Bridges legacy SC-style spliney handling into FUSE's loading pipeline.
    /// FUSE collects every spliney whose handler it does not recognize natively
    /// (DKW switches, ModularScenery objects, AlinasMapMod splineys, anything
    /// else that ships from a hosted old-loader plugin), builds a
    /// StrangeCustoms.Tracks.TrackState facade, fires
    /// Messenger.Default.Send(GraphWillChangeEvent), and merges whatever
    /// nodes/segments the plugins write into state.Tracks back into the
    /// converter root. Plugins self-select by handler name — FUSE intentionally
    /// has no knowledge of which plugin handles which handler.
    /// </summary>
    internal static class FuseSplineyPluginHost
    {
        private static readonly List<KeyValuePair<string, JObject>> Pending =
            new List<KeyValuePair<string, JObject>>();

        // Queued spliney-builder work for the runtime BuildSpliney pass. The
        // GraphWillChangeEvent fan-out runs during JSON conversion which is the
        // wrong phase to spawn Unity GameObjects, so we hold onto the spliney
        // data here and invoke ISplineyBuilder.BuildSpliney once the map scene
        // is live (after FuseDataPackageDiscovery.ApplyLoadedPackages).
        private static readonly List<LegacyBuilderTask> BuilderTasks =
            new List<LegacyBuilderTask>();

        /// <summary>
        /// Defers a legacy spliney whose handler FUSE doesn't recognize natively
        /// to the GraphWillChangeEvent path so loaded old-loader plugins can
        /// process it. Also queues the spliney for a post-apply BuildSpliney
        /// pass so any hosted plugin implementing StrangeCustoms.ISplineyBuilder
        /// can spawn its own visual mesh.
        /// </summary>
        public static void Register(string id, JObject splineyData)
        {
            if (string.IsNullOrWhiteSpace(id) || splineyData == null)
            {
                return;
            }

            var clone = (JObject)splineyData.DeepClone();
            Pending.Add(new KeyValuePair<string, JObject>(id, clone));

            var handler = clone["handler"]?.Value<string>() ?? clone["Handler"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(handler))
            {
                // Replace any previous task for this spliney id so a re-converted
                // package (FUSE's loader runs the legacy conversion pass twice
                // per map load) doesn't make us invoke BuildSpliney twice and
                // double up the runtime mesh/descriptor work.
                for (var i = BuilderTasks.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(BuilderTasks[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        BuilderTasks.RemoveAt(i);
                    }
                }

                BuilderTasks.Add(new LegacyBuilderTask(id, handler, (JObject)clone.DeepClone()));
            }
        }

        /// <summary>
        /// Builds a TrackState facade containing the pending splineys + a
        /// snapshot of the converter root's existing nodes/segments, fires
        /// GraphWillChangeEvent, then merges any new nodes/segments back into
        /// the converter root. Returns the count of emitted entries for logging.
        /// </summary>
        public static FlushResult Flush(JObject root)
        {
            var result = new FlushResult { PendingSplineyCount = Pending.Count };
            if (Pending.Count == 0)
            {
                return result;
            }

            var pendingSnapshot = Pending.ToArray();
            Pending.Clear();

            StrangeCustoms.Tracks.TrackState state;
            try
            {
                state = BuildState(pendingSnapshot, root);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE spliney plugin host could not build TrackState for legacy " +
                    $"GraphWillChangeEvent; skipping: {ex.GetBaseException().Message}");
                return result;
            }

            var existingNodeIds = new HashSet<string>(
                state.Tracks.Nodes.Keys,
                StringComparer.OrdinalIgnoreCase);
            var existingSegmentIds = new HashSet<string>(
                state.Tracks.Segments.Keys,
                StringComparer.OrdinalIgnoreCase);

            try
            {
                var evt = new StrangeCustoms.GraphWillChangeEvent(state, _ => { });
                Messenger.Default.Send(evt);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE spliney plugin host caught an exception while dispatching " +
                    "GraphWillChangeEvent to legacy plugin handlers; partial state may " +
                    "have been emitted",
                    ex);
            }

            result.NodesAdded = MergeNewNodes(state, existingNodeIds, root);
            result.SegmentsAdded = MergeNewSegments(state, existingSegmentIds, root);
            return result;
        }

        public static void Reset()
        {
            Pending.Clear();
            BuilderTasks.Clear();
        }

        private static StrangeCustoms.Tracks.TrackState BuildState(
            IReadOnlyList<KeyValuePair<string, JObject>> pending,
            JObject root)
        {
            var state = new StrangeCustoms.Tracks.TrackState();
            foreach (var entry in pending)
            {
                state.Splineys[entry.Key] = entry.Value;
            }

            // Plugins may inspect existing nodes/segments to make placement
            // decisions or to attach to existing topology, so populate the state
            // with what's already in the converter root.
            var rootNodes = root?["tracks"]?["nodes"] as JObject;
            if (rootNodes != null)
            {
                foreach (var property in rootNodes.Properties())
                {
                    if (property.Value is JObject node)
                    {
                        state.Tracks.Nodes[property.Name] = ToSerializedNode(node);
                    }
                }
            }

            var rootSegments = root?["tracks"]?["segments"] as JObject;
            if (rootSegments != null)
            {
                foreach (var property in rootSegments.Properties())
                {
                    if (property.Value is JObject segment)
                    {
                        state.Tracks.Segments[property.Name] = ToSerializedSegment(segment);
                    }
                }
            }

            return state;
        }

        private static StrangeCustoms.Tracks.SerializedNode ToSerializedNode(JObject node)
        {
            return new StrangeCustoms.Tracks.SerializedNode
            {
                Position = ReadVector3(node["position"]),
                Rotation = ReadVector3(node["rotation"]),
                FlipSwitchStand = node["flipSwitchStand"]?.Value<bool>() ?? false,
            };
        }

        private static StrangeCustoms.Tracks.SerializedSegment ToSerializedSegment(JObject segment)
        {
            return new StrangeCustoms.Tracks.SerializedSegment
            {
                StartId = segment["startNodeId"]?.Value<string>() ?? segment["startId"]?.Value<string>(),
                EndId = segment["endNodeId"]?.Value<string>() ?? segment["endId"]?.Value<string>(),
                Priority = segment["priority"]?.Value<int>() ?? 0,
                SpeedLimit = segment["speedLimit"]?.Value<int>() ?? 45,
                GroupId = segment["groupId"]?.Value<string>(),
            };
        }

        private static Vector3 ReadVector3(JToken token)
        {
            if (!(token is JObject obj))
            {
                return Vector3.zero;
            }

            return new Vector3(
                obj["x"]?.Value<float>() ?? 0f,
                obj["y"]?.Value<float>() ?? 0f,
                obj["z"]?.Value<float>() ?? 0f);
        }

        private static int MergeNewNodes(
            StrangeCustoms.Tracks.TrackState state,
            HashSet<string> existingIds,
            JObject root)
        {
            var rootNodes = root?["tracks"]?["nodes"] as JObject;
            if (rootNodes == null)
            {
                return 0;
            }

            var added = 0;
            foreach (var entry in state.Tracks.Nodes)
            {
                if (existingIds.Contains(entry.Key))
                {
                    continue;
                }

                rootNodes[entry.Key] = SerializeNode(entry.Value);
                added++;
            }

            return added;
        }

        private static int MergeNewSegments(
            StrangeCustoms.Tracks.TrackState state,
            HashSet<string> existingIds,
            JObject root)
        {
            var rootSegments = root?["tracks"]?["segments"] as JObject;
            if (rootSegments == null)
            {
                return 0;
            }

            var added = 0;
            foreach (var entry in state.Tracks.Segments)
            {
                if (existingIds.Contains(entry.Key))
                {
                    continue;
                }

                rootSegments[entry.Key] = SerializeSegment(entry.Value);
                added++;
            }

            return added;
        }

        private static JObject SerializeNode(StrangeCustoms.Tracks.SerializedNode node)
        {
            return new JObject
            {
                ["position"] = new JObject
                {
                    ["x"] = node.Position.x,
                    ["y"] = node.Position.y,
                    ["z"] = node.Position.z,
                },
                ["rotation"] = new JObject
                {
                    ["x"] = node.Rotation.x,
                    ["y"] = node.Rotation.y,
                    ["z"] = node.Rotation.z,
                },
                ["flipSwitchStand"] = node.FlipSwitchStand,
            };
        }

        private static JObject SerializeSegment(StrangeCustoms.Tracks.SerializedSegment segment)
        {
            var json = new JObject
            {
                ["startNodeId"] = segment.StartId,
                ["endNodeId"] = segment.EndId,
                ["style"] = "standard",
                ["trackClass"] = "main",
                ["speedLimit"] = segment.SpeedLimit,
                ["priority"] = segment.Priority,
            };
            if (!string.IsNullOrWhiteSpace(segment.GroupId))
            {
                json["groupId"] = segment.GroupId;
            }

            return json;
        }

        /// <summary>
        /// Drives the post-apply BuildSpliney pass for splineys that were
        /// deferred during conversion. Walks the queued tasks and, for each
        /// task whose handler matches a discovered StrangeCustoms.ISplineyBuilder
        /// implementation in any loaded assembly, invokes BuildSpliney so the
        /// hosted plugin can spawn its custom visual mesh and runtime
        /// descriptors. Returns a result describing what was processed for
        /// logging.
        /// </summary>
        public static BuilderInvocationResult InvokeBuilders(Transform parentTransform)
        {
            var result = new BuilderInvocationResult { TaskCount = BuilderTasks.Count };
            if (BuilderTasks.Count == 0)
            {
                return result;
            }

            var tasks = BuilderTasks.ToArray();
            BuilderTasks.Clear();

            Dictionary<string, StrangeCustoms.ISplineyBuilder> builders;
            try
            {
                builders = DiscoverSplineyBuilders();
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE spliney plugin host failed to scan loaded assemblies for " +
                    "ISplineyBuilder implementations; visual meshes for legacy splineys " +
                    "will not be created",
                    ex);
                return result;
            }

            result.BuilderTypeCount = builders.Count;
            if (builders.Count == 0)
            {
                return result;
            }

            foreach (var task in tasks)
            {
                if (!builders.TryGetValue(task.Handler, out var builder) || builder == null)
                {
                    result.UnmatchedCount++;
                    continue;
                }

                try
                {
                    var gameObject = builder.BuildSpliney(task.Id, parentTransform, task.Data);
                    RegisterPluginBuiltScenery(task.Id, gameObject);
                    result.BuiltCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    FuseLog.Warning(
                        $"FUSE spliney plugin host caught an exception while a hosted " +
                        $"old-loader plugin built spliney id='{task.Id}' " +
                        $"handler='{task.Handler}': {ex.GetBaseException().Message}");
                }
            }

            return result;
        }

        private static Dictionary<string, StrangeCustoms.ISplineyBuilder> DiscoverSplineyBuilders()
        {
            var builders = new Dictionary<string, StrangeCustoms.ISplineyBuilder>(StringComparer.OrdinalIgnoreCase);
            var fuseAssembly = typeof(FuseSplineyPluginHost).Assembly;
            var unloadableTypeCount = 0;
            string firstUnloadableType = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly.IsDynamic || assembly == fuseAssembly)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    // Type inspection itself can throw for a foreign type whose
                    // field/base types cannot be resolved (Mono raises
                    // TypeLoadException from IsAssignableFrom, e.g. a stray real
                    // Railloader.dll whose types bind against FUSE's shim). One
                    // such type must not abort the whole scan and drop every
                    // queued builder task (issue #207) — skip it and keep going.
                    if (!TryIsConcreteSplineyBuilderType(type, out var inspectionFailure))
                    {
                        if (inspectionFailure != null)
                        {
                            unloadableTypeCount++;
                            if (firstUnloadableType == null)
                            {
                                var rootFailure = inspectionFailure.GetBaseException();
                                firstUnloadableType =
                                    $"{assembly.GetName().Name}:{type?.FullName ?? "?"} ({rootFailure.GetType().Name}: {rootFailure.Message})";
                            }
                        }

                        continue;
                    }

                    try
                    {
                        if (Activator.CreateInstance(type) is StrangeCustoms.ISplineyBuilder instance)
                        {
                            // Handler IDs in legacy JSON are written as the
                            // builder type's full name (e.g. "DKW.DKWSpliney").
                            builders[type.FullName ?? type.Name] = instance;
                        }
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE spliney plugin host could not instantiate " +
                            $"ISplineyBuilder '{type.FullName}': {ex.GetBaseException().Message}");
                    }
                }
            }

            if (unloadableTypeCount > 0)
            {
                FuseLog.Warning(
                    $"FUSE spliney plugin host skipped {unloadableTypeCount} unloadable type(s) while scanning " +
                    $"for ISplineyBuilder implementations; first: {firstUnloadableType}. If this names a " +
                    "Railloader assembly, a stray legacy Railloader.dll is still being loaded from a mod folder.");
            }

            return builders;
        }

        /// <summary>
        /// A recovered legacy assembly can contain a few unusable types whose
        /// dependencies were absent during UMM's first load attempt. Mono can
        /// return those Type objects from GetTypes and then throw TypeLoadException
        /// from IsAssignableFrom. Skip only that damaged type so one unrelated
        /// plugin cannot abort discovery of every healthy spliney builder.
        /// </summary>
        internal static bool IsConcreteSplineyBuilderType(Type type)
        {
            return TryIsConcreteSplineyBuilderType(type, out _);
        }

        private static bool TryIsConcreteSplineyBuilderType(Type type, out Exception failure)
        {
            try
            {
                var result = type != null &&
                             type.IsClass &&
                             !type.IsAbstract &&
                             typeof(StrangeCustoms.ISplineyBuilder).IsAssignableFrom(type);
                failure = null;
                return result;
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
        }

        /// <summary>
        /// Some ISplineyBuilder implementations (e.g. CLB.ModularScenery)
        /// create a SceneryAssetInstance under the returned GameObject. FUSE's
        /// progression-gating, cross-package removal merge, and any other
        /// SceneryAPI.GetScenery caller assume that scenery items are
        /// discoverable via FuseSceneryRuntimeIndex — but plugin-built scenery
        /// never went through SceneryAPI.AddScenery so it doesn't get
        /// registered automatically. Register it here so a progression's
        /// "scenery://&lt;id&gt;" reference resolves and so the merged removal
        /// plan can find and delete it later.
        /// </summary>
        private static void RegisterPluginBuiltScenery(string id, GameObject gameObject)
        {
            if (string.IsNullOrWhiteSpace(id) || gameObject == null)
            {
                return;
            }

            var sceneryInstance = gameObject.GetComponent<SceneryAssetInstance>()
                ?? gameObject.GetComponentInChildren<SceneryAssetInstance>(true);
            if (sceneryInstance == null)
            {
                return;
            }

            FuseSceneryRuntimeIndex.Instance.Set(id, sceneryInstance);
        }

        private readonly struct LegacyBuilderTask
        {
            public LegacyBuilderTask(string id, string handler, JObject data)
            {
                Id = id;
                Handler = handler;
                Data = data;
            }

            public string Id { get; }
            public string Handler { get; }
            public JObject Data { get; }
        }

        public sealed class FlushResult
        {
            public int PendingSplineyCount { get; set; }
            public int NodesAdded { get; set; }
            public int SegmentsAdded { get; set; }
        }

        public sealed class BuilderInvocationResult
        {
            public int TaskCount { get; set; }
            public int BuilderTypeCount { get; set; }
            public int BuiltCount { get; set; }
            public int UnmatchedCount { get; set; }
            public int FailureCount { get; set; }
        }
    }
}
