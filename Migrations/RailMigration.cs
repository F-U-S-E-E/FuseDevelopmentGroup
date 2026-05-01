using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RAIL.Data;
using RAIL.Data.Common;
using RAIL.Infrastructure;

namespace RAIL.Migrations
{
    public static class RailMigration
    {
        public const string CurrentVersion = "1.0";

        private static readonly Version CurrentSemanticVersion = new Version(1, 0);
        private static readonly object WarningLock = new object();
        private static readonly HashSet<string> WarningsEmitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static RailModDefinition Migrate(RailModDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var packageId = GetPackageId(definition);
            var sourceVersion = NormalizeSchemaVersion(definition, packageId, true);

            if (sourceVersion.CompareTo(CurrentSemanticVersion) > 0)
            {
                WarnOnce(
                    packageId,
                    $"schema.future.{definition.SchemaVersion}",
                    $"RAIL package '{packageId}' declares future schemaVersion '{definition.SchemaVersion}'. " +
                    $"This runtime supports '{CurrentVersion}'; attempting best-effort load.");
            }
            else
            {
                RunVersionByVersionMigrations(definition, sourceVersion, packageId);
            }

            Normalize(definition);
            ApplyCompatibilityMigrations(definition, packageId);
            Normalize(definition);
            return definition;
        }

        public static void Normalize(RailModDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            NormalizeSchemaVersion(definition, GetPackageId(definition), false);

            definition.CoordinateSpace = string.IsNullOrWhiteSpace(definition.CoordinateSpace) ? "world" : definition.CoordinateSpace;
            definition.ModVersion = string.IsNullOrWhiteSpace(definition.ModVersion) ? "1.0.0" : definition.ModVersion;
            definition.Tracks = definition.Tracks ?? new RailTrackDefinition();
            definition.Operations = definition.Operations ?? new RailOperationsDefinition();
            definition.World = definition.World ?? new RailWorldDefinition();
            definition.Progression = definition.Progression ?? new RailProgressionRoot();
            definition.Extensions = definition.Extensions ?? new Dictionary<string, object>();

            definition.Tracks.Nodes = definition.Tracks.Nodes ?? new Dictionary<string, RailNode>();
            definition.Tracks.Segments = definition.Tracks.Segments ?? new Dictionary<string, RailSegment>();
            definition.Tracks.Spans = definition.Tracks.Spans ?? new Dictionary<string, RailSpan>();
            definition.Tracks.Areas = definition.Tracks.Areas ?? new Dictionary<string, RailArea>();
            definition.Tracks.Removals = definition.Tracks.Removals ?? new RailTrackRemovals();
            definition.Tracks.Removals.Nodes = definition.Tracks.Removals.Nodes ?? Array.Empty<string>();
            definition.Tracks.Removals.Segments = definition.Tracks.Removals.Segments ?? Array.Empty<string>();
            definition.Tracks.Removals.Spans = definition.Tracks.Removals.Spans ?? Array.Empty<string>();

            definition.Operations.Loads = definition.Operations.Loads ?? new Dictionary<string, RailLoad>();
            definition.Operations.Industries = definition.Operations.Industries ?? new Dictionary<string, RailIndustry>();
            definition.Operations.Loaders = definition.Operations.Loaders ?? new Dictionary<string, RailLoader>();
            definition.Operations.Turntables = definition.Operations.Turntables ?? new Dictionary<string, RailTurntable>();
            definition.Operations.Stations = definition.Operations.Stations ?? new Dictionary<string, RailStation>();

            definition.World.Scenery = definition.World.Scenery ?? new Dictionary<string, RailScenery>();
            definition.World.Splineys = definition.World.Splineys ?? new Dictionary<string, RailSpliney>();
            definition.World.TelegraphPoles = definition.World.TelegraphPoles ?? new Dictionary<string, RailTelegraphPoles>();
            definition.World.MapLabels = definition.World.MapLabels ?? new Dictionary<string, RailMapLabel>();
            definition.World.MapMasks = definition.World.MapMasks ?? new Dictionary<string, RailMapMask>();
            definition.World.MapTiles = definition.World.MapTiles ?? new Dictionary<string, RailMapTileSource>();
            definition.World.SceneClones = definition.World.SceneClones ?? new Dictionary<string, RailSceneClone>();
            definition.World.SuppressBaseScenePaths = MergeAliasArray(definition.World.SuppressBaseScenePaths, definition.World.SuppressScenePaths);
            definition.World.SuppressBaseTrackGroups = MergeAliasArray(definition.World.SuppressBaseTrackGroups, definition.World.SuppressGroups);
            definition.World.SuppressBaseAreas = MergeAliasArray(definition.World.SuppressBaseAreas, definition.World.SuppressAreas);
            definition.World.SuppressScenePaths = null;
            definition.World.SuppressGroups = null;
            definition.World.SuppressAreas = null;
            definition.World.Removals = definition.World.Removals ?? new RailWorldRemovals();
            definition.World.Removals.Scenery = definition.World.Removals.Scenery ?? Array.Empty<string>();
            definition.World.Removals.Splineys = definition.World.Removals.Splineys ?? Array.Empty<string>();
            definition.World.Removals.TelegraphPoles = definition.World.Removals.TelegraphPoles ?? Array.Empty<string>();
            definition.World.Removals.MapLabels = definition.World.Removals.MapLabels ?? Array.Empty<string>();
            definition.World.Removals.MapMasks = definition.World.Removals.MapMasks ?? Array.Empty<string>();
            definition.World.Removals.SceneClones = definition.World.Removals.SceneClones ?? Array.Empty<string>();

            definition.Progression.Progressions = definition.Progression.Progressions ?? new Dictionary<string, RailProgression>();
            definition.Progression.MapFeatures = definition.Progression.MapFeatures ?? new Dictionary<string, RailMapFeature>();

            foreach (var span in definition.Tracks.Spans.Values)
            {
                NormalizeTrackLocation(span?.Upper);
                NormalizeTrackLocation(span?.Lower);
            }

            foreach (var industry in definition.Operations.Industries.Values)
            {
                if (industry?.Components == null)
                {
                    continue;
                }

                foreach (var component in industry.Components.Values)
                {
                    if (component == null)
                    {
                        continue;
                    }

                    component.Type = NormalizeIndustryComponentType(component.Type);
                    component.TrackSpanIds = component.TrackSpanIds ?? Array.Empty<string>();
                    component.InputSpanIds = component.InputSpanIds ?? Array.Empty<string>();
                    component.InputTermsPerDay = component.InputTermsPerDay ?? new Dictionary<string, float>();
                    component.OutputTermsPerDay = component.OutputTermsPerDay ?? new Dictionary<string, float>();
                    component.TeamProfiles = component.TeamProfiles ?? new Dictionary<string, RailTeamTrackEntry>();
                    component.NeighborIds = component.NeighborIds ?? Array.Empty<string>();
                    component.BranchDefinitions = component.BranchDefinitions ?? Array.Empty<RailPassengerBranch>();
                }
            }
        }

