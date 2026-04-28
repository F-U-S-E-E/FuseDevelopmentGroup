using System.Collections.Generic;
using UnityEngine;

namespace RAIL.Data
{
    public sealed class RailOperationsDefinition
    {
        public Dictionary<string, RailLoad> Loads { get; set; } = new Dictionary<string, RailLoad>();
        public Dictionary<string, RailIndustry> Industries { get; set; } = new Dictionary<string, RailIndustry>();
        public Dictionary<string, RailLoader> Loaders { get; set; } = new Dictionary<string, RailLoader>();
        public Dictionary<string, RailTurntable> Turntables { get; set; } = new Dictionary<string, RailTurntable>();
        public Dictionary<string, RailStation> Stations { get; set; } = new Dictionary<string, RailStation>();
    }

    public sealed class RailLoad
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
    }

    public sealed class RailIndustry
    {
        public string Name { get; set; }
        public string AreaId { get; set; }
        public int? Order { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public bool UsesContract { get; set; }
        public Dictionary<string, RailIndustryComponent> Components { get; set; } = new Dictionary<string, RailIndustryComponent>();
    }

    public sealed class RailIndustryComponent
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string[] TrackSpanIds { get; set; }
        public string CarTypeFilter { get; set; }
        public string LoadId { get; set; }
        public bool SharedStorage { get; set; } = true;
        public float? StorageChangeRate { get; set; }
        public float? MaxStorage { get; set; }
        public float? CarTransferRate { get; set; }
        public bool? OrderAroundEmpties { get; set; }
        public bool? OrderAroundLoaded { get; set; }
        public string[] InputSpanIds { get; set; }
        public Dictionary<string, float> InputTermsPerDay { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> OutputTermsPerDay { get; set; } = new Dictionary<string, float>();
        public float? IdealCars { get; set; }
        public Dictionary<string, RailTeamTrackEntry> TeamProfiles { get; set; } = new Dictionary<string, RailTeamTrackEntry>();
        public bool? CanOverhaul { get; set; }
        public string PassengerStopId { get; set; }
        public string TimetableCode { get; set; }
        public int? BasePopulation { get; set; }
        public string[] NeighborIds { get; set; }
        public string Branch { get; set; }
        public RailPassengerBranch[] BranchDefinitions { get; set; }
    }

    public sealed class RailTeamTrackEntry
    {
        public bool IsExport { get; set; }
        public string LoadId { get; set; }
        public float LoadingTimeDays { get; set; }
        public string CarTypeFilter { get; set; }
    }

    public sealed class RailPassengerBranch
    {
        public string Branch { get; set; }
        public int TraverseTimeToNext { get; set; }
        public string MapFeature { get; set; }
        public Dictionary<string, RailPassengerIntermediate> Intermediates { get; set; } = new Dictionary<string, RailPassengerIntermediate>();
    }

    public sealed class RailPassengerIntermediate
    {
        public string Code { get; set; }
        public int TraverseTimeToNext { get; set; }
    }

    public sealed class RailLoader
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public string Prefab { get; set; }
        public string IndustryId { get; set; }
    }

    public sealed class RailTurntable
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public float Radius { get; set; }
        public int Subdivisions { get; set; } = 16;
        public string LegacyIdentifier { get; set; }
        public RailRoundhouse Roundhouse { get; set; }
    }

    public sealed class RailRoundhouse
    {
        public int Stalls { get; set; }
        public float StartAngle { get; set; }
        public float? StallAngle { get; set; }
        public float TrackLength { get; set; } = 46f;
        public string StartPrefab { get; set; }
        public string EndPrefab { get; set; }
        public string StallPrefab { get; set; }
    }

    public sealed class RailStation
    {
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public string Prefab { get; set; }
        public string PassengerStopId { get; set; }
    }
}
