using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Progression;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class ProgressionAPI
    {

        private static void ApplyMapFeatureDefinition(MapFeature feature, FuseMapFeature definition)
        {
            // PATCH SEMANTICS: this method is invoked both when a mod creates a
            // brand-new MapFeature AND when a mod's progression definition patches
            // a feature that already exists in the scene (a base-game feature the
            // mod selectively overrides). The contract differs by JSON shape per
            // field — see <see cref="FuseStringPatch"/> docs:
            //
            //   * Field omitted from JSON  -> definition.X is null -> NO CHANGE.
            //                                 Runtime keeps its existing value
            //                                 (e.g. base-game alarka MapFeature's
            //                                 areasEnableOnUnlock pointing at the
            //                                 actual Alarka area, even when the
            //                                 patch only wants to change the
            //                                 displayName).
            //
            //   * Field present as JSON array, e.g. "tracks": ["a","b"]
            //                                -> definition.X.Set is non-null
            //                                -> REPLACE existing with [a, b].
            //
            //   * Field present as JSON object, e.g. "tracks": {"a": true, "b": false}
            //                                -> definition.X.Patch is non-null
            //                                -> per-id MERGE on top of existing.
            //                                   "a" added if absent, "b" removed
            //                                   if present, anything else kept.
            //
            // The same field on the wire-data side carries both intents; the
            // FuseStringPatch container preserves the distinction so we can apply
            // the right semantics here without losing information the way the
            // earlier converter did.
            if (!string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                feature.displayName = definition.DisplayName;
            }
            else if (string.IsNullOrWhiteSpace(feature.displayName))
            {
                feature.displayName = feature.identifier;
            }
            if (definition.Description != null)
            {
                feature.description = definition.Description;
            }
            feature.defaultEnableInSandbox = definition.InitiallyEnabled;

            ApplyTrackGroupPatch(definition.TrackGroupsEnableOnUnlock, definition.GroupIds,
                ref feature.trackGroupsEnableOnUnlock);
            ApplyTrackGroupPatch(definition.TrackGroupsAvailableOnUnlock, definition.GroupIds,
                ref feature.trackGroupsAvailableOnUnlock);

            if (definition.PrerequisiteFeatureIds != null)
            {
                var existingIds = (feature.prerequisites ?? Array.Empty<MapFeature>())
                    .Where(prereq => prereq != null)
                    .Select(prereq => prereq.identifier);
                feature.prerequisites = ResolveMapFeatures(definition.PrerequisiteFeatureIds.ApplyTo(existingIds));
            }
            if (definition.GameObjectsEnableOnUnlock != null)
            {
                var existingPaths = (feature.gameObjectsEnableOnUnlock ?? Array.Empty<GameObject>())
                    .Where(go => go != null)
                    .Select(GetGameObjectPath);
                feature.gameObjectsEnableOnUnlock = ResolveGameObjects(definition.GameObjectsEnableOnUnlock.ApplyTo(existingPaths));
            }
            if (definition.AreasEnableOnUnlock != null)
            {
                var existingIds = (feature.areasEnableOnUnlock ?? Array.Empty<Area>())
                    .Where(area => area != null)
                    .Select(area => area.identifier);
                feature.areasEnableOnUnlock = ResolveAreas(definition.AreasEnableOnUnlock.ApplyTo(existingIds));
            }
            if (definition.UnlockExcludeIndustries != null)
            {
                var existingIds = (feature.unlockExcludeIndustries ?? Array.Empty<Industry>())
                    .Where(industry => industry != null)
                    .Select(industry => industry.identifier);
                feature.unlockExcludeIndustries = ResolveIndustries(definition.UnlockExcludeIndustries.ApplyTo(existingIds));
            }
            if (definition.UnlockIncludeIndustries != null)
            {
                var existingIds = (feature.unlockIncludeIndustries ?? Array.Empty<Industry>())
                    .Where(industry => industry != null)
                    .Select(industry => industry.identifier);
                feature.unlockIncludeIndustries = ResolveIndustries(definition.UnlockIncludeIndustries.ApplyTo(existingIds));
            }
            if (definition.UnlockIncludeIndustryComponents != null)
            {
                var existingIds = (feature.unlockIncludeIndustryComponents ?? Array.Empty<IndustryComponent>())
                    .Where(component => component != null)
                    .Select(SafeIndustryComponentId)
                    .Where(id => !string.IsNullOrWhiteSpace(id));
                feature.unlockIncludeIndustryComponents = ResolveIndustryComponents(
                    definition.UnlockIncludeIndustryComponents.ApplyTo(existingIds));
            }
            SanitizeMapFeature(feature);
            // Capture the resolved gate identifiers so the progression refresh can
            // re-bind them to live instances if an industry is later Remove+Add'd
            // (otherwise the unlock toggles ProgressionDisabled on a destroyed
            // reference and the live industry stays gated — e.g. a progression-gated
            // interchange that shows a panel but is excluded from EnabledInterchanges).
            RememberMapFeatureReferenceIds(feature);

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    "FUSE progression map feature applied " +
                    $"id='{feature.identifier}' display='{feature.displayName}' defaultSandbox={feature.defaultEnableInSandbox} " +
                    $"prereqIds=[{FormatPatchInputs(definition.PrerequisiteFeatureIds)}] " +
                    $"prereqResolvedCount={feature.prerequisites?.Length ?? 0} " +
                    $"tracksEnable=[{FormatIdList(feature.trackGroupsEnableOnUnlock)}] " +
                    $"tracksAvail=[{FormatIdList(feature.trackGroupsAvailableOnUnlock)}] " +
                    $"areas=[{FormatComponentIds(feature.areasEnableOnUnlock)}] " +
                    $"industriesInclude=[{FormatComponentIds(feature.unlockIncludeIndustries)}] " +
                    $"industriesExclude=[{FormatComponentIds(feature.unlockExcludeIndustries)}] " +
                    $"gameObjects={feature.gameObjectsEnableOnUnlock?.Length ?? 0}.");
            }
        }

        /// <summary>
        /// Applies a <see cref="FuseStringPatch"/> against the supplied track-group
        /// array (a raw string[] field on the live MapFeature). The legacy
        /// <c>GroupIds</c> fallback feeds the resolution when the explicit track
        /// group field is absent so older packages that pre-date the split into
        /// enable/available stay loadable.
        /// </summary>
        private static void ApplyTrackGroupPatch(FuseStringPatch explicitPatch, FuseStringPatch fallbackPatch, ref string[] target)
        {
            var chosen = explicitPatch != null && explicitPatch.HasValue ? explicitPatch : fallbackPatch;
            if (chosen == null || !chosen.HasValue)
            {
                return;
            }
            target = chosen.ApplyTo(target ?? Array.Empty<string>());
        }

        private static void ApplyProgressionDefinition(Progression progression, FuseProgression definition, string packageId)
        {
            if (progression.mapFeatureManager == null)
            {
                progression.mapFeatureManager = MapFeatureManager.Shared;
            }

            var sectionDefinitions = definition.Sections ?? new Dictionary<string, FuseSection>();
            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null || section.transform.parent != progression.transform)
                {
                    var gameObject = new GameObject(sectionDefinition.Key);
                    gameObject.transform.SetParent(progression.transform, false);
                    section = gameObject.AddComponent<Section>();
                    section.identifier = sectionDefinition.Key;
                }

                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            foreach (var sectionDefinition in sectionDefinitions)
            {
                var section = GetSection(sectionDefinition.Key);
                if (section == null)
                {
                    throw new InvalidOperationException($"Progression section '{sectionDefinition.Key}' could not be created.");
                }

                ApplySectionDefinition(section, sectionDefinition.Value, packageId);
                FuseSectionRuntimeIndex.Instance.Set(section.identifier, section);
            }

            ProgressionSectionsField?.SetValue(progression, progression.GetComponentsInChildren<Section>());
        }

        private static void ApplySectionDefinition(Section section, FuseSection definition, string packageId)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            section.displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName;
            section.description = definition.Description ?? string.Empty;
            var sectionUnlockFeature = EnsureSectionUnlockFeature(section, definition);

            // Section list fields use the same patch semantics as
            // MapFeature fields (see FuseStringPatch docs). If the JSON
            // is an array, replace; if it's an object dict, merge per-id;
            // if it's omitted, keep the runtime value untouched.
            var prereqPatch = definition.PrerequisiteSectionIds ?? definition.PrerequisiteSections;
            if (prereqPatch != null && prereqPatch.HasValue)
            {
                var existingIds = (section.prerequisiteSections ?? Array.Empty<Section>())
                    .Where(s => s != null)
                    .Select(s => s.identifier);
                section.prerequisiteSections = ResolveSections(prereqPatch.ApplyTo(existingIds));
            }

            if (definition.EnableFeaturesOnUnlock != null && definition.EnableFeaturesOnUnlock.HasValue)
            {
                var existingIds = (section.enableFeaturesOnUnlock ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.enableFeaturesOnUnlock = ResolveMapFeatures(definition.EnableFeaturesOnUnlock.ApplyTo(existingIds));
            }

            if (definition.EnableFeaturesOnAvailable != null && definition.EnableFeaturesOnAvailable.HasValue)
            {
                var existingIds = (section.enableFeaturesOnAvailable ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.enableFeaturesOnAvailable = ResolveMapFeatures(definition.EnableFeaturesOnAvailable.ApplyTo(existingIds));
            }

            if (definition.DisableFeaturesOnUnlock != null && definition.DisableFeaturesOnUnlock.HasValue)
            {
                var existingIds = (section.disableFeaturesOnUnlock ?? Array.Empty<MapFeature>())
                    .Where(f => f != null)
                    .Select(f => f.identifier);
                section.disableFeaturesOnUnlock = ResolveMapFeatures(definition.DisableFeaturesOnUnlock.ApplyTo(existingIds));
            }

            section.deliveryPhases = (definition.DeliveryPhases ?? Array.Empty<FuseDeliveryPhase>()).Select(CreateDeliveryPhase).ToArray();
            ApplyInterchangeTransfers(section, definition.InterchangeTransfers, packageId);

            // Null-safety: a freshly-created Section MonoBehaviour starts with
            // every array field null. If the mod's patch leaves a field as
            // "no change" (definition.X null OR HasValue false), our
            // conditional assignment above leaves the runtime field null
            // too. The game's Progression.PrerequisitesMet calls
            // section.prerequisiteSections.All(...) without a null check, so
            // a null array crashes Configure with ArgumentNullException.
            // Same exposure for every other Section[] / MapFeature[] field
            // the game iterates. Default them to empty arrays so the game's
            // existing null-naive .All / foreach calls survive.
            section.prerequisiteSections = section.prerequisiteSections ?? Array.Empty<Section>();
            section.enableFeaturesOnUnlock = section.enableFeaturesOnUnlock ?? Array.Empty<MapFeature>();
            section.enableFeaturesOnAvailable = section.enableFeaturesOnAvailable ?? Array.Empty<MapFeature>();
            section.disableFeaturesOnUnlock = section.disableFeaturesOnUnlock ?? Array.Empty<MapFeature>();

            if (FuseSettings.VerboseApplyReportDetails)
            {
                FuseLog.Info(
                    "FUSE progression section applied " +
                    $"id='{section.identifier}' display='{section.displayName}' package='{packageId ?? string.Empty}' " +
                    $"prereqSectionIds=[{FormatPatchInputs(prereqPatch)}] " +
                    $"prereqSectionsResolvedCount={section.prerequisiteSections?.Length ?? 0} " +
                    $"prereqSectionsResolved=[{FormatSectionIds(section.prerequisiteSections)}] " +
                    $"enableFeaturesOnUnlock=[{FormatFeatureIds(section.enableFeaturesOnUnlock)}] " +
                    $"enableFeaturesOnAvailable=[{FormatFeatureIds(section.enableFeaturesOnAvailable)}] " +
                    $"disableFeaturesOnUnlock=[{FormatFeatureIds(section.disableFeaturesOnUnlock)}] " +
                    $"deliveryPhases={section.deliveryPhases?.Length ?? 0} " +
                    $"hasSectionUnlockFeature={(sectionUnlockFeature != null)}.");
            }
        }

        private static void ApplyInterchangeTransfers(Section section, Dictionary<string, string> transfers, string packageId)
        {
            var preserved = (section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
                .Where(transfer => transfer != null && !IsFuseInterchangeTransfer(transfer))
                .ToList();

            foreach (var transfer in section.GetComponentsInChildren<InterchangeTransfer>(true) ?? Array.Empty<InterchangeTransfer>())
            {
                if (transfer == null || !IsFuseInterchangeTransfer(transfer))
                {
                    continue;
                }

                UnityEngine.Object.Destroy(transfer.gameObject);
            }

            var created = new List<InterchangeTransfer>();
            if (transfers != null && transfers.Count > 0)
            {
                foreach (var transfer in transfers)
                {
                    if (string.IsNullOrWhiteSpace(transfer.Key))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank source id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank source id'.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(transfer.Value))
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "blank target id");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='blank target id'.");
                        continue;
                    }

                    var from = ResolveInterchange(transfer.Key);
                    var to = ResolveInterchange(transfer.Value);
                    if (from == null || to == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "one or both interchange components were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='one or both interchange components were not found'.");
                        continue;
                    }

                    if (InterchangeTransferFromField == null || InterchangeTransferToField == null)
                    {
                        FuseLoadReport.RecordProgressionTransferSkip(
                            packageId,
                            section.identifier,
                            transfer.Key,
                            transfer.Value,
                            "base game fields were not found");
                        FuseLog.Warning(
                            $"FUSE progression transfer skipped package='{packageId ?? string.Empty}' " +
                            $"operation='apply progression' phase='interchange transfers' kind='interchange transfer' " +
                            $"id='{section.identifier ?? string.Empty}' source='{transfer.Key ?? string.Empty}' " +
                            $"target='{transfer.Value ?? string.Empty}' reason='base game fields were not found'.");
                        continue;
                    }

                    var gameObject = new GameObject(FuseInterchangeTransferPrefix + SanitizeObjectName(transfer.Key));
                    gameObject.transform.SetParent(section.transform, false);
                    var component = gameObject.AddComponent<InterchangeTransfer>();
                    InterchangeTransferFromField.SetValue(component, from);
                    InterchangeTransferToField.SetValue(component, to);
                    created.Add(component);
                    FuseLog.Info($"FUSE progression section '{section.identifier}' added interchange transfer '{transfer.Key}' -> '{transfer.Value}'.");
                }
            }

            RefreshSectionInterchangeTransfers(section, preserved.Concat(created).ToArray());
        }

        private static bool IsFuseInterchangeTransfer(InterchangeTransfer transfer)
        {
            return transfer != null &&
                   transfer.gameObject != null &&
                   transfer.gameObject.name.StartsWith(FuseInterchangeTransferPrefix, StringComparison.Ordinal);
        }

        private static void RefreshSectionInterchangeTransfers(Section section, InterchangeTransfer[] transfers)
        {
            if (section == null)
            {
                return;
            }

            SectionInterchangeTransfersField?.SetValue(section, transfers ?? Array.Empty<InterchangeTransfer>());
        }

        private static string SanitizeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '_').ToArray();
            return new string(chars);
        }

        private static MapFeature EnsureSectionUnlockFeature(Section section, FuseSection definition)
        {
            if (section == null || definition == null || !HasSectionUnlockFeaturePayload(definition))
            {
                return null;
            }

            var featureId = GetSectionUnlockFeatureId(section.identifier);
            var featureDefinition = new FuseMapFeature
            {
                DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? section.identifier : definition.DisplayName,
                Description = definition.Description,
                InitiallyEnabled = false,
                TrackGroupsEnableOnUnlock = definition.TrackGroupsEnableOnUnlock,
                TrackGroupsAvailableOnUnlock = definition.TrackGroupsAvailableOnUnlock,
                AreasEnableOnUnlock = definition.AreasEnableOnUnlock,
                GameObjectsEnableOnUnlock = definition.GameObjectsEnableOnUnlock,
                UnlockIncludeIndustries = definition.UnlockIncludeIndustries,
                UnlockExcludeIndustries = definition.UnlockExcludeIndustries,
                UnlockIncludeIndustryComponents = definition.UnlockIncludeIndustryComponents
            };

            var existing = GetMapFeature(featureId);
            if (existing != null)
            {
                ApplyMapFeatureDefinition(existing, featureDefinition);
                FuseMapFeatureRuntimeIndex.Instance.Set(featureId, existing);
                if (MapFeatureManager.Shared != null)
                {
                    RefreshMapFeatureManager(MapFeatureManager.Shared);
                }

                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.MapFeature, featureId, featureDefinition);
                FuseLog.Info($"FUSE refreshed progression section unlock feature '{featureId}' for section '{section.identifier}'.");
                return existing;
            }

            var created = AddMapFeature(featureId, featureDefinition);
            FuseLog.Info($"FUSE created progression section unlock feature '{featureId}' for section '{section.identifier}'.");
            return created;
        }

        private static bool HasSectionUnlockFeaturePayload(FuseSection definition)
        {
            // A section payload is "interesting" if any of the unlock-fan-out
            // patches authors anything — either an explicit replacement set
            // or a non-empty merge dict. EffectiveAdditions surfaces the
            // truthy-keys-only view, which is the right "is there anything
            // here to apply?" probe for the synthesized section-unlock
            // feature.
            return HasAny(definition.TrackGroupsEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.TrackGroupsAvailableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.AreasEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.GameObjectsEnableOnUnlock?.EffectiveAdditions) ||
                   HasAny(definition.UnlockIncludeIndustries?.EffectiveAdditions) ||
                   HasAny(definition.UnlockExcludeIndustries?.EffectiveAdditions) ||
                   HasAny(definition.UnlockIncludeIndustryComponents?.EffectiveAdditions);
        }

        private static string GetSectionUnlockFeatureId(string sectionId)
        {
            return "fuse.progression.section." + (sectionId ?? string.Empty) + ".unlock";
        }

        private static MapFeature[] AppendFeature(MapFeature[] features, MapFeature feature)
        {
            if (feature == null)
            {
                return features ?? Array.Empty<MapFeature>();
            }

            return (features ?? Array.Empty<MapFeature>())
                .Concat(new[] { feature })
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.identifier ?? candidate.name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static Section.DeliveryPhase CreateDeliveryPhase(FuseDeliveryPhase definition)
        {
            var deliveries = definition.Deliveries ?? Array.Empty<FuseDelivery>();
            var phase = new Section.DeliveryPhase
            {
                cost = definition.Cost,
                deliveries = deliveries.Select(CreateDelivery).ToArray()
            };

            if (deliveries.Length > 0)
            {
                phase.industryComponent = !string.IsNullOrWhiteSpace(definition.IndustryComponentId)
                    ? ResolveIndustryComponent(definition.IndustryComponentId)
                    : ResolveDeliveryPhaseIndustryComponent(definition);
            }

            return phase;
        }

        private static Section.Delivery CreateDelivery(FuseDelivery definition)
        {
            return new Section.Delivery
            {
                carTypeFilter = new CarTypeFilter(definition.CarTypeFilter ?? string.Empty),
                count = definition.Count,
                load = ResolveLoad(definition.LoadId),
                direction = ParseDeliveryDirection(definition.Direction)
            };
        }

        private static ProgressionIndustryComponent ResolveDeliveryPhaseIndustryComponent(FuseDeliveryPhase definition)
        {
            var destinationIds = (definition.Deliveries ?? Array.Empty<FuseDelivery>())
                .Select(delivery => delivery?.DestinationIndustryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (destinationIds.Length != 1)
            {
                throw new InvalidOperationException("Progression delivery phases with deliveries require industryComponentId, or a single destinationIndustryId that resolves to one ProgressionIndustryComponent.");
            }

            var industry = ResolveIndustry(destinationIds[0]);
            if (industry == null)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' was not found.");
            }

            var candidates = industry.GetComponentsInChildren<ProgressionIndustryComponent>(true)
                .Where(component => component != null)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has no ProgressionIndustryComponent. Set industryComponentId explicitly.");
            }

            throw new InvalidOperationException($"Progression delivery destination industry '{destinationIds[0]}' has {candidates.Length} ProgressionIndustryComponent entries. Set industryComponentId explicitly.");
        }

        private static Section.Delivery.Direction ParseDeliveryDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return Section.Delivery.Direction.LoadToIndustry;
            }

            switch (direction.Trim().ToLowerInvariant())
            {
                case "1":
                case "loadfromindustry":
                case "fromindustry":
                case "from":
                case "export":
                    return Section.Delivery.Direction.LoadFromIndustry;
                case "0":
                case "loadtoindustry":
                case "toindustry":
                case "to":
                case "import":
                    return Section.Delivery.Direction.LoadToIndustry;
                default:
                    return Section.Delivery.Direction.LoadToIndustry;
            }
        }
    }
}
