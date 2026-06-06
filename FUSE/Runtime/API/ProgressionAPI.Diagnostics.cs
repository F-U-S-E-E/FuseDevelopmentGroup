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

        /// <summary>
        /// Builds a structured, JSON-serializable snapshot of the live progression
        /// graph: every MapFeature with its track-group / area / industry gating
        /// targets, every Section with current unlocked/available state and prereqs,
        /// every referenced track group with the feature owners that would set it
        /// enabled vs disabled, and every Area / Industry (with components and
        /// track spans) and PassengerStop with the parent-chain a panel filter
        /// would walk. Mirrors the same data the verbose log dump emits, but as
        /// an object tree so callers can write it to a file and grep/diff offline.
        /// </summary>
        /// <param name="reason">Free-form label echoed into the payload; used to
        /// distinguish dumps taken at different points (e.g. "console dump",
        /// "post-apply", "before save").</param>
        /// <returns>An anonymous object suitable for direct
        /// <c>JsonConvert.SerializeObject</c>. If the MapFeatureManager isn't
        /// available yet (no map loaded), returns a sentinel object with
        /// <c>available=false</c> rather than throwing.</returns>
        public static object BuildProgressionDiagnosticPayload(string reason = "console dump")
        {
            var manager = MapFeatureManager.Shared;
            if (manager == null)
            {
                return new
                {
                    available = false,
                    reason = reason ?? "unspecified",
                    message = "MapFeatureManager.Shared was not available; load a map with FUSE active before dumping.",
                };
            }

            var states = ReadFeatureEnables(manager);
            var features = (manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
                .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                .ToArray();

            var featurePayloads = BuildFeaturePayloads(features, states);
            var sectionPayloads = BuildSectionPayloads();
            var trackGroupPayloads = BuildTrackGroupPayloads(features, states);
            var passengerStopPayloads = BuildPassengerStopPayloads();
            var areaPayloads = BuildAreaPayloads();
            var industryPayloads = BuildIndustryPayloads();

            return new
            {
                available = true,
                reason = reason ?? "unspecified",
                counts = new
                {
                    features = featurePayloads.Length,
                    sections = sectionPayloads.Length,
                    trackGroups = trackGroupPayloads.Length,
                    passengerStops = passengerStopPayloads.Length,
                    areas = areaPayloads.Length,
                    industries = industryPayloads.Length,
                },
                features = featurePayloads,
                sections = sectionPayloads,
                trackGroups = trackGroupPayloads,
                passengerStops = passengerStopPayloads,
                areas = areaPayloads,
                industries = industryPayloads,
            };
        }

        private static object[] BuildFeaturePayloads(MapFeature[] features, Dictionary<string, bool> states)
        {
            return features
                .Select(feature =>
                {
                    bool? kvoUnlocked = states != null && states.TryGetValue(feature.identifier, out var kv)
                        ? (bool?)kv
                        : null;
                    var defaultedTo = feature.defaultEnableInSandbox && StateManager.IsSandbox;
                    return (object)new
                    {
                        id = feature.identifier,
                        displayName = feature.displayName,
                        defaultEnableInSandbox = feature.defaultEnableInSandbox,
                        kvoUnlocked = kvoUnlocked,
                        defaultedTo = defaultedTo,
                        trackGroupsEnableOnUnlock = feature.trackGroupsEnableOnUnlock ?? Array.Empty<string>(),
                        trackGroupsAvailableOnUnlock = feature.trackGroupsAvailableOnUnlock ?? Array.Empty<string>(),
                        areasEnableOnUnlock = ListComponentIds(feature.areasEnableOnUnlock),
                        industriesInclude = ListComponentIds(feature.unlockIncludeIndustries),
                        industriesExclude = ListComponentIds(feature.unlockExcludeIndustries),
                        prerequisiteFeatureIds = ListFeatureIds(feature.prerequisites),
                    };
                })
                .ToArray();
        }

        private static object[] BuildSectionPayloads()
        {
            var sectionsRaw = UnityEngine.Object.FindObjectsOfType<Section>(true) ?? Array.Empty<Section>();
            return sectionsRaw
                .Where(section => section != null)
                .OrderBy(section => section.identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(section => (object)new
                {
                    id = section.identifier ?? string.Empty,
                    name = section.name ?? string.Empty,
                    displayName = section.displayName ?? string.Empty,
                    unlocked = section.Unlocked,
                    available = section.Available,
                    paidCount = section.PaidCount,
                    fulfilledCount = section.FulfilledCount,
                    prerequisiteSectionIds = ListSectionIds(section.prerequisiteSections),
                    enableFeaturesOnUnlock = ListFeatureIds(section.enableFeaturesOnUnlock),
                    deliveryPhaseCount = section.deliveryPhases?.Length ?? 0,
                    deliveryPhases = BuildSectionDeliveryPhasePayloads(section),
                })
                .ToArray();
        }

        private static object[] BuildSectionDeliveryPhasePayloads(Section section)
        {
            if (section?.deliveryPhases == null || section.deliveryPhases.Length == 0)
            {
                return Array.Empty<object>();
            }

            return section.deliveryPhases
                .Select((phase, phaseIndex) =>
                {
                    var component = phase?.industryComponent;
                    var receivedCounts = ReadProgressionReceivedCounts(component);
                    var spans = (component?.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                        .Where(span => span != null)
                        .ToArray();

                    return (object)new
                    {
                        index = phaseIndex,
                        cost = phase?.cost ?? 0,
                        industryComponentId = component != null ? component.Identifier : null,
                        industryComponentProgressionDisabled = component != null ? (bool?)component.ProgressionDisabled : null,
                        industryComponentActiveInHierarchy = component != null ? (bool?)component.gameObject.activeInHierarchy : null,
                        trackSpanCount = spans.Length,
                        trackSpans = spans
                            .Select(span => new
                            {
                                id = span.id ?? span.name,
                                lowerSegmentId = span.lower?.segment?.id,
                                upperSegmentId = span.upper?.segment?.id,
                            })
                            .ToArray(),
                        deliveries = BuildDeliveryPayloads(section, phase, phaseIndex, receivedCounts),
                        carsAtComponent = BuildProgressionComponentCarPayloads(component),
                    };
                })
                .ToArray();
        }

        private static object[] BuildDeliveryPayloads(
            Section section,
            Section.DeliveryPhase phase,
            int phaseIndex,
            Dictionary<string, int> receivedCounts)
        {
            if (phase?.deliveries == null || phase.deliveries.Length == 0)
            {
                return Array.Empty<object>();
            }

            return phase.deliveries
                .Select((delivery, deliveryIndex) =>
                {
                    var tag = ProgressionDeliveryTag(section, phaseIndex, deliveryIndex);
                    var receivedCount = 0;
                    receivedCounts?.TryGetValue(tag, out receivedCount);
                    var load = delivery?.load;
                    return (object)new
                    {
                        index = deliveryIndex,
                        tag,
                        carTypeFilter = delivery?.carTypeFilter?.ToString(),
                        count = delivery?.count ?? 0,
                        receivedCount,
                        loadId = load != null ? load.id : null,
                        loadName = load != null ? load.description : null,
                        loadUnits = load != null ? load.units.ToString() : null,
                        loadImportable = load != null ? (bool?)load.importable : null,
                        loadNominalQuantityPerCar = load != null ? (float?)load.NominalQuantityPerCarLoad : null,
                        direction = delivery?.direction.ToString(),
                    };
                })
                .ToArray();
        }

        private static string ProgressionDeliveryTag(Section section, int phaseIndex, int deliveryIndex)
        {
            return $"{section?.identifier ?? string.Empty}.{phaseIndex}.{deliveryIndex}";
        }

        private static Dictionary<string, int> ReadProgressionReceivedCounts(ProgressionIndustryComponent component)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (component == null)
            {
                return counts;
            }

            try
            {
                var field = typeof(ProgressionIndustryComponent).GetField("_keyValueObject", BindingFlags.Instance | BindingFlags.NonPublic);
                var keyValueObject = field?.GetValue(component) as IKeyValueObject;
                if (keyValueObject == null)
                {
                    return counts;
                }

                var value = keyValueObject["indRecv"];
                if (value.Type != KeyValue.Runtime.ValueType.Dictionary)
                {
                    return counts;
                }

                foreach (var pair in value.DictionaryValue)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        counts[pair.Key] = pair.Value.IntValue;
                    }
                }
            }
            catch
            {
            }

            return counts;
        }

        private static object[] BuildProgressionComponentCarPayloads(ProgressionIndustryComponent component)
        {
            var ops = OpsController.Shared;
            if (component == null || ops == null)
            {
                return Array.Empty<object>();
            }

            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return ops.CarsAtPosition(component)
                    .Where(car => car != null && seen.Add(car.id ?? string.Empty))
                    .OrderBy(car => car.DisplayName ?? car.id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(car =>
                    {
                        var waybill = car.Waybill;
                        return (object)new
                        {
                            id = car.id,
                            displayName = car.DisplayName,
                            carType = car.CarType,
                            componentCarTypeAccepted = component.carTypeFilter?.Matches(car.CarType) ?? false,
                            velocity = car.velocity,
                            waybillDestinationId = waybill != null ? waybill.Value.Destination.Identifier : null,
                            waybillTag = waybill != null ? waybill.Value.Tag : null,
                            waybillCompleted = waybill != null ? (bool?)waybill.Value.Completed : null,
                            loads = BuildCarLoadPayloads(car),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump payload cars for component '{component.Identifier}' failed", ex);
                return Array.Empty<object>();
            }
        }

        private static object[] BuildCarLoadPayloads(Car car)
        {
            if (car?.Definition?.LoadSlots == null)
            {
                return Array.Empty<object>();
            }

            var loads = new List<object>();
            for (var slotIndex = 0; slotIndex < car.Definition.LoadSlots.Count; slotIndex++)
            {
                var slot = car.Definition.LoadSlots[slotIndex];
                var loadInfo = car.GetLoadInfo(slotIndex);
                loads.Add(new
                {
                    slot = slotIndex,
                    loadId = loadInfo != null ? loadInfo.Value.LoadId : null,
                    quantity = loadInfo != null ? (float?)loadInfo.Value.Quantity : null,
                    maximumCapacity = slot?.MaximumCapacity,
                    loadUnits = slot?.LoadUnits.ToString(),
                    requiredLoadIdentifier = slot?.RequiredLoadIdentifier,
                });
            }

            return loads.ToArray();
        }

        // Build the union of groups referenced by a MapFeature with
        // groups actually used by live segments. The latter is needed
        // to surface "orphan" groups (segments carry the groupId but
        // no feature claims it) — without them the trackGroups list
        // hides exactly the cases worth investigating, e.g. graph-
        // only mods that ship visible-but-locked decorative track via
        // unowned group ids.
        private static object[] BuildTrackGroupPayloads(MapFeature[] features, IDictionary<string, bool> states)
        {
            var graph = Graph.Shared;
            if (graph == null)
            {
                return Array.Empty<object>();
            }

            var enabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var disabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features)
            {
                var groups = FeatureTrackGroups(feature).ToArray();
                if (groups.Length == 0)
                {
                    continue;
                }
                var enabled = IsFeatureEnabled(feature, states);
                var owners = enabled ? enabledOwners : disabledOwners;
                foreach (var groupId in groups)
                {
                    if (!owners.TryGetValue(groupId, out var list))
                    {
                        list = new List<string>();
                        owners[groupId] = list;
                    }
                    list.Add(feature.identifier);
                }
            }

            var allGroups = new HashSet<string>(enabledOwners.Keys, StringComparer.OrdinalIgnoreCase);
            allGroups.UnionWith(disabledOwners.Keys);
            var segmentGroupCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (graph.Segments != null)
            {
                foreach (var segment in graph.Segments)
                {
                    if (segment == null || string.IsNullOrWhiteSpace(segment.groupId))
                    {
                        continue;
                    }
                    allGroups.Add(segment.groupId);
                    segmentGroupCounts.TryGetValue(segment.groupId, out var count);
                    segmentGroupCounts[segment.groupId] = count + 1;
                }
            }

            return allGroups
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Select(groupId =>
                {
                    var isEnabledNow = graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId);
                    var isAvailableNow = graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId);
                    enabledOwners.TryGetValue(groupId, out var enabledBy);
                    disabledOwners.TryGetValue(groupId, out var disabledBy);
                    // Dedupe: a feature that lists the same group in both
                    // trackGroupsEnableOnUnlock AND trackGroupsAvailableOnUnlock
                    // yields the group twice from FeatureTrackGroups. Owners
                    // are about which features touch this group, not how
                    // many times.
                    var enabledByDistinct = enabledBy != null && enabledBy.Count > 0
                        ? enabledBy.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : Array.Empty<string>();
                    var disabledByDistinct = disabledBy != null && disabledBy.Count > 0
                        ? disabledBy.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : Array.Empty<string>();
                    var hasOwner = enabledByDistinct.Length > 0 || disabledByDistinct.Length > 0;
                    segmentGroupCounts.TryGetValue(groupId, out var segCount);
                    return (object)new
                    {
                        id = groupId,
                        graphEnabled = isEnabledNow,
                        graphAvailable = isAvailableNow,
                        segmentCount = segCount,
                        orphan = !hasOwner,
                        enabledBy = enabledByDistinct,
                        disabledBy = disabledByDistinct,
                    };
                })
                .ToArray();
        }

        private static object[] BuildPassengerStopPayloads()
        {
            try
            {
                var stops = Model.Ops.PassengerStop.FindAll()
                    .Where(stop => stop != null && !string.IsNullOrWhiteSpace(stop.identifier))
                    .ToArray();
                return stops
                    .OrderBy(s => s.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(stop =>
                    {
                        var industry = stop.GetComponentInParent<Industry>(true);
                        var component = stop.GetComponentInParent<IndustryComponent>(true);
                        var area = stop.GetComponentInParent<Area>(true);
                        return (object)new
                        {
                            id = stop.identifier,
                            progressionDisabled = stop.ProgressionDisabled,
                            parentIndustryId = industry != null ? industry.identifier : null,
                            parentIndustryProgressionDisabled = industry != null ? (bool?)industry.ProgressionDisabled : null,
                            parentComponentId = component != null ? component.Identifier : null,
                            parentComponentProgressionDisabled = component != null ? (bool?)component.ProgressionDisabled : null,
                            parentAreaId = area != null ? area.identifier : null,
                            activeSelf = stop.gameObject.activeSelf,
                            activeInHierarchy = stop.gameObject.activeInHierarchy,
                            wouldPassPanelFilter = !stop.ProgressionDisabled,
                            path = FormatGameObjectPath(stop.transform),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump payload passengerStops failed", ex);
                return Array.Empty<object>();
            }
        }

        private static object[] BuildAreaPayloads()
        {
            try
            {
                var areas = UnityEngine.Object.FindObjectsOfType<Area>(true)
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.identifier))
                    .ToArray();
                return areas
                    .OrderBy(a => a.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(area =>
                    {
                        var industries = area.Industries?.ToArray() ?? Array.Empty<Industry>();
                        var stopsActive = area.GetComponentsInChildren<Model.Ops.PassengerStop>();
                        var stopsAll = area.GetComponentsInChildren<Model.Ops.PassengerStop>(true);
                        return (object)new
                        {
                            id = area.identifier,
                            industryCount = industries.Length,
                            passengerStopsActiveCount = stopsActive.Length,
                            passengerStopsAllCount = stopsAll.Length,
                            activeInHierarchy = area.gameObject.activeInHierarchy,
                            industries = industries
                                .Where(i => i != null && !string.IsNullOrWhiteSpace(i.identifier))
                                .Select(i => new { id = i.identifier, progressionDisabled = i.ProgressionDisabled })
                                .ToArray(),
                            passengerStops = stopsActive
                                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.identifier))
                                .Select(s => new { id = s.identifier, progressionDisabled = s.ProgressionDisabled })
                                .ToArray(),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump payload areas failed", ex);
                return Array.Empty<object>();
            }
        }

        private static object[] BuildIndustryPayloads()
        {
            try
            {
                var industries = UnityEngine.Object.FindObjectsOfType<Industry>(true)
                    .Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                    .ToArray();
                return industries
                    .OrderBy(i => i.identifier, StringComparer.OrdinalIgnoreCase)
                    .Select(industry =>
                    {
                        var area = industry.GetComponentInParent<Area>(true);
                        var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                            .Where(c => c != null)
                            .ToArray();
                        return (object)new
                        {
                            id = industry.identifier,
                            name = industry.name,
                            progressionDisabled = industry.ProgressionDisabled,
                            componentCount = components.Length,
                            parentAreaId = area != null ? area.identifier : null,
                            activeInHierarchy = industry.gameObject.activeInHierarchy,
                            path = FormatGameObjectPath(industry.transform),
                            components = components
                                .OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                .Select(component =>
                                {
                                    var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                                        .Where(s => s != null)
                                        .ToArray();
                                    return new
                                    {
                                        id = component.Identifier,
                                        type = component.GetType().FullName,
                                        progressionDisabled = component.ProgressionDisabled,
                                        isVisible = component.IsVisible,
                                        loadId = TryReadLoadId(component),
                                        trackSpanCount = spans.Length,
                                        spans = spans
                                            .Select(s => new
                                            {
                                                id = s.id ?? s.name,
                                                lowerSegmentId = s.lower?.segment?.id,
                                                lowerSegmentGroup = s.lower?.segment?.groupId,
                                                upperSegmentId = s.upper?.segment?.id,
                                                upperSegmentGroup = s.upper?.segment?.groupId,
                                            })
                                            .ToArray(),
                                    };
                                })
                                .ToArray(),
                        };
                    })
                    .ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump payload industries failed", ex);
                return Array.Empty<object>();
            }
        }

        private static string[] ListComponentIds<T>(T[] components) where T : UnityEngine.Component
        {
            if (components == null || components.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(components.Length);
            foreach (var component in components)
            {
                if (component == null) continue;
                var id = component.GetType().GetProperty("identifier")?.GetValue(component) as string
                    ?? component.GetType().GetField("identifier")?.GetValue(component) as string
                    ?? component.name;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    list.Add(id);
                }
            }
            return list.ToArray();
        }

        private static string[] ListFeatureIds(MapFeature[] features)
        {
            if (features == null || features.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(features.Length);
            foreach (var feature in features)
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.identifier)) continue;
                list.Add(feature.identifier);
            }
            return list.ToArray();
        }

        private static string[] ListSectionIds(Section[] sections)
        {
            if (sections == null || sections.Length == 0)
            {
                return Array.Empty<string>();
            }
            var list = new List<string>(sections.Length);
            foreach (var section in sections)
            {
                if (section == null || string.IsNullOrWhiteSpace(section.identifier)) continue;
                list.Add(section.identifier);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Verbose dump of every MapFeature, Section, and track-group state after
        /// a progression refresh. Gated behind <see cref="FuseSettings.VerboseApplyReportDetails"/>
        /// because in a busy mod set this can run into thousands of lines; useful when
        /// diagnosing "feature unlocked when it shouldn't be" or "track group visible
        /// when its controlling feature is locked" reports.
        /// </summary>
        private static void DumpProgressionStateForDiagnostics(MapFeatureManager manager, string reason)
        {
            try
            {
                var states = ReadFeatureEnables(manager);
                var features = (manager.AvailableFeatures ?? Enumerable.Empty<MapFeature>())
                    .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier))
                    .ToArray();

                FuseLog.Info(
                    $"FUSE progression dump begin reason='{reason ?? "unspecified"}' features={features.Length} " +
                    $"featureStateEntries={states?.Count ?? 0}.");

                DumpFeatureStates(features, states);
                DumpSectionStates();
                DumpTrackGroupStates(features, states);
                DumpPassengerStopStates();
                DumpAreaStates();
                DumpIndustryStates();
                DumpMapEnhancerSimulation();
                DumpKeyValueBoolAnimators();
                DumpKeyValuePickableToggles();
                DumpCarLoaderSequencers();

                FuseLog.Info("FUSE progression dump end.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE progression diagnostic dump failed reason='{reason ?? "unspecified"}': {ex.Message}");
            }
        }

        // Per-feature line: identifier, defaultSandbox, current unlock from KVO,
        // and the track-group / area / industry references the feature gates.
        private static void DumpFeatureStates(MapFeature[] features, Dictionary<string, bool> states)
        {
            foreach (var feature in features)
            {
                var enabledInKvo = states != null && states.TryGetValue(feature.identifier, out var kv)
                    ? kv.ToString()
                    : "<unset>";
                var defaultedTo = feature.defaultEnableInSandbox && StateManager.IsSandbox ? "true" : "false";
                FuseLog.Info(
                    "  feature " +
                    $"id='{feature.identifier}' display='{feature.displayName}' " +
                    $"defaultSandbox={feature.defaultEnableInSandbox} kvoUnlocked={enabledInKvo} " +
                    $"defaultedTo={defaultedTo} " +
                    $"tracksEnable=[{FormatIdList(feature.trackGroupsEnableOnUnlock)}] " +
                    $"tracksAvail=[{FormatIdList(feature.trackGroupsAvailableOnUnlock)}] " +
                    $"areas=[{FormatComponentIds(feature.areasEnableOnUnlock)}] " +
                    $"industriesInclude=[{FormatComponentIds(feature.unlockIncludeIndustries)}] " +
                    $"prereqIds=[{FormatFeatureIds(feature.prerequisites)}].");
            }
        }

        // Per-section line: identifier, name, current Unlocked/Available, prereq sections.
        private static void DumpSectionStates()
        {
            var sections = UnityEngine.Object.FindObjectsOfType<Section>(true);
            FuseLog.Info($"FUSE progression dump sections count={sections?.Length ?? 0}.");
            if (sections == null) return;
            foreach (var section in sections)
            {
                if (section == null) continue;
                FuseLog.Info(
                    "  section " +
                    $"id='{section.identifier ?? string.Empty}' name='{section.name ?? string.Empty}' " +
                    $"display='{section.displayName ?? string.Empty}' " +
                    $"unlocked={section.Unlocked} available={section.Available} " +
                    $"paid={section.PaidCount} fulfilled={section.FulfilledCount} " +
                    $"prereqSections=[{FormatSectionIds(section.prerequisiteSections)}] " +
                    $"enableFeaturesOnUnlock=[{FormatFeatureIds(section.enableFeaturesOnUnlock)}] " +
                    $"deliveryPhases={section.deliveryPhases?.Length ?? 0}.");
            }
        }

        // Per-track-group line: id, current enabled/available, feature owners that
        // would set it enabled, feature owners that would set it disabled, what
        // its computed final state should be. Surfaces the "feature X has group Y
        // in trackGroupsEnableOnUnlock but Y is enabled despite X being locked"
        // pattern that took most of a debugging session to trace manually.
        private static void DumpTrackGroupStates(MapFeature[] features, IDictionary<string, bool> states)
        {
            var graph = Graph.Shared;
            if (graph == null) return;

            var enabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var disabledOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features)
            {
                var groups = FeatureTrackGroups(feature).ToArray();
                if (groups.Length == 0) continue;
                var enabled = IsFeatureEnabled(feature, states);
                var owners = enabled ? enabledOwners : disabledOwners;
                foreach (var groupId in groups)
                {
                    if (!owners.TryGetValue(groupId, out var list))
                    {
                        list = new List<string>();
                        owners[groupId] = list;
                    }
                    list.Add(feature.identifier);
                }
            }

            var allGroups = new HashSet<string>(enabledOwners.Keys, StringComparer.OrdinalIgnoreCase);
            allGroups.UnionWith(disabledOwners.Keys);
            FuseLog.Info($"FUSE progression dump trackGroups count={allGroups.Count}.");
            foreach (var groupId in allGroups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            {
                var isEnabledNow = graph.enabledGroupIds != null && graph.enabledGroupIds.Contains(groupId);
                var isAvailableNow = graph.availableGroupIds != null && graph.availableGroupIds.Contains(groupId);
                enabledOwners.TryGetValue(groupId, out var enabledBy);
                disabledOwners.TryGetValue(groupId, out var disabledBy);
                FuseLog.Info(
                    "  trackGroup " +
                    $"id='{groupId}' graphEnabled={isEnabledNow} graphAvailable={isAvailableNow} " +
                    $"enabledBy=[{(enabledBy != null ? string.Join(",", enabledBy) : string.Empty)}] " +
                    $"disabledBy=[{(disabledBy != null ? string.Join(",", disabledBy) : string.Empty)}].");
            }
        }

        // Per-passenger-stop dump: identifier, ProgressionDisabled,
        // closest parent Industry (id + flag), closest parent Area
        // (id), full GameObject hierarchy path, and whether the FUSE
        // car destination panel filter would currently let this stop
        // through. This is the ground truth for diagnosing "locked
        // station appears in passenger car destination picker".
        //
        // The path matters: the game's UpdateFeatureForUnlocked finds
        // PassengerStops via `area.GetComponentsInChildren<PassengerStop>()`
        // (no includeInactive). If a stop's path does not descend
        // from the feature's areasEnableOnUnlock Area, the game's
        // pass cannot reach it — and no amount of refreshing on our
        // end will help.
        private static void DumpPassengerStopStates()
        {
            try
            {
                var stops = Model.Ops.PassengerStop.FindAll()
                    .Where(stop => stop != null && !string.IsNullOrWhiteSpace(stop.identifier))
                    .ToArray();
                FuseLog.Info($"FUSE progression dump passengerStops count={stops.Length}.");
                foreach (var stop in stops.OrderBy(s => s.identifier, StringComparer.OrdinalIgnoreCase))
                {
                    var industry = stop.GetComponentInParent<Industry>(true);
                    var industryId = industry != null ? industry.identifier : "<none>";
                    var industryDisabled = industry != null ? industry.ProgressionDisabled : false;
                    var area = stop.GetComponentInParent<Area>(true);
                    var areaId = area != null ? area.identifier : "<none>";
                    var component = stop.GetComponentInParent<IndustryComponent>(true);
                    var componentId = component != null ? component.Identifier : "<none>";
                    var componentDisabled = component != null ? component.ProgressionDisabled : false;
                    var wouldPassFilter = !stop.ProgressionDisabled;
                    var path = FormatGameObjectPath(stop.transform);
                    var isActiveSelf = stop.gameObject.activeSelf;
                    var isActiveInHierarchy = stop.gameObject.activeInHierarchy;
                    FuseLog.Info(
                        $"  passengerStop id='{stop.identifier}' progressionDisabled={stop.ProgressionDisabled} " +
                        $"parentIndustry='{industryId}' industryProgDisabled={industryDisabled} " +
                        $"parentComponent='{componentId}' componentProgDisabled={componentDisabled} " +
                        $"parentArea='{areaId}' activeSelf={isActiveSelf} activeInHierarchy={isActiveInHierarchy} " +
                        $"wouldPassPanelFilter={wouldPassFilter} path='{path}'.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump passengerStops failed", ex);
            }
        }

        // Per-Area dump: mirrors what the game's UpdateFeatureForUnlocked
        // sees when it iterates a feature's areasEnableOnUnlock. Logs
        // exactly which Industry / PassengerStop children the game's
        // GetComponentsInChildren call would discover (using the same
        // includeInactive=false semantics) — so we can cross-reference
        // against the per-feature areas listing above and confirm or
        // refute "the area is empty when the game looks at it".
        private static void DumpAreaStates()
        {
            try
            {
                var areas = UnityEngine.Object.FindObjectsOfType<Area>(true)
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.identifier))
                    .ToArray();
                FuseLog.Info($"FUSE progression dump areas count={areas.Length}.");
                foreach (var area in areas.OrderBy(a => a.identifier, StringComparer.OrdinalIgnoreCase))
                {
                    var industries = area.Industries?.ToArray() ?? Array.Empty<Industry>();
                    // The game uses (false) implicitly; mirror that.
                    var stopsActive = area.GetComponentsInChildren<Model.Ops.PassengerStop>();
                    var stopsAll = area.GetComponentsInChildren<Model.Ops.PassengerStop>(true);
                    var industryIds = industries
                        .Where(i => i != null && !string.IsNullOrWhiteSpace(i.identifier))
                        .Select(i => $"{i.identifier}(disabled={i.ProgressionDisabled})")
                        .ToArray();
                    var stopIds = stopsActive
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.identifier))
                        .Select(s => $"{s.identifier}(disabled={s.ProgressionDisabled})")
                        .ToArray();
                    FuseLog.Info(
                        $"  area id='{area.identifier}' industries={industries.Length} " +
                        $"passengerStopsActive={stopsActive.Length} passengerStopsAll={stopsAll.Length} " +
                        $"activeInHierarchy={area.gameObject.activeInHierarchy} " +
                        $"industryList=[{string.Join(",", industryIds)}] " +
                        $"passengerStopList=[{string.Join(",", stopIds)}].");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump areas failed", ex);
            }
        }

        // Per-Industry dump: ground truth for "industry track shows
        // up on map when it shouldn't" and "captive freight service
        // looking wrong" reports. Shows ProgressionDisabled on the
        // industry, every IndustryComponent child (with its own
        // ProgressionDisabled, IsVisible — the actual map-visibility
        // predicate — type name, load id, and track-span resolution),
        // and the closest Area / GameObject path. The IsVisible
        // column matters: per game IL, IsVisible only checks the
        // component's own ProgressionDisabled and trackSpans.Length;
        // it does NOT propagate from the parent Industry. So a
        // locked Industry with components whose ProgressionDisabled
        // is false will still draw those components on the map.
        private static void DumpIndustryStates()
        {
            try
            {
                var industries = UnityEngine.Object.FindObjectsOfType<Industry>(true)
                    .Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier))
                    .ToArray();
                FuseLog.Info($"FUSE progression dump industries count={industries.Length}.");
                foreach (var industry in industries.OrderBy(i => i.identifier, StringComparer.OrdinalIgnoreCase))
                {
                    var area = industry.GetComponentInParent<Area>(true);
                    var areaId = area != null ? area.identifier : "<none>";
                    var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                        .Where(c => c != null)
                        .ToArray();
                    var path = FormatGameObjectPath(industry.transform);
                    FuseLog.Info(
                        $"  industry id='{industry.identifier}' name='{industry.name}' " +
                        $"progressionDisabled={industry.ProgressionDisabled} " +
                        $"componentCount={components.Length} parentArea='{areaId}' " +
                        $"activeInHierarchy={industry.gameObject.activeInHierarchy} " +
                        $"path='{path}'.");

                    foreach (var component in components.OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                            .Where(s => s != null)
                            .ToArray();
                        var spanIds = spans
                            .Select(s =>
                            {
                                var lowerSegment = s.lower?.segment?.id ?? "<null>";
                                var lowerGroup = s.lower?.segment?.groupId ?? "<null>";
                                var upperSegment = s.upper?.segment?.id ?? "<null>";
                                var upperGroup = s.upper?.segment?.groupId ?? "<null>";
                                return $"{s.id ?? s.name}(lower={lowerSegment}@{lowerGroup},upper={upperSegment}@{upperGroup})";
                            })
                            .ToArray();
                        // Resolve a load id when the component carries one
                        // (LoadConsumer / FUSE loaders/unloaders).
                        var loadId = TryReadLoadId(component);
                        var typeName = component.GetType().FullName ?? "<unknown>";
                        FuseLog.Info(
                            $"    component id='{component.Identifier}' type='{typeName}' " +
                            $"progressionDisabled={component.ProgressionDisabled} isVisible={component.IsVisible} " +
                            $"loadId='{loadId ?? "<n/a>"}' trackSpans={spans.Length} " +
                            $"spans=[{string.Join(",", spanIds)}].");
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump industries failed", ex);
            }
        }

        // MapEnhancer-simulation dump: mirrors Map Enhancer's industry
        // track lookup exactly, so we can compare what FUSE presents
        // vs what Map Enhancer reads when painting the map. MapEnhancer
        // walks OpsController.Shared.Areas -> area.Industries ->
        // industry.Components looking for each IndustryComponent;
        // when found, uses that Area's tagColor for every cached
        // TrackSegment of the component's TrackSpans (and marks them
        // as `_industrialSegments`). If the iteration misses the
        // component, MapEnhancer position-falls-back to
        // OpsController.Shared.ClosestAreaForGamePosition — that
        // fallback is the usual culprit when an industry track
        // shows the wrong colour, since it can pick an unrelated
        // adjacent area.
        private static void DumpMapEnhancerSimulation()
        {
            try
            {
                var ops = UnityEngine.Object.FindObjectOfType<Model.Ops.OpsController>();
                var areasList = ops?.Areas?.ToArray() ?? Array.Empty<Area>();
                FuseLog.Info(
                    $"FUSE progression dump mapEnhancer-sim ops={(ops != null)} areasCount={areasList.Length}.");

                var components = UnityEngine.Object.FindObjectsOfType<IndustryComponent>()
                    .Where(c => c != null && !(c is Model.Ops.ProgressionIndustryComponent))
                    .ToArray();
                FuseLog.Info(
                    $"FUSE progression dump mapEnhancer-sim industryComponents (excluding ProgressionIndustryComponent) count={components.Length}.");

                foreach (var component in components.OrderBy(c => c.Identifier ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    // Mirror MapEnhancer's exact lookup: walk
                    // OpsController.Shared.Areas -> Industries -> Components.
                    Area foundArea = null;
                    if (ops != null)
                    {
                        foreach (var area in areasList)
                        {
                            if (area?.Industries == null) continue;
                            foreach (var industry in area.Industries)
                            {
                                if (industry?.Components == null) continue;
                                var hit = false;
                                foreach (var comp in industry.Components)
                                {
                                    if (ReferenceEquals(comp, component))
                                    {
                                        foundArea = area;
                                        hit = true;
                                        break;
                                    }
                                }
                                if (hit) break;
                            }
                            if (foundArea != null) break;
                        }
                    }

                    // Position fallback used when the registry walk fails.
                    Area fallbackArea = null;
                    if (foundArea == null && ops != null)
                    {
                        try
                        {
                            var gamePos = Helpers.WorldTransformer.WorldToGame(component.transform.position);
                            fallbackArea = ops.ClosestAreaForGamePosition(new UnityEngine.Vector2(gamePos.x, gamePos.z));
                        }
                        catch
                        {
                        }
                    }

                    var pickedArea = foundArea ?? fallbackArea;
                    var pickedAreaId = pickedArea != null ? pickedArea.identifier : "<none>";
                    var pickedTag = pickedArea != null ? pickedArea.tagColor : default(UnityEngine.Color);
                    var pickedTagHex = pickedTag == default(UnityEngine.Color)
                        ? "<default>"
                        : $"#{(int)(pickedTag.r * 255):X2}{(int)(pickedTag.g * 255):X2}{(int)(pickedTag.b * 255):X2}";

                    var spans = (component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                        .Where(s => s != null)
                        .ToArray();
                    var cachedSegmentSummaries = new List<string>();
                    var spanCachedSegmentsField = TrackSpanCachedSegmentsField;
                    var spanUpdateMethod = TrackSpanUpdateCachedPointsMethod;
                    foreach (var span in spans)
                    {
                        try
                        {
                            spanUpdateMethod?.Invoke(span, null);
                        }
                        catch
                        {
                        }
                        var cachedRaw = spanCachedSegmentsField?.GetValue(span) as System.Collections.IList;
                        if (cachedRaw == null) continue;
                        foreach (var item in cachedRaw)
                        {
                            if (!(item is TrackSegment seg) || seg == null) continue;
                            cachedSegmentSummaries.Add(
                                $"{seg.id ?? "<null>"}(group='{seg.groupId ?? "<null>"}'," +
                                $"available={seg.Available},groupEnabled={seg.GroupEnabled})");
                        }
                    }

                    FuseLog.Info(
                        $"  mapEnhancer-sim component id='{component.Identifier}' " +
                        $"foundAreaViaIteration='{(foundArea != null ? foundArea.identifier : "<none>")}' " +
                        $"positionFallbackArea='{(fallbackArea != null ? fallbackArea.identifier : "<none>")}' " +
                        $"pickedArea='{pickedAreaId}' pickedTagColor={pickedTagHex} " +
                        $"componentProgDisabled={component.ProgressionDisabled} isVisible={component.IsVisible} " +
                        $"cachedSegments=[{string.Join(",", cachedSegmentSummaries)}].");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump mapEnhancer-sim failed", ex);
            }
        }

        // KeyValueBoolAnimator inventory: snapshot every animator in the
        // scene, its observed key, whether it found a KeyValueObject in
        // its parent chain, and the current bool value. Industry loader
        // / water column / coal chute / turntable animations all run on
        // this pipeline; a missing-parent-KVO entry here means that
        // animator will never play, which manifests as "loader doesn't
        // rotate/open when I expect it to."
        private static void DumpKeyValueBoolAnimators()
        {
            try
            {
                var animators = UnityEngine.Object.FindObjectsOfType<RollingStock.Controls.KeyValueBoolAnimator>(true);
                var withKvo = 0;
                var withoutKvo = 0;
                foreach (var animator in animators)
                {
                    if (animator == null) continue;
                    if (animator.GetComponentInParent<KeyValueObject>() != null) withKvo++; else withoutKvo++;
                }
                FuseLog.Info(
                    $"FUSE progression dump keyValueBoolAnimators count={animators.Length} " +
                    $"withParentKVO={withKvo} withoutParentKVO={withoutKvo}.");

                foreach (var animator in animators)
                {
                    if (animator == null) continue;
                    var kvo = animator.GetComponentInParent<KeyValueObject>();
                    var animPath = FormatGameObjectPath(animator.transform);
                    var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-found>";
                    bool? currentBool = null;
                    try
                    {
                        if (kvo != null && !string.IsNullOrEmpty(animator.key))
                        {
                            currentBool = kvo[animator.key].BoolValue;
                        }
                    }
                    catch
                    {
                    }
                    FuseLog.Info(
                        $"  animator path='{animPath}' key='{animator.key ?? "<null>"}' " +
                        $"parentKVO='{kvoPath}' currentBool={(currentBool.HasValue ? currentBool.Value.ToString() : "<n/a>")} " +
                        $"invert={animator.invert} active={animator.gameObject.activeInHierarchy} enabled={animator.enabled}.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump keyValueBoolAnimators failed", ex);
            }
        }

        // KeyValuePickableToggle inventory — these are the click
        // handlers for water columns / fueling stands / coal towers /
        // roundhouse stall doors. For a click to take effect:
        //   1. A Collider must exist on the GameObject or a child
        //      (raycast hit).
        //   2. GetComponentInParent<KeyValueObject>() must return a
        //      KVO (so the toggle can read/write the bool).
        //   3. The KVO must be registered globally
        //      (GlobalKeyValueObject + non-empty globalObjectId) so
        //      the PropertyChange message can be routed when the
        //      toggle fires.
        //
        // Any of those missing produces "I click the loader and
        // nothing happens." This dump exposes each precondition.
        private static void DumpKeyValuePickableToggles()
        {
            try
            {
                var toggles = UnityEngine.Object.FindObjectsOfType<RollingStock.Controls.KeyValuePickableToggle>(true);
                FuseLog.Info($"FUSE progression dump keyValuePickableToggles count={toggles.Length}.");
                foreach (var toggle in toggles)
                {
                    if (toggle == null) continue;
                    var togglePath = FormatGameObjectPath(toggle.transform);
                    var kvo = toggle.GetComponentInParent<KeyValueObject>();
                    var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-found>";

                    // Was the KVO registered globally (so PropertyChange messages route)?
                    // The presence of GlobalKeyValueObject on the same GameObject with a
                    // non-empty globalObjectId is the signal. Looked up reflectively to
                    // avoid pulling in the Unity Physics reference for a tiny check.
                    string globalId = null;
                    if (kvo != null)
                    {
                        try
                        {
                            var globalType = Type.GetType("RollingStock.Controls.GlobalKeyValueObject, Assembly-CSharp");
                            if (globalType != null)
                            {
                                var globalComp = kvo.GetComponent(globalType);
                                if (globalComp != null)
                                {
                                    var idField = globalType.GetField("globalObjectId");
                                    globalId = idField?.GetValue(globalComp) as string;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    FuseLog.Info(
                        $"  pickableToggle path='{togglePath}' key='{toggle.key ?? "<null>"}' " +
                        $"parentKVO='{kvoPath}' globalObjectId='{globalId ?? "<none>"}' " +
                        $"active={toggle.gameObject.activeInHierarchy} enabled={toggle.enabled} " +
                        $"maxPickDistance={toggle.MaxPickDistance}.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump keyValuePickableToggles failed", ex);
            }
        }

        // CarLoaderSequencer inventory — the bridge that turns a
        // click-toggled `request` bool into `prepareLoad` /
        // `animateLoad` writes consumed by the animator. If a water
        // column / fueling stand fires KeyValuePickableToggle.Activate
        // but no PropertyChanged ever fires on the animation keys,
        // this sequencer is likely missing, disabled, has an unbound
        // keyValueObject SerializeField, or its host isn't the
        // multiplayer host.
        private static void DumpCarLoaderSequencers()
        {
            try
            {
                var sequencers = UnityEngine.Object.FindObjectsOfType<RollingStock.CarLoaderSequencer>(true);
                FuseLog.Info($"FUSE progression dump carLoaderSequencers count={sequencers.Length}.");
                foreach (var sequencer in sequencers)
                {
                    if (sequencer == null) continue;
                    var path = FormatGameObjectPath(sequencer.transform);
                    var kvo = sequencer.keyValueObject;
                    var kvoPath = kvo != null ? FormatGameObjectPath(kvo.transform) : "<not-assigned>";
                    // Compare assigned kvo to the GameObject-tree KVO (GetComponentInParent)
                    // — sometimes the sequencer's SerializeField points at the wrong KVO
                    // (e.g. a stale reference from before cloning).
                    var nearestKvo = sequencer.GetComponentInParent<KeyValueObject>();
                    var nearestKvoPath = nearestKvo != null ? FormatGameObjectPath(nearestKvo.transform) : "<not-found>";
                    var matches = ReferenceEquals(kvo, nearestKvo);
                    FuseLog.Info(
                        $"  carLoaderSequencer path='{path}' kvoRef='{kvoPath}' " +
                        $"nearestParentKvo='{nearestKvoPath}' refMatchesNearest={matches} " +
                        $"readWants='{sequencer.readWantsLoadingKey}' readIsLoading='{sequencer.readIsLoadingKey}' " +
                        $"writeCanLoad='{sequencer.writeCanLoadKey}' writePrepare='{sequencer.writePrepareLoadKey}' " +
                        $"writeAnimate='{sequencer.writeAnimateLoadKey}' " +
                        $"active={sequencer.gameObject.activeInHierarchy} enabled={sequencer.enabled}.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE progression dump carLoaderSequencers failed", ex);
            }
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;
            var segments = new List<string>();
            var cursor = gameObject.transform;
            var depth = 0;
            while (cursor != null && depth < 32)
            {
                segments.Add(cursor.name);
                cursor = cursor.parent;
                depth++;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string FormatPatchInputs(FuseStringPatch patch)
        {
            if (patch == null || !patch.HasValue) return string.Empty;
            return string.Join(",", patch.EffectiveAdditions);
        }

        private static string FormatIdList(string[] ids)
        {
            return ids == null || ids.Length == 0 ? string.Empty : string.Join(",", ids);
        }

        /// <summary>
        /// Reflectively reads the load identifier from an IndustryComponent. Different
        /// subtypes expose their load on different members (<c>load</c>,
        /// <c>passengerLoad</c>, <c>loadId</c>), so probing by reflection avoids a
        /// brittle type switch and still surfaces the right id for diagnostic logs.
        /// </summary>
        private static string TryReadLoadId(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                var type = component.GetType();
                var candidateNames = new[] { "load", "passengerLoad", "Load", "PassengerLoad" };
                foreach (var name in candidateNames)
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (field != null)
                    {
                        var value = field.GetValue(component);
                        var id = ExtractLoadIdentifier(value);
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (property != null && property.CanRead)
                    {
                        var value = property.GetValue(component);
                        var id = ExtractLoadIdentifier(value);
                        if (!string.IsNullOrEmpty(id))
                        {
                            return id;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ExtractLoadIdentifier(object loadObj)
        {
            if (loadObj == null)
            {
                return null;
            }
            // String load id (used by some FUSE component shapes).
            if (loadObj is string s)
            {
                return s;
            }
            // Load / similar ScriptableObject reference: read its 'id' field/property.
            var type = loadObj.GetType();
            var idMember = type.GetField("id", BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetField("identifier", BindingFlags.Public | BindingFlags.Instance);
            if (idMember != null)
            {
                return idMember.GetValue(loadObj) as string;
            }
            var idProperty = type.GetProperty("id", BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetProperty("identifier", BindingFlags.Public | BindingFlags.Instance);
            if (idProperty != null && idProperty.CanRead)
            {
                return idProperty.GetValue(loadObj) as string;
            }
            return loadObj.ToString();
        }

        /// <summary>
        /// Formats a GameObject's hierarchy path as "Root/Child/Grandchild/...".
        /// Used by the verbose passenger-stop dump so we can verify the actual
        /// scene-graph location of each stop against the assumed
        /// Area > Industry > IndustryComponent > PassengerStop layout.
        /// </summary>
        private static string FormatGameObjectPath(UnityEngine.Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var segments = new List<string>();
            var cursor = transform;
            var depth = 0;
            while (cursor != null && depth < 16)
            {
                segments.Add(cursor.name);
                cursor = cursor.parent;
                depth++;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string FormatComponentIds<T>(T[] components) where T : UnityEngine.Component
        {
            if (components == null || components.Length == 0) return string.Empty;
            var parts = new List<string>(components.Length);
            foreach (var component in components)
            {
                if (component == null) continue;
                var idProp = component.GetType().GetProperty("identifier")?.GetValue(component) as string
                    ?? component.GetType().GetField("identifier")?.GetValue(component) as string
                    ?? component.name;
                parts.Add(idProp ?? "<null>");
            }
            return string.Join(",", parts);
        }

        private static string FormatFeatureIds(MapFeature[] features)
        {
            if (features == null || features.Length == 0) return string.Empty;
            var parts = new List<string>(features.Length);
            foreach (var feature in features)
            {
                parts.Add(feature?.identifier ?? "<null>");
            }
            return string.Join(",", parts);
        }

        private static string FormatSectionIds(Section[] sections)
        {
            if (sections == null || sections.Length == 0) return string.Empty;
            var parts = new List<string>(sections.Length);
            foreach (var section in sections)
            {
                parts.Add(section?.identifier ?? "<null>");
            }
            return string.Join(",", parts);
        }
    }
}
