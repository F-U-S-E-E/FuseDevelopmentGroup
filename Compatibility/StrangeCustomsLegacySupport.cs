using System;
using System.Collections.Generic;
using System.IO;
using Helpers;
using Model;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railloader;
using Track;
using UI.Builder;
using UI.Console;
using UnityEngine;

// Interop declarations matching the StrangeCustoms public-API shape that the
// old-loader plugin ecosystem (DKW, ModularScenery, Alina's Map Mod, etc.) was
// compiled against. FUSE.dll declares these types so the CLR can resolve
// TypeRefs from those legacy DLLs when FuseLegacySupportAssemblyShim redirects
// `StrangeCustoms.dll` requests to this assembly.
//
// Bodies are FUSE-controlled facades — none of the StrangeCustoms implementation
// has been copied. FUSE's loading pipeline owns these types and is free to
// remove them once we stop honoring the old-loader contract.

namespace StrangeCustoms
{
    // Shared [Obsolete] message used across the legacy shim. Lifted to a
    // constant so every type carries the same migration hint and the message
    // can be updated in one place.
    internal static class LegacyShim
    {
        internal const string Message = "Legacy StrangeCustoms compatibility shim. Use FUSE's native API for new packages.";
    }

    /// <summary>
    /// Spliney builder contract that legacy mods implement to produce custom
    /// scenery / track geometry from JSON data inside the legacy graph.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public interface ISplineyBuilder
    {
        GameObject BuildSpliney(string id, Transform parentTransform, JObject data);
    }

