using System;
using System.Collections.Generic;
using RAIL.Data;
using RAIL.Data.Common;

namespace RAIL.Migrations
{
    public static class RailMigration
    {
        public const int CurrentVersion = 1;

        public static RailModDefinition Migrate(RailModDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Normalize(definition);

            while (definition.SchemaVersion < CurrentVersion)
            {
                switch (definition.SchemaVersion)
                {
                    default:
                        throw new InvalidOperationException($"Unknown RAIL schema version {definition.SchemaVersion}.");
                }
            }

            if (definition.SchemaVersion > CurrentVersion)
            {
                throw new InvalidOperationException($"RAIL schema version {definition.SchemaVersion} is newer than this runtime supports ({CurrentVersion}).");
            }

            return definition;
        }

        public static void Normalize(RailModDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            if (definition.SchemaVersion <= 0)
            {
                definition.SchemaVersion = CurrentVersion;
            }

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
