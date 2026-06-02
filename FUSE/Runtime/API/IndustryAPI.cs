using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Model;
using Model.Ops;
using Model.Ops.Definition;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public static partial class IndustryAPI
    {
        private const string LegacyEmptyComponentType = "ConfusingSupplements.IndustryComponents.Empty";

        private static readonly FieldInfo IndustryRuntimeComponentsField = typeof(Industry).GetField("_cachedComponents", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedIndustryField = typeof(IndustryComponent).GetField("_cachedIndustry", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ComponentIdentifierField = typeof(IndustryComponent).GetField("_identifier", BindingFlags.Instance | BindingFlags.NonPublic);
        // FormulaicIndustryComponent privately memoizes its sibling
        // IndustryComponents in <c>_otherComponents</c> on first
        // <c>Service</c> tick and uses that cache to look up max storage for
        // each output term. If FUSE later replaces a sibling component
        // outright (e.g. Foxy's Kirkland Coal Patch changes
        // <c>kirkland-mine.coal</c> from IndustryLoader to
        // TeleportLoadingIndustry, which goes through Remove+Add), the
        // formula still holds a reference to the destroyed instance. Its
        // MaxStorageForLoad walk then misses the live replacement and
        // returns 0 — causing the formula to set "Production Stopped:
        // &lt;outputLoad&gt;" even though the real loader has plenty of
        // headroom. Resetting this field after every component
        // invalidation forces the next Service tick to rebuild the cache
        // from the current child set.
        private static readonly FieldInfo FormulaicOtherComponentsField = typeof(FormulaicIndustryComponent).GetField("_otherComponents", BindingFlags.Instance | BindingFlags.NonPublic);

        // InterchangedIndustryLoader (vanilla) and FuseInterchangedIndustryUnloader
        // (our shim) both lazy-cache the sibling Interchange MonoBehaviour on
        // first <c>Interchange</c> property access via the
        // <c>_interchange</c> / <c>_hasInterchange</c> pair. The exact
        // same staleness hazard as FormulaicIndustryComponent._otherComponents
        // applies: if a pack type-changes the Interchange sibling and FUSE
        // does Remove+Add, the cached reference points at a destroyed
        // MonoBehaviour and DisplayName / ServeInterchange / OrderCars
        // silently fall back to no-op behaviour. We collect both field
        // pairs by their declared name so we can clear them generically
        // for any IndustryComponent that has them — without taking a hard
        // compile-time dependency on InterchangedIndustryLoader's
        // existence (the game can ship without it in old/forked builds).
        private static readonly string[] InterchangedComponentCacheFieldNames =
        {
            "_interchange",
            "_hasInterchange"
        };
        private static readonly FieldInfo RepairPartsLoadField = typeof(RepairTrack).GetField("repairPartsLoad", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<string, int> IndustryOrders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> FuseCreatedIndustryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static int _industryApplyBatchDepth;
        private static bool _industryRefreshPending;
        private static Transform _fallbackRoot;
        // Batch-scoped snapshot of the scene's industries, captured once per apply
        // batch so the per-industry existence check doesn't FindObjectsOfType the
        // whole scene each time. Industries added during the batch are found via
        // FuseIndustryRuntimeIndex (checked first in GetIndustry), so this only needs
        // the INITIAL scene state and is never updated mid-batch. Cleared in
        // EndIndustryApplyBatch.
        private static Industry[] _batchIndustrySnapshot;

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
            gameObject.transform.localPosition = definition.Position ?? Vector3.zero;
            gameObject.transform.localRotation = Quaternion.Euler(definition.Rotation ?? Vector3.zero);

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
            // Only reparent / relocate / re-rotate when the definition
            // explicitly specifies these fields. Legacy SC patches against an
            // existing base-game industry (e.g. a top-level
            // industries.whittier-sawmill.components.{...} patch) routinely
            // omit areaId / position / rotation; running unconditional
            // mutations here used to drag the industry into the first Area
            // and the origin, after which MapEnhancer's
            // OpsController.Shared.Areas -> area.Industries lookup picked the
            // wrong Area's tagColor for every component on it.
            if (!string.IsNullOrWhiteSpace(definition.AreaId))
            {
                var root = GetIndustryRoot(definition);
                if (root != null && industry.transform.parent != root)
                {
                    industry.transform.SetParent(root, false);
                    FuseLog.Info($"FUSE reparented industry '{id}' to '{DescribeIndustryParent(root)}'.");
                }
            }

            industry.gameObject.name = displayName;
            industry.name = displayName;
            if (definition.Position.HasValue)
            {
                industry.transform.localPosition = definition.Position.Value;
            }

            if (definition.Rotation.HasValue)
            {
                industry.transform.localRotation = Quaternion.Euler(definition.Rotation.Value);
            }

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
            DestroyIndustryInternal(id, industry, true);
        }

        /// <summary>
        /// Removes the industry with <paramref name="id"/> if it exists. Returns false when the
        /// industry is not currently in the scene/cache — used by the legacy-conversion apply
        /// path so that "industries: { id: null }" directives can be expressed without
        /// throwing when the industry was already absent.
        /// </summary>
        public static bool TryRemoveIndustry(string id)
        {
            return TryRemoveIndustry(id, true);
        }

        internal static bool TryRemoveIndustry(string id, bool notify)
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

            DestroyIndustryInternal(id, industry, notify);
            return true;
        }

        private static void DestroyIndustryInternal(string id, Industry industry, bool notify)
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
            if (notify)
            {
                RefreshIndustriesAfterBatch("RemoveIndustry:" + id);
                FuseEvents.RaiseIndustryRemoved(id);
            }
        }

        // Returns the scene's industries for GetIndustry's existence / legacy-match
        // fallback. During an apply batch it reuses a single snapshot (see
        // _batchIndustrySnapshot); outside a batch it scans live (unchanged behavior).
        private static Industry[] GetIndustrySceneSnapshot()
        {
            if (_industryApplyBatchDepth <= 0)
            {
                return UnityEngine.Object.FindObjectsOfType<Industry>(true);
            }

            if (_batchIndustrySnapshot == null)
            {
                _batchIndustrySnapshot = UnityEngine.Object.FindObjectsOfType<Industry>(true);
            }

            return _batchIndustrySnapshot;
        }

        public static Industry GetIndustry(string id)
        {
            if (FuseIndustryRuntimeIndex.Instance.TryGetValue(id, out var cached) && cached != null)
            {
                return (Industry)cached;
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                var sceneMatch = GetIndustrySceneSnapshot()
                    .FirstOrDefault(industry => industry != null && string.Equals(industry.identifier, id, StringComparison.OrdinalIgnoreCase));
                if (sceneMatch != null)
                {
                    FuseIndustryRuntimeIndex.Instance.Set(sceneMatch.identifier, sceneMatch);
                    return sceneMatch;
                }

                var legacyAlias = NormalizeLegacyIndustryReference(id);
                if (!string.Equals(legacyAlias, id, StringComparison.OrdinalIgnoreCase))
                {
                    sceneMatch = GetIndustrySceneSnapshot()
                        .FirstOrDefault(industry => industry != null && string.Equals(industry.identifier, legacyAlias, StringComparison.OrdinalIgnoreCase));
                    if (sceneMatch != null)
                    {
                        FuseIndustryRuntimeIndex.Instance.Set(sceneMatch.identifier, sceneMatch);
                        return sceneMatch;
                    }
                }

                sceneMatch = GetIndustrySceneSnapshot()
                    .FirstOrDefault(industry => IndustryMatchesLegacyReference(industry, id));
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
                ? GetIndustrySceneSnapshot().FirstOrDefault(industry =>
                    string.Equals(industry.identifier, id, StringComparison.OrdinalIgnoreCase))
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

        private static Transform GetIndustryRoot(FuseIndustry definition)
        {
            if (!string.IsNullOrWhiteSpace(definition?.AreaId))
            {
                // Fast path: resolve via the cached area index without scanning the
                // scene. Only fall back to FindObjectsOfType<Area> on a cache miss
                // (rare), avoiding a full-scene scan per industry during apply.
                var cachedArea = TrackAPI.GetArea(definition.AreaId);
                if (cachedArea != null)
                {
                    return cachedArea.transform;
                }

                var areas = UnityEngine.Object.FindObjectsOfType<Area>(true);
                var matchedArea = areas.FirstOrDefault(area =>
                    area != null &&
                    (string.Equals(area.identifier, definition.AreaId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(area.name, definition.AreaId, StringComparison.OrdinalIgnoreCase)));
                if (matchedArea != null)
                {
                    return matchedArea.transform;
                }

                var probePosition = definition.Position ?? Vector3.zero;
                var nearestArea = areas
                    .Where(area => area != null)
                    .OrderBy(area => (area.transform.localPosition - probePosition).sqrMagnitude)
                    .FirstOrDefault();
                if (nearestArea != null)
                {
                    FuseLog.Warning($"FUSE could not find Area '{definition.AreaId}' for industry '{definition.Name ?? "<unnamed>"}'; using nearest Area '{nearestArea.identifier ?? nearestArea.name}'.");
                    return nearestArea.transform;
                }
            }
            else
            {
                var firstArea = UnityEngine.Object.FindObjectsOfType<Area>(true).FirstOrDefault(area => area != null);
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

        private static void RequireId(string id, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID is required.", parameterName);
            }
        }
    }
}