        public static bool TryParseSchemaVersion(string value, out Version version)
        {
            string normalized;
            return TryParseSchemaVersion(value, out version, out normalized);
        }

        public static bool IsFutureSchemaVersion(string value)
        {
            Version parsed;
            return TryParseSchemaVersion(value, out parsed) && parsed.CompareTo(CurrentSemanticVersion) > 0;
        }

        private static void RunVersionByVersionMigrations(RailModDefinition definition, Version sourceVersion, string packageId)
        {
            var version = sourceVersion;
            while (version.CompareTo(CurrentSemanticVersion) < 0)
            {
                Version nextVersion;
                if (!TryMigrateOneVersion(definition, version, packageId, out nextVersion))
                {
                    WarnOnce(
                        packageId,
                        $"schema.unknownOlder.{FormatVersion(version)}",
                        $"RAIL package '{packageId}' declares older unknown schemaVersion '{FormatVersion(version)}'. " +
                        $"No exact migration exists; applying best-effort normalization as '{CurrentVersion}'.");
                    definition.SchemaVersion = CurrentVersion;
                    return;
                }

                version = nextVersion;
            }

            definition.SchemaVersion = CurrentVersion;
        }

        private static bool TryMigrateOneVersion(RailModDefinition definition, Version version, string packageId, out Version nextVersion)
        {
            nextVersion = version;

            // Add version-specific migrations here as the schema advances.
            // Deprecation policy: renamed fields stay readable for one minor version,
            // emit one warning per package while they are migrated, and are removed in
            // the following minor version after authored packages have had a release
            // window to save with the new field name.
            switch (FormatVersion(version))
            {
                case CurrentVersion:
                    nextVersion = CurrentSemanticVersion;
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyCompatibilityMigrations(RailModDefinition definition, string packageId)
        {
            var migratedModelField = false;
            foreach (var scenery in definition.World.Scenery.Values)
            {
                if (scenery == null ||
                    !string.IsNullOrWhiteSpace(scenery.AssetIdentifier) ||
                    string.IsNullOrWhiteSpace(scenery.Model))
                {
                    continue;
                }

                scenery.AssetIdentifier = scenery.Model;
                migratedModelField = true;
            }

            if (migratedModelField)
            {
                WarnOnce(
                    packageId,
                    "deprecation.world.scenery.model",
                    $"RAIL package '{packageId}' uses deprecated world.scenery.*.model as an asset identifier. " +
                    "RAIL migrated it to assetIdentifier in memory; keep model only as a temporary compatibility field.");
            }
        }

        private static Version NormalizeSchemaVersion(RailModDefinition definition, string packageId, bool logWarnings)
        {
            Version version;
            string normalized;
            if (TryParseSchemaVersion(definition.SchemaVersion, out version, out normalized))
            {
                definition.SchemaVersion = normalized;
                return version;
            }

            definition.SchemaVersion = CurrentVersion;
            if (logWarnings)
            {
                WarnOnce(
                    packageId,
                    "schema.invalid",
                    $"RAIL package '{packageId}' has an invalid schemaVersion. Defaulting to '{CurrentVersion}' for best-effort load.");
            }

            return CurrentSemanticVersion;
        }

        private static bool TryParseSchemaVersion(string value, out Version version, out string normalized)
        {
            version = null;
            normalized = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            var parts = text.Split('.');
            if (parts.Length == 0 || parts.Length > 3)
            {
                return false;
            }

            int major;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out major) || major < 0)
            {
                return false;
            }

            var minor = 0;
            if (parts.Length >= 2 &&
                (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minor) || minor < 0))
            {
                return false;
            }

