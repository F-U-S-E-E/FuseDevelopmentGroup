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
using Track;
using UnityEngine;

namespace FUSE.API
{
    public static class IndustryAPI
    {
        private static readonly FieldInfo IndustryRuntimeComponentsField = typeof(Industry).GetField("_cachedComponents", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedIndustryField = typeof(IndustryComponent).GetField("_cachedIndustry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ComponentIdentifierField = typeof(IndustryComponent).GetField("_identifier", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RepairPartsLoadField = typeof(RepairTrack).GetField("repairPartsLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<string, int> IndustryOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> FuseCreatedIndustryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            AddOrUpdateComponents(industry, definition.Components);
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
            AddOrUpdateComponents(industry, definition.Components);
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
            industry.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(industry.gameObject);
            FuseIndustryRuntimeIndex.Instance.Remove(id);
            FuseCreatedIndustryIds.Remove(id);
            FuseRuntimeDefinitionCache.Remove(FuseDefinitionKind.Industry, id);
            RefreshIndustriesAfterBatch("RemoveIndustry:" + id);
            FuseEvents.RaiseIndustryRemoved(id);
        }

        public static Industry GetIndustry(string id)
        {
            if (FuseIndustryRuntimeIndex.Instance.TryGetValue(id, out var cached))
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

        private static void AddOrUpdateComponents(Industry industry, IDictionary<string, FuseIndustryComponent> components)
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

        private static void ApplyComponentDefinition(IndustryComponent component, FuseIndustryComponent definition)
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

            if (TryApplyOptionalType(component, "Model.Ops.InterchangedIndustryUnloader", obj =>
            {
                ApplyOptionalLoadField(obj, load);
            }))
            {
                return;
            }

            if (TryApplyOptionalType(component, "Model.Ops.TeleportLoadingIndustry", obj =>
            {
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
                return typeof(FormulaicIndustryComponent);
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
                var resolved = Type.GetType("Model.Ops.InterchangedIndustryUnloader, Assembly-CSharp", false, true);
                if (resolved != null)
                {
                    return resolved;
                }
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

            var reflected = TryResolveIndustryComponentType(type);
            if (reflected != null)
            {
                return reflected;
            }

            throw new NotSupportedException($"Industry component type '{type}' is not implemented yet.");
        }

        private static Type TryResolveIndustryComponentType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            var direct = Type.GetType(type + ", Assembly-CSharp", false, true);
            return direct != null && typeof(IndustryComponent).IsAssignableFrom(direct)
                ? direct
                : null;
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

            var type = Type.GetType(fullTypeName + ", Assembly-CSharp", false, true);
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

            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(instance) : null;
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
                var span = TrackAPI.GetSpan(id);
                if (span == null)
                {
                    FuseLog.Warning($"FUSE track span '{id}' was not found while resolving industry component spans; continuing without it.");
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
            return industry.GetComponentsInChildren<IndustryComponent>(true).FirstOrDefault(component => component.subIdentifier == subId);
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

        internal static void RefreshIndustriesAfterBatch(string source)
        {
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
                FuseLog.Info($"FUSE-created industry '{industryId}' name='{industry.name}' componentCount={railComponentCount}.");
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
