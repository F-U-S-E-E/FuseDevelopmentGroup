using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Data
{
    public sealed class FuseOperationsDefinition
    {
        public Dictionary<string, FuseLoad> Loads { get; set; } = new Dictionary<string, FuseLoad>();
        public Dictionary<string, FuseIndustry> Industries { get; set; } = new Dictionary<string, FuseIndustry>();
        public Dictionary<string, FuseLoader> Loaders { get; set; } = new Dictionary<string, FuseLoader>();
        public Dictionary<string, FuseTurntable> Turntables { get; set; } = new Dictionary<string, FuseTurntable>();
        public Dictionary<string, FuseStation> Stations { get; set; } = new Dictionary<string, FuseStation>();
        public FuseOperationsRemovals Removals { get; set; } = new FuseOperationsRemovals();
    }

    public sealed class FuseOperationsRemovals
    {
        public string[] Industries { get; set; } = Array.Empty<string>();
    }

    public sealed class FuseLoad
    {
        public string Name { get; set; }
        public string Units { get; set; }
        public float? Density { get; set; }
        public float? UnitWeightInPounds { get; set; }
        public bool? Importable { get; set; }
        public float? PayPerQuantity { get; set; }
        public float? CostPerUnit { get; set; }
        public string CarTypeFilter { get; set; }
        public string EmptyCarType { get; set; }
        public string LoadedCarType { get; set; }
        public string Icon { get; set; }
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
    }

    public sealed class FuseIndustry
    {
        public string Name { get; set; }
        public string AreaId { get; set; }
        public int? Order { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool UsesContract { get; set; }
        public bool MergeComponents { get; set; }

        /// <summary>
        /// When true, the <see cref="Components"/> dictionary is the
        /// complete intended set for this industry and any pre-existing
        /// runtime sub-components not present here must be removed. This
        /// is set by the legacy converter whenever a mod uses the
        /// <c>"$replace"</c> directive at the top of its source
        /// <c>components</c> dictionary (e.g. Foxy's East Whittier
        /// RepairIndustry.json). Without this signal, the loader's
        /// "industry already exists → force MergeComponents=true"
        /// safety net would keep vanilla components alive on top of a
        /// mod that explicitly asked for a wholesale replacement,
        /// producing the "East Whittier Fuel Service still has a
        /// repair track" duplication the user reported.
        /// </summary>
        public bool ReplaceComponents { get; set; }

        public Dictionary<string, FuseIndustryComponent> Components { get; set; } = new Dictionary<string, FuseIndustryComponent>();
    }

    public sealed class FuseIndustryComponent
    {
        /// <summary>
        /// Marks the component entry as a deletion request rather than an
        /// add/update. Emitted by the legacy data converter when a mod uses
        /// the Strange-Customs <c>"foo": null</c> idiom to remove a vanilla
        /// component (e.g. CollieSylvaRemoval nulls every component on
        /// sylva-tannery / sylva-paperboard / sylva-interchange). A JSON
        /// null cannot be carried straight through because
        /// <see cref="FUSE.Serialization.FuseSerializer.GetSettings"/> sets
        /// <c>NullValueHandling.Ignore</c>, which causes the deserializer
        /// to drop null dictionary entries entirely — leaving the apply
        /// path with an empty components dict and the vanilla components
        /// still in place. Routing the deletion through this explicit
        /// boolean survives serializer settings.
        /// </summary>
        public bool Remove { get; set; }
        public bool Partial { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string[] TrackSpanIds { get; set; }
        public FuseStringListPatch TrackSpanPatch { get; set; }
        public string CarTypeFilter { get; set; }
        public string LoadId { get; set; }
        public string ConvertedLoadId { get; set; }
        public bool SharedStorage { get; set; } = true;
        public float? StorageChangeRate { get; set; }
        public float? MaxStorage { get; set; }
        public float? CarTransferRate { get; set; }
        public float? CostPerUnit { get; set; }
        public float? NotBeforeHour { get; set; }
        public float? NotAfterHour { get; set; }
        public float? FillPercentage { get; set; }
        public string[] BookReasons { get; set; }
        public string Title { get; set; }
        public bool? OrderAroundEmpties { get; set; }
        public bool? OrderAroundLoaded { get; set; }
        public string[] InputSpanIds { get; set; }
        public Dictionary<string, float> InputTermsPerDay { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> OutputTermsPerDay { get; set; } = new Dictionary<string, float>();
        public float? IdealCars { get; set; }
        public Dictionary<string, FuseTeamTrackEntry> TeamProfiles { get; set; } = new Dictionary<string, FuseTeamTrackEntry>();
        public bool? CanOverhaul { get; set; }
        public string PassengerStopId { get; set; }
        public string TimetableCode { get; set; }
        public int? BasePopulation { get; set; }
        public string[] NeighborIds { get; set; }
        public string Branch { get; set; }
        public FusePassengerBranch[] BranchDefinitions { get; set; }

        // TeleportLoadingIndustry-specific fields. FUSE exposes them on the
        // generic component so the same FuseIndustryComponent shape can serve
        // every game-side component type.
        public string[] OutputSpanIds { get; set; }
        public float? CarLoadPeriod { get; set; }
        public float? CarLengthFeet { get; set; }

        /// <summary>
        /// Optional reflection-bound payload for custom community
        /// IndustryComponent implementations supplied by separate mods.
        /// </summary>
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
    }

    public sealed class FuseStringListPatch
    {
        public string[] Add { get; set; }
        public string[] Append { get; set; }
        public string[] Prepend { get; set; }
        public string[] Insert { get; set; }
        public string[] Replace { get; set; }
        public string[] Remove { get; set; }
    }

    public sealed class FuseTeamTrackEntry
    {
        public bool IsExport { get; set; }
        public string LoadId { get; set; }
        public float LoadingTimeDays { get; set; }
        public string CarTypeFilter { get; set; }
    }

    public sealed class FusePassengerBranch
    {
        public string Branch { get; set; }
        public int TraverseTimeToNext { get; set; }
        public string MapFeature { get; set; }
        public Dictionary<string, FusePassengerIntermediate> Intermediates { get; set; } = new Dictionary<string, FusePassengerIntermediate>();
    }

    public sealed class FusePassengerIntermediate
    {
        public string Code { get; set; }
        public int TraverseTimeToNext { get; set; }
    }

    public sealed class FuseLoader
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public string Prefab { get; set; }
        public string IndustryId { get; set; }
    }

    public sealed class FuseTurntable
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Radius { get; set; }
        public int Subdivisions { get; set; } = 16;
        public string LegacyIdentifier { get; set; }
        public FuseRoundhouse Roundhouse { get; set; }
        public FuseTurntableVisuals Visuals { get; set; }
    }

    public sealed class FuseTurntableVisuals
    {
        public string PitAssetIdentifier { get; set; }
        public Vector3 PitPosition { get; set; }
        public Vector3 PitRotation { get; set; }
        public Vector3 PitScale { get; set; } = Vector3.one;
        public string BridgeAssetIdentifier { get; set; }
        public Vector3 BridgePosition { get; set; }
        public Vector3 BridgeRotation { get; set; }
        public Vector3 BridgeScale { get; set; } = Vector3.one;
        public bool BridgeTrackEnabled { get; set; } = true;
        public float BridgeTrackGauge { get; set; } = 1.435f;
        public float BridgeTrackLength { get; set; }
        public float BridgeTrackYOffset { get; set; } = 0.08f;
        public string ControllerType { get; set; }
        public float InteractionRadius { get; set; } = 16f;
    }

    public sealed class FuseRoundhouse
    {
        public int Stalls { get; set; }
        public float StartAngle { get; set; }
        public float? StallAngle { get; set; }
        public float TrackLength { get; set; } = 46f;
        public string StartPrefab { get; set; }
        public string EndPrefab { get; set; }
        public string StallPrefab { get; set; }
    }

    public sealed class FuseStation
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public string Prefab { get; set; }
        public string PassengerStopId { get; set; }
    }
}
