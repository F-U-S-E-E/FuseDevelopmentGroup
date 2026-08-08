using System;
using System.Collections.Generic;
using System.Linq;
using Fuse.Core.Migrations;
using Fuse.Core.Model;

namespace Fuse.Core.Validation
{
    /// <summary>
    /// Unity-free mirror of the in-game <c>FUSE.Authoring.Validation.FuseDefinitionValidator</c>.
    /// Preflight validation of a <see cref="FuseModDefinition"/> for the external
    /// editor. Differences from the shipping copy: <c>Vector3</c> → <see cref="FuseVector3"/>,
    /// <c>FuseModSettingsStore</c> setting helpers → <see cref="FuseSettingTypes"/>.
    /// No game/runtime coupling — accumulates issues into a <see cref="ValidationResult"/>.
    /// </summary>
    public sealed class FuseDefinitionValidator : IValidator<FuseModDefinition>
    {
        public ValidationResult Validate(FuseModDefinition value)
        {
            var result = new ValidationResult();
            if (value == null)
            {
                result.AddError("$", "Definition is required.", "fuse.definition.required");
                return result;
            }

            FuseMigration.Normalize(value);
            Required(result, "id", value.Id);
            Required(result, "name", value.Name);
            ValidateMap(result, value.Map);
            ValidateMixinto(result, value.Mixinto);

            if (value.SchemaVersion != FuseMigration.CurrentVersion)
            {
                if (FuseMigration.IsFutureSchemaVersion(value.SchemaVersion))
                {
                    result.AddWarning(
                        "schemaVersion",
                        $"Schema version {value.SchemaVersion} is newer than this runtime supports ({FuseMigration.CurrentVersion}); FUSE will apply a best-effort load.",
                        "fuse.schema.version.future",
                        value.SchemaVersion);
                }
                else
                {
                    result.AddError("schemaVersion", $"Schema version must be {FuseMigration.CurrentVersion}.", "fuse.schema.version", value.SchemaVersion);
                }
            }

            ValidateOperations(result, value.Operations);
            ValidateTrack(result, value.Tracks, value.Operations);
            ValidateWorld(result, value.World);
            ValidateAudio(result, value.Audio);
            ValidateProgression(result, value.Progression);
            ValidateSettings(result, value.Settings);
            return result;
        }

        private static void ValidateMap(ValidationResult result, FuseMapDeclaration map)
        {
            if (map == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(map.DisplayName))
            {
                result.AddWarning("map.displayName", "Map displayName is blank; the package name will be shown instead.", "fuse.map.displayName.blank");
            }

            if (string.IsNullOrWhiteSpace(map.MapFolder))
            {
                result.AddError("map.mapFolder", "Map packages must set mapFolder to the package-relative folder containing Map.json and its tiles.", "fuse.map.folder.required");
                return;
            }

            var folder = map.MapFolder.Trim();
            var isRooted = folder.StartsWith("/", StringComparison.Ordinal) ||
                           folder.StartsWith("\\", StringComparison.Ordinal) ||
                           folder.IndexOf(':') >= 0;
            if (isRooted || folder.Contains(".."))
            {
                result.AddError("map.mapFolder", "Map mapFolder must be a package-relative path that stays inside the package folder.", "fuse.map.folder.outsidePackage", map.MapFolder);
            }
        }

