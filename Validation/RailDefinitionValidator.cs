using System.Collections.Generic;
using System.Linq;
using RAIL.API;
using RAIL.Data;
using RAIL.Data.Common;
using RAIL.Migrations;

namespace RAIL.Validation
{
    public sealed class RailDefinitionValidator : IValidator<RailModDefinition>
    {
        public ValidationResult Validate(RailModDefinition value)
        {
            var result = new ValidationResult();
            if (value == null)
            {
                result.AddError("$", "Definition is required.", "rail.definition.required");
                return result;
            }

            RailMigration.Normalize(value);
            Required(result, "id", value.Id);
            Required(result, "name", value.Name);
            Required(result, "author", value.Author);

            if (value.SchemaVersion != RailMigration.CurrentVersion)
            {
                result.AddError("schemaVersion", $"Schema version must be {RailMigration.CurrentVersion}.", "rail.schema.version", value.SchemaVersion);
            }

            ValidateOperations(result, value.Operations);
            ValidateTrack(result, value.Tracks, value.Operations);
            ValidateWorld(result, value.World);
            ValidateProgression(result, value.Progression);
            return result;
        }

        private static void ValidateTrack(ValidationResult result, RailTrackDefinition tracks, RailOperationsDefinition operations)
        {
            var generatedNodeIds = CollectGeneratedNodeIds(operations);
            var generatedSegmentIds = CollectGeneratedSegmentIds(operations);

            if (tracks.Removals != null)
            {
                ValidateTrackRemovalTargets(result, "tracks.removals.nodes", tracks.Removals.Nodes, tracks.Nodes.Keys);
                ValidateTrackRemovalTargets(result, "tracks.removals.segments", tracks.Removals.Segments, tracks.Segments.Keys);
                ValidateTrackRemovalTargets(result, "tracks.removals.spans", tracks.Removals.Spans, tracks.Spans.Keys);
            }

            foreach (var segment in tracks.Segments)
            {
                var path = $"tracks.segments.{segment.Key}";
                Required(result, $"{path}.startNodeId", segment.Value.StartNodeId);
                Required(result, $"{path}.endNodeId", segment.Value.EndNodeId);

                if (!string.IsNullOrWhiteSpace(segment.Value.StartNodeId) &&
                    !tracks.Nodes.ContainsKey(segment.Value.StartNodeId) &&
                    !generatedNodeIds.Contains(segment.Value.StartNodeId))
                {
                    result.AddWarning($"{path}.startNodeId", "Start node is not defined in this RAIL document. It must exist in the base game graph at runtime.", "rail.track.node.external", segment.Value.StartNodeId);
                }

                if (!string.IsNullOrWhiteSpace(segment.Value.EndNodeId) &&
                    !tracks.Nodes.ContainsKey(segment.Value.EndNodeId) &&
                    !generatedNodeIds.Contains(segment.Value.EndNodeId))
                {
                    result.AddWarning($"{path}.endNodeId", "End node is not defined in this RAIL document. It must exist in the base game graph at runtime.", "rail.track.node.external", segment.Value.EndNodeId);
                }

                if (segment.Value.SpeedLimit < 0 || segment.Value.SpeedLimit > 80)
                {
                    result.AddError($"{path}.speedLimit", "Speed limit must be between 0 and 80.", "rail.track.speedLimit", segment.Value.SpeedLimit);
                }
            }

            foreach (var span in tracks.Spans)
            {
                ValidateTrackLocation(result, $"tracks.spans.{span.Key}.upper", span.Value.Upper, tracks.Segments, generatedSegmentIds);
                ValidateTrackLocation(result, $"tracks.spans.{span.Key}.lower", span.Value.Lower, tracks.Segments, generatedSegmentIds);
            }

            foreach (var area in tracks.Areas)
            {
                var path = $"tracks.areas.{area.Key}";
                if (area.Value.Radius.HasValue && area.Value.Radius.Value < 0f)
                {
                    result.AddError($"{path}.radius", "Area radius must be greater than or equal to 0.", "rail.track.area.radius", area.Value.Radius.Value);
                }

                if (area.Value.TagColor != null && area.Value.TagColor.Length != 3 && area.Value.TagColor.Length != 4)
                {
                    result.AddError($"{path}.tagColor", "Area tagColor must contain 3 or 4 values.", "rail.track.area.tagColor", area.Value.TagColor.Length);
                }

                if (area.Value.Order.HasValue && area.Value.Order.Value < 0)
                {
                    result.AddError($"{path}.order", "Area order must be greater than or equal to 0.", "rail.track.area.order", area.Value.Order.Value);
                }
            }
        }

