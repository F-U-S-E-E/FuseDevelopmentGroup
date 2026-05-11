using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Data;
using FUSE.Infrastructure;

namespace FUSE.API
{
    public static class FuseRuntimeDefinitionCache
    {
        private static readonly Dictionary<string, object> Definitions =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static void Store<T>(string kind, string id, T definition)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id) || definition == null)
            {
                return;
            }

            Definitions[MakeKey(kind, id)] = Clone(definition);
        }

        public static bool TryGet<T>(string kind, string id, out T definition)
            where T : class
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            if (!Definitions.TryGetValue(MakeKey(kind, id), out var stored) || stored == null)
            {
                return false;
            }

            if (stored is T typed)
            {
                definition = Clone(typed);
                return definition != null;
            }

            return false;
        }

        public static void Remove(string kind, string id)
        {
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            Definitions.Remove(MakeKey(kind, id));
        }

        private static string MakeKey(string kind, string id)
        {
            return kind.Trim() + "\n" + id.Trim();
        }

        private static T Clone<T>(T definition)
            where T : class
        {
            if (definition == null)
            {
                return null;
            }

            try
            {
                // Use known-type manual copy to avoid all Newtonsoft serializer
                // contract/resolver bugs that affect the FUSE data types.
                if (definition is FuseIndustryComponent component)
                    return CloneComponent(component) as T;
                if (definition is FuseIndustry industry)
                    return CloneIndustry(industry) as T;
                if (definition is FuseLoad load)
                    return CloneLoad(load) as T;
                if (definition is FuseLoader loader)
                    return CloneLoader(loader) as T;
                if (definition is FuseStation station)
                    return CloneStation(station) as T;
                if (definition is FuseTurntable turntable)
                    return CloneTurntable(turntable) as T;

                // Unknown type — return original (best-effort, same as before).
                return definition;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE failed to clone runtime definition '{typeof(T).FullName}': {ex.Message}");
                return definition;
            }
        }

        private static FuseIndustryComponent CloneComponent(FuseIndustryComponent src)
        {
            if (src == null) return null;
            return new FuseIndustryComponent
            {
                Type                = src.Type,
                Name                = src.Name,
                TrackSpanIds        = src.TrackSpanIds?.ToArray(),
                CarTypeFilter       = src.CarTypeFilter,
                LoadId              = src.LoadId,
                SharedStorage       = src.SharedStorage,
                StorageChangeRate   = src.StorageChangeRate,
                MaxStorage          = src.MaxStorage,
                CarTransferRate     = src.CarTransferRate,
                OrderAroundEmpties  = src.OrderAroundEmpties,
                OrderAroundLoaded   = src.OrderAroundLoaded,
                InputSpanIds        = src.InputSpanIds?.ToArray(),
                InputTermsPerDay    = src.InputTermsPerDay  == null ? null : new Dictionary<string, float>(src.InputTermsPerDay),
                OutputTermsPerDay   = src.OutputTermsPerDay == null ? null : new Dictionary<string, float>(src.OutputTermsPerDay),
                IdealCars           = src.IdealCars,
                TeamProfiles        = CloneTeamProfiles(src.TeamProfiles),
                CanOverhaul         = src.CanOverhaul,
                PassengerStopId     = src.PassengerStopId,
                TimetableCode       = src.TimetableCode,
                BasePopulation      = src.BasePopulation,
                NeighborIds         = src.NeighborIds?.ToArray(),
                Branch              = src.Branch,
                BranchDefinitions   = src.BranchDefinitions?.Select(ClonePassengerBranch).ToArray(),
                OutputSpanIds       = src.OutputSpanIds?.ToArray(),
                CarLoadPeriod       = src.CarLoadPeriod,
                CarLengthFeet       = src.CarLengthFeet,
            };
        }

        private static FuseIndustry CloneIndustry(FuseIndustry src)
        {
            if (src == null) return null;
            return new FuseIndustry
            {
                Name         = src.Name,
                AreaId       = src.AreaId,
                Order        = src.Order,
                Position     = src.Position,
                Rotation     = src.Rotation,
                UsesContract = src.UsesContract,
                Components   = src.Components == null ? null
                    : src.Components.ToDictionary(
                        kvp => kvp.Key,
                        kvp => CloneComponent(kvp.Value)),
            };
        }

        private static FuseLoad CloneLoad(FuseLoad src)
        {
            if (src == null) return null;
            return new FuseLoad
            {
                Name               = src.Name,
                Units              = src.Units,
                Density            = src.Density,
                UnitWeightInPounds = src.UnitWeightInPounds,
                Importable         = src.Importable,
                PayPerQuantity     = src.PayPerQuantity,
                CostPerUnit        = src.CostPerUnit,
                CarTypeFilter      = src.CarTypeFilter,
                EmptyCarType       = src.EmptyCarType,
                LoadedCarType      = src.LoadedCarType,
                Icon               = src.Icon,
            };
        }

        private static FuseLoader CloneLoader(FuseLoader src)
        {
            if (src == null) return null;
            return new FuseLoader
            {
                Position   = src.Position,
                Rotation   = src.Rotation,
                Prefab     = src.Prefab,
                IndustryId = src.IndustryId,
            };
        }

        private static FuseStation CloneStation(FuseStation src)
        {
            if (src == null) return null;
            return new FuseStation
            {
                Position       = src.Position,
                Rotation       = src.Rotation,
                Prefab         = src.Prefab,
                PassengerStopId = src.PassengerStopId,
            };
        }

        private static FuseTurntable CloneTurntable(FuseTurntable src)
        {
            if (src == null) return null;
            return new FuseTurntable
            {
                Position         = src.Position,
                Rotation         = src.Rotation,
                Radius           = src.Radius,
                Subdivisions     = src.Subdivisions,
                LegacyIdentifier = src.LegacyIdentifier,
                Roundhouse       = src.Roundhouse == null ? null : new FuseRoundhouse
                {
                    Stalls      = src.Roundhouse.Stalls,
                    StartAngle  = src.Roundhouse.StartAngle,
                    StallAngle  = src.Roundhouse.StallAngle,
                    TrackLength = src.Roundhouse.TrackLength,
                    StartPrefab = src.Roundhouse.StartPrefab,
                    EndPrefab   = src.Roundhouse.EndPrefab,
                    StallPrefab = src.Roundhouse.StallPrefab,
                },
            };
        }

        private static Dictionary<string, FuseTeamTrackEntry> CloneTeamProfiles(Dictionary<string, FuseTeamTrackEntry> src)
        {
            if (src == null) return null;
            return src.ToDictionary(kvp => kvp.Key, kvp => kvp.Value == null ? null : new FuseTeamTrackEntry
            {
                IsExport        = kvp.Value.IsExport,
                LoadId          = kvp.Value.LoadId,
                LoadingTimeDays = kvp.Value.LoadingTimeDays,
                CarTypeFilter   = kvp.Value.CarTypeFilter,
            });
        }

        private static FusePassengerBranch ClonePassengerBranch(FusePassengerBranch src)
        {
            if (src == null) return null;
            return new FusePassengerBranch
            {
                Branch              = src.Branch,
                TraverseTimeToNext  = src.TraverseTimeToNext,
                MapFeature          = src.MapFeature,
                Intermediates       = src.Intermediates == null ? null
                    : src.Intermediates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value == null ? null : new FusePassengerIntermediate
                    {
                        Code               = kvp.Value.Code,
                        TraverseTimeToNext = kvp.Value.TraverseTimeToNext,
                    }),
            };
        }
    }
}