            version = new Version(major, minor);
            normalized = FormatVersion(version);
            return true;
        }

        private static string FormatVersion(Version version)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, Math.Max(version.Minor, 0));
        }

        private static string GetPackageId(RailModDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(definition?.Id))
            {
                return definition.Id.Trim();
            }

            if (!string.IsNullOrWhiteSpace(definition?.Name))
            {
                return definition.Name.Trim();
            }

            return "<unknown>";
        }

        private static void WarnOnce(string packageId, string code, string message)
        {
            var key = $"{packageId}:{code}";
            lock (WarningLock)
            {
                if (!WarningsEmitted.Add(key))
                {
                    return;
                }
            }

            RailLog.Warning(message);
        }

        private static string NormalizeIndustryComponentType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return type;
            }

            switch (type.Trim().ToLowerInvariant())
            {
                case "model.ops.industryloader":
                case "industryloader":
                    return "loader";
                case "model.ops.industryunloader":
                case "industryunloader":
                    return "unloader";
                case "model.ops.formulaicindustrycomponent":
                case "formulaicindustrycomponent":
                    return "formulaic";
                case "model.ops.repairtrack":
                case "repair-track":
                    return "repairTrack";
                case "model.ops.teamtrack":
                case "team-track":
                    return "teamTrack";
                case "model.ops.interchange":
                    return "interchange";
                case "model.ops.interchangedindustryloader":
                case "interchanged-loader":
                    return "interchangedLoader";
                case "alinasmapmod.paxstationcomponent":
                case "alinasmapmod.stations.paxstationcomponent":
                case "paxstationcomponent":
                case "passenger-stop":
                case "passengerstop":
                    return "passengerStop";
                default:
                    return type.Trim();
            }
        }

        private static string[] MergeAliasArray(string[] preferred, string[] alias)
        {
            return (preferred ?? Array.Empty<string>())
                .Concat(alias ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void NormalizeTrackLocation(RailTrackLocation location)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.End))
            {
                return;
            }

            switch (location.End.Trim().ToUpperInvariant())
            {
                case "A":
                case "START":
                    location.End = "A";
                    break;
                case "B":
                case "END":
                    location.End = "B";
                    break;
                default:
                    location.End = location.End.Trim();
                    break;
            }
        }
    }
}