        private static void ValidateTrackLocation(ValidationResult result, string path, RailTrackLocation location, IDictionary<string, RailSegment> segments, ISet<string> generatedSegmentIds)
        {
            if (location == null)
            {
                result.AddError(path, "Track location is required.", "rail.track.location.required");
                return;
            }

            Required(result, $"{path}.segmentId", location.SegmentId);
            if (location.Normalized == null && location.Distance == null)
            {
                result.AddError(path, "Track location must set either normalized or distance.", "rail.track.location.measure");
            }

            if (location.Normalized != null && (location.Normalized < 0f || location.Normalized > 1f))
            {
                result.AddError($"{path}.normalized", "Normalized location must be between 0 and 1.", "rail.track.location.normalized", location.Normalized);
            }

            if (!string.IsNullOrWhiteSpace(location.End) &&
                location.End != "A" &&
                location.End != "B")
            {
                result.AddError($"{path}.end", "Track location end must be A or B.", "rail.track.location.end", location.End);
            }

            if (!string.IsNullOrWhiteSpace(location.SegmentId) &&
                !segments.ContainsKey(location.SegmentId) &&
                !generatedSegmentIds.Contains(location.SegmentId))
            {
                result.AddWarning($"{path}.segmentId", "Segment is not defined in this RAIL document. It must exist in the base game graph at runtime.", "rail.track.segment.external", location.SegmentId);
            }
        }

        private static void ValidateTrackRemovalTargets(ValidationResult result, string path, IEnumerable<string> removals, IEnumerable<string> definitions)
        {
            if (removals == null)
            {
                return;
            }

            var definedIds = new HashSet<string>(definitions ?? Enumerable.Empty<string>());
            var seen = new HashSet<string>();
            var index = 0;
            foreach (var id in removals)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddError($"{path}[{index}]", "Removal IDs must not be blank.", "rail.track.removal.blank");
                }
                else if (!seen.Add(id))
                {
                    result.AddWarning($"{path}[{index}]", "Removal ID is listed more than once.", "rail.track.removal.duplicate", id);
                }
                else if (definedIds.Contains(id))
                {
                    result.AddError($"{path}[{index}]", "A track object cannot be defined and removed in the same RAIL document.", "rail.track.removal.conflict", id);
                }

