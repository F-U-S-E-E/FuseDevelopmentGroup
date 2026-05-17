using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Events;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace FUSE.API
{
    public static class IndustryAPI
    {
        private const string LegacyEmptyComponentType = "ConfusingSupplements.IndustryComponents.Empty";

        private static readonly FieldInfo IndustryRuntimeComponentsField = typeof(Industry).GetField("_cachedComponents", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedIndustryField = typeof(IndustryComponent).GetField("_cachedIndustry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ComponentIdentifierField = typeof(IndustryComponent).GetField("_identifier", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RepairPartsLoadField = typeof(RepairTrack).GetField("repairPartsLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<string, int> IndustryOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> FuseCreatedIndustryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int _industryApplyBatchDepth;
        private static bool _industryRefreshPending;
        private static Transform _fallbackRoot;

        public static Industry AddIndustry(string id, FuseIndustry definition)
        {
            return AddIndustry(id, definition, true);
        }

        internal static Industry AddIndustry(string id, FuseIndustry definition, bool notify)
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
            FuseCreatedIndustryIds.Add(id);
            FuseIndustryRuntimeIndex.Instance.Set(id, industry);
            FuseLog.Info($"FUSE created industry '{id}' name='{displayName}' parent='{DescribeIndustryParent(root)}' componentDefinitionCount={definition.Components?.Count ?? 0}.");
            AddOrUpdateComponents(industry, definition.Components, definition.MergeComponents);
            gameObject.SetActive(true);
            if (notify)
            {
                RefreshIndustriesAfterBatch("AddIndustry:" + id);
            }

            FuseEvents.RaiseIndustryAdded(industry);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Industry, id, definition);
            return industry;
        }

        public static void UpdateIndustry(string id, FuseIndustry definition)
        {
            UpdateIndustry(id, definition, true);
        }

        internal static void UpdateIndustry(string id, FuseIndustry definition, bool notify)
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
                FuseLog.Info($"FUSE reparented industry '{id}' to '{DescribeIndustryParent(root)}'.");
            }

            industry.gameObject.name = displayName;
            industry.name = displayName;
            industry.transform.localPosition = definition.Position;
            industry.transform.localRotation = Quaternion.Euler(definition.Rotation);
            industry.usesContract = definition.UsesContract;
            RememberIndustryOrder(id, definition.Order);
            FuseCreatedIndustryIds.Add(id);
            AddOrUpdateComponents(industry, definition.Components, definition.MergeComponents);
            FuseIndustryRuntimeIndex.Instance.Set(id, industry);
            if (notify)
            {
                RefreshIndustriesAfterBatch("UpdateIndustry:" + id);
            }

            FuseEvents.RaiseIndustryUpdated(industry);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.Industry, id, definition);
        }

        public static void RemoveIndustry(string id)
        {
            var industry = RequireIndustry(id);
            DestroyIndustryInternal(id, industry);
        }

        /// <summary>
        /// Removes the industry with <paramref name="id"/> if it exists. Returns false when the
        /// industry is not currently in the scene/cache — used by the legacy-conversion apply
        /// path so that "industries: { id: null }" directives can be expressed without
        /// throwing when the industry was already absent.
        /// </summary>
        public static bool TryRemoveIndustry(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var industry = GetIndustry(id);
            if (industry == null)
            {
                return false;
            }

            DestroyIndustryInternal(id, industry);
            return true;
        }

        private static void DestroyIndustryInternal(string id, Industry industry)
        {
            // Synchronously tear down every IndustryComponent before destroying the Industry
            // GameObject and use DestroyImmediate throughout so the runtime objects are fully
            // gone before the post-remove IndustriesDidChange notification fires. Deferred
            // Destroy leaves the dead Industry alive until end-of-frame; downstream
            // IndustriesDidChange handlers can then still walk it and re-activate scene clones
            // by name match, defeating package mandelas that disabled the matching scenery.
            industry.gameObject.SetActive(false);

            var components = industry.GetComponentsInChildren<IndustryComponent>(true);
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                if (component == null)
                {
                    continue;
                }

                var subIdentifier = component.subIdentifier;
                if (!string.IsNullOrWhiteSpace(subIdentifier))
                {
                    FuseIndustryComponentRuntimeIndex.Instance.Remove(GetComponentIdentifier(industry, component));
                }

                UnityEngine.Object.DestroyImmediate(component);
            }

            UnityEngine.Object.DestroyImmediate(industry.gameObject);
            FuseIndustryRuntimeIndex.Instance.Remove(id);
            FuseCreatedIndustryIds.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Industry, id);
            RefreshIndustriesAfterBatch("RemoveIndustry:" + id);
            FuseEvents.RaiseIndustryRemoved(id);
        }

        public static Industry GetIndustry(string id)
        {
            if (FuseIndustryRuntimeIndex.Instance.TryGetValue(id, out var cached) && cached != null)
            {
                return (Industry)cached;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                var sceneMatch = UnityEngine.Object.FindObjectsOfType<Industry>(true)
                    .FirstOrDefault(industry => industry != null && string.Equals(industry.identifier, id, StringComparison.OrdinalIgnoreCase));
                if (sceneMatch != null)
                {
                    FuseIndustryRuntimeIndex.Instance.Set(sceneMatch.identifier, sceneMatch);
                    return sceneMatch;
                }
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

            return FuseCacheRegistry.IsReady && !string.IsNullOrWhiteSpace(id)
                ? UnityEngine.Object.FindObjectsOfType<Industry>(true).FirstOrDefault(industry => industry.identifier == id)
                : null;
        }

        public static IEnumerable<Industry> GetAllIndustries()
        {
            return UnityEngine.Object.FindObjectsOfType<Industry>();
        }

        public static FuseIndustry GetIndustryDefinition(string id)
        {
            return GetDefinition(GetIndustry(id));
        }

        public static FuseIndustry GetDefinition(Industry industry)
        {
            if (industry == null)
            {
                return null;
            }

            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Industry, industry.identifier, out FuseIndustry definition);
            definition = definition ?? new FuseIndustry();
            definition.Name = industry.name;
            definition.Position = industry.transform.localPosition;
            definition.Rotation = industry.transform.localEulerAngles;
            definition.UsesContract = industry.usesContract;

            var area = industry.GetComponentInParent<Area>(true);
            if (area != null)
            {
                definition.AreaId = area.identifier;
            }

            definition.Components = definition.Components ?? new Dictionary<string, FuseIndustryComponent>();
            foreach (var component in industry.GetComponentsInChildren<IndustryComponent>(true)
                         .Where(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier)))
            {
                definition.Components[component.subIdentifier] = GetDefinition(component);
            }

            return definition;
        }

        public static FuseIndustryComponent GetComponentDefinition(string industryId, string subId)
        {
            var industry = GetIndustry(industryId);
            return industry == null ? null : GetDefinition(GetComponent(industry, subId));
        }

        public static FuseIndustryComponent GetDefinition(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            var industryId = component.Industry != null ? component.Industry.identifier : string.Empty;
            var key = GetComponentDefinitionKey(industryId, component.subIdentifier);
            FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.IndustryComponent, key, out FuseIndustryComponent definition);
            definition = definition ?? new FuseIndustryComponent();
            definition.Type = GetComponentTypeAlias(component);
            definition.Name = component.name;
            definition.TrackSpanIds = component.trackSpans?
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                .Select(span => span.id)
                .ToArray();
            definition.CarTypeFilter = component.carTypeFilter.ToString();
            definition.SharedStorage = component.sharedStorage;

            var loader = component as IndustryLoader;
            if (loader != null)
            {
                definition.LoadId = loader.load != null ? loader.load.id : definition.LoadId;
                definition.StorageChangeRate = loader.productionRate;
                definition.MaxStorage = loader.maxStorage;
                definition.CarTransferRate = loader.carLoadRate;
                definition.OrderAroundEmpties = loader.orderEmpties;
                definition.OrderAroundLoaded = loader.orderAwayLoaded;
                return definition;
            }

            var loaderBase = component as IndustryLoaderBase;
            if (loaderBase != null)
            {
                definition.LoadId = loaderBase.load != null ? loaderBase.load.id : definition.LoadId;
                definition.StorageChangeRate = loaderBase.productionRate;
                definition.MaxStorage = loaderBase.maxStorage;
                definition.OrderAroundEmpties = loaderBase.orderEmpties;
            }

            var unloader = component as IndustryUnloader;
            if (unloader != null)
            {
                definition.LoadId = unloader.load != null ? unloader.load.id : definition.LoadId;
                definition.StorageChangeRate = unloader.storageConsumptionRate;
                definition.MaxStorage = unloader.maxStorage;
                definition.CarTransferRate = unloader.carUnloadRate;
                definition.OrderAroundEmpties = unloader.orderAwayEmpties;
                definition.OrderAroundLoaded = unloader.orderLoads;
                return definition;
            }

            var formulaic = component as FormulaicIndustryComponent;
            if (formulaic != null)
            {
                definition.InputTermsPerDay = ToFormulaTerms(formulaic.inputTerms);
                definition.OutputTermsPerDay = ToFormulaTerms(formulaic.outputTerms);
                return definition;
            }

            var repairTrack = component as RepairTrack;
            if (repairTrack != null)
            {
                definition.CanOverhaul = repairTrack.canOverhaul;
                var repairLoad = RepairPartsLoadField?.GetValue(repairTrack) as Load;
                definition.LoadId = repairLoad != null ? repairLoad.id : definition.LoadId;
                return definition;
            }

            if (IsType(component, "Model.Ops.TeleportLoadingIndustry"))
            {
                ReadTeleportLoadingFields(component, definition);
                return definition;
            }

            var fuseInterchangedUnloader = component as FuseInterchangedIndustryUnloader;
            if (fuseInterchangedUnloader != null)
            {
                definition.LoadId = fuseInterchangedUnloader.load != null ? fuseInterchangedUnloader.load.id : definition.LoadId;
                return definition;
            }

            if (IsType(component, "Model.Ops.InterchangedIndustryUnloader"))
            {
                var unloaderLoad = ReadObjectField(component, "load") as Load;
                definition.LoadId = unloaderLoad != null ? unloaderLoad.id : definition.LoadId;
                return definition;
            }

            var passengerStop = component as FusePassengerStopComponent;
            if (passengerStop != null)
            {
                definition.PassengerStopId = passengerStop.PassengerStopId;
                definition.TimetableCode = passengerStop.TimetableCode;
                definition.BasePopulation = passengerStop.BasePopulation;
                definition.NeighborIds = passengerStop.NeighborIds;
                definition.Branch = passengerStop.Branch;
                definition.BranchDefinitions = passengerStop.BranchDefinitions;
                definition.LoadId = passengerStop.PassengerLoad != null ? passengerStop.PassengerLoad.id : definition.LoadId;
            }

            return definition;
        }

        public static IndustryComponent AddComponent(string industryId, string subId, FuseIndustryComponent definition)
        {
            return AddComponent(RequireIndustry(industryId), subId, definition, true);
        }

        public static void UpdateComponent(string industryId, string subId, FuseIndustryComponent definition)
        {
            var industry = RequireIndustry(industryId);
            var component = GetComponent(industry, subId) ?? ResolveLegacyComponentAlias(industry, subId, definition, null);
            if (component == null)
            {
                if (definition?.Partial == true && string.IsNullOrWhiteSpace(definition.Type))
                {
                    var materialized = MaterializeMissingPartialComponent(industry, subId, definition);
                    if (materialized == null)
                    {
                        FuseLog.Warning(
                            $"FUSE skipped partial industry component patch '{industry.identifier}.{subId}' " +
                            "because no existing runtime component matched it.");
                        return;
                    }

                    FuseLog.Warning(
                        $"FUSE materialized legacy partial industry component '{industry.identifier}.{subId}' " +
                        $"as type='{materialized.Type}' because no existing runtime component matched it.");
                    AddComponent(industry, subId, materialized, true);
                    return;
                }

                AddComponent(industry, subId, definition, true);
                return;
            }

            if (definition.Partial)
            {
                ApplyPartialComponentDefinition(component, definition);
                InvalidateIndustryComponents(industry);
                FuseIndustryComponentRuntimeIndex.Instance.Set(GetComponentIdentifier(industry, component), component);
                RefreshIndustriesAfterBatch("UpdateComponent:" + industry.identifier + "." + subId);
                FuseEvents.RaiseIndustryComponentUpdated(component);
                FuseApiPersistence.RecordDefinition(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, subId), definition);
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
            FuseIndustryComponentRuntimeIndex.Instance.Set(GetComponentIdentifier(industry, component), component);
            RefreshIndustriesAfterBatch("UpdateComponent:" + industry.identifier + "." + subId);
            FuseEvents.RaiseIndustryComponentUpdated(component);
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, subId), definition);
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

            FuseIndustryComponentRuntimeIndex.Instance.Remove(identifier);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, subId));
            if (notify)
            {
                InvalidateIndustryComponents(industry);
                RefreshIndustriesAfterBatch("RemoveComponent:" + identifier);
            }

            FuseEvents.RaiseIndustryComponentRemoved(identifier);
        }

        private static IndustryComponent AddComponent(Industry industry, string subId, FuseIndustryComponent definition, bool notify)
        {
            subId = NormalizeComponentSubId(industry, subId, definition, null);
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
            var attachToIndustryObject = typeof(FormulaicIndustryComponent).IsAssignableFrom(componentType);
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

            FuseIndustryComponentRuntimeIndex.Instance.Set(GetComponentIdentifier(industry, component), component);
            FuseLog.Info($"FUSE created industry component '{industry.identifier}.{subId}' type='{componentType.FullName}' attachedTo='{(attachToIndustryObject ? "industry" : "child")}' host='{gameObject.name}' trackSpanCount={component.trackSpans?.Length ?? 0} loadId='{definition.LoadId ?? string.Empty}'.");
            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, subId), definition);
            if (notify)
            {
                InvalidateIndustryComponents(industry);
                RefreshIndustriesAfterBatch("AddComponent:" + industry.identifier + "." + subId);
            }

            FuseEvents.RaiseIndustryComponentAdded(component);
            return component;
        }

        private static IDictionary<string, FuseIndustryComponent> NormalizeComponentDefinitions(Industry industry, IDictionary<string, FuseIndustryComponent> components)
        {
            if (components == null)
            {
                return null;
            }

            var normalized = new Dictionary<string, FuseIndustryComponent>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in components)
            {
                var subId = NormalizeComponentSubId(industry, pair.Key, pair.Value, normalized);
                normalized[subId] = pair.Value;
            }

            return normalized;
        }

        private static string NormalizeComponentSubId(Industry industry, string requestedSubId, FuseIndustryComponent definition, IDictionary<string, FuseIndustryComponent> existing)
        {
            var subId = (requestedSubId ?? string.Empty).Trim();
            if (subId.Length > 0)
            {
                return MakeUniqueExplicitComponentSubId(subId, existing);
            }

            var normalizedType = FuseIndustryComponentTypes.Normalize(definition?.Type);
            if (string.Equals(normalizedType, FuseIndustryComponentTypes.Formulaic, StringComparison.OrdinalIgnoreCase))
            {
                subId = "formula";
            }
            else if (string.Equals(normalizedType, FuseIndustryComponentTypes.RepairTrack, StringComparison.OrdinalIgnoreCase))
            {
                subId = "repair";
            }
            else if (string.Equals(normalizedType, FuseIndustryComponentTypes.TeamTrack, StringComparison.OrdinalIgnoreCase))
            {
                subId = "teamtrack";
            }
            else if (!string.IsNullOrWhiteSpace(definition?.LoadId))
            {
                subId = SanitizeComponentSubId(definition.LoadId);
            }
            else if (!string.IsNullOrWhiteSpace(definition?.Name))
            {
                subId = SanitizeComponentSubId(definition.Name);
            }
            else
            {
                subId = "component";
            }

            subId = MakeUniqueComponentSubId(subId, existing);
            FuseLog.Warning(
                $"FUSE normalized empty legacy industry component id for industry '{industry?.identifier ?? "<unknown>"}' " +
                $"type='{definition?.Type ?? string.Empty}' name='{definition?.Name ?? string.Empty}' to subId='{subId}'.");
            return subId;
        }

        private static string MakeUniqueComponentSubId(string preferred, IDictionary<string, FuseIndustryComponent> existing)
        {
            var baseId = SanitizeComponentSubId(preferred);
            if (string.IsNullOrWhiteSpace(baseId))
            {
                baseId = "component";
            }

            if (existing == null || !existing.ContainsKey(baseId))
            {
                return baseId;
            }

            var index = 2;
            while (existing.ContainsKey(baseId + "-" + index))
            {
                index++;
            }

            return baseId + "-" + index;
        }

        private static string MakeUniqueExplicitComponentSubId(string preferred, IDictionary<string, FuseIndustryComponent> existing)
        {
            var baseId = (preferred ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseId))
            {
                return MakeUniqueComponentSubId("component", existing);
            }

            if (existing == null || !existing.ContainsKey(baseId))
            {
                return baseId;
            }

            var index = 2;
            while (existing.ContainsKey(baseId + "-" + index))
            {
                index++;
            }

            return baseId + "-" + index;
        }

        private static string SanitizeComponentSubId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value.Trim()
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
                .ToArray();
            var collapsed = new string(chars);
            while (collapsed.Contains("--"))
            {
                collapsed = collapsed.Replace("--", "-");
            }

            return collapsed.Trim('-');
        }

        private static void AddOrUpdateComponents(Industry industry, IDictionary<string, FuseIndustryComponent> components, bool mergeComponents)
        {
            components = NormalizeComponentDefinitions(industry, components);
            var wasActive = industry.gameObject.activeSelf;
            industry.gameObject.SetActive(false);
            try
            {
                var definedSubIds = new HashSet<string>(
                    components?.Keys ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                if (mergeComponents)
                {
                    FuseLog.Info($"FUSE merging industry components for '{industry.identifier}' without stale removal.");
                }
                else
                {
                    RemoveStaleComponents(industry, definedSubIds);
                }

                if (components == null)
                {
                    return;
                }

                foreach (var component in components)
                {
                    try
                    {
                        var runtime = GetComponent(industry, component.Key) ?? ResolveLegacyComponentAlias(industry, component.Key, component.Value, definedSubIds);
                        if (runtime == null)
                        {
                            if (component.Value?.Partial == true && string.IsNullOrWhiteSpace(component.Value.Type))
                            {
                                var materialized = MaterializeMissingPartialComponent(industry, component.Key, component.Value);
                                if (materialized == null)
                                {
                                    FuseLog.Warning(
                                        $"FUSE skipped partial industry component patch '{industry.identifier}.{component.Key}' " +
                                        "because no existing runtime component matched it.");
                                    continue;
                                }

                                FuseLog.Warning(
                                    $"FUSE materialized legacy partial industry component '{industry.identifier}.{component.Key}' " +
                                    $"as type='{materialized.Type}' because no existing runtime component matched it.");
                                AddComponent(industry, component.Key, materialized, false);
                                continue;
                            }

                            AddComponent(industry, component.Key, component.Value, false);
                        }
                        else if (component.Value?.Partial == true)
                        {
                            ApplyPartialComponentDefinition(runtime, component.Value);
                            FuseIndustryComponentRuntimeIndex.Instance.Set(GetComponentIdentifier(industry, runtime), runtime);
                            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, component.Key), component.Value);
                        }
                        else if (runtime.GetType() != ResolveComponentType(component.Value.Type))
                        {
                            RemoveComponent(industry, component.Key, false);
                            AddComponent(industry, component.Key, component.Value, false);
                        }
                        else
                        {
                            ApplyComponentDefinition(runtime, component.Value);
                            FuseIndustryComponentRuntimeIndex.Instance.Set(GetComponentIdentifier(industry, runtime), runtime);
                            FuseApiPersistence.RecordDefinition(FuseDefinitionKind.IndustryComponent, GetComponentDefinitionKey(industry.identifier, component.Key), component.Value);
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

        private static FuseIndustryComponent MaterializeMissingPartialComponent(Industry industry, string subId, FuseIndustryComponent definition)
        {
            if (definition == null ||
                !definition.Partial ||
                !string.IsNullOrWhiteSpace(definition.Type) ||
                !HasTrackSpanPatch(definition))
            {
                return null;
            }

            var materialized = CloneComponentDefinition(definition);
            materialized.Partial = false;
            var inferredLoad = InferLegacyPartialComponentLoad(industry, subId, materialized);
            if (string.IsNullOrWhiteSpace(materialized.LoadId) &&
                !string.IsNullOrWhiteSpace(inferredLoad?.LoadId))
            {
                materialized.LoadId = inferredLoad.LoadId;
            }

            var snapshot = FuseBaseGameIndustrySnapshot.Find(industry?.identifier, subId);
            if (snapshot != null)
            {
                FuseLog.Info(
                    $"FUSE recovered destroyed base-game component '{industry.identifier}.{subId}' " +
                    $"from snapshot type='{snapshot.ComponentTypeFullName}' loadId='{snapshot.LoadId ?? "<null>"}' " +
                    $"existingSpans=[{string.Join(",", snapshot.TrackSpanIds)}].");

                if (string.IsNullOrWhiteSpace(materialized.LoadId) &&
                    !string.IsNullOrWhiteSpace(snapshot.LoadId))
                {
                    materialized.LoadId = snapshot.LoadId;
                }

                if (string.IsNullOrWhiteSpace(materialized.Name) &&
                    !string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    materialized.Name = snapshot.Name;
                }

                materialized.Type = ResolveSnapshotComponentTypeAlias(snapshot.ComponentTypeFullName)
                    ?? InferMissingPartialComponentType(subId, inferredLoad);

                MergeSnapshotTrackSpansIntoMaterialized(materialized, snapshot.TrackSpanIds);
            }
            else
            {
                materialized.Type = InferMissingPartialComponentType(subId, inferredLoad);
            }
            var legacyInterchangeTarget = string.Equals(
                    FuseIndustryComponentTypes.Normalize(materialized.Type),
                    FuseIndustryComponentTypes.Interchange,
                    StringComparison.OrdinalIgnoreCase)
                ? FindLegacyInterchangeMaterializationTarget(industry, definition)
                : null;
            if (legacyInterchangeTarget != null)
            {
                var targetSpanIds = legacyInterchangeTarget.trackSpans?
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id)
                    .ToArray();
                if (targetSpanIds != null && targetSpanIds.Length > 0)
                {
                    materialized.TrackSpanIds = targetSpanIds;
                    materialized.TrackSpanPatch = null;
                }
            }

            if (string.IsNullOrWhiteSpace(materialized.Name))
            {
                materialized.Name = InferMissingPartialComponentName(industry, subId, materialized, legacyInterchangeTarget);
            }

            if (string.IsNullOrWhiteSpace(materialized.CarTypeFilter) &&
                ShouldDefaultMaterializedCarTypeFilter(materialized.Type))
            {
                materialized.CarTypeFilter = "*";
            }

            return materialized;
        }

        private static string ResolveSnapshotComponentTypeAlias(string componentTypeFullName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeFullName))
            {
                return null;
            }

            // Map the runtime IndustryComponent System.Type.FullName onto the FUSE type
            // alias the rest of the materialization pipeline expects. We avoid hardcoding
            // the assembly-qualified form so the materialized definition stays consistent
            // with what the converter produces.
            if (componentTypeFullName.EndsWith("IndustryUnloader", StringComparison.Ordinal) ||
                componentTypeFullName.EndsWith(".IndustryUnloader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Unloader;
            }

            if (componentTypeFullName.EndsWith("IndustryLoader", StringComparison.Ordinal) ||
                componentTypeFullName.EndsWith(".IndustryLoader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Loader;
            }

            if (componentTypeFullName.EndsWith("InterchangedIndustryUnloader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.InterchangedUnloader;
            }

            if (componentTypeFullName.EndsWith("InterchangedIndustryLoader", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.InterchangedLoader;
            }

            if (componentTypeFullName.EndsWith("Interchange", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Interchange;
            }

            if (componentTypeFullName.EndsWith("RepairTrack", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.RepairTrack;
            }

            if (componentTypeFullName.EndsWith("TeamTrack", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.TeamTrack;
            }

            if (componentTypeFullName.EndsWith("FormulaicIndustryComponent", StringComparison.Ordinal))
            {
                return FuseIndustryComponentTypes.Formulaic;
            }

            return null;
        }

        private static void MergeSnapshotTrackSpansIntoMaterialized(FuseIndustryComponent materialized, string[] snapshotSpanIds)
        {
            if (materialized == null || snapshotSpanIds == null || snapshotSpanIds.Length == 0)
            {
                return;
            }

            // Prepend the snapshot's existing spans onto whatever the patch is adding, so
            // the legacy {"$add": ...} entries layer on top of the original base-game
            // configuration instead of replacing it.
            if (materialized.TrackSpanPatch != null)
            {
                materialized.TrackSpanPatch = PrependSpansToPatch(materialized.TrackSpanPatch, snapshotSpanIds);
            }

            var existingIds = materialized.TrackSpanIds ?? Array.Empty<string>();
            var combined = new List<string>(snapshotSpanIds.Length + existingIds.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spanId in snapshotSpanIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    combined.Add(spanId);
                }
            }
            foreach (var spanId in existingIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    combined.Add(spanId);
                }
            }

            materialized.TrackSpanIds = combined.ToArray();
        }

        private static FuseStringListPatch PrependSpansToPatch(FuseStringListPatch patch, string[] snapshotSpanIds)
        {
            if (patch == null)
            {
                return null;
            }

            var prepend = new List<string>(patch.Prepend ?? Array.Empty<string>());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in prepend)
            {
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    seen.Add(existing);
                }
            }

            var inserts = new List<string>();
            foreach (var spanId in snapshotSpanIds)
            {
                if (!string.IsNullOrWhiteSpace(spanId) && seen.Add(spanId))
                {
                    inserts.Add(spanId);
                }
            }

            // Insert at the front so subsequent $add / $append entries come AFTER the
            // snapshot's original spans (matching the legacy ordering).
            inserts.AddRange(prepend);
            return new FuseStringListPatch
            {
                Replace = patch.Replace,
                Prepend = inserts.ToArray(),
                Add = patch.Add,
                Append = patch.Append,
                Insert = patch.Insert,
                Remove = patch.Remove
            };
        }

        private static string InferMissingPartialComponentType(string subId, LegacyPartialLoadInference inferredLoad)
        {
            if (IsLegacyInterchangeAlias(subId))
            {
                return FuseIndustryComponentTypes.Interchange;
            }

            if (inferredLoad != null)
            {
                if (inferredLoad.IsInput && !inferredLoad.IsOutput)
                {
                    return FuseIndustryComponentTypes.Unloader;
                }

                if (inferredLoad.IsOutput && !inferredLoad.IsInput)
                {
                    return FuseIndustryComponentTypes.Loader;
                }
            }

            return LegacyEmptyComponentType;
        }

        private static string InferMissingPartialComponentName(
            Industry industry,
            string subId,
            FuseIndustryComponent definition,
            IndustryComponent legacyInterchangeTarget)
        {
            if (string.Equals(
                    FuseIndustryComponentTypes.Normalize(definition?.Type),
                    FuseIndustryComponentTypes.Interchange,
                    StringComparison.OrdinalIgnoreCase))
            {
                var targetName = ReadDisplayName(legacyInterchangeTarget);
                if (!LooksLikeRawLegacyDisplayName(targetName))
                {
                    return targetName;
                }
            }

            if (!LooksLikeRawLegacyDisplayName(industry?.name))
            {
                return industry.name;
            }

            return subId;
        }

        private sealed class LegacyPartialLoadInference
        {
            public string LoadId { get; set; }
            public bool IsInput { get; set; }
            public bool IsOutput { get; set; }
        }

        private static LegacyPartialLoadInference InferLegacyPartialComponentLoad(
            Industry industry,
            string subId,
            FuseIndustryComponent definition)
        {
            if (industry == null)
            {
                return null;
            }

            foreach (var loadId in GetLegacyPartialLoadCandidates(subId, definition))
            {
                var inference = FindFormulaLoadRole(industry, loadId);
                if (inference != null)
                {
                    return inference;
                }
            }

            var explicitLoadId = definition?.LoadId;
            return string.IsNullOrWhiteSpace(explicitLoadId)
                ? null
                : new LegacyPartialLoadInference { LoadId = explicitLoadId.Trim() };
        }

        private static IEnumerable<string> GetLegacyPartialLoadCandidates(string subId, FuseIndustryComponent definition)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in new[] { definition?.LoadId, subId })
            {
                var candidate = value?.Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static LegacyPartialLoadInference FindFormulaLoadRole(Industry industry, string loadId)
        {
            if (industry == null || string.IsNullOrWhiteSpace(loadId))
            {
                return null;
            }

            var inference = new LegacyPartialLoadInference { LoadId = loadId.Trim() };
            foreach (var formulaic in industry.GetComponentsInChildren<FormulaicIndustryComponent>(true))
            {
                if (formulaic == null)
                {
                    continue;
                }

                inference.IsInput |= ContainsFormulaLoad(formulaic.inputTerms, inference.LoadId);
                inference.IsOutput |= ContainsFormulaLoad(formulaic.outputTerms, inference.LoadId);
            }

            return inference.IsInput || inference.IsOutput ? inference : null;
        }

        private static bool ContainsFormulaLoad(IEnumerable<FormulaicIndustryComponent.Term> terms, string loadId)
        {
            if (terms == null || string.IsNullOrWhiteSpace(loadId))
            {
                return false;
            }

            return terms.Any(term =>
                term.load != null &&
                string.Equals(term.load.id, loadId, StringComparison.OrdinalIgnoreCase));
        }

        private static Interchange FindLegacyInterchangeMaterializationTarget(Industry industry, FuseIndustryComponent definition)
        {
            var requestedSpanIds = GetTrackSpanPatchReferenceIds(definition);
            if (industry == null || requestedSpanIds.Length == 0)
            {
                return null;
            }

            var requested = new HashSet<string>(requestedSpanIds, StringComparer.OrdinalIgnoreCase);
            var area = industry.GetComponentInParent<Area>(true);
            var candidates = area != null
                ? area.GetComponentsInChildren<Interchange>(true)
                : UnityEngine.Object.FindObjectsOfType<Interchange>(true);

            var target = candidates
                .Where(component => component != null)
                .Select(component => new
                {
                    Component = component,
                    Score = CountTrackSpanMatches(component, requested),
                    FullMatch = ContainsAllTrackSpanIds(component, requested)
                })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.FullMatch)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => ReadDisplayName(candidate.Component), StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Component)
                .FirstOrDefault();

            if (target != null)
            {
                FuseLog.Info(
                    $"FUSE matched missing legacy interchange component for industry '{industry.identifier}' " +
                    $"to overlapping component '{DescribeComponent(target)}'.");
            }

            return target;
        }

        private static string[] GetTrackSpanPatchReferenceIds(FuseIndustryComponent definition)
        {
            var ids = new List<string>();
            AddDistinct(ids, definition?.TrackSpanIds);
            var patch = definition?.TrackSpanPatch;
            if (patch != null)
            {
                AddDistinct(ids, patch.Replace);
                AddDistinct(ids, patch.Prepend);
                AddDistinct(ids, patch.Add);
                AddDistinct(ids, patch.Append);
                AddDistinct(ids, patch.Insert);
            }

            return ids.ToArray();
        }

        private static int CountTrackSpanMatches(IndustryComponent component, ISet<string> requestedSpanIds)
        {
            if (component?.trackSpans == null || requestedSpanIds == null || requestedSpanIds.Count == 0)
            {
                return 0;
            }

            return component.trackSpans.Count(span =>
                span != null &&
                !string.IsNullOrWhiteSpace(span.id) &&
                requestedSpanIds.Contains(span.id));
        }

        private static bool ContainsAllTrackSpanIds(IndustryComponent component, ISet<string> requestedSpanIds)
        {
            if (component?.trackSpans == null || requestedSpanIds == null || requestedSpanIds.Count == 0)
            {
                return false;
            }

            var componentSpanIds = new HashSet<string>(
                component.trackSpans
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id),
                StringComparer.OrdinalIgnoreCase);
            return requestedSpanIds.All(componentSpanIds.Contains);
        }

        private static string ReadDisplayName(IndustryComponent component)
        {
            if (component == null)
            {
                return null;
            }

            try
            {
                return component.DisplayName;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not read display name for component '{component.name}': {ex.Message}");
                return component.name;
            }
        }

        private static bool LooksLikeRawLegacyDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var text = value.Trim();
            return string.Equals(text, "t1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "interchange", StringComparison.OrdinalIgnoreCase) ||
                   (!text.Any(char.IsWhiteSpace) &&
                    text.Any(ch => ch == '-' || ch == '_' || ch == '.'));
        }

        private static bool ContainsText(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasTrackSpanPatch(FuseIndustryComponent definition)
        {
            return (definition?.TrackSpanIds != null &&
                    definition.TrackSpanIds.Any(id => !string.IsNullOrWhiteSpace(id))) ||
                   HasStringListPatch(definition?.TrackSpanPatch);
        }

        private static FuseIndustryComponent CloneComponentDefinition(FuseIndustryComponent definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new FuseIndustryComponent
            {
                Partial = definition.Partial,
                Type = definition.Type,
                Name = definition.Name,
                TrackSpanIds = definition.TrackSpanIds?.ToArray(),
                TrackSpanPatch = CloneStringListPatch(definition.TrackSpanPatch),
                CarTypeFilter = definition.CarTypeFilter,
                LoadId = definition.LoadId,
                ConvertedLoadId = definition.ConvertedLoadId,
                SharedStorage = definition.SharedStorage,
                StorageChangeRate = definition.StorageChangeRate,
                MaxStorage = definition.MaxStorage,
                CarTransferRate = definition.CarTransferRate,
                CostPerUnit = definition.CostPerUnit,
                NotBeforeHour = definition.NotBeforeHour,
                NotAfterHour = definition.NotAfterHour,
                FillPercentage = definition.FillPercentage,
                BookReasons = definition.BookReasons?.ToArray(),
                Title = definition.Title,
                OrderAroundEmpties = definition.OrderAroundEmpties,
                OrderAroundLoaded = definition.OrderAroundLoaded,
                InputSpanIds = definition.InputSpanIds?.ToArray(),
                InputTermsPerDay = definition.InputTermsPerDay == null ? null : new Dictionary<string, float>(definition.InputTermsPerDay),
                OutputTermsPerDay = definition.OutputTermsPerDay == null ? null : new Dictionary<string, float>(definition.OutputTermsPerDay),
                IdealCars = definition.IdealCars,
                TeamProfiles = definition.TeamProfiles == null ? null : new Dictionary<string, FuseTeamTrackEntry>(definition.TeamProfiles),
                CanOverhaul = definition.CanOverhaul,
                PassengerStopId = definition.PassengerStopId,
                TimetableCode = definition.TimetableCode,
                BasePopulation = definition.BasePopulation,
                NeighborIds = definition.NeighborIds?.ToArray(),
                Branch = definition.Branch,
                BranchDefinitions = definition.BranchDefinitions?.ToArray(),
                OutputSpanIds = definition.OutputSpanIds?.ToArray(),
                CarLoadPeriod = definition.CarLoadPeriod,
                CarLengthFeet = definition.CarLengthFeet,
                Fields = definition.Fields == null ? null : new Dictionary<string, object>(definition.Fields)
            };
        }

        private static FuseStringListPatch CloneStringListPatch(FuseStringListPatch patch)
        {
            if (patch == null)
            {
                return null;
            }

            return new FuseStringListPatch
            {
                Add = patch.Add?.ToArray(),
                Append = patch.Append?.ToArray(),
                Prepend = patch.Prepend?.ToArray(),
                Insert = patch.Insert?.ToArray(),
                Replace = patch.Replace?.ToArray(),
                Remove = patch.Remove?.ToArray()
            };
        }

        private static bool HasStringListPatch(FuseStringListPatch patch)
        {
            return patch != null &&
                   (patch.Add != null ||
                    patch.Append != null ||
                    patch.Prepend != null ||
                    patch.Insert != null ||
                    patch.Replace != null ||
                    patch.Remove != null);
        }

        private static TrackSpan[] ApplyTrackSpanPatch(TrackSpan[] current, FuseStringListPatch patch)
        {
            if (!HasStringListPatch(patch))
            {
                return current ?? Array.Empty<TrackSpan>();
            }

            var ids = new List<string>();
            if (patch.Replace != null)
            {
                AddDistinct(ids, patch.Replace);
            }
            else if (current != null)
            {
                AddDistinct(ids, current
                    .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                    .Select(span => span.id));
            }

            PrependDistinct(ids, patch.Prepend);
            AddDistinct(ids, patch.Add);
            AddDistinct(ids, patch.Append);
            AddDistinct(ids, patch.Insert);
            RemoveIds(ids, patch.Remove);
            return ResolveSpans(ids.ToArray());
        }

        private static void AddDistinct(ICollection<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var seen = new HashSet<string>(target.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var id = value?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    target.Add(id);
                }
            }
        }

        private static void PrependDistinct(List<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var prepend = new List<string>();
            var seen = new HashSet<string>(target.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var id = value?.Trim();
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                {
                    prepend.Add(id);
                }
            }

            if (prepend.Count > 0)
            {
                target.InsertRange(0, prepend);
            }
        }

        private static void RemoveIds(ICollection<string> target, IEnumerable<string> values)
        {
            if (target == null || values == null)
            {
                return;
            }

            var removals = new HashSet<string>(
                values.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (removals.Count == 0)
            {
                return;
            }

            var retained = target.Where(id => !removals.Contains(id ?? string.Empty)).ToArray();
            target.Clear();
            foreach (var id in retained)
            {
                target.Add(id);
            }
        }

        private static string ResolveCarTypeFilter(IndustryComponent component, string value, bool isPassengerStop)
        {
            if (isPassengerStop && string.IsNullOrWhiteSpace(value))
            {
                return "*";
            }

            if (component is Interchange && string.IsNullOrWhiteSpace(value))
            {
                return "*";
            }

            return value ?? string.Empty;
        }

        private static void ApplyPartialComponentDefinition(IndustryComponent component, FuseIndustryComponent definition)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var isPassengerStop = component is FusePassengerStopComponent;
            if (!string.IsNullOrWhiteSpace(definition.Name))
            {
                component.name = definition.Name;
            }

            if (HasStringListPatch(definition.TrackSpanPatch))
            {
                component.trackSpans = ApplyTrackSpanPatch(component.trackSpans, definition.TrackSpanPatch);
            }
            else if (definition.TrackSpanIds != null && definition.TrackSpanIds.Length > 0)
            {
                component.trackSpans = MergeSpans(component.trackSpans, ResolveSpans(definition.TrackSpanIds));
            }

            if (definition.CarTypeFilter != null)
            {
                component.carTypeFilter = new CarTypeFilter(ResolveCarTypeFilter(component, definition.CarTypeFilter, isPassengerStop));
            }

            var effectiveLoadId = isPassengerStop && string.IsNullOrWhiteSpace(definition.LoadId)
                ? null
                : definition.LoadId;
            var hasLoadPatch = !string.IsNullOrWhiteSpace(effectiveLoadId);
            var load = hasLoadPatch ? ResolveLoad(effectiveLoadId) : null;

            var loader = component as IndustryLoader;
            if (loader != null)
            {
                if (hasLoadPatch)
                {
                    loader.load = load;
                }

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
                if (hasLoadPatch)
                {
                    unloader.load = load;
                }

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
                if (definition.InputTermsPerDay != null && definition.InputTermsPerDay.Count > 0)
                {
                    formulaic.inputTerms = BuildFormulaTerms(definition.InputTermsPerDay);
                }

                if (definition.OutputTermsPerDay != null && definition.OutputTermsPerDay.Count > 0)
                {
                    formulaic.outputTerms = BuildFormulaTerms(definition.OutputTermsPerDay);
                }

                return;
            }

            var repairTrack = component as RepairTrack;
            if (repairTrack != null)
            {
                if (hasLoadPatch && load != null)
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
                if (definition.TeamProfiles != null && definition.TeamProfiles.Count > 0)
                {
                    teamTrack.profile = BuildTeamTrackProfile(definition.TeamProfiles);
                }

                return;
            }

            var interchangedLoader = component as InterchangedIndustryLoader;
            if (interchangedLoader != null)
            {
                if (hasLoadPatch)
                {
                    interchangedLoader.load = load;
                }

                return;
            }

            var fuseInterchangedUnloader = component as FuseInterchangedIndustryUnloader;
            if (fuseInterchangedUnloader != null)
            {
                if (hasLoadPatch)
                {
                    fuseInterchangedUnloader.load = load;
                }

                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.InterchangedIndustryUnloader", obj =>
            {
                if (hasLoadPatch)
                {
                    ApplyOptionalLoadField(obj, load);
                }
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.TeleportLoadingIndustry", obj =>
            {
                if (hasLoadPatch)
                {
                    ApplyOptionalLoadField(obj, load);
                }

                ApplyPartialTeleportLoadingFields(obj, definition);
            }))
            {
                return;
            }

            var passengerStop = component as FusePassengerStopComponent;
            if (passengerStop != null)
            {
                passengerStop.PassengerStopId = definition.PassengerStopId ?? passengerStop.PassengerStopId;
                if (hasLoadPatch)
                {
                    passengerStop.PassengerLoad = load;
                }

                passengerStop.TimetableCode = definition.TimetableCode ?? passengerStop.TimetableCode;
                passengerStop.BasePopulation = definition.BasePopulation ?? passengerStop.BasePopulation;
                passengerStop.NeighborIds = definition.NeighborIds ?? passengerStop.NeighborIds;
                passengerStop.Branch = definition.Branch ?? passengerStop.Branch;
                passengerStop.BranchDefinitions = definition.BranchDefinitions ?? passengerStop.BranchDefinitions;
            }

            ApplyCustomIndustryComponentFields(component, definition, load);
            var appliedComponent = component as IFuseAppliedComponent;
            appliedComponent?.OnFuseDefinitionApplied();
        }

        private static void ApplyComponentDefinition(IndustryComponent component, FuseIndustryComponent definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var isPassengerStop = component is FusePassengerStopComponent;
            component.name = string.IsNullOrWhiteSpace(definition.Name) ? component.subIdentifier : definition.Name;
            component.trackSpans = HasStringListPatch(definition.TrackSpanPatch)
                ? ApplyTrackSpanPatch(component.trackSpans, definition.TrackSpanPatch)
                : ResolveSpans(definition.TrackSpanIds);
            component.carTypeFilter = new CarTypeFilter(ResolveCarTypeFilter(component, definition.CarTypeFilter, isPassengerStop));
            component.sharedStorage = definition.SharedStorage;

            var effectiveLoadId = isPassengerStop && string.IsNullOrWhiteSpace(definition.LoadId)
                ? "passengers"
                : definition.LoadId;
            var load = ResolveLoad(effectiveLoadId);
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

            var fuseInterchangedUnloader = component as FuseInterchangedIndustryUnloader;
            if (fuseInterchangedUnloader != null)
            {
                fuseInterchangedUnloader.load = load;
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.InterchangedIndustryUnloader", obj =>
            {
                ApplyOptionalLoadField(obj, load);
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.TeleportLoadingIndustry", obj =>
            {
                ApplyOptionalLoadField(obj, load);
                ApplyTeleportLoadingFields(obj, definition);
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.ProgressionIndustryComponent", obj =>
            {
                FuseLog.Info(
                    $"FUSE applied package='{definition.Type ?? "<unspecified>"}' " +
                    $"operation='industry component apply' kind='progression' " +
                    $"id='{DescribeComponent(component)}' " +
                    "message='progression industry component bound'.");
            }))
            {
                return;
            }

            var interchange = component as Interchange;
            if (interchange != null)
            {
                FuseLog.Info($"FUSE applied generic interchange setup for component '{DescribeComponent(component)}' trackSpanCount={component.trackSpans?.Length ?? 0}.");
                return;
            }

            var passengerStop = component as FusePassengerStopComponent;
            if (passengerStop != null)
            {
                passengerStop.PassengerStopId = definition.PassengerStopId;
                passengerStop.PassengerLoad = load;
                passengerStop.TimetableCode = definition.TimetableCode;
                passengerStop.BasePopulation = definition.BasePopulation ?? passengerStop.BasePopulation;
                passengerStop.NeighborIds = definition.NeighborIds ?? Array.Empty<string>();
                passengerStop.Branch = definition.Branch;
                passengerStop.BranchDefinitions = definition.BranchDefinitions ?? Array.Empty<FusePassengerBranch>();
            }

            ApplyCustomIndustryComponentFields(component, definition, load);

            var appliedComponent = component as IFuseAppliedComponent;
            if (appliedComponent != null)
            {
                appliedComponent.OnFuseDefinitionApplied();
            }
        }

        private static Type ResolveComponentType(string type)
        {
            var normalized = FuseIndustryComponentTypes.Normalize(type);
            if (string.Equals(normalized, FuseIndustryComponentTypes.Loader, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IndustryLoader);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.Unloader, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(IndustryUnloader);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.Formulaic, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FuseFormulaicIndustryComponent);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.RepairTrack, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(RepairTrack);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.TeamTrack, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(TeamTrack);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.Interchange, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(Interchange);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.InterchangedLoader, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(InterchangedIndustryLoader);
            }

            // The next three types may not exist in every game build. Resolve
            // reflectively so FUSE still compiles and runs when Assembly-CSharp
            // doesn't ship them. If the resolver returns null, we fall through
            // to the NotSupportedException at the bottom.
            if (string.Equals(normalized, FuseIndustryComponentTypes.InterchangedUnloader, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FuseInterchangedIndustryUnloader);
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.TeleportLoading, StringComparison.OrdinalIgnoreCase))
            {
                var resolved = Type.GetType("Model.Ops.TeleportLoadingIndustry, Assembly-CSharp", false, true);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.Progression, StringComparison.OrdinalIgnoreCase))
            {
                var resolved = Type.GetType("Model.Ops.ProgressionIndustryComponent, Assembly-CSharp", false, true);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            if (string.Equals(normalized, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FusePassengerStopComponent);
            }

            if (IsLegacyEmptyComponentType(normalized))
            {
                return typeof(FuseLegacyPlaceholderIndustryComponent);
            }

            var reflected = TryResolveIndustryComponentType(normalized);
            if (reflected == null && !string.Equals(normalized, type, StringComparison.OrdinalIgnoreCase))
            {
                reflected = TryResolveIndustryComponentType(type);
            }

            if (reflected != null)
            {
                return reflected;
            }

            throw new NotSupportedException($"Industry component type '{type}' is not implemented yet.");
        }

        private static bool IsLegacyEmptyComponentType(string type)
        {
            return string.Equals(FuseIndustryComponentTypes.Normalize(type), LegacyEmptyComponentType, StringComparison.OrdinalIgnoreCase);
        }

        private static Type TryResolveIndustryComponentType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var direct = Type.GetType(type + ", Assembly-CSharp", false, true);
            if (direct != null && typeof(IndustryComponent).IsAssignableFrom(direct))
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate = null;
                try
                {
                    candidate = assembly.GetType(type, false, true);
                }
                catch
                {
                    // Some plugin assemblies can throw while resolving metadata.
                }

                if (candidate != null && typeof(IndustryComponent).IsAssignableFrom(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetComponentTypeAlias(IndustryComponent component)
        {
            if (component is IndustryLoader)
            {
                return FuseIndustryComponentTypes.Loader;
            }

            if (component is IndustryUnloader)
            {
                return FuseIndustryComponentTypes.Unloader;
            }

            if (component is FormulaicIndustryComponent)
            {
                return FuseIndustryComponentTypes.Formulaic;
            }

            if (component is RepairTrack)
            {
                return FuseIndustryComponentTypes.RepairTrack;
            }

            if (component is TeamTrack)
            {
                return FuseIndustryComponentTypes.TeamTrack;
            }

            if (component is Interchange)
            {
                return FuseIndustryComponentTypes.Interchange;
            }

            if (component is InterchangedIndustryLoader)
            {
                return FuseIndustryComponentTypes.InterchangedLoader;
            }

            if (component is FuseInterchangedIndustryUnloader)
            {
                return FuseIndustryComponentTypes.InterchangedUnloader;
            }

            if (IsType(component, "Model.Ops.InterchangedIndustryUnloader"))
            {
                return FuseIndustryComponentTypes.InterchangedUnloader;
            }

            if (IsType(component, "Model.Ops.TeleportLoadingIndustry"))
            {
                return FuseIndustryComponentTypes.TeleportLoading;
            }

            if (IsType(component, "Model.Ops.ProgressionIndustryComponent"))
            {
                return FuseIndustryComponentTypes.Progression;
            }

            if (component is FusePassengerStopComponent)
            {
                return FuseIndustryComponentTypes.PassengerStop;
            }

            if (component is FuseLegacyPlaceholderIndustryComponent)
            {
                return LegacyEmptyComponentType;
            }

            return component.GetType().FullName;
        }

        // Reflection helpers for component types that may be absent in some
        // game versions. They keep the apply / read pipeline tolerant without
        // taking a hard compile-time dependency on every Model.Ops subclass.

        private static bool IsType(object instance, string fullTypeName)
        {
            if (instance == null || string.IsNullOrEmpty(fullTypeName))
            {
                return false;
            }

            var type = TryResolveIndustryComponentType(fullTypeName);
            return type != null && type.IsInstanceOfType(instance);
        }

        private static bool TryApplyOptionalType(IndustryComponent component, string fullTypeName, Action<IndustryComponent> apply)
        {
            if (!IsType(component, fullTypeName))
            {
                return false;
            }

            apply?.Invoke(component);
            return true;
        }

        private static void ApplyOptionalLoadField(IndustryComponent component, Load load)
        {
            var field = component.GetType().GetField("load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && (load != null || field.GetValue(component) == null))
            {
                field.SetValue(component, load);
            }
        }

        private static void ApplyTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .SetValue(component, ResolveSpans(definition.InputSpanIds));
            type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .SetValue(component, ResolveSpans(definition.OutputSpanIds));
            if (definition.CarLoadPeriod != null)
            {
                type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLoadPeriod.Value);
            }

            if (definition.CarLengthFeet != null)
            {
                type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLengthFeet.Value);
            }
        }

        private static void ApplyPartialTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            if (definition.InputSpanIds != null && definition.InputSpanIds.Length > 0)
            {
                var field = type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var existing = field?.GetValue(component) as TrackSpan[];
                field?.SetValue(component, MergeSpans(existing, ResolveSpans(definition.InputSpanIds)));
            }

            if (definition.OutputSpanIds != null && definition.OutputSpanIds.Length > 0)
            {
                var field = type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var existing = field?.GetValue(component) as TrackSpan[];
                field?.SetValue(component, MergeSpans(existing, ResolveSpans(definition.OutputSpanIds)));
            }

            if (definition.CarLoadPeriod != null)
            {
                type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLoadPeriod.Value);
            }

            if (definition.CarLengthFeet != null)
            {
                type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .SetValue(component, definition.CarLengthFeet.Value);
            }
        }

        private static void ReadTeleportLoadingFields(IndustryComponent component, FuseIndustryComponent definition)
        {
            var type = component.GetType();
            var inputSpans = type.GetField("inputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(component) as TrackSpan[];
            var outputSpans = type.GetField("outputSpans", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(component) as TrackSpan[];

            definition.InputSpanIds = inputSpans?
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                .Select(span => span.id)
                .ToArray();
            definition.OutputSpanIds = outputSpans?
                .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                .Select(span => span.id)
                .ToArray();

            var carLoadPeriod = type.GetField("carLoadPeriod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (carLoadPeriod != null)
            {
                definition.CarLoadPeriod = (float)carLoadPeriod.GetValue(component);
            }

            var carLengthFeet = type.GetField("carLengthFeet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (carLengthFeet != null)
            {
                definition.CarLengthFeet = (float)carLengthFeet.GetValue(component);
            }
        }

        private static object ReadObjectField(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            var field = FindInstanceField(instance.GetType(), fieldName);
            return field != null ? field.GetValue(instance) : null;
        }

        private static void ApplyCustomIndustryComponentFields(IndustryComponent component, FuseIndustryComponent definition, Load load)
        {
            if (component == null || definition == null)
            {
                return;
            }

            var typeName = component.GetType().FullName;
            if (FuseIndustryComponentTypes.IsKnown(definition.Type))
            {
                return;
            }

            SetLoadField(component, "load", load);
            SetLoadField(component, "convertedLoad", ResolveLoad(definition.ConvertedLoadId));
            SetFloatField(component, "carLoadRate", definition.CarTransferRate);
            SetFloatField(component, "carUnloadRate", definition.CarTransferRate);
            SetFloatField(component, "loadRate", definition.CarTransferRate);
            SetFloatField(component, "maxStorage", definition.MaxStorage);
            SetFloatField(component, "costPerUnit", definition.CostPerUnit);
            SetFloatField(component, "notBefore", definition.NotBeforeHour);
            SetFloatField(component, "notAfter", definition.NotAfterHour);
            SetFloatField(component, "fillPercentage", definition.FillPercentage);
            SetStringField(component, "title", definition.Title ?? definition.Name);
            SetStringArrayField(component, "bookReasons", definition.BookReasons);
            ApplyCustomFieldBag(component, definition.Fields);

            FuseLog.Info(
                $"FUSE applied reflective custom industry component fields type='{typeName}' " +
                $"id='{DescribeComponent(component)}' loadId='{definition.LoadId ?? string.Empty}' " +
                $"convertedLoadId='{definition.ConvertedLoadId ?? string.Empty}'.");
        }

        private static void SetLoadField(object instance, string fieldName, Load load)
        {
            if (load != null)
            {
                SetFieldValue(instance, fieldName, load);
            }
        }

        private static void SetFloatField(object instance, string fieldName, float? value)
        {
            if (value != null)
            {
                SetFieldValue(instance, fieldName, value.Value);
            }
        }

        private static void SetStringField(object instance, string fieldName, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                SetFieldValue(instance, fieldName, value);
            }
        }

        private static void SetStringArrayField(object instance, string fieldName, string[] value)
        {
            if (value != null)
            {
                SetFieldValue(instance, fieldName, value);
            }
        }

        private static void ApplyCustomFieldBag(object instance, IDictionary<string, object> fields)
        {
            if (instance == null || fields == null || fields.Count == 0)
            {
                return;
            }

            foreach (var pair in fields)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                SetFieldValue(instance, pair.Key, pair.Value);
            }
        }

        private static void SetFieldValue(object instance, string fieldName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName) || value == null)
            {
                return;
            }

            var field = FindInstanceField(instance.GetType(), fieldName);
            if (field != null)
            {
                TrySetMemberValue(instance, field.Name, field.FieldType, converted => field.SetValue(instance, converted), value);
                return;
            }

            var property = FindInstanceProperty(instance.GetType(), fieldName);
            if (property != null && property.CanWrite)
            {
                TrySetMemberValue(instance, property.Name, property.PropertyType, converted => property.SetValue(instance, converted, null), value);
            }
        }

        private static void TrySetMemberValue(object instance, string memberName, Type memberType, Action<object> setter, object value)
        {
            try
            {
                var converted = ConvertCustomFieldValue(memberType, value);
                if (converted != null || !memberType.IsValueType || Nullable.GetUnderlyingType(memberType) != null)
                {
                    setter(converted);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not set custom industry component field '{memberName}' " +
                    $"type='{instance.GetType().FullName}' error='{ex.Message}'.");
            }
        }

        private static object ConvertCustomFieldValue(Type targetType, object value)
        {
            if (targetType == null || value == null)
            {
                return null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                targetType = nullableType;
            }

            if (value is JValue jValue)
            {
                value = jValue.Value;
            }

            if (value is JToken token)
            {
                if (typeof(Load).IsAssignableFrom(targetType) && token.Type == JTokenType.String)
                {
                    return ResolveLoad(token.ToString());
                }

                if (typeof(TrackSpan[]).IsAssignableFrom(targetType) && token is JArray spanArray)
                {
                    return ResolveSpans(spanArray.Values<string>().ToArray());
                }

                return token.ToObject(targetType);
            }

            if (typeof(Load).IsAssignableFrom(targetType) && value is string loadId)
            {
                return ResolveLoad(loadId);
            }

            if (typeof(TrackSpan[]).IsAssignableFrom(targetType) && value is IEnumerable<string> spanIds)
            {
                return ResolveSpans(spanIds.ToArray());
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            if (targetType.IsEnum)
            {
                return value is string text
                    ? Enum.Parse(targetType, text, true)
                    : Enum.ToObject(targetType, value);
            }

            return Convert.ChangeType(value, targetType);
        }

        private static FieldInfo FindInstanceField(Type type, string fieldName)
        {
            while (type != null && !string.IsNullOrWhiteSpace(fieldName))
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindInstanceProperty(Type type, string propertyName)
        {
            while (type != null && !string.IsNullOrWhiteSpace(propertyName))
            {
                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    return property;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static Dictionary<string, float> ToFormulaTerms(IEnumerable<FormulaicIndustryComponent.Term> terms)
        {
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (terms == null)
            {
                return result;
            }

            foreach (var term in terms)
            {
                if (term.load == null || string.IsNullOrWhiteSpace(term.load.id))
                {
                    continue;
                }

                result[term.load.id] = term.unitsPerDay;
            }

            return result;
        }

        private static string GetComponentDefinitionKey(string industryId, string subId)
        {
            return (industryId ?? string.Empty) + "/" + (subId ?? string.Empty);
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

        private static TeamTrackProfile BuildTeamTrackProfile(IDictionary<string, FuseTeamTrackEntry> entries)
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
                var span = TrackAPI.GetSpan(id) ??
                           TrackAPI.TryEnsureBaseGraphSpan(id, "industry component span binding");
                if (span == null)
                {
                    FuseLog.Warning($"FUSE track span '{id}' was not found while resolving industry component spans; continuing without it.");
                    continue;
                }

                spans.Add(span);
            }

            return spans.ToArray();
        }

        private static TrackSpan[] MergeSpans(TrackSpan[] existing, TrackSpan[] additions)
        {
            if (existing == null || existing.Length == 0)
            {
                return additions ?? Array.Empty<TrackSpan>();
            }

            if (additions == null || additions.Length == 0)
            {
                return existing;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<TrackSpan>();
            foreach (var span in existing.Concat(additions))
            {
                if (span == null)
                {
                    continue;
                }

                var id = span.id ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id) || seen.Add(id))
                {
                    result.Add(span);
                }
            }

            return result.ToArray();
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
                FuseLog.Warning($"FUSE load '{loadId}' was not found while resolving industry component load data; continuing with null load.");
                return null;
            }

            FuseLoadRuntimeIndex.Instance.Set(load.id, load);
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
                FuseLog.Info($"FUSE removing stale industry component '{industry.identifier}.{subId}' because it is not present in the current definition.");
                RemoveComponent(industry, subId, false);
            }
        }

        private static void LogComponentLoadFailure(Industry industry, string subId, FuseIndustryComponent definition, Exception ex)
        {
            var spanIds = definition?.TrackSpanIds == null
                ? string.Empty
                : string.Join(",", definition.TrackSpanIds);
            FuseLog.Warning(
                $"FUSE failed to load industry component industry='{industry?.identifier ?? "<unknown>"}' " +
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
            return industry.GetComponentsInChildren<IndustryComponent>(true).FirstOrDefault(component => component != null && string.Equals(component.subIdentifier, subId, StringComparison.OrdinalIgnoreCase));
        }

        private static IndustryComponent ResolveLegacyComponentAlias(
            Industry industry,
            string subId,
            FuseIndustryComponent definition,
            ISet<string> definedSubIds)
        {
            if (industry == null ||
                string.IsNullOrWhiteSpace(subId) ||
                definition?.Partial != true ||
                !string.IsNullOrWhiteSpace(definition.Type) ||
                !HasTrackSpanPatch(definition))
            {
                return null;
            }

            IndustryComponent matched = null;
            if (IsLegacyInterchangeAlias(subId))
            {
                var interchanges = industry.GetComponentsInChildren<Interchange>(true)
                    .Where(component => component != null)
                    .Cast<IndustryComponent>()
                    .ToArray();
                matched = interchanges.FirstOrDefault(component =>
                              string.Equals(component.subIdentifier, "interchange", StringComparison.OrdinalIgnoreCase)) ??
                          (interchanges.Length == 1 ? interchanges[0] : null);
            }

            if (matched == null)
            {
                var inferredLoad = InferLegacyPartialComponentLoad(industry, subId, definition);
                matched = FindLegacyLoadComponentAlias(
                    industry,
                    inferredLoad,
                    definedSubIds);
                if (matched != null &&
                    string.IsNullOrWhiteSpace(definition.LoadId) &&
                    !string.IsNullOrWhiteSpace(inferredLoad?.LoadId) &&
                    string.IsNullOrWhiteSpace(GetDefinition(matched)?.LoadId))
                {
                    definition.LoadId = inferredLoad.LoadId;
                }
            }

            if (matched != null)
            {
                FuseLog.Info(
                    $"FUSE bound legacy partial industry component '{industry.identifier}.{subId}' " +
                    $"to existing component '{DescribeComponent(matched)}'.");
            }

            return matched;
        }

        private static IndustryComponent FindLegacyLoadComponentAlias(
            Industry industry,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (industry == null || string.IsNullOrWhiteSpace(inferredLoad?.LoadId))
            {
                return null;
            }

            var inferredRuntimeLoad = ResolveLoad(inferredLoad.LoadId);
            var inferredCarTypes = GetCarTypesForLoad(inferredLoad.LoadId);
            var candidates = industry.GetComponentsInChildren<IndustryComponent>(true)
                .Where(component => component != null && !(component is FormulaicIndustryComponent))
                .Select(component => new LegacyLoadAliasCandidate
                {
                    Component = component,
                    LoadId = GetDefinition(component)?.LoadId,
                    AcceptsInferredLoad = ComponentAcceptsCarsWithLoad(component, inferredRuntimeLoad),
                    LoadCarTypeMatchCount = CountLoadCompatibleCarTypes(component, inferredCarTypes)
                })
                .ToArray();

            var exact = candidates
                .Where(candidate =>
                    string.Equals(candidate.LoadId, inferredLoad.LoadId, StringComparison.OrdinalIgnoreCase) ||
                    candidate.AcceptsInferredLoad)
                .OrderByDescending(candidate => ScoreLegacyLoadComponentAlias(candidate.Component, inferredLoad))
                .ThenByDescending(candidate => candidate.LoadCarTypeMatchCount)
                .ThenBy(candidate => candidate.Component.subIdentifier, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Component)
                .FirstOrDefault();
            if (exact != null)
            {
                return exact;
            }

            var compatibleCarType = FindCarTypeLegacyLoadComponentAlias(candidates, inferredLoad, definedSubIds);
            if (compatibleCarType != null)
            {
                return compatibleCarType;
            }

            return FindDirectionalLegacyLoadComponentAlias(candidates, inferredLoad, definedSubIds);
        }

        private static IndustryComponent FindCarTypeLegacyLoadComponentAlias(
            IEnumerable<LegacyLoadAliasCandidate> candidates,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (candidates == null || inferredLoad == null)
            {
                return null;
            }

            var ranked = candidates
                .Where(candidate => candidate.Component != null)
                .Where(candidate => string.IsNullOrWhiteSpace(candidate.LoadId))
                .Where(candidate => candidate.LoadCarTypeMatchCount > 0)
                .Where(candidate => !IsDefinedLegacyComponent(candidate.Component, definedSubIds))
                .Where(candidate => IsLegacyLoadDirectionMatch(candidate.Component, inferredLoad))
                .Select(candidate => new
                {
                    candidate.Component,
                    candidate.LoadCarTypeMatchCount,
                    Score = ScoreLegacyLoadComponentAlias(candidate.Component, inferredLoad)
                })
                .OrderByDescending(candidate => candidate.LoadCarTypeMatchCount)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Component.subIdentifier, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ranked.Length == 0)
            {
                return null;
            }

            if (ranked.Length > 1 &&
                ranked[0].LoadCarTypeMatchCount == ranked[1].LoadCarTypeMatchCount &&
                ranked[0].Score == ranked[1].Score)
            {
                return null;
            }

            return ranked[0].Component;
        }

        private static IndustryComponent FindDirectionalLegacyLoadComponentAlias(
            IEnumerable<LegacyLoadAliasCandidate> candidates,
            LegacyPartialLoadInference inferredLoad,
            ISet<string> definedSubIds)
        {
            if (candidates == null || inferredLoad == null)
            {
                return null;
            }

            var directional = candidates
                .Where(candidate => candidate.Component != null)
                .Where(candidate => string.IsNullOrWhiteSpace(candidate.LoadId))
                .Where(candidate => !IsDefinedLegacyComponent(candidate.Component, definedSubIds))
                .Where(candidate => IsLegacyLoadDirectionMatch(candidate.Component, inferredLoad))
                .Select(candidate => candidate.Component)
                .ToArray();

            return directional.Length == 1 ? directional[0] : null;
        }

        private sealed class LegacyLoadAliasCandidate
        {
            public IndustryComponent Component { get; set; }
            public string LoadId { get; set; }
            public bool AcceptsInferredLoad { get; set; }
            public int LoadCarTypeMatchCount { get; set; }
        }

        private static bool ComponentAcceptsCarsWithLoad(IndustryComponent component, Load load)
        {
            if (component == null || load == null)
            {
                return false;
            }

            try
            {
                return component.AcceptsCarsWithLoad(load);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy support could not query load acceptance for component '{DescribeComponent(component)}' " +
                    $"loadId='{load.id ?? string.Empty}': {ex.Message}");
                return false;
            }
        }

        private static string[] GetCarTypesForLoad(string loadId)
        {
            if (string.IsNullOrWhiteSpace(loadId))
            {
                return Array.Empty<string>();
            }

            try
            {
                var prefabStore = TrainController.Shared?.PrefabStore;
                if (prefabStore == null)
                {
                    return Array.Empty<string>();
                }

                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in prefabStore.AllCarDefinitionInfos)
                {
                    var definition = item.Definition;
                    if (definition == null ||
                        definition.LoadSlots == null ||
                        string.IsNullOrWhiteSpace(definition.CarType))
                    {
                        continue;
                    }

                    if (definition.LoadSlots.Any(slot =>
                            slot != null &&
                            string.Equals(slot.RequiredLoadIdentifier, loadId, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(definition.CarType.Trim());
                    }
                }

                return result.ToArray();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy support could not infer car types for loadId='{loadId.Trim()}': {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static int CountLoadCompatibleCarTypes(IndustryComponent component, IEnumerable<string> carTypes)
        {
            if (component?.carTypeFilter == null || carTypes == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var carType in carTypes)
            {
                if (!string.IsNullOrWhiteSpace(carType) &&
                    component.carTypeFilter.Matches(carType.Trim()))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsDefinedLegacyComponent(IndustryComponent component, ISet<string> definedSubIds)
        {
            return component != null &&
                   definedSubIds != null &&
                   !string.IsNullOrWhiteSpace(component.subIdentifier) &&
                   definedSubIds.Contains(component.subIdentifier);
        }

        private static bool IsLegacyLoadDirectionMatch(IndustryComponent component, LegacyPartialLoadInference inferredLoad)
        {
            if (component == null || inferredLoad == null)
            {
                return false;
            }

            if (inferredLoad.IsInput && !inferredLoad.IsOutput)
            {
                return component is IndustryUnloader;
            }

            if (inferredLoad.IsOutput && !inferredLoad.IsInput)
            {
                return component is IndustryLoaderBase;
            }

            return false;
        }

        private static bool ShouldDefaultMaterializedCarTypeFilter(string type)
        {
            var normalized = FuseIndustryComponentTypes.Normalize(type);
            return string.Equals(normalized, FuseIndustryComponentTypes.Loader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.Unloader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.InterchangedLoader, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, FuseIndustryComponentTypes.InterchangedUnloader, StringComparison.OrdinalIgnoreCase);
        }

        private static int ScoreLegacyLoadComponentAlias(
            IndustryComponent component,
            LegacyPartialLoadInference inferredLoad)
        {
            if (component == null || inferredLoad == null)
            {
                return 0;
            }

            if (inferredLoad.IsInput && !inferredLoad.IsOutput && component is IndustryUnloader)
            {
                return 3;
            }

            if (inferredLoad.IsOutput && !inferredLoad.IsInput && component is IndustryLoader)
            {
                return 3;
            }

            if (component is IndustryLoader || component is IndustryUnloader)
            {
                return 2;
            }

            return 1;
        }

        private static bool IsLegacyInterchangeAlias(string subId)
        {
            return string.Equals(subId, "t1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(subId, "interchange", StringComparison.OrdinalIgnoreCase) ||
                   ContainsText(subId, "interchange");
        }

        private static Transform GetIndustryRoot(FuseIndustry definition)
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
                    FuseLog.Warning($"FUSE could not find Area '{definition.AreaId}' for industry '{definition.Name ?? "<unnamed>"}'; using nearest Area '{nearestArea.identifier ?? nearestArea.name}'.");
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
                _fallbackRoot = new GameObject("FUSE Industries").transform;
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

            var clearedIndustryComponentList = IndustryRuntimeComponentsField != null;
            IndustryRuntimeComponentsField?.SetValue(industry, null);

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

            FuseLog.Info($"FUSE invalidated industry component caches for '{industry.identifier}' cachedComponentsCleared={clearedIndustryComponentList} componentIdentityRefreshed={refreshedCount}.");
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

        internal static void BeginIndustryApplyBatch()
        {
            _industryApplyBatchDepth++;
        }

        internal static void EndIndustryApplyBatch(string source)
        {
            if (_industryApplyBatchDepth > 0)
            {
                _industryApplyBatchDepth--;
            }

            if (_industryApplyBatchDepth == 0 && _industryRefreshPending)
            {
                _industryRefreshPending = false;
                RefreshIndustriesAfterBatch(source ?? "industry apply batch");
            }
        }

        internal static void RefreshIndustriesAfterBatch(string source)
        {
            if (_industryApplyBatchDepth > 0)
            {
                _industryRefreshPending = true;
                return;
            }

            ApplyIndustryOrdering();
            Messenger.Default.Send(default(IndustriesDidChange));
            FuseIndustryRuntimeIndex.Instance.Rebuild();
            FuseIndustryComponentRuntimeIndex.Instance.Rebuild();
            var industryCount = UnityEngine.Object.FindObjectsOfType<Industry>(true).Length;
            var componentCount = UnityEngine.Object.FindObjectsOfType<IndustryComponent>(true).Length;
            FuseLog.Info($"FUSE refreshed industries after '{source}' sceneIndustryCount={industryCount} sceneIndustryComponentCount={componentCount} cacheIndustryCount={FuseIndustryRuntimeIndex.Instance.Count} cacheIndustryComponentCount={FuseIndustryComponentRuntimeIndex.Instance.Count}.");
            foreach (var industryId in FuseCreatedIndustryIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var industry = GetIndustry(industryId);
                if (industry == null)
                {
                    FuseLog.Warning($"FUSE-created industry '{industryId}' was not found after '{source}'.");
                    continue;
                }

                var railComponentCount = industry.GetComponentsInChildren<IndustryComponent>(true)
                    .Count(component => component != null && !string.IsNullOrWhiteSpace(component.subIdentifier));
                FuseRuntimeDefinitionCache.TryGet(FuseDefinitionKind.Industry, industryId, out FuseIndustry sourceDefinition);
                var sourceComponentCount = sourceDefinition?.Components?.Count ?? 0;
                if (railComponentCount == 0 && sourceComponentCount == 0)
                {
                    FuseLog.Info($"FUSE-created source-empty industry shell '{industryId}' name='{industry.name}' runtimeComponents=0 sourceComponents=0.");
                    continue;
                }

                FuseLog.Info($"FUSE-created industry '{industryId}' name='{industry.name}' runtimeComponents={railComponentCount} sourceComponents={sourceComponentCount}.");
            }
        }

        internal static string LocationPanelSortKey(Industry industry, string fallback)
        {
            if (industry != null &&
                !string.IsNullOrWhiteSpace(industry.identifier) &&
                IndustryOrders.TryGetValue(industry.identifier, out var order))
            {
                var signedSortKey = (long)order - int.MinValue;
                return signedSortKey.ToString("D10") + "|" + (fallback ?? string.Empty);
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
                FuseLog.Info($"FUSE applied industry ordering for {orderedCount} industry object(s).");
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
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not read industry component Identifier for '{component.name}': {ex.Message}");
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
