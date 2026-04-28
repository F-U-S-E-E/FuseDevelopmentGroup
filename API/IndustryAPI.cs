using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using RAIL.Cache;
using RAIL.Data;
using RAIL.Events;
using RAIL.Infrastructure;
using Track;
using UnityEngine;

namespace RAIL.API
{
    public static class IndustryAPI
    {
        private static readonly FieldInfo IndustryCachedComponentsField = typeof(Industry).GetField("_cachedComponents", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedIndustryField = typeof(IndustryComponent).GetField("_cachedIndustry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ComponentIdentifierField = typeof(IndustryComponent).GetField("_identifier", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RepairPartsLoadField = typeof(RepairTrack).GetField("repairPartsLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<string, int> IndustryOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> RailCreatedIndustryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Transform _fallbackRoot;

        public static Industry AddIndustry(string id, RailIndustry definition)
        {
            return AddIndustry(id, definition, true);
        }

        internal static Industry AddIndustry(string id, RailIndustry definition, bool notify)
        {
            RequireId(id, nameof(id));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetIndustry(id) != null)
            {
                throw new InvalidOperationException($"Industry '{id}' already exists.");
            }

            var root = GetIndustryRoot(definition);
            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? id : definition.Name;
            var gameObject = new GameObject(displayName);
            gameObject.SetActive(false);
            gameObject.transform.SetParent(root, false);
            gameObject.transform.localPosition = definition.Position;
            gameObject.transform.localRotation = Quaternion.Euler(definition.Rotation);

            var industry = gameObject.AddComponent<Industry>();
            industry.identifier = id;
            industry.name = displayName;
            industry.usesContract = definition.UsesContract;

            RememberIndustryOrder(id, definition.Order);
            RailCreatedIndustryIds.Add(id);
            IndustryCache.Instance.Set(id, industry);
            RailLog.Info($"RAIL created industry '{id}' name='{displayName}' parent='{DescribeIndustryParent(root)}' componentDefinitionCount={definition.Components?.Count ?? 0}.");
            AddOrUpdateComponents(industry, definition.Components);
            gameObject.SetActive(true);
            if (notify)
            {
                RefreshIndustriesAfterBatch("AddIndustry:" + id);
            }

            RailEvents.RaiseIndustryAdded(industry);
            return industry;
        }

        public static void UpdateIndustry(string id, RailIndustry definition)
        {
            UpdateIndustry(id, definition, true);
        }

        internal static void UpdateIndustry(string id, RailIndustry definition, bool notify)
        {
            var industry = RequireIndustry(id);
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var displayName = string.IsNullOrWhiteSpace(definition.Name) ? id : definition.Name;
            var root = GetIndustryRoot(definition);
            if (root != null && industry.transform.parent != root)
            {
                industry.transform.SetParent(root, false);
                RailLog.Info($"RAIL reparented industry '{id}' to '{DescribeIndustryParent(root)}'.");
            }

            industry.gameObject.name = displayName;
            industry.name = displayName;
            industry.transform.localPosition = definition.Position;
            industry.transform.localRotation = Quaternion.Euler(definition.Rotation);
            industry.usesContract = definition.UsesContract;
            RememberIndustryOrder(id, definition.Order);
            AddOrUpdateComponents(industry, definition.Components);
            IndustryCache.Instance.Set(id, industry);
            if (notify)
            {
                RefreshIndustriesAfterBatch("UpdateIndustry:" + id);
            }

            RailEvents.RaiseIndustryUpdated(industry);
        }

        public static void RemoveIndustry(string id)
        {
            var industry = RequireIndustry(id);
            industry.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(industry.gameObject);
            IndustryCache.Instance.Remove(id);
            RailCreatedIndustryIds.Remove(id);
            RefreshIndustriesAfterBatch("RemoveIndustry:" + id);
            RailEvents.RaiseIndustryRemoved(id);
        }

        public static Industry GetIndustry(string id)
        {
            if (IndustryCache.Instance.TryGetValue(id, out var cached))
            {
                return (Industry)cached;
            }

            var controller = OpsController.Shared;
            if (controller != null)
            {
                var result = controller.IndustryForId(id);
                if (result != null)
                {
                    return result;
                }
            }

            return RailCacheRegistry.IsReady && !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Industry>(true).FirstOrDefault(industry => industry.identifier == id)
                : null;
        }

        public static IEnumerable<Industry> GetAllIndustries()
        {
            return UnityEngine.Object.FindObjectsOfType<Industry>();
        }

        public static IndustryComponent AddComponent(string industryId, string subId, RailIndustryComponent definition)
        {
            return AddComponent(RequireIndustry(industryId), subId, definition, true);
        }

        public static void UpdateComponent(string industryId, string subId, RailIndustryComponent definition)
        {
            var industry = RequireIndustry(industryId);
            var component = GetComponent(industry, subId);
            if (component == null)
            {
                AddComponent(industry, subId, definition, true);
                return;
            }

            var expectedType = ResolveComponentType(definition.Type);
            if (component.GetType() != expectedType)
            {
                RemoveComponent(industry, subId, false);
                AddComponent(industry, subId, definition, false);
                InvalidateIndustryComponents(industry);
                RefreshIndustriesAfterBatch("UpdateComponent:" + industry.identifier + "." + subId);
                return;
            }

            ApplyComponentDefinition(component, definition);
            InvalidateIndustryComponents(industry);
            IndustryComponentCache.Instance.Set(GetComponentIdentifier(industry, component), component);
            RefreshIndustriesAfterBatch("UpdateComponent:" + industry.identifier + "." + subId);
            RailEvents.RaiseIndustryComponentUpdated(component);
        }

        public static void RemoveComponent(string industryId, string subId)
        {
            var industry = RequireIndustry(industryId);
            RemoveComponent(industry, subId, true);
        }

        private static void RemoveComponent(Industry industry, string subId, bool notify)
        {
            var component = GetComponent(industry, subId);
            if (component == null)
            {
                return;
            }

            var identifier = GetComponentIdentifier(industry, component);
            component.subIdentifier = string.Empty;
            if (component.gameObject == industry.gameObject)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
            else
            {
                component.gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(component.gameObject);
            }

            IndustryComponentCache.Instance.Remove(identifier);
            if (notify)
            {
                InvalidateIndustryComponents(industry);
                RefreshIndustriesAfterBatch("RemoveComponent:" + identifier);
            }

            RailEvents.RaiseIndustryComponentRemoved(identifier);
        }

        private static IndustryComponent AddComponent(Industry industry, string subId, RailIndustryComponent definition, bool notify)
        {
            RequireId(subId, nameof(subId));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (GetComponent(industry, subId) != null)
            {
                throw new InvalidOperationException($"Industry component '{industry.identifier}.{subId}' already exists.");
            }

            var componentType = ResolveComponentType(definition.Type);
            var attachToIndustryObject = componentType == typeof(FormulaicIndustryComponent);
            var gameObject = attachToIndustryObject
                ? industry.gameObject
                : new GameObject(string.IsNullOrWhiteSpace(definition.Name) ? "Component" : definition.Name);
            if (!attachToIndustryObject)
            {
                gameObject.SetActive(false);
                gameObject.transform.SetParent(industry.transform, false);
            }

            var component = (IndustryComponent)gameObject.AddComponent(componentType);
            component.subIdentifier = subId;
            PrimeComponentIdentity(industry, component);
            ApplyComponentDefinition(component, definition);
            if (!attachToIndustryObject)
            {
                gameObject.SetActive(true);
            }

            IndustryComponentCache.Instance.Set(GetComponentIdentifier(industry, component), component);
            RailLog.Info($"RAIL created industry component '{industry.identifier}.{subId}' type='{componentType.FullName}' attachedTo='{(attachToIndustryObject ? "industry" : "child")}' host='{gameObject.name}' trackSpanCount={component.trackSpans?.Length ?? 0} loadId='{definition.LoadId ?? string.Empty}'.");
            if (notify)
            {
                InvalidateIndustryComponents(industry);
                RefreshIndustriesAfterBatch("AddComponent:" + industry.identifier + "." + subId);
            }

            RailEvents.RaiseIndustryComponentAdded(component);
            return component;
        }

        private static void AddOrUpdateComponents(Industry industry, IDictionary<string, RailIndustryComponent> components)
        {
            var wasActive = industry.gameObject.activeSelf;
            industry.gameObject.SetActive(false);
            try
            {
                var definedSubIds = new HashSet<string>(
                    components?.Keys ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                RemoveStaleComponents(industry, definedSubIds);

                if (components == null)
                {
                    return;
                }

                foreach (var component in components)
                {
                    try
                    {
                        var runtime = GetComponent(industry, component.Key);
                        if (runtime == null)
                        {
                            AddComponent(industry, component.Key, component.Value, false);
                        }
                        else if (runtime.GetType() != ResolveComponentType(component.Value.Type))
                        {
                            RemoveComponent(industry, component.Key, false);
                            AddComponent(industry, component.Key, component.Value, false);
                        }
                        else
                        {
                            ApplyComponentDefinition(runtime, component.Value);
                            IndustryComponentCache.Instance.Set(GetComponentIdentifier(industry, runtime), runtime);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogComponentLoadFailure(industry, component.Key, component.Value, ex);
                    }
                }
            }
            finally
            {
                InvalidateIndustryComponents(industry);
                industry.gameObject.SetActive(wasActive);
            }
        }

        private static void ApplyComponentDefinition(IndustryComponent component, RailIndustryComponent definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            component.name = string.IsNullOrWhiteSpace(definition.Name) ? component.subIdentifier : definition.Name;
            component.trackSpans = ResolveSpans(definition.TrackSpanIds);
            component.carTypeFilter = new CarTypeFilter(definition.CarTypeFilter ?? string.Empty);
            component.sharedStorage = definition.SharedStorage;

            var load = ResolveLoad(definition.LoadId);
            var loader = component as IndustryLoader;
            if (loader != null)
            {
                loader.load = load;
                loader.productionRate = definition.StorageChangeRate ?? loader.productionRate;
                loader.maxStorage = definition.MaxStorage ?? loader.maxStorage;
                loader.carLoadRate = definition.CarTransferRate ?? loader.carLoadRate;
                loader.orderEmpties = definition.OrderAroundEmpties ?? loader.orderEmpties;
                loader.orderAwayLoaded = definition.OrderAroundLoaded ?? loader.orderAwayLoaded;
                return;
            }

            var unloader = component as IndustryUnloader;
            if (unloader != null)
            {
                unloader.load = load;
                unloader.storageConsumptionRate = definition.StorageChangeRate ?? unloader.storageConsumptionRate;
                unloader.maxStorage = definition.MaxStorage ?? unloader.maxStorage;
                unloader.carUnloadRate = definition.CarTransferRate ?? unloader.carUnloadRate;
                unloader.orderAwayEmpties = definition.OrderAroundEmpties ?? unloader.orderAwayEmpties;
                unloader.orderLoads = definition.OrderAroundLoaded ?? unloader.orderLoads;
                return;
            }

            var formulaic = component as FormulaicIndustryComponent;
            if (formulaic != null)
            {
                formulaic.inputTerms = BuildFormulaTerms(definition.InputTermsPerDay);
                formulaic.outputTerms = BuildFormulaTerms(definition.OutputTermsPerDay);
                return;
            }

            var repairTrack = component as RepairTrack;
            if (repairTrack != null)
            {
                if (load != null)
                {
                    RepairPartsLoadField?.SetValue(repairTrack, load);
                }

                if (definition.CanOverhaul != null)
                {
                    repairTrack.canOverhaul = definition.CanOverhaul.Value;
                }

                return;
            }

            var teamTrack = component as TeamTrack;
            if (teamTrack != null)
            {
                teamTrack.idealCars = definition.IdealCars ?? teamTrack.idealCars;
                teamTrack.profile = BuildTeamTrackProfile(definition.TeamProfiles);
                return;
            }

            var interchangedLoader = component as InterchangedIndustryLoader;
            if (interchangedLoader != null)
            {
                interchangedLoader.load = load;
                return;
            }

            var interchange = component as Interchange;
            if (interchange != null)
            {
                RailLog.Info($"RAIL applied generic interchange setup for component '{DescribeComponent(component)}' trackSpanCount={component.trackSpans?.Length ?? 0}.");
                return;
            }

            var passengerStop = component as RailPassengerStopComponent;
            if (passengerStop != null)
            {
                passengerStop.PassengerStopId = definition.PassengerStopId;
                passengerStop.PassengerLoad = load;
                passengerStop.TimetableCode = definition.TimetableCode;
                passengerStop.BasePopulation = definition.BasePopulation ?? passengerStop.BasePopulation;
                passengerStop.NeighborIds = definition.NeighborIds ?? Array.Empty<string>();
                passengerStop.Branch = definition.Branch;
                passengerStop.BranchDefinitions = definition.BranchDefinitions ?? Array.Empty<RailPassengerBranch>();
            }

            var appliedComponent = component as IRailAppliedComponent;
            if (appliedComponent != null)
            {
                appliedComponent.OnRailDefinitionApplied();
            }
        }

        private static Type ResolveComponentType(string type)
        {
            if (string.Equals(type, "loader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.IndustryLoader", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IndustryLoader);
            }

            if (string.Equals(type, "unloader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.IndustryUnloader", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IndustryUnloader);
            }

            if (string.Equals(type, "formulaic", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.FormulaicIndustryComponent", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FormulaicIndustryComponent);
            }

            if (string.Equals(type, "repairTrack", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "repair-track", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.RepairTrack", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(RepairTrack);
            }

            if (string.Equals(type, "teamTrack", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "team-track", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.TeamTrack", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(TeamTrack);
            }

            if (string.Equals(type, "interchange", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.Interchange", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(Interchange);
            }

            if (string.Equals(type, "interchangedLoader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "interchanged-loader", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Model.Ops.InterchangedIndustryLoader", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(InterchangedIndustryLoader);
            }

            if (string.Equals(type, "passengerStop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "passenger-stop", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "AlinasMapMod.PaxStationComponent", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(RailPassengerStopComponent);
            }

            throw new NotSupportedException($"Industry component type '{type}' is not implemented yet.");
        }

        private static List<FormulaicIndustryComponent.Term> BuildFormulaTerms(IDictionary<string, float> terms)
        {
            var result = new List<FormulaicIndustryComponent.Term>();
            if (terms == null)
            {
                return result;
            }

            foreach (var term in terms)
            {
                var load = ResolveLoad(term.Key);
                if (load == null)
                {
                    continue;
                }

                result.Add(new FormulaicIndustryComponent.Term
                {
                    load = load,
                    unitsPerDay = term.Value
                });
            }

            return result;
        }

        private static TeamTrackProfile BuildTeamTrackProfile(IDictionary<string, RailTeamTrackEntry> entries)
        {
            var profile = ScriptableObject.CreateInstance<TeamTrackProfile>();
            profile.entries = new List<TeamTrackProfile.Entry>();
            if (entries == null)
            {
                return profile;
            }

            foreach (var entry in entries.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var resolvedLoad = ResolveLoad(entry.Value?.LoadId);
                profile.entries.Add(new TeamTrackProfile.Entry
                {
                    tag = entry.Key,
                    export = entry.Value != null && entry.Value.IsExport,
                    load = resolvedLoad,
                    loadingTime = entry.Value?.LoadingTimeDays ?? 1f,
                    carTypeFilter = new CarTypeFilter(entry.Value?.CarTypeFilter ?? string.Empty)
                });
            }

            return profile;
        }

        private static TrackSpan[] ResolveSpans(string[] spanIds)
        {
            if (spanIds == null || spanIds.Length == 0)
            {
                return Array.Empty<TrackSpan>();
            }

            var spans = new List<TrackSpan>();
            foreach (var id in spanIds)
            {
                var span = TrackAPI.GetSpan(id);
                if (span == null)
                {
                    RailLog.Warning($"RAIL track span '{id}' was not found while resolving industry component spans; continuing without it.");
                    continue;
                }

                spans.Add(span);
            }

            return spans.ToArray();
        }

        private static Load ResolveLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var load = CarPrototypeLibrary.instance?.LoadForId(loadId);
            if (load == null)
            {
                RailLog.Warning($"RAIL load '{loadId}' was not found while resolving industry component load data; continuing with null load.");
                return null;
            }

            LoadCache.Instance.Set(load.id, load);
            return load;
        }

        private static void RemoveStaleComponents(Industry industry, ISet<string> definedSubIds)
        {
            if (industry == null)
            {
                return;
            }

            var staleSubIds = industry
                .GetComponentsInChildren<IndustryComponent>(true)
                .Where(component =>
                    component != null &&
                    !string.IsNullOrWhiteSpace(component.subIdentifier) &&
                    (definedSubIds == null || !definedSubIds.Contains(component.subIdentifier)))
                .Select(component => component.subIdentifier)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var subId in staleSubIds)
            {
                RailLog.Info($"RAIL removing stale industry component '{industry.identifier}.{subId}' because it is not present in the current definition.");
                RemoveComponent(industry, subId, false);
            }
        }

        private static void LogComponentLoadFailure(Industry industry, string subId, RailIndustryComponent definition, Exception ex)
        {
            var spanIds = definition?.TrackSpanIds == null
                ? string.Empty
                : string.Join(",", definition.TrackSpanIds);
            RailLog.Warning(
                $"RAIL failed to load industry component industry='{industry?.identifier ?? "<unknown>"}' " +
                $"subId='{subId ?? string.Empty}' type='{definition?.Type ?? string.Empty}' " +
                $"loadId='{definition?.LoadId ?? string.Empty}' trackSpanIds='{spanIds}' " +
                $"error='{ex?.Message ?? "<no message>"}'");
        }

        private static Industry RequireIndustry(string id)
        {
            var industry = GetIndustry(id);
            if (industry == null)
            {
                throw new InvalidOperationException($"Industry '{id}' was not found.");
            }

            return industry;
        }

        private static IndustryComponent GetComponent(Industry industry, string subId)
        {
            return industry.GetComponentsInChildren<IndustryComponent>(true).FirstOrDefault(component => component.subIdentifier == subId);
        }

        private static Transform GetIndustryRoot(RailIndustry definition)
        {
            var areas = UnityEngine.Object.FindObjectsOfType<Area>(true);
            if (!string.IsNullOrWhiteSpace(definition?.AreaId))
            {
                var matchedArea = TrackAPI.GetArea(definition.AreaId) ?? areas.FirstOrDefault(area =>
                    area != null &&
                    (string.Equals(area.identifier, definition.AreaId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(area.name, definition.AreaId, StringComparison.OrdinalIgnoreCase)));
                if (matchedArea != null)
                {
                    return matchedArea.transform;
                }

                var nearestArea = areas
                    .Where(area => area != null)
                    .OrderBy(area => (area.transform.localPosition - definition.Position).sqrMagnitude)
                    .FirstOrDefault();
                if (nearestArea != null)
                {
                    RailLog.Warning($"RAIL could not find Area '{definition.AreaId}' for industry '{definition.Name ?? "<unnamed>"}'; using nearest Area '{nearestArea.identifier ?? nearestArea.name}'.");
                    return nearestArea.transform;
                }
            }
            else
            {
                var firstArea = areas.FirstOrDefault(area => area != null);
                if (firstArea != null)
                {
                    return firstArea.transform;
                }
            }

            if (OpsController.Shared != null)
            {
                return OpsController.Shared.transform;
            }

            if (_fallbackRoot == null)
            {
                _fallbackRoot = new GameObject("RAIL Industries").transform;
                UnityEngine.Object.DontDestroyOnLoad(_fallbackRoot.gameObject);
            }

            return _fallbackRoot;
        }

        private static void InvalidateIndustryComponents(Industry industry)
        {
            if (industry == null)
            {
                return;
            }

            var clearedIndustryCache = IndustryCachedComponentsField != null;
            IndustryCachedComponentsField?.SetValue(industry, null);

            var refreshedCount = 0;
            foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true))
            {
                if (component == null || string.IsNullOrWhiteSpace(component.subIdentifier))
                {
                    continue;
                }

                CachedIndustryField?.SetValue(component, null);
                ComponentIdentifierField?.SetValue(component, null);
                PrimeComponentIdentity(industry, component);
                refreshedCount++;
            }

            RailLog.Info($"RAIL invalidated industry component caches for '{industry.identifier}' cachedComponentsCleared={clearedIndustryCache} componentIdentityRefreshed={refreshedCount}.");
        }

        private static string GetComponentIdentifier(Industry industry, IndustryComponent component)
        {
            if (industry == null)
            {
                throw new ArgumentNullException(nameof(industry));
            }

            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            PrimeComponentIdentity(industry, component);
            return component.Identifier;
        }

        private static void PrimeComponentIdentity(Industry industry, IndustryComponent component)
        {
            if (industry == null || component == null)
            {
                return;
            }

            CachedIndustryField?.SetValue(component, industry);
            ComponentIdentifierField?.SetValue(component, industry.identifier + "." + component.subIdentifier);
        }

        internal static void RefreshIndustriesAfterBatch(string source)
        {
            ApplyIndustryOrdering();
            Messenger.Default.Send(default(IndustriesDidChange));
            IndustryCache.Instance.Rebuild();
            IndustryComponentCache.Instance.Rebuild();
            var industryCount = UnityEngine.Object.FindObjectsOfType<Industry>(true).Length;
            var componentCount = UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true).Length;
            RailLog.Info($"RAIL refreshed industries after '{source}' sceneIndustryCount={industryCount} sceneIndustryComponentCount={componentCount} cacheIndustryCount={IndustryCache.Instance.Count} cacheIndustryComponentCount={IndustryComponentCache.Instance.Count}.");
            foreach (var industryId in RailCreatedIndustryIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var industry = GetIndustry(industryId);
                if (industry == null)
                {
                    RailLog.Warning($"RAIL-created industry '{industryId}' was not found after '{source}'.");
                    continue;
                }

                var railComponentCount = industry.GetComponentsInChildren<IndustryComponent>(true)
                    .Count(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier));
                RailLog.Info($"RAIL-created industry '{industryId}' name='{industry.name}' componentCount={railComponentCount}.");
            }
        }

        internal static string LocationPanelSortKey(Industry industry, string fallback)
        {
            if (industry != null &&
                !string.IsNullOrWhiteSpace(industry.identifier) &&
                IndustryOrders.TryGetValue(industry.identifier, out var order))
            {
                return order.ToString("D8") + "|" + (fallback ?? string.Empty);
            }

            return "Z|" + (fallback ?? string.Empty);
        }

        private static void ApplyIndustryOrdering()
        {
            var areas = UnityEngine.Object.FindObjectsOfType<Area>(true);
            var orderedCount = 0;
            foreach (var area in areas)
            {
                if (area == null)
                {
                    continue;
                }

                var orderedIndustries = area.GetComponentsInChildren<Industry>(true)
                    .Where(industry =>
                        industry != null &&
                        industry.transform.parent == area.transform &&
                        !string.IsNullOrWhiteSpace(industry.identifier) &&
                        IndustryOrders.ContainsKey(industry.identifier))
                    .OrderBy(industry => IndustryOrders[industry.identifier])
                    .ThenBy(industry => industry.name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (orderedIndustries.Length == 0)
                {
                    continue;
                }

                var firstIndex = orderedIndustries.Min(industry => industry.transform.GetSiblingIndex());
                for (var index = 0; index < orderedIndustries.Length; index++)
                {
                    orderedIndustries[index].transform.SetSiblingIndex(firstIndex + index);
                }

                orderedCount += orderedIndustries.Length;
            }

            if (orderedCount > 0)
            {
                RailLog.Info($"RAIL applied industry ordering for {orderedCount} industry object(s).");
            }
        }

        private static void RememberIndustryOrder(string id, int? order)
        {
            if (order.HasValue)
            {
                IndustryOrders[id] = order.Value;
                return;
            }

            IndustryOrders.Remove(id);
        }

        private static string DescribeIndustryParent(Transform parent)
        {
            if (parent == null)
            {
                return "<none>";
            }

            var area = parent.GetComponent<Area>();
            if (area != null)
            {
                return $"{parent.name} (Area id='{area.identifier ?? string.Empty}')";
            }

            var ops = parent.GetComponent<OpsController>();
            if (ops != null)
            {
                return $"{parent.name} (OpsController)";
            }

            return $"{parent.name} ({parent.gameObject.GetType().Name})";
        }

        private static string DescribeComponent(IndustryComponent component)
        {
            if (component == null)
            {
                return "<null>";
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(component.Identifier))
                {
                    return component.Identifier;
                }
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(component.subIdentifier) ? component.name : component.subIdentifier;
        }

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
