using System;
using System.Collections.Generic;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Central lookup tables used across the legacy-to-FUSE converter.
    /// Mirrors the module-level constants at the top of the Python
    /// <c>fuse_convert.py</c> source so a developer cross-referencing
    /// can find both in one place.
    /// </summary>
    /// <remarks>
    /// Each table is loaded once into a static read-only field;
    /// callers compare against them with case-insensitive lookup
    /// where the Python source lower-cases its key before checking.
    /// Keep tables here even if a single converter file is the only
    /// consumer today — moving a constant into a converter usually
    /// foreshadows that a different converter starts needing it.
    /// </remarks>
    internal static class LegacyConverterConstants
    {
        public const string FuseSchemaVersion = "1.0";

        /// <summary>
        /// Maps Strange Customs / AlinasMapMod handler ids to the
        /// canonical FUSE spliney type. Anything not in this map is
        /// either dispatched to a specialised converter (turntable,
        /// loader, station, ...) or passed through with the original
        /// handler stashed in <c>extensions.originalHandler</c>.
        /// </summary>
        public static readonly Dictionary<string, string> HandlerMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StrangeCustoms.FlowyThingBuilder"] = "road",
            ["StrangeCustoms.AutoTrestleBuilder"] = "trestle",
            ["StrangeCustoms.RiverBuilder"] = "river",
            ["StrangeCustoms.WaterfallBuilder"] = "waterfall",
            ["StrangeCustoms.TerrainRoadBuilder"] = "terrainRoad",
        };

        public const string TurntableHandler = "AlinasMapMod.Turntable.TurntableBuilder";

        public static readonly HashSet<string> LoaderHandlers = new HashSet<string>(StringComparer.Ordinal)
        {
            "AlinasMapMod.Loaders.LoaderBuilder",
            "AlinasMapMod.LoaderBuilder",
        };

        public static readonly HashSet<string> StationHandlers = new HashSet<string>(StringComparer.Ordinal)
        {
            "AlinasMapMod.Stations.StationAgentBuilder",
            "AlinasMapMod.StationAgentBuilder",
        };

        public const string MapLabelHandler = "AlinasMapMod.MapLabelBuilder";

        public static readonly HashSet<string> TelegraphPoleMoverHandlers = new HashSet<string>(StringComparer.Ordinal)
        {
            "AlinasMapMod.TelegraphPoleMover",
            "AlinasMapMod.TelegraphPoles.TelegraphPoleMover",
        };

        /// <summary>RR crossing handlers (compared case-insensitively in the Python source).</summary>
        public static readonly HashSet<string> RrCrossingHandlers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cutil.rrcrossing",
            "cutil.railroadcrossing",
        };

        public const string DkwSplineyHandler = "DKW.DKWSpliney";

        /// <summary>
        /// Custom industry-component types supported by the
        /// ConfusingSupplements add-on. Anything else gets a
        /// fields-bag bucket via <c>collect_custom_component_fields</c>.
        /// </summary>
        public static readonly HashSet<string> SupportedCustomIndustryComponentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "confusingsupplements.industrycomponents.captiveconversionloader",
            "confusingsupplements.industrycomponents.captiveconversionunloader",
            "confusingsupplements.industrycomponents.pay4resource",
            "confusingsupplements.industrycomponents.empty",
        };

        /// <summary>
        /// FUSE-native industry component types. The Python source
        /// uses these for two checks:
        /// <list type="bullet">
        ///   <item>infer_component_type — if the legacy id normalises
        ///     to a canonical type, treat it as that type.</item>
        ///   <item>collect_custom_component_fields — bucket extra
        ///     fields when the type is NOT canonical.</item>
        /// </list>
        /// Lookups are case-sensitive against this exact spelling
        /// (matching <c>CANONICAL_COMPONENT_TYPES</c> in Python).
        /// </summary>
        public static readonly HashSet<string> CanonicalComponentTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "loader", "unloader", "formulaic", "repairTrack", "teamTrack",
            "interchange", "interchangedLoader", "interchangedUnloader",
            "teleportLoading", "progression", "passengerStop",
        };

        /// <summary>
        /// Subset of canonical components that bind a single load.
        /// Used by <c>infer_load_id_from_component_id</c> to know
        /// whether the component id can stand in for a load id.
        /// </summary>
        public static readonly HashSet<string> LoadComponentTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "loader", "unloader", "repairTrack",
            "interchangedLoader", "interchangedUnloader", "passengerStop",
        };

        /// <summary>
        /// Lowercased canonical component field names — everything
        /// outside this set bubbles into <c>fields</c> for unknown
        /// component types.
        /// </summary>
        public static readonly HashSet<string> ComponentSchemaKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "type", "name", "trackSpanIds", "trackSpans", "spans",
            "carTypeFilter", "loadId", "load",
            "convertedLoadId", "convertedLoad",
            "sharedStorage", "storageChangeRate", "maxStorage",
            "carTransferRate", "costPerUnit",
            "notBeforeHour", "notAfterHour", "fillPercentage",
            "bookReasons", "title",
            "orderAroundEmpties", "orderAroundLoaded",
            "inputSpanIds", "outputSpanIds",
            "inputTermsPerDay", "outputTermsPerDay",
            "idealCars", "teamProfiles", "canOverhaul",
            "passengerStopId", "timetableCode", "basePopulation",
            "neighborIds", "branch", "branchDefinitions", "branches",
            "carLoadPeriod", "carLengthFeet",
            "extraData", "fields",
        };

        /// <summary>
        /// Lowercased canonical load field names. Mirrors
        /// <c>LOAD_SCHEMA_KEYS</c> from Python.
        /// </summary>
        public static readonly HashSet<string> LoadSchemaKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "description", "units", "density",
            "unitWeightInPounds", "importable", "payPerQuantity",
            "costPerUnit", "carTypeFilter",
            "emptyCarType", "loadedCarType",
            "icon", "fields",
        };

        /// <summary>
        /// Requirement ids the legacy ecosystem expects to be
        /// implicitly satisfied by FUSE itself; we strip them out of
        /// converted <c>Requirements</c> lists rather than promoting
        /// them to <c>*.FUSE</c> ids.
        /// </summary>
        public static readonly HashSet<string> CoreLegacyRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "railroader", "railloader", "rail-loader",
            "railloader.injector", "railloader.interchange",
            "assetloader",
            "alinanova21.mapeditor", "mapeditor", "mmapeditor",
            "zamu.strangecustoms", "strangecustoms",
            "zamu.confusingsupplements", "confusingsupplements",
            "zamu.foryourconvenience", "foryourconvenience",
            "alinanova21.alinasmapmod", "alinasmapmod", "alinamapmod",
            "fuse",
        };

        public static bool IsCoreLegacyRequirement(string packageId)
        {
            var value = (packageId ?? string.Empty).Trim();
            while (value.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase) ||
                   value.EndsWith(".RAIL", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 5);
            }

            return CoreLegacyRequirements.Contains(value);
        }

        /// <summary>
        /// Component type alias table — port of the dictionary
        /// embedded in <c>normalize_component_type</c>. Keys are
        /// already lowercased; the function lower-cases input before
        /// lookup. Values keep their canonical camelCase / FQ name
        /// because the FUSE schema is case-sensitive on these.
        /// </summary>
        public static readonly Dictionary<string, string> ComponentTypeAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["model.ops.industryloader"] = "loader",
            ["model.opsnew.industryloader"] = "loader",
            ["industryloader"] = "loader",
            ["model.ops.industryunloader"] = "unloader",
            ["model.opsnew.industryunloader"] = "unloader",
            ["industryunloader"] = "unloader",
            ["model.ops.formulaicindustrycomponent"] = "formulaic",
            ["model.opsnew.formulaicindustrycomponent"] = "formulaic",
            ["formulaicindustrycomponent"] = "formulaic",
            ["model.ops.repairtrack"] = "repairTrack",
            ["model.opsnew.repairtrack"] = "repairTrack",
            ["repair-track"] = "repairTrack",
            ["model.ops.teamtrack"] = "teamTrack",
            ["model.opsnew.teamtrack"] = "teamTrack",
            ["team-track"] = "teamTrack",
            ["model.ops.interchange"] = "interchange",
            ["model.opsnew.interchange"] = "interchange",
            ["interchangereloader.ops.interchangereloader"] = "interchange",
            ["model.ops.interchangedindustryloader"] = "interchangedLoader",
            ["model.opsnew.interchangedindustryloader"] = "interchangedLoader",
            ["interchanged-loader"] = "interchangedLoader",
            ["model.ops.interchangedindustryunloader"] = "interchangedUnloader",
            ["model.opsnew.interchangedindustryunloader"] = "interchangedUnloader",
            ["interchanged-unloader"] = "interchangedUnloader",
            ["interchangedunloader"] = "interchangedUnloader",
            ["model.ops.teleportloadingindustry"] = "teleportLoading",
            ["model.opsnew.teleportloadingindustry"] = "teleportLoading",
            ["teleport-loading"] = "teleportLoading",
            ["teleportloadingindustry"] = "teleportLoading",
            ["model.ops.progressionindustrycomponent"] = "progression",
            ["model.opsnew.progressionindustrycomponent"] = "progression",
            ["progression-industry"] = "progression",
            ["progressionindustry"] = "progression",
            ["progressionindustrycomponent"] = "progression",
            ["alinasmapmod.paxstationcomponent"] = "passengerStop",
            ["alinasmapmod.stations.paxstationcomponent"] = "passengerStop",
            ["paxstationcomponent"] = "passengerStop",
            ["passenger-stop"] = "passengerStop",
            ["passengerstop"] = "passengerStop",
            ["captiveconversionloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
            ["captive-conversion-loader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
            ["confusingsupplements.captiveconversionloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
            ["confusingsupplements.industrycomponents.captiveconversionloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader",
            ["captiveconversionunloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
            ["captive-conversion-unloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
            ["confusingsupplements.captiveconversionunloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
            ["confusingsupplements.industrycomponents.captiveconversionunloader"] = "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader",
            ["pay4resource"] = "ConfusingSupplements.IndustryComponents.Pay4Resource",
            ["pay-for-resource"] = "ConfusingSupplements.IndustryComponents.Pay4Resource",
            ["confusingsupplements.pay4resource"] = "ConfusingSupplements.IndustryComponents.Pay4Resource",
            ["confusingsupplements.industrycomponents.pay4resource"] = "ConfusingSupplements.IndustryComponents.Pay4Resource",
            ["confusingsupplements.empty"] = "ConfusingSupplements.IndustryComponents.Empty",
            ["confusingsupplements.industrycomponents.empty"] = "ConfusingSupplements.IndustryComponents.Empty",
        };

        /// <summary>
        /// Progression fields whose legacy shape is a bool dictionary
        /// (<c>{ "FeatureX": true, "FeatureY": false }</c>) that the
        /// converter normalises to an array of enabled keys.
        ///
        /// Deliberately NOT listed: <c>enableFeaturesAtStart</c>. Flattening
        /// it to an array would turn its FuseStringPatch semantics from
        /// per-id MERGE into REPLACE, and a mod patching a base-game
        /// progression (e.g. "ewh") would then wipe the base career's own
        /// start features (wh-el, ewh-intch). It must pass through verbatim
        /// so the object form reaches the runtime as a merge.
        /// </summary>
        public static readonly HashSet<string> BoolDictionaryArrayFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "prerequisiteFeatureIds",
            "prerequisiteSections",
            "prerequisiteSectionIds",
            "enableFeaturesOnUnlock",
            "disableFeaturesOnUnlock",
            "enableFeaturesOnAvailable",
            "unlockIncludeIndustries",
            "unlockExcludeIndustries",
            "unlockIncludeIndustryComponents",
            "areasEnableOnUnlock",
            "gameObjectsEnableOnUnlock",
            "trackGroupsEnableOnUnlock",
            "trackGroupsAvailableOnUnlock",
        };

        /// <summary>
        /// Hard-coded load definitions for ids that legacy mods
        /// reference but never define (Strange Customs supplied them
        /// implicitly through a separate asset pack). The converter
        /// injects these into a fragment's
        /// <c>operations.loads</c> dictionary if they're referenced
        /// without being defined, to keep references resolvable.
        /// </summary>
        public static readonly Dictionary<string, Newtonsoft.Json.Linq.JObject> KnownCompatLoads = new Dictionary<string, Newtonsoft.Json.Linq.JObject>(StringComparer.OrdinalIgnoreCase)
        {
            ["machine-parts"] = new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = "Machine Parts",
                ["units"] = "Pounds",
                ["density"] = 42.5,
                ["unitWeightInPounds"] = 0.0,
                ["importable"] = true,
                ["payPerQuantity"] = 0.0,
                ["costPerUnit"] = 0.0,
            },
            ["mining-explosives"] = new Newtonsoft.Json.Linq.JObject
            {
                ["name"] = "Mining Explosives",
                ["units"] = "Pounds",
                ["density"] = 37.5,
                ["unitWeightInPounds"] = 0.0,
                ["importable"] = true,
                ["payPerQuantity"] = 0.0,
                ["costPerUnit"] = 0.0,
            },
        };
    }
}
