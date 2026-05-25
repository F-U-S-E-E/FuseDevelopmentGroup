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

        private static Dictionary<string, FuseIndustryComponent> NormalizeComponentDefinitions(Industry industry, IDictionary<string, FuseIndustryComponent> components)
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
                        // Legacy Strange-Customs null-component sentinel: the
                        // legacy converter turns <c>"foo": null</c> into
                        // <c>{ "remove": true }</c> so that
                        // <see cref="FuseIndustryComponent.Remove"/> survives
                        // Newtonsoft's <c>NullValueHandling.Ignore</c> setting
                        // (a JSON null would have been dropped during
                        // deserialization, leaving the apply path with an
                        // empty dict — the Parson's Tannery / sylva-paperboard /
                        // sylva-interchange "removal still shows in the
                        // industry list" bug). Honour the flag here before
                        // the type-resolution branches below try to inspect
                        // <c>component.Value.Type</c>.
                        if (component.Value == null || component.Value.Remove)
                        {
                            var existing = GetComponent(industry, component.Key);
                            if (existing != null)
                            {
                                FuseLog.Info(
                                    $"FUSE removing industry component '{industry.identifier}.{component.Key}' " +
                                    "because the definition entry is a delete sentinel (legacy SC null-component).");
                                RemoveComponent(industry, component.Key, false);
                            }
                            else
                            {
                                FuseLog.Info(
                                    $"FUSE legacy delete-component request for '{industry.identifier}.{component.Key}' " +
                                    "had no matching runtime component to remove; skipping.");
                            }
                            continue;
                        }

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
                // Map Enhancer caches an industry's industrial-track segments
                // off IndustryComponent.Start, which only fires once per
                // component. Anything we added/extended above (especially
                // TrackSpan patches that bring in mod-added segments after
                // scene Start) won't get picked up by Map Enhancer's
                // segment-color cache unless we refresh it explicitly.
                FUSE.Interface.FuseMapEnhancerCompat.RefreshIndustry(industry, "industry component add/update");
            }
        }

        private static void RemoveStaleComponents(Industry industry, HashSet<string> definedSubIds)
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
    }
}