                index++;
            }
        }

        private static void ValidateOperations(ValidationResult result, RailOperationsDefinition operations)
        {
            if (operations.Loads != null)
            {
                foreach (var load in operations.Loads)
                {
                    var path = $"operations.loads.{load.Key}";
                    Required(result, $"{path}.name", load.Value.Name);

                    if (!string.IsNullOrWhiteSpace(load.Value.Units))
                    {
                        var units = load.Value.Units.ToLowerInvariant();
                        if (units != "pounds" && units != "gallons" && units != "quantity")
                        {
                            result.AddError($"{path}.units", "Load units must be Pounds, Gallons, or Quantity.", "rail.operations.loads.units", load.Value.Units);
                        }
                    }

                    if (load.Value.Density.HasValue && load.Value.Density.Value < 0f)
                    {
                        result.AddError($"{path}.density", "Load density must be greater than or equal to 0.", "rail.operations.loads.density", load.Value.Density.Value);
                    }

                    if (load.Value.UnitWeightInPounds.HasValue && load.Value.UnitWeightInPounds.Value < 0f)
                    {
                        result.AddError($"{path}.unitWeightInPounds", "Load unitWeightInPounds must be greater than or equal to 0.", "rail.operations.loads.unitWeightInPounds", load.Value.UnitWeightInPounds.Value);
                    }

                    if (load.Value.PayPerQuantity.HasValue && load.Value.PayPerQuantity.Value < 0f)
                    {
                        result.AddError($"{path}.payPerQuantity", "Load payPerQuantity must be greater than or equal to 0.", "rail.operations.loads.payPerQuantity", load.Value.PayPerQuantity.Value);
                    }

                    if (load.Value.CostPerUnit.HasValue && load.Value.CostPerUnit.Value < 0f)
                    {
                        result.AddError($"{path}.costPerUnit", "Load costPerUnit must be greater than or equal to 0.", "rail.operations.loads.costPerUnit", load.Value.CostPerUnit.Value);
                    }
                }
            }

            foreach (var industry in operations.Industries)
            {
                Required(result, $"operations.industries.{industry.Key}.name", industry.Value.Name);
                if (industry.Value.Order.HasValue && industry.Value.Order.Value < 0)
                {
                    result.AddError($"operations.industries.{industry.Key}.order", "Industry order must be greater than or equal to 0.", "rail.operations.industry.order", industry.Value.Order.Value);
                }

                foreach (var component in industry.Value.Components)
                {
                    var componentPath = $"operations.industries.{industry.Key}.components.{component.Key}";
                    Required(result, $"{componentPath}.type", component.Value.Type);
                    Required(result, $"{componentPath}.name", component.Value.Name);

                    var componentType = (component.Value.Type ?? string.Empty).ToLowerInvariant();
                    switch (componentType)
                    {
                        case "passengerstop":
                        case "passenger-stop":
                        case "alinasmapmod.paxstationcomponent":
                            Required(result, $"{componentPath}.timetableCode", component.Value.TimetableCode);
                            if ((component.Value.TrackSpanIds == null || component.Value.TrackSpanIds.Length == 0) &&
                                (component.Value.InputSpanIds == null || component.Value.InputSpanIds.Length == 0))
                            {
                                result.AddError($"{componentPath}.trackSpanIds", "Passenger stop components require at least one track span.", "rail.operations.passengerStop.trackSpanIds");
                            }
                            break;

                        case "formulaic":
                        case "model.ops.formulaicindustrycomponent":
                            if ((component.Value.InputTermsPerDay == null || component.Value.InputTermsPerDay.Count == 0) &&
                                (component.Value.OutputTermsPerDay == null || component.Value.OutputTermsPerDay.Count == 0))
                            {
                                result.AddError($"{componentPath}.inputTermsPerDay", "Formulaic components require inputTermsPerDay and/or outputTermsPerDay.", "rail.operations.formulaic.terms");
                            }
                            break;

                        case "teamtrack":
                        case "team-track":
                        case "model.ops.teamtrack":
                            if (component.Value.TeamProfiles == null || component.Value.TeamProfiles.Count == 0)
                            {
                                result.AddError($"{componentPath}.teamProfiles", "Team track components require at least one team profile entry.", "rail.operations.teamTrack.profile");
                            }
                            break;
                    }
                }
            }

            foreach (var loader in operations.Loaders)
            {
                Required(result, $"operations.loaders.{loader.Key}.prefab", loader.Value.Prefab);
            }

            foreach (var station in operations.Stations)
            {
                Required(result, $"operations.stations.{station.Key}.prefab", station.Value.Prefab);
                Required(result, $"operations.stations.{station.Key}.passengerStopId", station.Value.PassengerStopId);
            }

            foreach (var turntable in operations.Turntables)
            {
                if (turntable.Value.Radius <= 0f)
                {
                    result.AddError($"operations.turntables.{turntable.Key}.radius", "Turntable radius must be greater than 0.", "rail.turntable.radius", turntable.Value.Radius);
                }

                if (turntable.Value.Subdivisions < 4 || turntable.Value.Subdivisions > 32)
                {
                    result.AddError($"operations.turntables.{turntable.Key}.subdivisions", "Turntable subdivisions must be between 4 and 32.", "rail.turntable.subdivisions", turntable.Value.Subdivisions);
                }

                if (turntable.Value.Roundhouse != null &&
                    turntable.Value.Roundhouse.Stalls > 0 &&
                    turntable.Value.Roundhouse.TrackLength <= 0f)
                {
                    result.AddError($"operations.turntables.{turntable.Key}.roundhouse.trackLength", "Roundhouse track length must be greater than 0.", "rail.turntable.roundhouse.trackLength", turntable.Value.Roundhouse.TrackLength);
                }
            }
        }

        private static void ValidateWorld(ValidationResult result, RailWorldDefinition world)
        {
            if (world.Removals != null)
            {
                ValidateWorldRemovalTargets(result, "world.removals.scenery", world.Removals.Scenery, world.Scenery.Keys);
                ValidateWorldRemovalTargets(result, "world.removals.splineys", world.Removals.Splineys, world.Splineys.Keys);
                ValidateWorldRemovalTargets(result, "world.removals.telegraphPoles", world.Removals.TelegraphPoles, world.TelegraphPoles.Keys);
                ValidateWorldRemovalTargets(result, "world.removals.mapLabels", world.Removals.MapLabels, world.MapLabels.Keys);
                ValidateWorldRemovalTargets(result, "world.removals.mapMasks", world.Removals.MapMasks, world.MapMasks.Keys);
                ValidateWorldRemovalTargets(result, "world.removals.sceneClones", world.Removals.SceneClones, world.SceneClones.Keys);
            }

            foreach (var scenery in world.Scenery)
            {
                Required(result, $"world.scenery.{scenery.Key}.model", scenery.Value.Model);
            }

            foreach (var spliney in world.Splineys)
            {
                Required(result, $"world.splineys.{spliney.Key}.type", spliney.Value.Type);
                if (spliney.Value.Points == null || spliney.Value.Points.Length < 2)
                {
                    result.AddError($"world.splineys.{spliney.Key}.points", "Spliney objects require at least two points.", "rail.spliney.points");
                }
            }

            foreach (var telegraph in world.TelegraphPoles)
            {
                if (telegraph.Value.Points == null || telegraph.Value.Points.Length < 2)
                {
                    result.AddError($"world.telegraphPoles.{telegraph.Key}.points", "Telegraph pole sets require at least two points.", "rail.telegraph.points");
                }
            }

            foreach (var mapMask in world.MapMasks)
            {
                var path = $"world.mapMasks.{mapMask.Key}";
                var type = (mapMask.Value.Type ?? string.Empty).ToLowerInvariant();
                switch (type)
                {
                    case "circle":
                        if (!mapMask.Value.Radius.HasValue || mapMask.Value.Radius.Value <= 0f)
                        {
                            result.AddError($"{path}.radius", "Circle map masks require a positive radius.", "rail.mapMask.circle.radius", mapMask.Value.Radius);
                        }
                        break;

                    case "rectangle":
                        if (!mapMask.Value.Size.HasValue || mapMask.Value.Size.Value.x <= 0f || mapMask.Value.Size.Value.z <= 0f)
                        {
                            result.AddError($"{path}.size", "Rectangle map masks require a positive size.", "rail.mapMask.rectangle.size", mapMask.Value.Size);
                        }
                        break;

                    case "curve":
                        if (mapMask.Value.Points == null || mapMask.Value.Points.Length < 2)
                        {
                            result.AddError($"{path}.points", "Curve map masks require at least two points.", "rail.mapMask.curve.points");
                        }
                        break;

                    default:
                        result.AddError($"{path}.type", "Map mask type must be circle, rectangle, or curve.", "rail.mapMask.type", mapMask.Value.Type);
                        break;
                }
            }

            foreach (var mapTile in world.MapTiles)
            {
                if (mapTile.Value == null)
                {
                    result.AddError($"world.mapTiles.{mapTile.Key}", "Map tile source is required.", "rail.mapTiles.required");
                    continue;
                }

                Required(result, $"world.mapTiles.{mapTile.Key}.directory", mapTile.Value.Directory);
                Required(result, $"world.mapTiles.{mapTile.Key}.sourceFolder", mapTile.Value.SourceFolder);
            }

            foreach (var sceneClone in world.SceneClones)
            {
                if (sceneClone.Value == null)
                {
                    result.AddError($"world.sceneClones.{sceneClone.Key}", "Scene clone definition is required.", "rail.sceneClone.required");
                    continue;
                }

                Required(result, $"world.sceneClones.{sceneClone.Key}.targetPath", sceneClone.Value.TargetPath);
            }
        }

        private static void ValidateWorldRemovalTargets(ValidationResult result, string path, IEnumerable<string> removals, IEnumerable<string> definitions)
        {
            if (removals == null)
            {
                return;
            }

            var definedIds = new HashSet<string>(definitions ?? Enumerable.Empty<string>(), System.StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var id in removals)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddError($"{path}[{index}]", "Removal IDs must not be blank.", "rail.world.removal.blank");
                }
                else if (!seen.Add(id))
                {
                    result.AddWarning($"{path}[{index}]", "Removal ID is listed more than once.", "rail.world.removal.duplicate", id);
                }
                else if (definedIds.Contains(id))
                {
                    result.AddError($"{path}[{index}]", "A world object cannot be defined and removed in the same RAIL document.", "rail.world.removal.conflict", id);
                }

                index++;
            }
        }

        private static void ValidateProgression(ValidationResult result, RailProgressionRoot progression)
        {
            foreach (var progressionEntry in progression.Progressions)
            {
                foreach (var section in progressionEntry.Value.Sections)
                {
                    Required(result, $"progression.progressions.{progressionEntry.Key}.sections.{section.Key}.displayName", section.Value.DisplayName);
                    var phases = section.Value.DeliveryPhases;
                    if (phases == null)
                    {
                        continue;
                    }

                    for (var phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
                    {
                        var phase = phases[phaseIndex];
                        var deliveries = phase.Deliveries;
                        if (deliveries != null && deliveries.Length > 0)
                        {
                            Required(result, $"progression.progressions.{progressionEntry.Key}.sections.{section.Key}.deliveryPhases[{phaseIndex}].industryComponentId", phase.IndustryComponentId);
                        }

                        if (deliveries == null)
                        {
                            continue;
                        }

                        for (var deliveryIndex = 0; deliveryIndex < deliveries.Length; deliveryIndex++)
                        {
                            var delivery = deliveries[deliveryIndex];
                            if (delivery.Count < 1)
                            {
                                result.AddError($"progression.progressions.{progressionEntry.Key}.sections.{section.Key}.deliveryPhases[{phaseIndex}].deliveries[{deliveryIndex}].count", "Delivery count must be greater than 0.", "rail.progression.delivery.count", delivery.Count);
                            }
                        }
                    }
                }
            }
        }

        private static void Required(ValidationResult result, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError(field, "Value is required.", "rail.required");
            }
        }

        private static HashSet<string> CollectGeneratedNodeIds(RailOperationsDefinition operations)
        {
            var result = new HashSet<string>();
            if (operations?.Turntables == null)
            {
                return result;
            }

            foreach (var turntable in operations.Turntables)
            {
                var definition = turntable.Value;
                if (definition == null)
                {
                    continue;
                }

                for (var index = 0; index < definition.Subdivisions; index++)
                {
                    result.Add(TurntableAPI.GetPitNodeId(turntable.Key, index, definition));
                }

                if (definition.Roundhouse == null || definition.Roundhouse.Stalls <= 0)
                {
                    continue;
                }

                for (var index = 1; index <= definition.Roundhouse.Stalls; index++)
                {
                    result.Add(TurntableAPI.GetRoundhouseNodeId(turntable.Key, index, definition));
                }
            }

            return result;
        }

        private static HashSet<string> CollectGeneratedSegmentIds(RailOperationsDefinition operations)
        {
            var result = new HashSet<string>();
            if (operations?.Turntables == null)
            {
                return result;
            }

            foreach (var turntable in operations.Turntables.Where(entry => entry.Value?.Roundhouse != null && entry.Value.Roundhouse.Stalls > 0))
            {
                for (var index = 1; index <= turntable.Value.Roundhouse.Stalls; index++)
                {
                    result.Add(TurntableAPI.GetRoundhouseSegmentId(turntable.Key, index, turntable.Value));
                }
            }

            return result;
        }
    }
}
