using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Authoring.Data.Common;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Authoring.Migrations
{
    public static class FuseMigration
    {
        public const string CurrentVersion = "1.0";

        private static readonly Version CurrentSemanticVersion = new Version(1, 0);
        private static readonly object WarningLock = new object();
        private static readonly HashSet<string> WarningsEmitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static FuseModDefinition Migrate(FuseModDefinition definition)
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
                    $"FUSE package '{packageId}' declares future schemaVersion '{definition.SchemaVersion}'. " +
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

        public static void Normalize(FuseModDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            NormalizeSchemaVersion(definition, GetPackageId(definition), false);

            definition.Author = definition.Author ?? string.Empty;
            definition.Tags = (definition.Tags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            definition.CoordinateSpace = string.IsNullOrWhiteSpace(definition.CoordinateSpace) ? "world" : definition.CoordinateSpace;
            definition.ModVersion = string.IsNullOrWhiteSpace(definition.ModVersion) ? "1.0.0" : definition.ModVersion;
            NormalizeMixinto(definition.Mixinto);
            definition.Tracks = definition.Tracks ?? new FuseTrackDefinition();
            definition.Operations = definition.Operations ?? new FuseOperationsDefinition();
            definition.World = definition.World ?? new FuseWorldDefinition();
            definition.Audio = definition.Audio ?? new FuseAudioRoot();
            definition.Progression = definition.Progression ?? new FuseProgressionRoot();
            definition.Settings = definition.Settings ?? new Dictionary<string, FuseModSettingDefinition>();
            definition.FeatureRules = definition.FeatureRules ?? new Dictionary<string, FuseFeatureRule>();
            definition.Extensions = definition.Extensions ?? new Dictionary<string, object>();

            foreach (var setting in definition.Settings.Values.Where(setting => setting != null))
            {
                setting.Type = string.IsNullOrWhiteSpace(setting.Type) ? "text" : setting.Type.Trim();
                setting.Scope = string.IsNullOrWhiteSpace(setting.Scope) ? "user" : setting.Scope.Trim();
                setting.Values = setting.Values ?? Array.Empty<string>();
            }
            NormalizeFeatureRules(definition.FeatureRules);

            definition.Tracks.Nodes = definition.Tracks.Nodes ?? new Dictionary<string, FuseNode>();
            definition.Tracks.Segments = definition.Tracks.Segments ?? new Dictionary<string, FuseSegment>();
            definition.Tracks.Spans = definition.Tracks.Spans ?? new Dictionary<string, FuseSpan>();
            definition.Tracks.Areas = definition.Tracks.Areas ?? new Dictionary<string, FuseArea>();
            definition.Tracks.Removals = definition.Tracks.Removals ?? new FuseTrackRemovals();
            definition.Tracks.Removals.Nodes = definition.Tracks.Removals.Nodes ?? Array.Empty<string>();
            definition.Tracks.Removals.Segments = definition.Tracks.Removals.Segments ?? Array.Empty<string>();
            definition.Tracks.Removals.Spans = definition.Tracks.Removals.Spans ?? Array.Empty<string>();

            definition.Operations.Loads = definition.Operations.Loads ?? new Dictionary<string, FuseLoad>();
            definition.Operations.Industries = definition.Operations.Industries ?? new Dictionary<string, FuseIndustry>();
            definition.Operations.Loaders = definition.Operations.Loaders ?? new Dictionary<string, FuseLoader>();
            definition.Operations.Turntables = definition.Operations.Turntables ?? new Dictionary<string, FuseTurntable>();
            definition.Operations.Stations = definition.Operations.Stations ?? new Dictionary<string, FuseStation>();

            definition.World.Scenery = definition.World.Scenery ?? new Dictionary<string, FuseScenery>();
            definition.World.SpawnPoints = definition.World.SpawnPoints ?? Array.Empty<FuseSpawnPoint>();
            definition.World.Splineys = definition.World.Splineys ?? new Dictionary<string, FuseSpliney>();
            definition.World.WaterSurfaces = definition.World.WaterSurfaces ?? new Dictionary<string, FuseWaterSurface>();
            definition.World.TelegraphPoles = definition.World.TelegraphPoles ?? new Dictionary<string, FuseTelegraphPoles>();
            definition.World.TelegraphPoleMovements = definition.World.TelegraphPoleMovements ?? Array.Empty<FuseTelegraphPoleMovement>();
            definition.World.MapLabels = definition.World.MapLabels ?? new Dictionary<string, FuseMapLabel>();
            definition.World.MapMasks = definition.World.MapMasks ?? new Dictionary<string, FuseMapMask>();
            definition.World.MapTiles = definition.World.MapTiles ?? new Dictionary<string, FuseMapTileSource>();
            definition.World.SceneClones = definition.World.SceneClones ?? new Dictionary<string, FuseSceneClone>();
            definition.World.SuppressBaseScenePaths = MergeAliasArray(definition.World.SuppressBaseScenePaths, definition.World.SuppressScenePaths);
            definition.World.SuppressBaseTrackGroups = MergeAliasArray(definition.World.SuppressBaseTrackGroups, definition.World.SuppressGroups);
            definition.World.SuppressBaseAreas = MergeAliasArray(definition.World.SuppressBaseAreas, definition.World.SuppressAreas);
            definition.World.SuppressScenePaths = null;
            definition.World.SuppressGroups = null;
            definition.World.SuppressAreas = null;
            definition.World.Removals = definition.World.Removals ?? new FuseWorldRemovals();
            definition.World.Removals.Scenery = definition.World.Removals.Scenery ?? Array.Empty<string>();
            definition.World.Removals.Splineys = definition.World.Removals.Splineys ?? Array.Empty<string>();
            definition.World.Removals.WaterSurfaces = definition.World.Removals.WaterSurfaces ?? Array.Empty<string>();
            definition.World.Removals.TelegraphPoles = definition.World.Removals.TelegraphPoles ?? Array.Empty<string>();
            definition.World.Removals.MapLabels = definition.World.Removals.MapLabels ?? Array.Empty<string>();
            definition.World.Removals.MapMasks = definition.World.Removals.MapMasks ?? Array.Empty<string>();
            definition.World.Removals.SceneClones = definition.World.Removals.SceneClones ?? Array.Empty<string>();

            definition.Audio.Whistles = definition.Audio.Whistles ?? new Dictionary<string, FuseWhistleAudio>();
            definition.Audio.Horns = definition.Audio.Horns ?? new Dictionary<string, FuseHornAudio>();
            definition.Audio.Bells = definition.Audio.Bells ?? new Dictionary<string, FuseBellAudio>();
            foreach (var horn in definition.Audio.Horns.Values)
            {
                if (horn == null)
                {
                    continue;
                }

                horn.Layers = horn.Layers ?? Array.Empty<FuseHornLayer>();
                foreach (var layer in horn.Layers)
                {
                    if (layer == null)
                    {
                        continue;
                    }

                    layer.Keyframes = layer.Keyframes ?? Array.Empty<FuseAudioKeyframe>();
                }
            }

            foreach (var bell in definition.Audio.Bells.Values)
            {
                if (bell == null)
                {
                    continue;
                }

                bell.IndexTimes = bell.IndexTimes ?? Array.Empty<float>();
            }

            foreach (var movement in definition.World.TelegraphPoleMovements)
            {
                if (movement == null)
                {
                    continue;
                }

                movement.PoleIndices = movement.PoleIndices ?? Array.Empty<int>();
            }

            foreach (var scenery in definition.World.Scenery.Values)
            {
                if (scenery == null)
                {
                    continue;
                }

                scenery.AnchorSpanIds = scenery.AnchorSpanIds ?? Array.Empty<string>();
                scenery.Scale = scenery.Scale == default ? Vector3.one : scenery.Scale;
            }

            definition.Progression.Progressions = definition.Progression.Progressions ?? new Dictionary<string, FuseProgression>();
            definition.Progression.Sections = definition.Progression.Sections ?? Array.Empty<FuseSection>();
            definition.Progression.MapFeatures = definition.Progression.MapFeatures ?? new Dictionary<string, FuseMapFeature>();
            NormalizeProgression(definition);

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

                    component.Type = FuseIndustryComponentTypes.Normalize(component.Type);
                    component.TrackSpanIds = component.TrackSpanIds ?? Array.Empty<string>();
                    component.InputSpanIds = component.InputSpanIds ?? Array.Empty<string>();
                    component.OutputSpanIds = component.OutputSpanIds ?? Array.Empty<string>();
                    component.InputTermsPerDay = component.InputTermsPerDay ?? new Dictionary<string, float>();
                    component.OutputTermsPerDay = component.OutputTermsPerDay ?? new Dictionary<string, float>();
                    component.TeamProfiles = component.TeamProfiles ?? new Dictionary<string, FuseTeamTrackEntry>();
                    component.NeighborIds = component.NeighborIds ?? Array.Empty<string>();
                    component.BranchDefinitions = component.BranchDefinitions ?? Array.Empty<FusePassengerBranch>();
                }
            }
        }

        private static void NormalizeProgression(FuseModDefinition definition)
        {
            var root = definition.Progression;
            var defaultProgressionId = string.IsNullOrWhiteSpace(root.ProgressionId)
                ? GetPackageId(definition)
                : root.ProgressionId.Trim();
            root.ProgressionId = defaultProgressionId;

            foreach (var rootSection in root.Sections ?? Array.Empty<FuseSection>())
            {
                NormalizeSection(rootSection);
                if (rootSection == null || string.IsNullOrWhiteSpace(rootSection.Id))
                {
                    continue;
                }

                var progressionId = string.IsNullOrWhiteSpace(rootSection.ProgressionId)
                    ? defaultProgressionId
                    : rootSection.ProgressionId.Trim();
                rootSection.ProgressionId = progressionId;

                FuseProgression progression;
                if (!root.Progressions.TryGetValue(progressionId, out progression) || progression == null)
                {
                    progression = new FuseProgression();
                    root.Progressions[progressionId] = progression;
                }

                progression.Sections = progression.Sections ?? new Dictionary<string, FuseSection>();
                progression.Sections[rootSection.Id] = rootSection;
            }

            foreach (var progressionEntry in root.Progressions)
            {
                if (progressionEntry.Value == null)
                {
                    continue;
                }

                progressionEntry.Value.Sections = progressionEntry.Value.Sections ?? new Dictionary<string, FuseSection>();
                foreach (var sectionEntry in progressionEntry.Value.Sections)
                {
                    var section = sectionEntry.Value;
                    if (section == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(section.Id))
                    {
                        section.Id = sectionEntry.Key;
                    }

                    if (string.IsNullOrWhiteSpace(section.ProgressionId))
                    {
                        section.ProgressionId = progressionEntry.Key;
                    }

                    NormalizeSection(section);
                }
            }

            foreach (var feature in root.MapFeatures.Values)
            {
                if (feature == null)
                {
                    continue;
                }

                // Intentionally do NOT coerce null patches to "empty set"
                // here. Under the new FuseStringPatch contract, null means
                // "field omitted from the authored JSON → keep the live
                // runtime value untouched at apply time," while an empty
                // set ([]) means "explicit replace with empty." The
                // previous migration normalization that mapped null to
                // Array.Empty<string>() collapsed both shapes into the
                // latter — silently destroying authorial intent for mod
                // patches that only wanted to change one or two fields.
                // Downstream consumers (ApplyMapFeatureDefinition,
                // validation, impact lookup) are already null-safe.
            }
        }

        private static void NormalizeMixinto(FuseMixintoDefinition mixinto)
        {
            if (mixinto == null)
            {
                return;
            }

            mixinto.Target = string.IsNullOrWhiteSpace(mixinto.Target) ? null : mixinto.Target.Trim();
            mixinto.SourceFile = string.IsNullOrWhiteSpace(mixinto.SourceFile) ? null : mixinto.SourceFile.Trim();
            mixinto.Requires = mixinto.Requires ?? Array.Empty<FuseModRequirement>();
            mixinto.ConflictsWith = mixinto.ConflictsWith ?? Array.Empty<FuseModRequirement>();
            NormalizeModReferences(mixinto.Requires);
            NormalizeModReferences(mixinto.ConflictsWith);
        }

        private static void NormalizeFeatureRules(IDictionary<string, FuseFeatureRule> rules)
        {
            if (rules == null)
                return;
            foreach (var rule in rules.Values.Where(rule => rule != null))
            {
                rule.Operator = string.IsNullOrWhiteSpace(rule.Operator) ? "equals" : rule.Operator.Trim();
                rule.Targets = rule.Targets ?? new FuseFeatureTargets();
                var targets = rule.Targets;
                targets.TrackNodes = targets.TrackNodes ?? Array.Empty<string>();
                targets.TrackSegments = targets.TrackSegments ?? Array.Empty<string>();
                targets.TrackSpans = targets.TrackSpans ?? Array.Empty<string>();
                targets.TrackAreas = targets.TrackAreas ?? Array.Empty<string>();
                targets.Loads = targets.Loads ?? Array.Empty<string>();
                targets.Industries = targets.Industries ?? Array.Empty<string>();
                targets.IndustryComponents = targets.IndustryComponents ?? Array.Empty<string>();
                targets.Loaders = targets.Loaders ?? Array.Empty<string>();
                targets.Turntables = targets.Turntables ?? Array.Empty<string>();
                targets.Stations = targets.Stations ?? Array.Empty<string>();
                targets.Scenery = targets.Scenery ?? Array.Empty<string>();
                targets.Splineys = targets.Splineys ?? Array.Empty<string>();
                targets.WaterSurfaces = targets.WaterSurfaces ?? Array.Empty<string>();
                targets.TelegraphPoles = targets.TelegraphPoles ?? Array.Empty<string>();
                targets.MapLabels = targets.MapLabels ?? Array.Empty<string>();
                targets.MapMasks = targets.MapMasks ?? Array.Empty<string>();
                targets.MapTiles = targets.MapTiles ?? Array.Empty<string>();
                targets.SceneClones = targets.SceneClones ?? Array.Empty<string>();
                targets.Progressions = targets.Progressions ?? Array.Empty<string>();
                targets.MapFeatures = targets.MapFeatures ?? Array.Empty<string>();
                targets.Whistles = targets.Whistles ?? Array.Empty<string>();
                targets.Horns = targets.Horns ?? Array.Empty<string>();
                targets.Bells = targets.Bells ?? Array.Empty<string>();
            }
        }

        private static void NormalizeModReferences(FuseModRequirement[] references)
        {
            foreach (var requirement in references ?? Array.Empty<FuseModRequirement>())
            {
                if (requirement == null)
                {
                    continue;
                }

                requirement.Id = string.IsNullOrWhiteSpace(requirement.Id) ? null : requirement.Id.Trim();
                requirement.NotBefore = string.IsNullOrWhiteSpace(requirement.NotBefore) ? null : requirement.NotBefore.Trim();
                requirement.NotAfter = string.IsNullOrWhiteSpace(requirement.NotAfter) ? null : requirement.NotAfter.Trim();
            }
        }

        private static void NormalizeSection(FuseSection section)
        {
            if (section == null)
            {
                return;
            }

            section.PrerequisiteSectionIds = MergeAliasArray(section.PrerequisiteSectionIds, section.PrerequisiteSections);
            section.PrerequisiteSections = null;
            // Patch fields intentionally left null when omitted — see
            // matching note in NormalizeMapFeature above.
            section.InterchangeTransfers = NormalizeInterchangeTransfers(section.InterchangeTransfers);
            section.DeliveryPhases = section.DeliveryPhases ?? Array.Empty<FuseDeliveryPhase>();

            foreach (var phase in section.DeliveryPhases)
            {
                if (phase == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(phase.IndustryComponentId) &&
                    !string.IsNullOrWhiteSpace(phase.IndustryComponent))
                {
                    phase.IndustryComponentId = phase.IndustryComponent.Trim();
                }

                phase.Deliveries = phase.Deliveries ?? Array.Empty<FuseDelivery>();
                foreach (var delivery in phase.Deliveries)
                {
                    if (delivery == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(delivery.LoadId) &&
                        !string.IsNullOrWhiteSpace(delivery.Load))
                    {
                        delivery.LoadId = delivery.Load.Trim();
                    }

                    delivery.Direction = NormalizeDeliveryDirection(delivery.Direction);
                }
            }
        }

        private static string NormalizeDeliveryDirection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "0":
                case "loadtoindustry":
                case "toindustry":
                case "to":
                case "import":
                    return "loadToIndustry";
                case "1":
                case "loadfromindustry":
                case "fromindustry":
                case "from":
                case "export":
                    return "loadFromIndustry";
                default:
                    return value.Trim();
            }
        }

        private static Dictionary<string, string> NormalizeInterchangeTransfers(Dictionary<string, string> transfers)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (transfers == null)
            {
                return result;
            }

            foreach (var transfer in transfers)
            {
                var sourceId = string.IsNullOrWhiteSpace(transfer.Key) ? null : transfer.Key.Trim();
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    continue;
                }

                result[sourceId] = string.IsNullOrWhiteSpace(transfer.Value) ? null : transfer.Value.Trim();
            }

            return result;
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

        private static void RunVersionByVersionMigrations(FuseModDefinition definition, Version sourceVersion, string packageId)
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
                        $"FUSE package '{packageId}' declares older unknown schemaVersion '{FormatVersion(version)}'. " +
                        $"No exact migration exists; applying best-effort normalization as '{CurrentVersion}'.");
                    definition.SchemaVersion = CurrentVersion;
                    return;
                }

                version = nextVersion;
            }

            definition.SchemaVersion = CurrentVersion;
        }

        private static bool TryMigrateOneVersion(FuseModDefinition definition, Version version, string packageId, out Version nextVersion)
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

        private static void ApplyCompatibilityMigrations(FuseModDefinition definition, string packageId)
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
                    $"FUSE package '{packageId}' uses deprecated world.scenery.*.model as an asset identifier. " +
                    "FUSE migrated it to assetIdentifier in memory; keep model only as a temporary compatibility field.");
            }
        }

        private static Version NormalizeSchemaVersion(FuseModDefinition definition, string packageId, bool logWarnings)
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
                    $"FUSE package '{packageId}' has an invalid schemaVersion. Defaulting to '{CurrentVersion}' for best-effort load.");
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

        private static string GetPackageId(FuseModDefinition definition)
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

            FuseLog.Warning(message);
        }

        private static string[] MergeAliasArray(string[] preferred, string[] alias)
        {
            return (preferred ?? Array.Empty<string>())
                .Concat(alias ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Patch-aware alias merge: when a section / map-feature ships the
        /// same data under two property names (e.g. legacy
        /// <c>PrerequisiteSections</c> alongside the canonical
        /// <c>PrerequisiteSectionIds</c>) and exactly one of them is
        /// populated, surface that one. If both are populated, the explicit
        /// preferred wins — we don't attempt a structural merge across two
        /// patches because that would conflate "this is the value" with
        /// "this is a per-id adjustment to whatever the value was."
        /// </summary>
        private static FuseStringPatch MergeAliasArray(FuseStringPatch preferred, FuseStringPatch alias)
        {
            if (preferred != null && preferred.HasValue)
            {
                return preferred;
            }
            return alias;
        }

        private static void NormalizeTrackLocation(FuseTrackLocation location)
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
