using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Authoring.Data
{
    public static class FuseIndustryComponentTypes
    {
        public const string Loader = "loader";
        public const string Unloader = "unloader";
        public const string Formulaic = "formulaic";
        public const string RepairTrack = "repairTrack";
        public const string TeamTrack = "teamTrack";
        public const string Interchange = "interchange";
        public const string InterchangedLoader = "interchangedLoader";
        public const string InterchangedUnloader = "interchangedUnloader";
        public const string TeleportLoading = "teleportLoading";
        public const string Progression = "progression";
        public const string PassengerStop = "passengerStop";

        private static readonly string[] CanonicalTypes =
        {
            Loader,
            Unloader,
            Formulaic,
            RepairTrack,
            TeamTrack,
            Interchange,
            InterchangedLoader,
            InterchangedUnloader,
            TeleportLoading,
            Progression,
            PassengerStop
        };

        private static readonly HashSet<string> CanonicalTypeSet =
            new HashSet<string>(CanonicalTypes, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "loader", Loader },
                { "industryloader", Loader },
                { "model.ops.industryloader", Loader },
                { "model.opsnew.industryloader", Loader },
                { "unloader", Unloader },
                { "industryunloader", Unloader },
                { "model.ops.industryunloader", Unloader },
                { "model.opsnew.industryunloader", Unloader },
                { "formulaic", Formulaic },
                { "formulaicindustrycomponent", Formulaic },
                { "model.ops.formulaicindustrycomponent", Formulaic },
                { "model.opsnew.formulaicindustrycomponent", Formulaic },
                { "repairtrack", RepairTrack },
                { "repair-track", RepairTrack },
                { "model.ops.repairtrack", RepairTrack },
                { "model.opsnew.repairtrack", RepairTrack },
                { "teamtrack", TeamTrack },
                { "team-track", TeamTrack },
                { "model.ops.teamtrack", TeamTrack },
                { "model.opsnew.teamtrack", TeamTrack },
                { "interchange", Interchange },
                { "model.ops.interchange", Interchange },
                { "model.opsnew.interchange", Interchange },
                { "interchangereloader.ops.interchangereloader", Interchange },
                { "interchangedloader", InterchangedLoader },
                { "interchanged-loader", InterchangedLoader },
                { "model.ops.interchangedindustryloader", InterchangedLoader },
                { "model.opsnew.interchangedindustryloader", InterchangedLoader },
                { "interchangedunloader", InterchangedUnloader },
                { "interchanged-unloader", InterchangedUnloader },
                { "model.ops.interchangedindustryunloader", InterchangedUnloader },
                { "model.opsnew.interchangedindustryunloader", InterchangedUnloader },
                { "teleportloading", TeleportLoading },
                { "teleport-loading", TeleportLoading },
                { "teleportloadingindustry", TeleportLoading },
                { "model.ops.teleportloadingindustry", TeleportLoading },
                { "model.opsnew.teleportloadingindustry", TeleportLoading },
                { "progression", Progression },
                { "progressionindustry", Progression },
                { "progression-industry", Progression },
                { "progressionindustrycomponent", Progression },
                { "model.ops.progressionindustrycomponent", Progression },
                { "model.opsnew.progressionindustrycomponent", Progression },
                { "passengerstop", PassengerStop },
                { "passenger-stop", PassengerStop },
                { "paxstationcomponent", PassengerStop },
                { "alinasmapmod.paxstationcomponent", PassengerStop },
                { "alinasmapmod.stations.paxstationcomponent", PassengerStop },
                { "captiveconversionloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader" },
                { "captive-conversion-loader", "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader" },
                { "confusingsupplements.captiveconversionloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader" },
                { "confusingsupplements.industrycomponents.captiveconversionloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader" },
                { "captiveconversionunloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader" },
                { "captive-conversion-unloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader" },
                { "confusingsupplements.captiveconversionunloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader" },
                { "confusingsupplements.industrycomponents.captiveconversionunloader", "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader" },
                { "pay4resource", "ConfusingSupplements.IndustryComponents.Pay4Resource" },
                { "pay-for-resource", "ConfusingSupplements.IndustryComponents.Pay4Resource" },
                { "confusingsupplements.pay4resource", "ConfusingSupplements.IndustryComponents.Pay4Resource" },
                { "confusingsupplements.industrycomponents.pay4resource", "ConfusingSupplements.IndustryComponents.Pay4Resource" },
                { "adrfdr.pay4resource", "ConfusingSupplements.IndustryComponents.Pay4Resource" },
                { "confusingsupplements.empty", "ConfusingSupplements.IndustryComponents.Empty" },
                { "confusingsupplements.industrycomponents.empty", "ConfusingSupplements.IndustryComponents.Empty" }
            };

        public static IReadOnlyCollection<string> Canonical => CanonicalTypes;

        public static string Normalize(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return type;
            }

            var trimmed = type.Trim();
            return Aliases.TryGetValue(trimmed, out var canonical)
                ? canonical
                : trimmed;
        }

        public static bool IsKnown(string type)
        {
            return CanonicalTypeSet.Contains(Normalize(type) ?? string.Empty);
        }

        public static bool IsCustomTypeCandidate(string type)
        {
            var normalized = Normalize(type);
            return !string.IsNullOrWhiteSpace(normalized) &&
                   !IsKnown(normalized) &&
                   normalized.Contains(".");
        }

        public static bool UsesLoadId(string type)
        {
            var normalized = Normalize(type);
            return string.Equals(normalized, Loader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, Unloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, RepairTrack, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, InterchangedLoader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, InterchangedUnloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, PassengerStop, StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesTrackSpanIds(string type)
        {
            var normalized = Normalize(type);
            return string.Equals(normalized, Loader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, Unloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, RepairTrack, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, TeamTrack, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, Interchange, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, InterchangedLoader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, InterchangedUnloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, Progression, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, PassengerStop, StringComparison.OrdinalIgnoreCase);
        }

        public static string KnownTypesForMessage()
        {
            return string.Join(", ", CanonicalTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }
}
