using System;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace FUSE.UnityTests
{
    /// <summary>
    /// EditMode regression canary for every Railroader private/internal
    /// member FUSE reaches via reflection by NAME. FUSE caches each of
    /// these in a <c>static readonly FieldInfo</c> (or MethodInfo /
    /// PropertyInfo / ConstructorInfo) at class-load time. If
    /// Railroader renames a member in a future patch the lookup
    /// silently returns null, FUSE either calls
    /// <c>SetValue(null, ...)</c> on it and gets a
    /// NullReferenceException at first invocation (loud-but-late) or
    /// the failure becomes an even more silent "feature does nothing"
    /// (passenger stop spans never bind, turntable nodes lose their
    /// references, etc.).
    ///
    /// This suite catches both failure modes at CI time. Each [Test]
    /// pins one (target type, member, BindingFlags) tuple. A failure
    /// here ALWAYS means one of two things:
    ///
    ///   1. Railroader renamed / moved / changed-accessibility on the
    ///      member. Production code that depends on it must be
    ///      updated to use the new name. Search FUSE for the old
    ///      name to find every caller.
    ///   2. FUSE moved its reflection target intentionally (e.g.
    ///      stopped depending on a particular private field). In
    ///      that case delete the corresponding test method here.
    ///
    /// Either way, the failure is actionable — it tells you which
    /// member is gone, not just "something's null at runtime."
    ///
    /// Coverage scope: ~50 accesses across ~25 Railroader types in
    /// FUSE/Runtime/API, FUSE/Loading, FUSE/Patches, FUSE/Interface.
    /// UnityModManager / Harmony / Multiplayer / Map.Runtime
    /// reflection paths that FUSE wraps in conditional fallbacks
    /// (e.g. <c>MultiplayerType?.GetProperty(...)</c>) are
    /// deliberately omitted — those are designed to no-op if the
    /// target isn't present and producing a CI failure for them
    /// would be wrong.
    ///
    /// FusePrefabResolver and other internal symbols come in via
    /// FUSE.csproj's InternalsVisibleTo to <c>FUSE.UnityTests.Tests</c>.
    /// Railroader types resolve from Assembly-CSharp.dll (and a few
    /// satellite assemblies like Map.Runtime.dll) which
    /// prepare_assets.ps1 copies into Assets/Plugins/.
    /// </summary>
    public class RailroaderReflectionSurfaceTests
    {
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // -----------------------------------------------------------------
        // PassengerStop — FusePassengerStopComponent.cs writes _spans and
        // _markers directly to bypass the (event-firing) public setters
        // and reads _keyValueObject + _allPassengerStops to manage the
        // global passenger stop registry.
        // -----------------------------------------------------------------

        [Test]
        public void PassengerStop_allPassengerStops_StaticField()
        {
            AssertField("Model.Ops.PassengerStop", "_allPassengerStops", StaticNonPublic);
        }

        [Test]
        public void PassengerStop_spans_InstanceField()
        {
            AssertField("Model.Ops.PassengerStop", "_spans", InstanceNonPublic);
        }

        [Test]
        public void PassengerStop_markers_InstanceField()
        {
            AssertField("Model.Ops.PassengerStop", "_markers", InstanceNonPublic);
        }

        [Test]
        public void PassengerStop_keyValueObject_InstanceField()
        {
            AssertField("Model.Ops.PassengerStop", "_keyValueObject", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // Turntable — TurntableAPI.cs writes nodes/bridgeGroupId/_segment
        // when materialising a turntable from a FuseTurntable definition.
        // -----------------------------------------------------------------

        [Test]
        public void Turntable_nodes_InstanceField()
        {
            AssertField("Track.Turntable", "nodes", InstanceNonPublic);
        }

        [Test]
        public void Turntable_bridgeGroupId_InstanceField()
        {
            AssertField("Track.Turntable", "bridgeGroupId", InstanceNonPublic);
        }

        [Test]
        public void Turntable_segment_InstanceField()
        {
            AssertField("Track.Turntable", "_segment", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // Graph — TrackAPI.cs and TurntableAPI.cs mutate the graph's
        // private caches directly when FUSE applies track changes that
        // require the caches to be rebuilt before the public APIs read
        // them. Each cache field must remain accessible.
        // -----------------------------------------------------------------

        [Test]
        public void Graph_nodes_InstanceField()
        {
            // TrackAPI replays node mutations directly into the graph's
            // backing collections after applying a track change so the
            // public Graph.Nodes view sees them on the same frame.
            AssertField("Track.Graph", "nodes", InstanceNonPublic);
        }

        [Test]
        public void Graph_segments_InstanceField()
        {
            AssertField("Track.Graph", "segments", InstanceNonPublic);
        }

        [Test]
        public void Graph_spans_InstanceField()
        {
            AssertField("Track.Graph", "spans", InstanceNonPublic);
        }

        [Test]
        public void Graph_AddSpan_InstanceMethod()
        {
            // TrackAPI invokes AddSpan to install new spans when the
            // public API doesn't expose a non-marker registration path.
            AssertMethod("Track.Graph", "AddSpan", InstanceNonPublic);
        }

        [Test]
        public void Graph_cachedTurntableControllers_InstanceField()
        {
            AssertField("Track.Graph", "_cachedTurntableControllers", InstanceNonPublic);
        }

        [Test]
        public void Graph_nodeConnectionsCache_InstanceField()
        {
            AssertField("Track.Graph", "_nodeConnectionsCache", InstanceNonPublic);
        }

        [Test]
        public void Graph_nodeIsDeadEndCache_InstanceField()
        {
            AssertField("Track.Graph", "_nodeIsDeadEndCache", InstanceNonPublic);
        }

        [Test]
        public void Graph_cachedReachableSegments_InstanceField()
        {
            AssertField("Track.Graph", "_cachedReachableSegments", InstanceNonPublic);
        }

        [Test]
        public void Graph_decodedSwitchCache_InstanceField()
        {
            AssertField("Track.Graph", "_decodedSwitchCache", InstanceNonPublic);
        }

        [Test]
        public void Graph_segmentsReachableFromOthers_InstanceField()
        {
            AssertField("Track.Graph", "_segmentsReachableFromOthers", InstanceNonPublic);
        }

        [Test]
        public void Graph_curvatureSampleCache_InstanceField()
        {
            AssertField("Track.Graph", "_curvatureSampleCache", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // StationAgent — StationAPI.cs writes area / passengerStop /
        // secondaryAreas when materialising a station from definition.
        // -----------------------------------------------------------------

        [Test]
        public void StationAgent_area_InstanceField()
        {
            AssertField("Model.Ops.StationAgent", "area", InstanceNonPublic);
        }

        [Test]
        public void StationAgent_passengerStop_InstanceField()
        {
            AssertField("Model.Ops.StationAgent", "passengerStop", InstanceNonPublic);
        }

        [Test]
        public void StationAgent_secondaryAreas_InstanceField()
        {
            AssertField("Model.Ops.StationAgent", "secondaryAreas", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // MapLabel / MapStore / MapManager — map display + tile registry.
        // -----------------------------------------------------------------

        [Test]
        public void MapLabel_canvas_InstanceField()
        {
            // MapAPI.AddMapLabel binds the canvas reference via reflection.
            // If _canvas is renamed, every authored map label silently
            // renders off-screen because the canvas reference is null.
            AssertField("UI.Map.MapLabel", "_canvas", InstanceNonPublic);
        }

        [Test]
        public void MapStore_descriptors_InstanceField()
        {
            // FuseMapTileRegistry maintains its own view of the tile
            // descriptor list by reading/writing this field directly.
            AssertField("Map.Runtime.MapStore", "_descriptors", InstanceNonPublic);
        }

        [Test]
        public void MapManager_store_InstanceField()
        {
            // FuseMapTileRegistry resolves the active MapStore through
            // MapManager's private _store field.
            AssertField("Map.Runtime.MapManager", "_store", InstanceNonPublic);
        }

        [Test]
        public void MapManager_RebuildAll_PublicInstanceMethod()
        {
            // FuseRuntimeReloadService invokes RebuildAll to recompute
            // map tiles after a FUSE re-apply. If the method is gone or
            // its signature changed, runtime reload silently doesn't
            // refresh the map.
            AssertMethod("Map.Runtime.MapManager", "RebuildAll", InstancePublic);
        }

        [Test]
        public void MapManager_Instance_StaticPublicProperty()
        {
            AssertProperty("Map.Runtime.MapManager", "Instance", BindingFlags.Static | BindingFlags.Public);
        }

        // -----------------------------------------------------------------
        // AssetPackRuntimeStore — heavily-patched store; FUSE's
        // asset-pack mounting reads BasePath, the load task, and the
        // bundle resolver to drive its own resolution trace.
        // -----------------------------------------------------------------

        [Test]
        public void AssetPackRuntimeStore_BasePath_Property()
        {
            // BasePath is private/internal in shipped Railroader; FUSE
            // accesses it via AccessTools.Property which probes both
            // public and non-public.
            var prop = AccessTools.Property(RequireType("AssetPack.Runtime.AssetPackRuntimeStore"), "BasePath");
            Assert.NotNull(prop,
                "AssetPackRuntimeStore.BasePath not found via AccessTools.Property — FUSE's BasePath probe will return null.");
        }

        [Test]
        public void AssetPackRuntimeStore_loadAssetBundleTask_Field()
        {
            var field = AccessTools.Field(RequireType("AssetPack.Runtime.AssetPackRuntimeStore"), "_loadAssetBundleTask");
            Assert.NotNull(field,
                "AssetPackRuntimeStore._loadAssetBundleTask not found via AccessTools.Field.");
        }

        [Test]
        public void AssetPackRuntimeStore_LoadedBundle_Method()
        {
            var method = AccessTools.Method(RequireType("AssetPack.Runtime.AssetPackRuntimeStore"), "LoadedBundle");
            Assert.NotNull(method,
                "AssetPackRuntimeStore.LoadedBundle not found via AccessTools.Method.");
        }

        [Test]
        public void AssetPackRuntimeStore_container_Field()
        {
            var field = AccessTools.Field(RequireType("AssetPack.Runtime.AssetPackRuntimeStore"), "_container");
            Assert.NotNull(field,
                "AssetPackRuntimeStore._container not found via AccessTools.Field — FuseAssetPackRegistry mutates this directly.");
        }

        [Test]
        public void AssetPackRuntimeStore_AssetBundlePath_PropertyGetter()
        {
            // FuseAssetPackRuntimeStoreAssetBundlePathPatch targets this
            // getter; a rename here detaches the AssetBundle path
            // override and asset-pack mod loading silently breaks.
            var getter = AccessTools.PropertyGetter(RequireType("AssetPack.Runtime.AssetPackRuntimeStore"), "AssetBundlePath");
            Assert.NotNull(getter,
                "AssetPackRuntimeStore.AssetBundlePath getter not found — the bundle-path patch cannot bind.");
        }

        [Test]
        public void ContainerSerialization_JsonSerializerSettings_Method()
        {
            // FuseAssetPackRegistry and FuseLegacyContainerMixintoRegistry
            // both call ContainerSerialization.JsonSerializerSettings to
            // round-trip asset-pack container JSON through the game's
            // Newtonsoft.Json contract resolver.
            var method = AccessTools.Method(RequireType("ContainerSerialization"), "JsonSerializerSettings");
            Assert.NotNull(method,
                "ContainerSerialization.JsonSerializerSettings not found — asset-pack serialization helpers will throw.");
        }

        // -----------------------------------------------------------------
        // PrefabStore — patched in multiple places to filter
        // AllCarDefinitionInfos, intercept AllDefinitionInfosOfType,
        // and reach into the underlying _stores list.
        // -----------------------------------------------------------------

        [Test]
        public void PrefabStore_stores_Field()
        {
            var field = AccessTools.Field(RequireType("Model.Database.PrefabStore"), "_stores");
            Assert.NotNull(field,
                "PrefabStore._stores not found — multiple patches and asset-pack helpers depend on it.");
        }

        [Test]
        public void PrefabStore_AllDefinitionInfosOfType_GenericMethod()
        {
            // FuseAudioPatches and FusePrefabStoreMaterialDefinitionsPatch
            // both call this method's generic-definition form.
            var method = AccessTools.Method(RequireType("Model.Database.PrefabStore"), "AllDefinitionInfosOfType");
            Assert.NotNull(method,
                "PrefabStore.AllDefinitionInfosOfType not found — material/audio patches will fail to install.");
        }

        [Test]
        public void PrefabStore_AllCarDefinitionInfos_Property()
        {
            // FusePrefabStoreAllCarDefinitionInfosFilterPatch postfix
            // targets the getter of this property.
            var getter = AccessTools.PropertyGetter(RequireType("Model.Database.PrefabStore"), "AllCarDefinitionInfos");
            Assert.NotNull(getter,
                "PrefabStore.AllCarDefinitionInfos getter not found — the car-definition filter patch cannot bind.");
        }

        // -----------------------------------------------------------------
        // CarInspector — FusePassengerCarPanelPatch needs the inspector's
        // private _car backing field to identify which car the panel
        // currently shows.
        // -----------------------------------------------------------------

        [Test]
        public void CarInspector_car_InstanceField()
        {
            var field = AccessTools.Field(RequireType("UI.CarInspector.CarInspector"), "_car");
            Assert.NotNull(field,
                "CarInspector._car not found — the passenger car panel patch will throw on every invocation.");
        }

        // -----------------------------------------------------------------
        // MapFeature.Unlocked — FuseProgressionImpactLookup probes BOTH
        // a property and a field named "Unlocked" because Railroader
        // has shipped both shapes in different versions. The lookup
        // succeeds as long as ONE of the two is present; this test
        // pins that contract so we'd catch a hypothetical future
        // version that exposes neither.
        // -----------------------------------------------------------------

        [Test]
        public void MapFeature_Unlocked_PropertyOrField()
        {
            var t = RequireType("Game.Progression.MapFeature");
            var property = t.GetProperty("Unlocked", InstanceAny);
            var field = t.GetField("Unlocked", InstanceAny);
            Assert.IsTrue(property != null || field != null,
                "MapFeature.Unlocked not found as either property OR field — FuseProgressionImpactLookup's fallback chain breaks.");
        }

        // -----------------------------------------------------------------
        // TrackSpan — FuseMapEnhancerCompat invalidates and rebuilds
        // span caches by writing _cachedSegments and re-invoking the
        // private UpdateCachedPointsIfNeeded method.
        // -----------------------------------------------------------------

        [Test]
        public void TrackSpan_cachedSegments_InstanceField()
        {
            AssertField("Track.TrackSpan", "_cachedSegments", InstanceNonPublic);
        }

        [Test]
        public void TrackSpan_UpdateCachedPointsIfNeeded_InstanceMethod()
        {
            AssertMethod("Track.TrackSpan", "UpdateCachedPointsIfNeeded", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // IndustryComponent / Industry / FormulaicIndustryComponent /
        // RepairTrack — IndustryAPI's component-apply pipeline writes
        // private fields directly on the appropriate subtype.
        // -----------------------------------------------------------------

        [Test]
        public void IndustryComponent_cachedIndustry_InstanceField()
        {
            AssertField("Model.Ops.IndustryComponent", "_cachedIndustry", InstanceNonPublic);
        }

        [Test]
        public void IndustryComponent_identifier_InstanceField()
        {
            AssertField("Model.Ops.IndustryComponent", "_identifier", InstanceNonPublic);
        }

        [Test]
        public void Industry_cachedComponents_InstanceField()
        {
            AssertField("Model.Ops.Industry", "_cachedComponents", InstanceNonPublic);
        }

        [Test]
        public void FormulaicIndustryComponent_otherComponents_InstanceField()
        {
            AssertField("Model.Ops.FormulaicIndustryComponent", "_otherComponents", InstanceNonPublic);
        }

        [Test]
        public void RepairTrack_repairPartsLoad_InstanceField()
        {
            // FUSE looks this up with NonPublic flags even though the
            // field is treated as semi-public — match the production
            // lookup exactly so an accessibility change is caught.
            AssertField("Model.Ops.RepairTrack", "repairPartsLoad", InstanceNonPublic);
        }

        [Test]
        public void IndustryContentHoverable_industry_InstanceField()
        {
            // LoaderAPI binds this backing reference when materialising
            // hoverable industry content.
            AssertField("Model.Ops.IndustryContentHoverable", "industry", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // MapFeatureManager / ProgressionManager / Progression / Section /
        // InterchangeTransfer — ProgressionAPI rewrites private state on
        // every progression apply.
        // -----------------------------------------------------------------

        [Test]
        public void MapFeatureManager_features_InstanceField()
        {
            AssertField("Game.Progression.MapFeatureManager", "_features", InstanceNonPublic);
        }

        [Test]
        public void MapFeatureManager_FeatureEnables_Property()
        {
            AssertProperty("Game.Progression.MapFeatureManager", "FeatureEnables", InstanceNonPublic);
        }

        [Test]
        public void MapFeatureManager_HandleFeatureEnablesChanged_Method()
        {
            AssertMethod("Game.Progression.MapFeatureManager", "HandleFeatureEnablesChanged", InstanceNonPublic);
        }

        [Test]
        public void ProgressionManager_progressions_InstanceField()
        {
            AssertField("Game.Progression.ProgressionManager", "_progressions", InstanceNonPublic);
        }

        [Test]
        public void ProgressionManager_current_InstanceField()
        {
            AssertField("Game.Progression.ProgressionManager", "_current", InstanceNonPublic);
        }

        [Test]
        public void Progression_Sections_AutoPropertyBackingField()
        {
            // Compiler-generated backing field for an auto-property.
            // If Railroader converts Sections to a manual backing field
            // or a computed property, this name disappears.
            AssertField("Game.Progression.Progression", "<Sections>k__BackingField", InstanceNonPublic);
        }

        [Test]
        public void Progression_UpdateSectionStates_Method()
        {
            AssertMethod("Game.Progression.Progression", "UpdateSectionStates", InstanceNonPublic);
        }

        [Test]
        public void Section_InterchangeTransfers_AutoPropertyBackingField()
        {
            AssertField("Game.Progression.Section", "<InterchangeTransfers>k__BackingField", InstanceNonPublic);
        }

        [Test]
        public void InterchangeTransfer_from_InstanceField()
        {
            AssertField("Game.Progression.InterchangeTransfer", "from", InstanceNonPublic);
        }

        [Test]
        public void InterchangeTransfer_to_InstanceField()
        {
            AssertField("Game.Progression.InterchangeTransfer", "to", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // RiverBuilder / TelegraphPoleManager — SplineyAPI and MapAPI
        // mutate private mesh/spline state.
        // -----------------------------------------------------------------

        [Test]
        public void RiverBuilder_splineProfile_InstanceField()
        {
            AssertField("AutoTrestle.RiverBuilder", "splineProfile", InstanceNonPublic);
        }

        [Test]
        public void TelegraphPoleManager_polePrefabs_InstanceField()
        {
            AssertField("TelegraphPoles.TelegraphPoleManager", "polePrefabs", InstanceNonPublic);
        }

        [Test]
        public void TelegraphPoleManager_wirePrefab_InstanceField()
        {
            AssertField("TelegraphPoles.TelegraphPoleManager", "wirePrefab", InstanceNonPublic);
        }

        [Test]
        public void TelegraphPoleManager_Rebuild_InstanceMethod()
        {
            AssertMethod("TelegraphPoles.TelegraphPoleManager", "Rebuild", InstanceNonPublic);
        }

        // -----------------------------------------------------------------
        // CTCAutoSignal / Model.Car — patched targets in
        // FuseRuntimeReferenceCleanupPatches and TrainControllerPatches.
        // -----------------------------------------------------------------

        [Test]
        public void CTCAutoSignal_AspectForBlockAndNextSignal_Method()
        {
            var method = AccessTools.Method(RequireType("CTCAutoSignal"), "AspectForBlockAndNextSignal");
            Assert.NotNull(method,
                "CTCAutoSignal.AspectForBlockAndNextSignal not found — FuseRuntimeReferenceCleanupPatches cannot install.");
        }

        [Test]
        public void ModelCar_GetCenterPosition_Method()
        {
            var method = AccessTools.Method(RequireType("Model.Car"), "GetCenterPosition");
            Assert.NotNull(method,
                "Model.Car.GetCenterPosition not found — TrainControllerPatches.CheckForCarsAtPointPatch cannot bind.");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Type RequireType(string fullName)
        {
            // Probe via AccessTools first because it walks all loaded
            // assemblies — that matches FUSE's runtime behaviour when
            // a type lives in a satellite assembly (Map.Runtime,
            // TelegraphPoles, etc.) that isn't statically referenced
            // by FUSE.csproj.
            var type = AccessTools.TypeByName(fullName) ?? Type.GetType(fullName);

            // If the fully-qualified name doesn't resolve (often because
            // this file guessed the wrong namespace prefix), fall back to
            // the leaf name — AccessTools.TypeByName searches by simple
            // name across all loaded assemblies too. This keeps the test
            // resilient against namespace drift in the FUSE source
            // without becoming silent: if the type IS gone entirely, the
            // leaf-name lookup fails the same way the full-name lookup
            // did and the Assert below still fires.
            if (type == null)
            {
                var dotIndex = fullName.LastIndexOf('.');
                if (dotIndex >= 0 && dotIndex < fullName.Length - 1)
                {
                    type = AccessTools.TypeByName(fullName.Substring(dotIndex + 1));
                }
            }

            Assert.NotNull(type,
                $"Railroader type '{fullName}' was not loaded into the test AppDomain. " +
                "Either Railroader renamed/moved the type, prepare_assets.ps1 didn't copy the assembly " +
                "that contains it into Assets/Plugins/, or this test's expected namespace is wrong " +
                "(check the FUSE source's actual reflection call site for the canonical name).");
            return type;
        }

        private static void AssertField(string typeFullName, string memberName, BindingFlags flags)
        {
            var type = RequireType(typeFullName);
            var field = type.GetField(memberName, flags);
            Assert.NotNull(field,
                $"Field '{typeFullName}.{memberName}' (flags={flags}) not found. " +
                "FUSE caches this in a static FieldInfo; lookup returning null means subsequent " +
                "SetValue/GetValue calls would NullReferenceException at first invocation. " +
                "Either Railroader renamed the field or FUSE no longer needs this reflection — " +
                "in the latter case, delete this test.");
        }

        private static void AssertMethod(string typeFullName, string memberName, BindingFlags flags)
        {
            var type = RequireType(typeFullName);
            var method = type.GetMethod(memberName, flags);
            Assert.NotNull(method,
                $"Method '{typeFullName}.{memberName}' (flags={flags}) not found. " +
                "FUSE caches this in a static MethodInfo; reflection invocation would " +
                "NullReferenceException at first call.");
        }

        private static void AssertProperty(string typeFullName, string memberName, BindingFlags flags)
        {
            var type = RequireType(typeFullName);
            var property = type.GetProperty(memberName, flags);
            Assert.NotNull(property,
                $"Property '{typeFullName}.{memberName}' (flags={flags}) not found. " +
                "FUSE caches this in a static PropertyInfo; reflection access would " +
                "NullReferenceException at first call.");
        }
    }
}