        private static void ValidateMixinto(ValidationResult result, FuseMixintoDefinition mixinto)
        {
            if (mixinto == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(mixinto.Target))
            {
                result.AddWarning("mixinto.target", "Mixinto target is blank; FUSE will still treat this fragment as a normal conditional data file.", "fuse.mixinto.target.blank");
            }

            if (string.IsNullOrWhiteSpace(mixinto.SourceFile))
            {
                result.AddWarning("mixinto.sourceFile", "Mixinto sourceFile is blank; conversion provenance will be less clear.", "fuse.mixinto.sourceFile.blank");
            }

            var requirements = mixinto.Requires ?? Array.Empty<FuseModRequirement>();
            for (var index = 0; index < requirements.Length; index++)
            {
                var requirement = requirements[index];
                var path = $"mixinto.requires[{index}]";
                if (requirement == null)
                {
                    result.AddWarning(path, "Null mixinto requirement will be ignored.", "fuse.mixinto.requirement.null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(requirement.Id))
                {
                    result.AddWarning($"{path}.id", "Mixinto requirement id is blank and will be ignored.", "fuse.mixinto.requirement.id.blank");
                }
            }
        }

        private static void ValidateSettings(ValidationResult result, IDictionary<string, FuseModSettingDefinition> settings)
        {
            if (settings == null)
            {
                return;
            }

            foreach (var pair in settings)
            {
                var path = $"settings.{pair.Key}";
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    result.AddError("settings", "Setting IDs must not be blank.", "fuse.settings.id.blank");
                    continue;
                }

                var setting = pair.Value;
                if (setting == null)
                {
                    result.AddWarning(path, "Null setting definition will be ignored by the Settings UI.", "fuse.settings.null");
                    continue;
                }

                var type = FuseSettingTypes.NormalizeType(setting.Type);
                var scope = FuseSettingTypes.NormalizeScope(setting.Scope);
                if (type == "enum" && (setting.Values == null || setting.Values.Length == 0))
                {
                    result.AddWarning($"{path}.values", "Enum setting has no values; FUSE will render it as text.", "fuse.settings.enum.values.empty");
                }

                if (type == "number" && setting.Min.HasValue && setting.Max.HasValue && setting.Min.Value > setting.Max.Value)
                {
                    result.AddError(path, "Number setting min must be less than or equal to max.", "fuse.settings.number.range", pair.Key);
                }

                if (type == "number" && setting.Step.HasValue && setting.Step.Value <= 0d)
                {
                    result.AddWarning($"{path}.step", "Number setting step should be greater than zero.", "fuse.settings.number.step", pair.Key);
                }

                if (scope == FuseSettingTypes.ScopeServer && setting.ReloadRequired)
                {
                    result.AddWarning(path, "Server-scoped reload-required settings should be documented for multiplayer profile sharing.", "fuse.settings.server.reloadRequired", pair.Key);
                }
            }
        }

        private static void ValidateTrack(ValidationResult result, FuseTrackDefinition tracks, FuseOperationsDefinition operations)
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
                if (segment.Value == null)
                {
                    result.AddError(path, "Track segment is required.", "fuse.track.segment.required");
                    continue;
                }

                var missingStartNode = string.IsNullOrWhiteSpace(segment.Value.StartNodeId);
                var missingEndNode = string.IsNullOrWhiteSpace(segment.Value.EndNodeId);
                if (segment.Value.Partial)
                {
                    if (missingStartNode && missingEndNode)
                    {
                        result.AddError(path, "Partial track segment patches must provide at least one endpoint.", "fuse.track.segment.partialEndpoint.empty");
                    }
                    else if (missingStartNode || missingEndNode)
                    {
                        result.AddWarning(path, "Partial track segment patch will hydrate the missing endpoint from the runtime graph.", "fuse.track.segment.partialEndpoint", segment.Key);
                    }
                }
                else
                {
                    Required(result, $"{path}.startNodeId", segment.Value.StartNodeId);
                    Required(result, $"{path}.endNodeId", segment.Value.EndNodeId);
                }

                if (!string.IsNullOrWhiteSpace(segment.Value.StartNodeId) &&
                    !tracks.Nodes.ContainsKey(segment.Value.StartNodeId) &&
                    !generatedNodeIds.Contains(segment.Value.StartNodeId))
                {
                    result.AddWarning($"{path}.startNodeId", "Start node is not defined in this FUSE document. It must exist in the base game graph at runtime.", "fuse.track.node.external", segment.Value.StartNodeId);
                }

                if (!string.IsNullOrWhiteSpace(segment.Value.EndNodeId) &&
                    !tracks.Nodes.ContainsKey(segment.Value.EndNodeId) &&
                    !generatedNodeIds.Contains(segment.Value.EndNodeId))
                {
                    result.AddWarning($"{path}.endNodeId", "End node is not defined in this FUSE document. It must exist in the base game graph at runtime.", "fuse.track.node.external", segment.Value.EndNodeId);
                }

                if (segment.Value.SpeedLimit < 0 || segment.Value.SpeedLimit > 80)
                {
                    result.AddError($"{path}.speedLimit", "Speed limit must be between 0 and 80.", "fuse.track.speedLimit", segment.Value.SpeedLimit);
                }
            }

            foreach (var span in tracks.Spans)
            {
                var path = $"tracks.spans.{span.Key}";
                if (span.Value == null)
                {
                    result.AddError(path, "Track span is required.", "fuse.track.span.required");
                    continue;
                }

                ValidateTrackLocation(result, $"{path}.upper", span.Value.Upper, tracks.Segments, generatedSegmentIds);
                ValidateTrackLocation(result, $"{path}.lower", span.Value.Lower, tracks.Segments, generatedSegmentIds);
                ValidateSameSegmentSpan(result, path, span.Value, tracks.Segments, tracks.Nodes);
            }

            foreach (var area in tracks.Areas)
            {
                var path = $"tracks.areas.{area.Key}";
                if (area.Value.Radius.HasValue && area.Value.Radius.Value < 0f)
                {
                    result.AddError($"{path}.radius", "Area radius must be greater than or equal to 0.", "fuse.track.area.radius", area.Value.Radius.Value);
                }

                if (area.Value.TagColor != null && area.Value.TagColor.Length != 3 && area.Value.TagColor.Length != 4)
                {
                    result.AddError($"{path}.tagColor", "Area tagColor must contain 3 or 4 values.", "fuse.track.area.tagColor", area.Value.TagColor.Length);
                }

                // Order is a signed sort key. Legacy route mods commonly use
                // negative values to place extension towns before base-game
                // areas, so validation must allow the full int range.
            }
        }

        private static void ValidateTrackLocation(ValidationResult result, string path, FuseTrackLocation location, Dictionary<string, FuseSegment> segments, HashSet<string> generatedSegmentIds)
        {
            if (location == null)
            {
                result.AddError(path, "Track location is required.", "fuse.track.location.required");
                return;
            }

            Required(result, $"{path}.segmentId", location.SegmentId);
            if (location.Normalized == null && location.Distance == null)
            {
                result.AddError(path, "Track location must set either normalized or distance.", "fuse.track.location.measure");
            }
            else if (location.Normalized != null && location.Distance != null)
            {
                result.AddError(path, "Track location must set normalized or distance, not both.", "fuse.track.location.measure.exclusive");
            }

            if (location.Normalized != null && (location.Normalized < 0f || location.Normalized > 1f))
            {
                result.AddError($"{path}.normalized", "Normalized location must be between 0 and 1.", "fuse.track.location.normalized", location.Normalized);
            }

            if (location.Distance != null && location.Distance.Value < 0f)
            {
                result.AddError($"{path}.distance", "Distance must be greater than or equal to 0.", "fuse.track.location.distance", location.Distance);
            }

            if (!string.IsNullOrWhiteSpace(location.End) &&
                NormalizeLocationEnd(location.End) == null)
            {
                result.AddError($"{path}.end", "Track location end must be A/B or Start/End.", "fuse.track.location.end", location.End);
            }

            if (!string.IsNullOrWhiteSpace(location.SegmentId) &&
                !segments.ContainsKey(location.SegmentId) &&
                !generatedSegmentIds.Contains(location.SegmentId))
            {
                result.AddWarning($"{path}.segmentId", "Segment is not defined in this FUSE document. It must exist in the base game graph at runtime.", "fuse.track.segment.external", location.SegmentId);
            }
        }