    /// <summary>
    /// Console command surface kept for legacy plugins that reference it. FUSE
    /// has its own diagnostic command set; this is a no-op stub.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class DumpMandelasCommand : IConsoleCommand
    {
        public string Keyword => "dump-mandelas";
        public string HelpText => "Legacy StrangeCustoms diagnostic; not wired under FUSE.";
        public string Usage => Keyword;

        public string Execute(string[] components)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// File-cache shape that legacy plugins use to fetch external assets at
    /// runtime. The shim declares the surface so plugin IL JITs cleanly;
    /// FUSE's asset pipeline does not populate <see cref="Instance"/>, so any
    /// runtime call goes through the no-op bodies below.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class FileCache : MonoBehaviour
    {
        [Obsolete(LegacyShim.Message)]
        public class CacheEntry
        {
            public CacheEntry(string fileName)
            {
                FileName = fileName ?? string.Empty;
            }

            public string FileName { get; }
            public DateTime LastUpdate { get; protected set; }
            public bool IsValid { get; protected set; }
            public bool IsExpired
            {
                get
                {
                    try
                    {
                        return File.GetLastWriteTime(FileName) >= LastUpdate;
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
            public bool IsLoading { get; set; }

            public virtual void Invalidate()
            {
                IsValid = false;
            }
        }

        [Obsolete(LegacyShim.Message)]
        public class CacheEntry<T> : CacheEntry
        {
            public CacheEntry(string fileName) : base(fileName)
            {
            }

            public T Value { get; private set; }
            public event Action<T> Loaded;

            public void Set(T value)
            {
                Value = value;
                LastUpdate = DateTime.UtcNow;
                IsValid = true;
                IsLoading = false;
                var handler = Loaded;
                Loaded = null;
                handler?.Invoke(value);
            }

            public void Set(Func<T> deferredSet)
            {
                if (deferredSet == null)
                {
                    return;
                }
                Set(deferredSet());
            }

            public override void Invalidate()
            {
                base.Invalidate();
                Value = default;
            }

            public void Register(Action<T> callback)
            {
                if (callback == null)
                {
                    return;
                }
                if (IsValid)
                {
                    callback(Value);
                    return;
                }
                Loaded += callback;
            }

            public override string ToString()
            {
                return $"CacheEntry<{typeof(T).Name}> '{FileName}' valid={IsValid}";
            }
        }

        public static FileCache Instance { get; private set; }

        public void LoadAudioClip(string fileName, Action<AudioClip> callback)
        {
            // FUSE does not run the legacy file-cache pipeline. The callback is
            // never invoked under FUSE; the method exists for interop only.
        }

        public Texture2D LoadTexture(string fileName, out bool wasCached)
        {
            wasCached = false;
            return null;
        }

        public bool TryGetValue<T>(string fileName, out CacheEntry<T> value)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Single point in a legacy spliney definition. Position-and-rotation pair.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedSplinePoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
    }

    /// <summary>
    /// Point in a legacy river-style spliney with a width modulation.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedRiverPoint
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Width { get; set; } = 1f;
    }

    /// <summary>
    /// Default spliney builder for "flowy" splines. FUSE does not own the
    /// rendering, so the build method returns null; the type exists so plugin
    /// IL that references it JITs.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class FlowyThingBuilder : ISplineyBuilder
    {
        public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
        {
            return null;
        }
    }

    /// <summary>
    /// Carries a writable view of the legacy graph state to plugins that
    /// subscribe via Messenger.Default. Plugins mutate State.Tracks /
    /// State.Splineys to inject nodes/segments before FUSE applies them.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public struct GraphWillChangeEvent
    {
        public global::StrangeCustoms.Tracks.TrackState State;
        private readonly Action<string[]> _onMarkChanged;

        internal GraphWillChangeEvent(global::StrangeCustoms.Tracks.TrackState state, Action<string[]> onMarkChanged)
        {
            State = state;
            _onMarkChanged = onMarkChanged;
        }

        public void MarkChanged(params string[] path)
        {
            _onMarkChanged?.Invoke(path);
        }
    }

    /// <summary>
    /// Fired after the legacy graph has been applied to the game.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public struct GraphDidChangeEvent
    {
        public global::StrangeCustoms.Tracks.TrackState State;

        internal GraphDidChangeEvent(global::StrangeCustoms.Tracks.TrackState state)
        {
            State = state;
        }
    }

    /// <summary>
    /// Fired before legacy mixinto JSON is deserialized so plugins can layer
    /// in additional patches. FUSE's converter owns the patch path natively,
    /// so this carries no patch state by default; the ApplyPatch method exists
    /// for interop and is a no-op unless FUSE wires it through.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public struct GraphJsonWillDeserializeEvent
    {
        private readonly IReadOnlyDictionary<string, string> _changedKeys;
        private readonly Action<string, JObject> _onApplyPatch;

        internal GraphJsonWillDeserializeEvent(
            IReadOnlyDictionary<string, string> changedKeys,
            Action<string, JObject> onApplyPatch)
        {
            _changedKeys = changedKeys;
            _onApplyPatch = onApplyPatch;
        }

        public IReadOnlyDictionary<string, string> ChangedKeys =>
            _changedKeys ?? EmptyChangedKeys;

        public void ApplyPatch(string patchSource, JObject patch)
        {
            _onApplyPatch?.Invoke(patchSource, patch);
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyChangedKeys =
            new Dictionary<string, string>(0);
    }

    /// <summary>
    /// Plugin host shape that the legacy StrangeCustoms mod registered. FUSE
    /// supersedes the original; this stub exists so legacy plugins that
    /// reference <c>StrangeCustomsPlugin.Shared</c> for tab handlers JIT
    /// against a defined type. Shared is set by the SingletonPluginBase
    /// constructor in the FUSE-shimmed Railloader namespace.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class StrangeCustomsPlugin : SingletonPluginBase<StrangeCustomsPlugin>, IModTabHandler
    {
        public StrangeCustomsPlugin(IModDefinition self, IModdingContext moddingContext, IUIHelper uiHelper)
        {
            // FUSE does not invoke the legacy plugin lifecycle here; the
            // constructor exists only so the type can be loaded.
        }

        public override void OnDisable()
        {
        }

        public void ModTabDidOpen(UIPanelBuilder builder)
        {
        }

        public void ModTabDidClose()
        {
        }
    }
}

namespace StrangeCustoms.Tracks
{
    /// <summary>
    /// Empty marker subinterface used by some legacy plugin generics. Inherits
    /// the outer <see cref="StrangeCustoms.ISplineyBuilder"/> contract.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public interface ISplineyBuilder : StrangeCustoms.ISplineyBuilder
    {
    }

    /// <summary>
    /// Mutable view of the legacy graph that GraphWillChangeEvent hands to
    /// plugins. FUSE populates Tracks and Splineys before firing the event and
    /// reads back any mutations to fold into its own graph.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class TrackState
    {
        public GraphTracks Tracks { get; internal set; } = new GraphTracks();
        public Dictionary<string, JObject> Splineys { get; internal set; } =
            new Dictionary<string, JObject>();
    }

    [Obsolete(LegacyShim.Message)]
    public class GraphTracks
    {
        public Dictionary<string, SerializedNode> Nodes { get; internal set; } =
            new Dictionary<string, SerializedNode>();
        public Dictionary<string, SerializedSegment> Segments { get; internal set; } =
            new Dictionary<string, SerializedSegment>();
    }

    [Obsolete(LegacyShim.Message)]
    public class SerializedNode
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool FlipSwitchStand { get; set; }

        public SerializedNode()
        {
        }

        public SerializedNode(TrackNode trackNode)
        {
            if (trackNode == null)
            {
                return;
            }

            var t = trackNode.transform;
            Position = t.localPosition;
            Rotation = t.eulerAngles;
            FlipSwitchStand = trackNode.flipSwitchStand;
        }
    }

    [Obsolete(LegacyShim.Message)]
    public class SerializedSegment
    {
        public TrackSegment.Style Style { get; set; }
        public TrackClass TrackClass { get; set; }
        public string StartId { get; set; }
        public string EndId { get; set; }
        public int Priority { get; set; }
        public int SpeedLimit { get; set; } = 45;
        public string GroupId { get; set; }

        public SerializedSegment()
        {
        }

        public SerializedSegment(TrackSegment trackSegment)
        {
            if (trackSegment == null)
            {
                return;
            }

            StartId = trackSegment.a?.id;
            EndId = trackSegment.b?.id;
            Style = trackSegment.style;
            TrackClass = trackSegment.trackClass;
            Priority = trackSegment.priority;
            SpeedLimit = trackSegment.speedLimit;
            GroupId = trackSegment.groupId;
        }
    }

    /// <summary>
    /// Which end of a TrackSegment a SerializedLocation is anchored at.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public enum SerializedSegmentEnd
    {
        Start,
        End
    }

    /// <summary>
    /// Pointer into the track graph at a specific distance from a segment end.
    /// Referenced by SignalsEverywhere's SerializedCTCSignal.Location field
    /// and by CustomSpawnPoints, which round-trips through the implicit
    /// conversion to <see cref="SerializableLocation"/>.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedLocation
    {
        public string SegmentId { get; set; }
        public float Distance { get; set; }
        public SerializedSegmentEnd End { get; set; }

        public SerializedLocation()
        {
        }

        public SerializedLocation(SerializableLocation loc)
        {
            SegmentId = loc.segmentId;
            Distance = loc.distance;
            // Game's TrackSegment.End enum is { A=0, B=1 }; map A to the
            // "Start" alias the legacy contract used and B to "End".
            End = loc.end switch
            {
                TrackSegment.End.A => SerializedSegmentEnd.Start,
                TrackSegment.End.B => SerializedSegmentEnd.End,
                _ => throw new ArgumentException($"Unrecognized TrackSegment.End value '{loc.end}'.", nameof(loc)),
            };
        }

        public static implicit operator SerializableLocation(SerializedLocation loc)
        {
            if (loc is null)
            {
                return default;
            }

            var nativeEnd = loc.End switch
            {
                SerializedSegmentEnd.Start => TrackSegment.End.A,
                SerializedSegmentEnd.End => TrackSegment.End.B,
                _ => throw new ArgumentException($"Unrecognized SerializedSegmentEnd value '{loc.End}'.", nameof(loc)),
            };

            return new SerializableLocation(loc.SegmentId, loc.Distance, nativeEnd);
        }
    }

    /// <summary>
    /// A track span defined by Upper and Lower SerializedLocations. Referenced
    /// from SignalsEverywhere's SerializedCTCBlock.Spans collection.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedSpan
    {
        public SerializedLocation Upper { get; set; }
        public SerializedLocation Lower { get; set; }
        public bool Normalize { get; set; }

        public SerializedSpan()
        {
        }
    }

    /// <summary>
    /// Top-level area definition referenced by ColorPatcher's Harmony patches
    /// (which decode it as an attribute parameter type). The shim populates
    /// only the surface the legacy ecosystem reads; FUSE owns area state
    /// natively.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedArea
    {
        public string Name { get; set; } = "[UNNAMED NEW AREA]";
        public Vector3 Position { get; set; }
        public float Radius { get; set; }
        public float[] TagColor { get; set; } = new float[3];
        public Dictionary<string, SerializedIndustry> Industries { get; set; }
        public int Order { get; set; }

        public SerializedArea()
        {
        }

        public SerializedArea(Area area)
        {
            // Constructor signature is part of the legacy contract; FUSE never
            // round-trips a live Area through this type, so the body is a
            // no-op.
        }
    }

    /// <summary>
    /// Industry definition under a <see cref="SerializedArea"/>.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedIndustry
    {
        public string Name { get; set; }
        public Vector3 LocalPosition { get; set; }
        public bool UsesContract { get; set; }
        public Dictionary<string, SerializedComponent> Components { get; set; }

        public SerializedIndustry()
        {
        }

        public SerializedIndustry(Industry industry)
        {
        }
    }

    /// <summary>
    /// Load (commodity) definition. The legacy contract included a constructor
    /// that copied from a game-side <see cref="Load"/>; FUSE never invokes it
    /// but keeps the signature for interop.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedLoad
    {
        public string Description { get; set; }
        public LoadUnits Units { get; set; }
        public float Density { get; set; }
        public float UnitWeightInPounds { get; set; }
        public bool Importable { get; set; }
        public float PayPerQuantity { get; set; }
        public float CostPerUnit { get; set; }

        public SerializedLoad()
        {
        }

        public SerializedLoad(Load load)
        {
        }
    }

    /// <summary>
    /// Scenery instance definition. <see cref="ExtraData"/> is populated via
    /// Newtonsoft's [JsonExtensionData] sink so unknown fields survive the
    /// round trip.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedScenery
    {
        public string ModelIdentifier { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; } = Vector3.zero;
        public Vector3 Scale { get; set; } = Vector3.one;

        [JsonExtensionData]
        public Dictionary<string, JToken> ExtraData { get; set; }

        public SerializedScenery()
        {
        }

        public SerializedScenery(SceneryAssetInstance scenery)
        {
        }
    }

    /// <summary>
    /// Per-instance overrides that legacy mods write into the graph. Every
    /// field is optional and a null value means "inherit the source".
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class Mandela
    {
        public bool? Enabled { get; set; }
        public Vector3? LocalPosition { get; set; }
        public Vector3? LocalRotation { get; set; }
        public Vector3? LocalScale { get; set; }
        public string InstantiateFrom { get; set; }
    }

    /// <summary>
    /// Legacy industry-component definition shape. The original carries many
    /// more fields and SC-internal apply/validate methods that hook the game's
    /// IndustryComponent hierarchy; this facade declares only the public
    /// surface that downstream plugins (InterchangedIndustryUnloader's Harmony
    /// patches on .ctor and ApplyTo) need to bind against. Apply behavior is
    /// FUSE's responsibility — see FuseLegacyDataConverter for how component
    /// definitions are routed through native FUSE types.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedComponent
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string[] TrackSpans { get; set; }
        public string CarTypeFilter { get; set; }
        public bool SharedStorage { get; set; } = true;
        public string LoadId { get; set; }
        public float? StorageChangeRate { get; set; }
        public float? MaxStorage { get; set; }
        public bool? OrderAroundEmpties { get; set; }
        public float? CarTransferRate { get; set; }
        public bool? OrderAroundLoaded { get; set; }
        public string[] InputSpans { get; set; }
        public string[] OutputSpans { get; set; }
        public float? CarLoadPeriod { get; set; }
        public float? CarLengthFeet { get; set; }
        public Dictionary<string, float> InputTermsPerDay { get; set; }
        public Dictionary<string, float> OutputTermsPerDay { get; set; }
        public bool? CanOverhaul { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JToken> ExtraData { get; set; }

        public SerializedComponent()
        {
        }

        /// <summary>
        /// Patch target for legacy plugins that Postfix the legacy round-trip
        /// constructor (e.g. InterchangedIndustryUnloaderMod). FUSE never
        /// serializes a live IndustryComponent back into a SerializedComponent
        /// — our pipeline is JSON → game one-way — so this constructor body
        /// is intentionally a no-op. Its existence lets Harmony's PatchAll
        /// resolve the target method so adjacent patches on the same plugin
        /// install successfully.
        /// </summary>
        public SerializedComponent(IndustryComponent component)
        {
        }

        /// <summary>
        /// Patch target for legacy plugins that Postfix component application.
        /// FUSE configures these fields natively in
        /// <see cref="FUSE.API.IndustryAPI"/>.ApplyComponentDefinition, so
        /// this method is a no-op — but Harmony needs the target to exist so
        /// the plugin's Harmony.PatchAll doesn't crash before installing its
        /// other (game-type) patches.
        /// </summary>
        public void ApplyTo(IndustryComponent gameComponent, PatchingContext ctx)
        {
        }
    }

    /// <summary>
    /// Editor-side patch authoring surface. The legacy mod used this from
    /// in-game tooling to author patch JSON; FUSE has its own authoring
    /// pipeline, so every method here is a no-op for runtime interop.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class PatchEditor
    {
        public PatchEditor(string fileName)
        {
            FileName = fileName ?? string.Empty;
        }

        public string FileName { get; }

        public void AddOrUpdateNode(string id, Vector3 position, Vector3 eulerRotation, bool flipSwitchStand = false)
        {
        }

        public void AddOrUpdateSegment(string segmentId, string startId, string endId, int priority = 0, string groupId = null, int speedLimit = 0, TrackSegment.Style style = TrackSegment.Style.Standard, TrackClass trackClass = default)
        {
        }

        public void AddOrUpdateSpan(string spanId, string lowerId, float lowerDistance, SerializedSegmentEnd lowerEnd, string upperId, float upperDistance, SerializedSegmentEnd upperEnd, bool normalize = false)
        {
        }

        public void AddOrUpdateSpliney(string splineyId, Func<JObject, JObject> addOrUpdate)
        {
        }

        public void AddOrUpdateScenery(string sceneryId, string modelIdentifier, Vector3 position, Vector3 eulerRotation, Vector3 scale)
        {
        }

        public void ResetNode(string id) { }
        public void ResetSegment(string id) { }
        public void ResetSpan(string id) { }
        public void ResetSpliney(string id) { }
        public void ResetScenery(string id) { }

        public void RemoveNode(string id) { }
        public void RemoveSegment(string id) { }
        public void RemoveSpan(string id) { }
        public void RemoveSpliney(string id) { }
        public void RemoveScenery(string id) { }

        public Dictionary<string, JObject> GetNodes() => new Dictionary<string, JObject>();
        public Dictionary<string, JObject> GetSegments() => new Dictionary<string, JObject>();
        public Dictionary<string, JObject> GetSpans() => new Dictionary<string, JObject>();
        public Dictionary<string, JObject> GetSplineys() => new Dictionary<string, JObject>();
        public Dictionary<string, JObject> GetScenery() => new Dictionary<string, JObject>();

        public bool Undo() => false;
        public bool Redo() => false;
        public void Save() { }
    }

    /// <summary>
    /// Spliney definition pointer. Carries only the handler key; the rest of
    /// the data lives in a JObject the legacy mod resolves dynamically.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedSpliney
    {
        public string Handler { get; set; }
    }

    /// <summary>
    /// Lightweight node entry used by <see cref="SerializedSimpleGraph"/>.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedSimpleNode
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public string Tag { get; set; }

        public SerializedSimpleNode()
        {
        }
    }

    /// <summary>
    /// Lightweight graph wrapper for tag-only node collections.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SerializedSimpleGraph
    {
        public Dictionary<string, SerializedSimpleNode> Nodes { get; internal set; } =
            new Dictionary<string, SerializedSimpleNode>();

        public SerializedSimpleGraph()
        {
        }
    }

    /// <summary>
    /// Legacy runtime patching context — carries a logger, a touched-keys
    /// dictionary, and a node-id lookup that hosted old-loader plugins use to
    /// resolve graph nodes by id during patching. FUSE doesn't drive the
    /// legacy patching pipeline itself, but legacy plugins like
    /// SignalsEverywhere construct their own context subclass and call
    /// <c>NodesById[id]</c>, <c>Logger</c>, and <c>TouchedKeys.Keys</c> when
    /// applying CTC mixintos. NodesById is populated from the live graph so SE
    /// can resolve switch/block nodes even when FUSE applied the topology.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class PatchingContext
    {
        public Serilog.ILogger Logger { get; }

        public IReadOnlyDictionary<string, string> TouchedKeys { get; }

        public IReadOnlyDictionary<string, TrackNode> NodesById { get; }

        public PatchingContext(Serilog.ILogger logger, IReadOnlyDictionary<string, string> changedEntries)
        {
            Logger = logger ?? Serilog.Log.ForContext<PatchingContext>();
            TouchedKeys = changedEntries ?? new Dictionary<string, string>(0);
            NodesById = BuildNodeIndex();
        }

        /// <summary>
        /// Looks up a <see cref="Load"/> by id via the game's prototype library.
        /// Hosted plugins (e.g. InterchangedIndustryUnloaderMod) call this from
        /// their ApplyTo Postfix. The Postfix never fires under FUSE — we
        /// configure components natively — but the method exists so the
        /// patch body type-checks at JIT time.
        /// </summary>
        public Load GetLoad(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return CarPrototypeLibrary.instance?.LoadForId(id);
        }

        private static IReadOnlyDictionary<string, TrackNode> BuildNodeIndex()
        {
            var dict = new Dictionary<string, TrackNode>(StringComparer.Ordinal);
            var graph = Graph.Shared;
            if (graph == null)
            {
                return dict;
            }

            foreach (var node in graph.Nodes)
            {
                if (node != null && !string.IsNullOrEmpty(node.id))
                {
                    dict[node.id] = node;
                }
            }

            return dict;
        }
    }

    /// <summary>
    /// Exception raised by the legacy mod's patch validators (e.g. "No LoadId
    /// specified", "At least one TrackSpan must be specified"). Hosted plugins
    /// reference this type from inside their Postfix bodies; even though FUSE
    /// never invokes the patches, the type must exist for the patch method
    /// bodies to type-check at JIT time.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class SCPatchingException : Exception
    {
        public SCPatchingException(string message, string parameterName)
            : base(message)
        {
            ParameterName = parameterName;
        }

        public string ParameterName { get; }
    }
}

namespace StrangeCustoms.Tracks.Industries
{
    /// <summary>
    /// Marker contract for custom industry components implemented in legacy
    /// plugins. YardSort's <c>YardComponent</c> derives from this. FUSE never
    /// dispatches through it; the methods exist so the legacy IL resolves.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public interface ICustomIndustryComponent
    {
        void SerializeComponent(SerializedComponent serializedComponent);
        void DeserializeComponent(SerializedComponent serializedComponent, PatchingContext ctx);
    }

    /// <summary>
    /// Optional title contract: legacy industry components implement this to
    /// surface a human-readable name in the legacy mod's UI.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public interface ICustomIndustryTitle
    {
        string Title { get; }
    }
}

namespace StrangeCustoms.Splineys
{
    /// <summary>
    /// Base MonoBehaviour for spliney scripts that legacy mods attach to
    /// scene objects. FUSE does not invoke <see cref="Deserialize"/>; the
    /// abstract shape is present so plugin IL resolves.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public abstract class GenericSpliney : MonoBehaviour
    {
        public abstract void Deserialize(JObject data);
    }

    /// <summary>
    /// Typed variant that routes JSON through a strongly-typed settings
    /// object. Plugins override the typed Deserialize; the JObject override
    /// uses Newtonsoft's default serializer because FUSE does not expose the
    /// legacy mod's customized one.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public abstract class GenericSpliney<TSettings> : GenericSpliney
    {
        public override void Deserialize(JObject data)
        {
            if (data == null)
            {
                Deserialize(default(TSettings));
                return;
            }

            var settings = data.ToObject<TSettings>();
            Deserialize(settings);
        }

        protected abstract void Deserialize(TSettings settings);
    }

    /// <summary>
    /// Builder shape that pairs a <see cref="GenericSpliney{TSettings}"/>
    /// subclass with the spliney-registration contract. FUSE owns spliney
    /// instantiation natively, so the build method returns null when invoked.
    /// </summary>
    [Obsolete(LegacyShim.Message)]
    public class GenericSplineyBuilder<T> : ISplineyBuilder where T : GenericSpliney
    {
        public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
        {
            return null;
        }
    }
}