        private static void ValidateSameSegmentSpan(ValidationResult result, string path, FuseSpan span, Dictionary<string, FuseSegment> segments, IDictionary<string, FuseNode> nodes)
        {
            if (span?.Upper == null || span.Lower == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(span.Upper.SegmentId) ||
                string.IsNullOrWhiteSpace(span.Lower.SegmentId) ||
                !string.Equals(span.Upper.SegmentId, span.Lower.SegmentId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var upperEnd = NormalizeLocationEnd(span.Upper.End) ?? "A";
            var lowerEnd = NormalizeLocationEnd(span.Lower.End) ?? "A";
            if (string.Equals(upperEnd, lowerEnd, StringComparison.OrdinalIgnoreCase))
            {
                result.AddWarning(path, "Same-segment span endpoints face the same direction. Runtime will re-anchor this legacy-compatible span if the physical positions are valid.", "fuse.track.span.sameSegment.sameDirection", span.Upper.SegmentId);
            }

            FuseSegment segment;
            if (!segments.TryGetValue(span.Upper.SegmentId, out segment) || segment == null)
            {
                return;
            }

            var length = EstimateSegmentLength(segment, nodes);
            if (!length.HasValue || length.Value <= 0f)
            {
                return;
            }

            var upperDistance = GetLocationDistance(span.Upper, length.Value);
            var lowerDistance = GetLocationDistance(span.Lower, length.Value);
            if (!upperDistance.HasValue || !lowerDistance.HasValue)
            {
                return;
            }

            if (upperDistance.Value < 0f || upperDistance.Value > length.Value)
            {
                result.AddWarning($"{path}.upper.distance", "Upper track location is outside the estimated straight-line segment length. Runtime graph validation will use the actual curved segment length.", "fuse.track.span.upper.distance", upperDistance.Value);
            }

            if (lowerDistance.Value < 0f || lowerDistance.Value > length.Value)
            {
                result.AddWarning($"{path}.lower.distance", "Lower track location is outside the estimated straight-line segment length. Runtime graph validation will use the actual curved segment length.", "fuse.track.span.lower.distance", lowerDistance.Value);
            }

            // Runtime span apply has a compatibility repair for legacy AMM
            // same-segment crossed endpoints. Treat this as recoverable here
            // so preflight only reports span issues that need user action.
        }

        private static string NormalizeLocationEnd(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "A";
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "A":
                case "START":
                    return "A";
                case "B":
                case "END":
                    return "B";
                default:
                    return null;
            }
        }

        private static float? GetLocationDistance(FuseTrackLocation location, float segmentLength)
        {
            if (location == null)
            {
                return null;
            }

            var distance = location.Distance ?? ((location.Normalized ?? 0f) * segmentLength);
            distance += location.Offset;
            return distance;
        }

        private static float? EstimateSegmentLength(FuseSegment segment, IDictionary<string, FuseNode> nodes)
        {
            if (segment == null ||
                nodes == null ||
                string.IsNullOrWhiteSpace(segment.StartNodeId) ||
                string.IsNullOrWhiteSpace(segment.EndNodeId))
            {
                return null;
            }

            FuseNode start;
            FuseNode end;
            if (!nodes.TryGetValue(segment.StartNodeId, out start) ||
                !nodes.TryGetValue(segment.EndNodeId, out end) ||
                start == null ||
                end == null)
            {
                return null;
            }

            // Full graph curvature is only known at runtime. The straight-line
            // estimate catches obviously crossed same-segment spans in authored
            // JSON; TrackAPI performs the authoritative runtime check.
            return FuseVector3.Distance(start.Position, end.Position);
        }

        private static void ValidateTrackRemovalTargets(ValidationResult result, string path, IEnumerable<string> removals, IEnumerable<string> definitions)
        {
            if (removals == null)
            {
                return;
            }

            var definedIds = new HashSet<string>(definitions ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var id in removals)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddError($"{path}[{index}]", "Removal IDs must not be blank.", "fuse.track.removal.blank");
                }
                else if (!seen.Add(id))
                {
                    result.AddWarning($"{path}[{index}]", "Removal ID is listed more than once.", "fuse.track.removal.duplicate", id);
                }
                else if (definedIds.Contains(id))
                {
                    result.AddError($"{path}[{index}]", "A track object cannot be defined and removed in the same FUSE document.", "fuse.track.removal.conflict", id);
                }

                index++;
            }
        }

        private static void ValidateOperations(ValidationResult result, FuseOperationsDefinition operations)
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
                            result.AddError($"{path}.units", "Load units must be Pounds, Gallons, or Quantity.", "fuse.operations.loads.units", load.Value.Units);
                        }
                    }

                    if (load.Value.Density.HasValue && load.Value.Density.Value < 0f)
                    {
                        result.AddError($"{path}.density", "Load density must be greater than or equal to 0.", "fuse.operations.loads.density", load.Value.Density.Value);
                    }

                    if (load.Value.UnitWeightInPounds.HasValue && load.Value.UnitWeightInPounds.Value < 0f)
                    {
                        result.AddError($"{path}.unitWeightInPounds", "Load unitWeightInPounds must be greater than or equal to 0.", "fuse.operations.loads.unitWeightInPounds", load.Value.UnitWeightInPounds.Value);
                    }

                    ValidateCarTypeFilter(result, $"{path}.carTypeFilter", load.Value.CarTypeFilter);
                }
            }

            foreach (var industry in operations.Industries)
            {
                Required(result, $"operations.industries.{industry.Key}.name", industry.Value.Name);
                // Order is a signed sort key for preserving legacy source
                // ordering in the company locations list.

                foreach (var component in industry.Value.Components)
                {
                    var componentPath = $"operations.industries.{industry.Key}.components.{component.Key}";
                    if (component.Value == null)
                    {
                        result.AddError(componentPath, "Industry component definition is required.", "fuse.operations.component.required");
                        continue;
                    }

                    // Remove-sentinel: a component entry whose only purpose is to
                    // ask the apply path to delete the matching runtime sub-component
                    // (the legacy converter emits these for Strange-Customs
                    // "foo": null deletions). These entries intentionally carry no
                    // type/name and the other component fields are meaningless —
                    // skip the required-field and shape validation entirely.
                    if (component.Value.Remove)
                    {
                        continue;
                    }

                    if (!component.Value.Partial)
                    {
                        Required(result, $"{componentPath}.type", component.Value.Type);
                        Required(result, $"{componentPath}.name", component.Value.Name);
                    }

                    ValidateIndustryComponent(result, componentPath, component.Value, component.Value.Partial);
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
                    result.AddError($"operations.turntables.{turntable.Key}.radius", "Turntable radius must be greater than 0.", "fuse.turntable.radius", turntable.Value.Radius);
                }

                if (turntable.Value.Subdivisions < 4 || turntable.Value.Subdivisions > 32)
                {
                    result.AddError($"operations.turntables.{turntable.Key}.subdivisions", "Turntable subdivisions must be between 4 and 32.", "fuse.turntable.subdivisions", turntable.Value.Subdivisions);
                }

                if (turntable.Value.Roundhouse != null &&
                    turntable.Value.Roundhouse.Stalls > 0 &&
                    turntable.Value.Roundhouse.TrackLength <= 0f)
                {
                    result.AddError($"operations.turntables.{turntable.Key}.roundhouse.trackLength", "Roundhouse track length must be greater than 0.", "fuse.turntable.roundhouse.trackLength", turntable.Value.Roundhouse.TrackLength);
                }
            }
        }

        private static void ValidateWorld(ValidationResult result, FuseWorldDefinition world)
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

            ValidateSuppressionIds(result, "world.suppressBaseScenePaths", world.SuppressBaseScenePaths, "Scene suppression path is empty.", "fuse.world.suppression.scenePath.empty");
            ValidateSuppressionIds(result, "world.suppressBaseTrackGroups", world.SuppressBaseTrackGroups, "Track group suppression id is empty.", "fuse.world.suppression.trackGroup.empty");
            ValidateSuppressionIds(result, "world.suppressBaseAreas", world.SuppressBaseAreas, "Area suppression id is empty.", "fuse.world.suppression.area.empty");

            foreach (var scenery in world.Scenery)
            {
                if (string.IsNullOrWhiteSpace(scenery.Value?.AssetIdentifier) &&
                    string.IsNullOrWhiteSpace(scenery.Value?.Model))
                {
                    result.AddError(
                        $"world.scenery.{scenery.Key}.assetIdentifier",
                        "Scenery requires an AssetIdentifier (or legacy Model) to resolve a PrefabStore asset.",
                        "fuse.world.scenery.assetIdentifier.required");
                }

                ValidateNoBlank(result, $"world.scenery.{scenery.Key}.anchorSpanIds", scenery.Value?.AnchorSpanIds, "fuse.world.scenery.anchorSpan.empty");
            }

            var spawnPoints = world.SpawnPoints ?? Array.Empty<FuseSpawnPoint>();
            var seenSpawnPoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var spawnIndex = 0; spawnIndex < spawnPoints.Length; spawnIndex++)
            {
                var spawnPoint = spawnPoints[spawnIndex];
                var path = $"world.spawnPoints[{spawnIndex}]";
                if (spawnPoint == null)
                {
                    result.AddError(path, "Spawn point is required.", "fuse.world.spawnPoint.required");
                    continue;
                }

                Required(result, $"{path}.name", spawnPoint.Name);
                if (!string.IsNullOrWhiteSpace(spawnPoint.Name) && !seenSpawnPoints.Add(spawnPoint.Name.Trim()))
                {
                    result.AddError($"{path}.name", "Spawn point names must be unique within a package.", "fuse.world.spawnPoint.duplicate", spawnPoint.Name);
                }

                if (spawnPoint.Radius.HasValue && spawnPoint.Radius.Value <= 0f)
                {
                    result.AddError($"{path}.radius", "Spawn point radius must be greater than 0.", "fuse.world.spawnPoint.radius", spawnPoint.Radius);
                }
            }

            foreach (var spliney in world.Splineys)
            {
                Required(result, $"world.splineys.{spliney.Key}.type", spliney.Value.Type);
                if (spliney.Value.Points == null || spliney.Value.Points.Length < 2)
                {
                    result.AddError($"world.splineys.{spliney.Key}.points", "Spliney objects require at least two points.", "fuse.spliney.points");
                }
            }

            foreach (var telegraph in world.TelegraphPoles)
            {
                if (telegraph.Value.Points == null || telegraph.Value.Points.Length < 2)
                {
                    result.AddError($"world.telegraphPoles.{telegraph.Key}.points", "Telegraph pole sets require at least two points.", "fuse.telegraph.points");
                }
            }

            var telegraphMovements = world.TelegraphPoleMovements ?? Array.Empty<FuseTelegraphPoleMovement>();
            for (var movementIndex = 0; movementIndex < telegraphMovements.Length; movementIndex++)
            {
                var movement = telegraphMovements[movementIndex];
                var path = $"world.telegraphPoleMovements[{movementIndex}]";
                if (movement == null)
                {
                    result.AddError(path, "Telegraph pole movement is required.", "fuse.telegraphPoleMovement.required");
                    continue;
                }

                if (movement.PoleIndices == null || movement.PoleIndices.Length == 0)
                {
                    result.AddError($"{path}.poleIndices", "Telegraph pole movement requires at least one pole index.", "fuse.telegraphPoleMovement.poleIndices");
                    continue;
                }

                var seenPoleIndices = new HashSet<int>();
                for (var poleIndex = 0; poleIndex < movement.PoleIndices.Length; poleIndex++)
                {
                    var value = movement.PoleIndices[poleIndex];
                    if (value < 0)
                    {
                        result.AddError($"{path}.poleIndices[{poleIndex}]", "Telegraph pole index must be greater than or equal to 0.", "fuse.telegraphPoleMovement.poleIndex", value);
                    }
                    else if (!seenPoleIndices.Add(value))
                    {
                        result.AddWarning($"{path}.poleIndices[{poleIndex}]", "Telegraph pole index is listed more than once in the same movement.", "fuse.telegraphPoleMovement.duplicatePoleIndex", value);
                    }
                }

                if (movement.Offset == default(FuseVector3))
                {
                    result.AddWarning($"{path}.offset", "Telegraph pole movement offset is zero.", "fuse.telegraphPoleMovement.zeroOffset");
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
                            result.AddError($"{path}.radius", "Circle map masks require a positive radius.", "fuse.mapMask.circle.radius", mapMask.Value.Radius);
                        }
                        break;

                    case "rectangle":
                        if (!mapMask.Value.Size.HasValue || mapMask.Value.Size.Value.x <= 0f || mapMask.Value.Size.Value.z <= 0f)
                        {
                            result.AddError($"{path}.size", "Rectangle map masks require a positive size.", "fuse.mapMask.rectangle.size", mapMask.Value.Size);
                        }
                        break;

                    case "curve":
                        if (mapMask.Value.Points == null || mapMask.Value.Points.Length < 2)
                        {
                            result.AddError($"{path}.points", "Curve map masks require at least two points.", "fuse.mapMask.curve.points");
                        }
                        break;

                    default:
                        result.AddError($"{path}.type", "Map mask type must be circle, rectangle, or curve.", "fuse.mapMask.type", mapMask.Value.Type);
                        break;
                }
            }

            foreach (var mapTile in world.MapTiles)
            {
                if (mapTile.Value == null)
                {
                    result.AddError($"world.mapTiles.{mapTile.Key}", "Map tile source is required.", "fuse.mapTiles.required");
                    continue;
                }

                Required(result, $"world.mapTiles.{mapTile.Key}.directory", mapTile.Value.Directory);
                Required(result, $"world.mapTiles.{mapTile.Key}.sourceFolder", mapTile.Value.SourceFolder);
            }

            foreach (var sceneClone in world.SceneClones)
            {
                if (sceneClone.Value == null)
                {
                    result.AddError($"world.sceneClones.{sceneClone.Key}", "Scene clone definition is required.", "fuse.sceneClone.required");
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

            var definedIds = new HashSet<string>(definitions ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var id in removals)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddError($"{path}[{index}]", "Removal IDs must not be blank.", "fuse.world.removal.blank");
                }
                else if (!seen.Add(id))
                {
                    result.AddWarning($"{path}[{index}]", "Removal ID is listed more than once.", "fuse.world.removal.duplicate", id);
                }
                else if (definedIds.Contains(id))
                {
                    result.AddError($"{path}[{index}]", "A world object cannot be defined and removed in the same FUSE document.", "fuse.world.removal.conflict", id);
                }

                index++;
            }
        }

        private static void ValidateSuppressionIds(ValidationResult result, string path, IEnumerable<string> values, string message, string code)
        {
            if (values == null)
            {
                return;
            }

            var index = 0;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    result.AddWarning($"{path}[{index}]", message, code);
                }

                index++;
            }
        }

        private static void ValidateProgression(ValidationResult result, FuseProgressionRoot progression)
        {
            var rootSectionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootSections = progression.Sections ?? Array.Empty<FuseSection>();
            for (var index = 0; index < rootSections.Length; index++)
            {
                var section = rootSections[index];
                var path = $"progression.sections[{index}]";
                if (section == null)
                {
                    result.AddError(path, "Progression section is required.", "fuse.progression.section.required");
                    continue;
                }

                Required(result, $"{path}.id", section.Id);
                if (!string.IsNullOrWhiteSpace(section.Id) && !rootSectionIds.Add(section.Id))
                {
                    result.AddError($"{path}.id", "Root progression section IDs must be unique within a package.", "fuse.progression.section.duplicate", section.Id);
                }
            }

            foreach (var progressionEntry in progression.Progressions)
            {
                if (progressionEntry.Value == null)
                {
                    result.AddError($"progression.progressions.{progressionEntry.Key}", "Progression definition is required.", "fuse.progression.required");
                    continue;
                }

                foreach (var section in progressionEntry.Value.Sections)
                {
                    ValidateProgressionSection(
                        result,
                        $"progression.progressions.{progressionEntry.Key}.sections.{section.Key}",
                        section.Value,
                        requireId: false);
                }
            }

            foreach (var feature in progression.MapFeatures)
            {
                var path = $"progression.mapFeatures.{feature.Key}";
                if (feature.Value == null)
                {
                    result.AddError(path, "Map feature definition is required.", "fuse.progression.mapFeature.required");
                    continue;
                }

                Required(result, $"{path}.displayName", feature.Value.DisplayName);
                ValidateNoBlank(result, $"{path}.trackGroupsEnableOnUnlock", feature.Value.TrackGroupsEnableOnUnlock, "fuse.progression.mapFeature.trackGroup.empty");
                ValidateNoBlank(result, $"{path}.trackGroupsAvailableOnUnlock", feature.Value.TrackGroupsAvailableOnUnlock, "fuse.progression.mapFeature.trackGroup.empty");
                ValidateNoBlank(result, $"{path}.areasEnableOnUnlock", feature.Value.AreasEnableOnUnlock, "fuse.progression.mapFeature.area.empty");
                ValidateNoBlank(result, $"{path}.gameObjectsEnableOnUnlock", feature.Value.GameObjectsEnableOnUnlock, "fuse.progression.mapFeature.gameObject.empty");
                ValidateNoBlank(result, $"{path}.unlockIncludeIndustries", feature.Value.UnlockIncludeIndustries, "fuse.progression.mapFeature.industry.empty");
                ValidateNoBlank(result, $"{path}.unlockExcludeIndustries", feature.Value.UnlockExcludeIndustries, "fuse.progression.mapFeature.industry.empty");
                ValidateNoBlank(result, $"{path}.unlockIncludeIndustryComponents", feature.Value.UnlockIncludeIndustryComponents, "fuse.progression.mapFeature.industryComponent.empty");
            }
        }

        private static void ValidateAudio(ValidationResult result, FuseAudioRoot audio)
        {
            if (audio == null)
            {
                return;
            }

            foreach (var whistle in audio.Whistles ?? new Dictionary<string, FuseWhistleAudio>())
            {
                var path = $"audio.whistles.{whistle.Key}";
                if (whistle.Value == null)
                {
                    result.AddError(path, "Whistle audio definition is required.", "fuse.audio.whistle.required");
                    continue;
                }

                Required(result, $"{path}.name", whistle.Value.Name);
                Required(result, $"{path}.clip", whistle.Value.Clip);
            }

            foreach (var horn in audio.Horns ?? new Dictionary<string, FuseHornAudio>())
            {
                var path = $"audio.horns.{horn.Key}";
                if (horn.Value == null)
                {
                    result.AddError(path, "Horn audio definition is required.", "fuse.audio.horn.required");
                    continue;
                }

                Required(result, $"{path}.name", horn.Value.Name);
                if (horn.Value.Layers == null || horn.Value.Layers.Length == 0)
                {
                    result.AddError($"{path}.layers", "Horn audio requires at least one layer.", "fuse.audio.horn.layers");
                    continue;
                }

                for (var layerIndex = 0; layerIndex < horn.Value.Layers.Length; layerIndex++)
                {
                    var layer = horn.Value.Layers[layerIndex];
                    var layerPath = $"{path}.layers[{layerIndex}]";
                    if (layer == null)
                    {
                        result.AddError(layerPath, "Horn audio layer is required.", "fuse.audio.horn.layer.required");
                        continue;
                    }

                    Required(result, $"{layerPath}.file", layer.File);
                    if (layer.Keyframes == null || layer.Keyframes.Length == 0)
                    {
                        result.AddWarning($"{layerPath}.keyframes", "Horn layer has no keyframes; FUSE will use a constant volume curve.", "fuse.audio.horn.keyframes.empty");
                    }
                }
            }

            foreach (var bell in audio.Bells ?? new Dictionary<string, FuseBellAudio>())
            {
                var path = $"audio.bells.{bell.Key}";
                if (bell.Value == null)
                {
                    result.AddError(path, "Bell audio definition is required.", "fuse.audio.bell.required");
                    continue;
                }

                Required(result, $"{path}.name", bell.Value.Name);
                Required(result, $"{path}.file", bell.Value.File);
            }
        }

        private static void ValidateProgressionSection(ValidationResult result, string path, FuseSection section, bool requireId)
        {
            if (section == null)
            {
                result.AddError(path, "Progression section is required.", "fuse.progression.section.required");
                return;
            }

            if (requireId)
            {
                Required(result, $"{path}.id", section.Id);
            }

            Required(result, $"{path}.displayName", section.DisplayName);
            ValidateNoBlank(result, $"{path}.trackGroupsEnableOnUnlock", section.TrackGroupsEnableOnUnlock, "fuse.progression.section.trackGroup.empty");
            ValidateNoBlank(result, $"{path}.trackGroupsAvailableOnUnlock", section.TrackGroupsAvailableOnUnlock, "fuse.progression.section.trackGroup.empty");
            ValidateNoBlank(result, $"{path}.areasEnableOnUnlock", section.AreasEnableOnUnlock, "fuse.progression.section.area.empty");
            ValidateNoBlank(result, $"{path}.gameObjectsEnableOnUnlock", section.GameObjectsEnableOnUnlock, "fuse.progression.section.gameObject.empty");
            ValidateNoBlank(result, $"{path}.unlockIncludeIndustries", section.UnlockIncludeIndustries, "fuse.progression.section.industry.empty");
            ValidateNoBlank(result, $"{path}.unlockExcludeIndustries", section.UnlockExcludeIndustries, "fuse.progression.section.industry.empty");
            ValidateNoBlank(result, $"{path}.unlockIncludeIndustryComponents", section.UnlockIncludeIndustryComponents, "fuse.progression.section.industryComponent.empty");
            ValidateInterchangeTransfers(result, $"{path}.interchangeTransfers", section.InterchangeTransfers);

            var phases = section.DeliveryPhases;
            if (phases == null)
            {
                return;
            }

            for (var phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                var phase = phases[phaseIndex];
                var phasePath = $"{path}.deliveryPhases[{phaseIndex}]";
                if (phase == null)
                {
                    result.AddError(phasePath, "Delivery phase is required.", "fuse.progression.deliveryPhase.required");
                    continue;
                }

                if (phase.Cost < 0)
                {
                    result.AddWarning($"{phasePath}.cost", "Delivery phase cost is negative; FUSE keeps this legacy value because some route mods use negative costs as progression credits.", "fuse.progression.deliveryPhase.cost.legacyNegative", phase.Cost);
                }


                var deliveries = phase.Deliveries;
                if (deliveries != null && deliveries.Length > 0)
                {
                    var hasDestination = deliveries.Any(delivery => !string.IsNullOrWhiteSpace(delivery?.DestinationIndustryId));
                    if (string.IsNullOrWhiteSpace(phase.IndustryComponentId) && !hasDestination)
                    {
                        result.AddError($"{phasePath}.industryComponentId", "Delivery phases with deliveries require industryComponentId, or delivery destinationIndustryId for runtime inference.", "fuse.progression.deliveryPhase.industryComponentId");
                    }
                }

                if (deliveries == null)
                {
                    continue;
                }

                for (var deliveryIndex = 0; deliveryIndex < deliveries.Length; deliveryIndex++)
                {
                    var delivery = deliveries[deliveryIndex];
                    var deliveryPath = $"{phasePath}.deliveries[{deliveryIndex}]";
                    if (delivery == null)
                    {
                        result.AddError(deliveryPath, "Delivery is required.", "fuse.progression.delivery.required");
                        continue;
                    }

                    if (delivery.Count < 1)
                    {
                        result.AddError($"{deliveryPath}.count", "Delivery count must be greater than 0.", "fuse.progression.delivery.count", delivery.Count);
                    }

                    if (!string.IsNullOrWhiteSpace(delivery.Direction))
                    {
                        var direction = delivery.Direction.Trim().ToLowerInvariant();
                        if (direction != "loadtoindustry" &&
                            direction != "toindustry" &&
                            direction != "to" &&
                            direction != "import" &&
                            direction != "loadfromindustry" &&
                            direction != "fromindustry" &&
                            direction != "from" &&
                            direction != "export")
                        {
                            result.AddError($"{deliveryPath}.direction", "Delivery direction must be loadToIndustry or loadFromIndustry.", "fuse.progression.delivery.direction", delivery.Direction);
                        }
                    }

                    ValidateCarTypeFilter(result, $"{deliveryPath}.carTypeFilter", delivery.CarTypeFilter);
                }
            }
        }

        private static void ValidateNoBlank(ValidationResult result, string path, FuseStringPatch values, string code)
        {
            // Validate against the additions-only view — the patch dict's
            // false-valued removal entries are not "blank values that ended
            // up in the resulting list", they're explicit removals that
            // should be allowed.
            ValidateNoBlank(result, path, (IEnumerable<string>)values?.EffectiveAdditions, code);
        }

        private static void ValidateNoBlank(ValidationResult result, string path, IEnumerable<string> values, string code)
        {
            if (values == null)
            {
                return;
            }

            var index = 0;
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    result.AddWarning($"{path}[{index}]", "Value should not be blank.", code);
                }

                index++;
            }
        }

        private static void ValidateCarTypeFilter(ValidationResult result, string path, string filter)
        {
            // Empty and "*" both mean "any car type" at runtime, so only a
            // filter with real tokens needs checking. The game splits the
            // expression on ',' without trimming, so a token with surrounding
            // whitespace can never match a car type. That is still only a
            // warning, not an error: packages with padded tokens loaded and
            // ran under the legacy modding stack, so FUSE must load them
            // too — the warning plus its fix hint tells the author how to
            // repair the filter.
            if (string.IsNullOrWhiteSpace(filter) || filter == "*")
            {
                return;
            }

            var tokens = filter.Split(',');
            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (string.IsNullOrWhiteSpace(token))
                {
                    result.AddWarning(path, "Car type filter contains an empty entry (doubled, leading, or trailing comma); the game ignores it.", "fuse.operations.component.carTypeFilter.emptyToken", filter);
                }
                else if (token.Trim().Length != token.Length)
                {
                    result.AddWarning(path, $"Car type filter entry '{token}' has surrounding whitespace and can never match a car type; write the filter without spaces (e.g. \"FB,XM\").", "fuse.operations.component.carTypeFilter.malformed", filter);
                }
            }
        }

        private static void ValidateInterchangeTransfers(ValidationResult result, string path, IDictionary<string, string> transfers)
        {
            if (transfers == null)
            {
                return;
            }

            foreach (var transfer in transfers)
            {
                var sourcePath = $"{path}.{transfer.Key}";
                if (string.IsNullOrWhiteSpace(transfer.Key))
                {
                    result.AddError(path, "Interchange transfer source id must not be blank.", "fuse.progression.interchangeTransfer.source.empty");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(transfer.Value))
                {
                    continue;
                }

                if (string.Equals(transfer.Key.Trim(), transfer.Value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    result.AddWarning(sourcePath, "Interchange transfer source and target are the same; FUSE will create it, but it has no practical effect.", "fuse.progression.interchangeTransfer.sameTarget", transfer.Key);
                }
            }
        }

        private static void ValidateIndustryComponent(ValidationResult result, string path, FuseIndustryComponent component, bool partial = false)
        {
            var type = FuseIndustryComponentTypes.Normalize(component.Type);
            if (!string.IsNullOrWhiteSpace(type) && !FuseIndustryComponentTypes.IsKnown(type))
            {
                if (FuseIndustryComponentTypes.IsCustomTypeCandidate(type))
                {
                    result.AddWarning(
                        $"{path}.type",
                        $"Industry component type '{component.Type}' is not a built-in FUSE type; runtime will attempt to resolve '{type}' from loaded assemblies.",
                        "fuse.operations.component.type.custom",
                        type);
                }
                else
                {
                    result.AddError(
                        $"{path}.type",
                        $"Industry component type must be one of: {FuseIndustryComponentTypes.KnownTypesForMessage()}, or a fully-qualified custom component type.",
                        "fuse.operations.component.type",
                        component.Type);
                    return;
                }
            }

            if (!partial &&
                FuseIndustryComponentTypes.UsesTrackSpanIds(type) &&
                (component.TrackSpanIds == null || component.TrackSpanIds.Length == 0))
            {
                if (string.Equals(type, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase))
                {
                    // Legacy AMM accepted spanless passengerStop components
                    // ("virtual" timetable stops with no physical platform).
                }
                else
                {
                    result.AddError($"{path}.trackSpanIds", $"Industry component type '{type}' requires at least one track span.", "fuse.operations.component.trackSpanIds");
                }
            }

            if (!partial && FuseIndustryComponentTypes.UsesLoadId(type))
            {
                if (string.IsNullOrWhiteSpace(component.LoadId))
                {
                    if (!string.Equals(type, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddWarning($"{path}.loadId", $"Industry component type '{type}' usually needs a loadId to function.", "fuse.operations.component.loadId");
                    }
                }
            }

            if (component.StorageChangeRate.HasValue && component.StorageChangeRate.Value < 0f)
            {
                result.AddError($"{path}.storageChangeRate", "Storage change rate must be greater than or equal to 0.", "fuse.operations.component.storageChangeRate", component.StorageChangeRate.Value);
            }

            if (component.MaxStorage.HasValue && component.MaxStorage.Value < 0f)
            {
                result.AddError($"{path}.maxStorage", "Max storage must be greater than or equal to 0.", "fuse.operations.component.maxStorage", component.MaxStorage.Value);
            }

            if (component.CarTransferRate.HasValue && component.CarTransferRate.Value < 0f)
            {
                result.AddError($"{path}.carTransferRate", "Car transfer rate must be greater than or equal to 0.", "fuse.operations.component.carTransferRate", component.CarTransferRate.Value);
            }

            ValidateCarTypeFilter(result, $"{path}.carTypeFilter", component.CarTypeFilter);
            if (component.TeamProfiles != null)
            {
                foreach (var profile in component.TeamProfiles)
                {
                    ValidateCarTypeFilter(result, $"{path}.teamProfiles.{profile.Key}.carTypeFilter", profile.Value?.CarTypeFilter);
                }
            }

            if (!partial &&
                string.Equals(type, FuseIndustryComponentTypes.Formulaic, StringComparison.OrdinalIgnoreCase) &&
                (component.InputTermsPerDay == null || component.InputTermsPerDay.Count == 0) &&
                (component.OutputTermsPerDay == null || component.OutputTermsPerDay.Count == 0))
            {
                result.AddError($"{path}.inputTermsPerDay", "Formulaic components require inputTermsPerDay and/or outputTermsPerDay.", "fuse.operations.formulaic.terms");
            }

            if (!partial &&
                string.Equals(type, FuseIndustryComponentTypes.TeamTrack, StringComparison.OrdinalIgnoreCase) &&
                (component.TeamProfiles == null || component.TeamProfiles.Count == 0))
            {
                result.AddError($"{path}.teamProfiles", "Team track components require at least one team profile entry.", "fuse.operations.teamTrack.profile");
            }

            if (!partial && string.Equals(type, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase))
            {
                Required(result, $"{path}.passengerStopId", component.PassengerStopId);
                Required(result, $"{path}.timetableCode", component.TimetableCode);
            }

            if (!partial && string.Equals(type, FuseIndustryComponentTypes.TeleportLoading, StringComparison.OrdinalIgnoreCase))
            {
                if ((component.InputSpanIds == null || component.InputSpanIds.Length == 0) &&
                    (component.OutputSpanIds == null || component.OutputSpanIds.Length == 0))
                {
                    result.AddError($"{path}.inputSpanIds", "Teleport loading components require inputSpanIds and/or outputSpanIds.", "fuse.operations.teleportLoading.spans");
                }

                if (component.CarLoadPeriod.HasValue && component.CarLoadPeriod.Value < 0f)
                {
                    result.AddError($"{path}.carLoadPeriod", "Car load period must be greater than or equal to 0.", "fuse.operations.teleportLoading.carLoadPeriod", component.CarLoadPeriod.Value);
                }

                if (component.CarLengthFeet.HasValue && component.CarLengthFeet.Value < 0f)
                {
                    result.AddError($"{path}.carLengthFeet", "Car length feet must be greater than or equal to 0.", "fuse.operations.teleportLoading.carLengthFeet", component.CarLengthFeet.Value);
                }
            }
        }

        private static void Required(ValidationResult result, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError(field, "Value is required.", "fuse.required");
            }
        }

        private static HashSet<string> CollectGeneratedNodeIds(FuseOperationsDefinition operations)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    result.Add(FuseTurntableIds.GetPitNodeId(turntable.Key, index, definition));
                }

                if (definition.Roundhouse == null || definition.Roundhouse.Stalls <= 0)
                {
                    continue;
                }

                for (var index = 1; index <= definition.Roundhouse.Stalls; index++)
                {
                    result.Add(FuseTurntableIds.GetRoundhouseNodeId(turntable.Key, index, definition));
                }
            }

            return result;
        }

        private static HashSet<string> CollectGeneratedSegmentIds(FuseOperationsDefinition operations)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (operations?.Turntables == null)
            {
                return result;
            }

            foreach (var turntable in operations.Turntables.Where(entry => entry.Value?.Roundhouse != null && entry.Value.Roundhouse.Stalls > 0))
            {
                for (var index = 1; index <= turntable.Value.Roundhouse.Stalls; index++)
                {
                    result.Add(FuseTurntableIds.GetRoundhouseSegmentId(turntable.Key, index, turntable.Value));
                }
            }

            return result;
        }
    }
}
