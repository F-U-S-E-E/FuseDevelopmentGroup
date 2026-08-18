using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Authoring.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    internal static partial class FuseLegacyDataConverter
    {

        private static JObject ConvertComponent(string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            var explicitType = ReadString(item, "type", "Type");
            var trackSpanToken = item["trackSpanIds"] ?? item["trackSpans"] ?? item["spans"];
            var trackSpanPatch = ToStringListPatch(trackSpanToken);
            var isPartial = ShouldConvertAsPartialComponent(item, explicitType, trackSpanPatch);
            var type = isPartial ? null : NormalizeComponentType(explicitType ?? InferComponentType(id, item, inferenceContext));
            var normalizedType = isPartial ? null : FuseIndustryComponentTypes.Normalize(type);
            var isPassengerStop = string.Equals(normalizedType, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase);
            var explicitLoadId = ReadString(item, "loadId", "LoadId", "load");
            var result = new JObject();
            if (isPartial)
            {
                result["partial"] = true;
            }
            else
            {
                result["type"] = type;
                result["name"] = ReadString(item, "name") ?? id;
            }

            if (isPartial && !string.IsNullOrWhiteSpace(ReadString(item, "name")))
            {
                result["name"] = ReadString(item, "name");
            }

            result["trackSpanIds"] = trackSpanPatch != null ? ToStringArrayFromPatch(trackSpanPatch) : ToStringArray(trackSpanToken);
            result["trackSpanPatch"] = trackSpanPatch;
            result["carTypeFilter"] = Clone(item["carTypeFilter"]);
            result["loadId"] = explicitLoadId ?? (isPartial ? null : (isPassengerStop ? "passengers" : InferLoadIdFromComponentId(id, normalizedType, item)));
            result["convertedLoadId"] = ReadString(item, "convertedLoadId", "convertedLoad");
            result["sharedStorage"] = isPartial ? null : Clone(item["sharedStorage"]);
            result["storageChangeRate"] = Clone(item["storageChangeRate"]);
            result["maxStorage"] = Clone(item["maxStorage"]);
            result["carTransferRate"] = Clone(item["carTransferRate"]);
            result["costPerUnit"] = Clone(item["costPerUnit"]);
            result["notBeforeHour"] = Clone(item["notBeforeHour"]);
            result["notAfterHour"] = Clone(item["notAfterHour"]);
            result["fillPercentage"] = Clone(item["fillPercentage"]);
            result["bookReasons"] = ToStringArray(item["bookReasons"]);
            result["title"] = ReadString(item, "title");
            result["orderAroundEmpties"] = Clone(item["orderAroundEmpties"]);
            result["orderAroundLoaded"] = Clone(item["orderAroundLoaded"]);
            result["inputSpanIds"] = ToStringArray(item["inputSpanIds"] ?? item["inputSpans"]);
            result["outputSpanIds"] = ToStringArray(item["outputSpanIds"] ?? item["outputSpans"]);
            result["inputTermsPerDay"] = Clone(item["inputTermsPerDay"]);
            result["outputTermsPerDay"] = Clone(item["outputTermsPerDay"]);
            result["idealCars"] = Clone(item["idealCars"]);
            result["teamProfiles"] = Clone(item["teamProfiles"]);
            result["canOverhaul"] = Clone(item["canOverhaul"]);
            result["passengerStopId"] = ReadString(item, "passengerStopId", "passengerStop") ?? (isPassengerStop ? id : null);
            result["timetableCode"] = ReadString(item, "timetableCode");
            result["basePopulation"] = Clone(item["basePopulation"]);
            result["neighborIds"] = ToStringArray(item["neighborIds"] ?? item["neighbors"]);
            result["branch"] = ReadString(item, "branch");
            result["fields"] = ConvertCustomComponentFields(type, item);

            return CleanObject(result);
        }

        private sealed class ComponentTypeInferenceContext
        {
            private readonly HashSet<string> inputLoadIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> outputLoadIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddInputLoad(string loadId)
            {
                AddLoadId(inputLoadIds, loadId);
            }

            public void AddOutputLoad(string loadId)
            {
                AddLoadId(outputLoadIds, loadId);
            }

            public bool IsInputOnly(string loadId)
            {
                return !string.IsNullOrWhiteSpace(loadId) &&
                       inputLoadIds.Contains(loadId.Trim()) &&
                       !outputLoadIds.Contains(loadId.Trim());
            }

            public bool IsOutputOnly(string loadId)
            {
                return !string.IsNullOrWhiteSpace(loadId) &&
                       outputLoadIds.Contains(loadId.Trim()) &&
                       !inputLoadIds.Contains(loadId.Trim());
            }

            private static void AddLoadId(HashSet<string> sink, string loadId)
            {
                if (!string.IsNullOrWhiteSpace(loadId))
                {
                    sink.Add(loadId.Trim());
                }
            }
        }

        private static bool ShouldConvertAsPartialComponent(JObject item, string explicitType, JObject trackSpanPatch)
        {
            if (item == null)
            {
                return false;
            }

            if (trackSpanPatch != null && trackSpanPatch.HasValues)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                var normalizedType = FuseIndustryComponentTypes.Normalize(NormalizeComponentType(explicitType));
                var isLegacyRuntimeType = !string.Equals(
                    explicitType.Trim(),
                    normalizedType,
                    StringComparison.OrdinalIgnoreCase);

                // Legacy game-graph mixins commonly repeat the runtime type while
                // patching an existing base-game component by industry/component id.
                // The type is descriptive in that shape; it does not turn the patch
                // into a new standalone component. If a span-bound component carries
                // no span binding, preserve it as a partial merge so the runtime keeps
                // the base component's spans. Spanless passenger stops are the one
                // supported standalone exception.
                return isLegacyRuntimeType &&
                       HasComponentPatchPayload(item) &&
                       FuseIndustryComponentTypes.UsesTrackSpanIds(normalizedType) &&
                       !string.Equals(normalizedType, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase) &&
                       !HasLoadComponentBindingShape(item);
            }

            if (!HasStandaloneComponentShape(item))
            {
                return true;
            }

            return HasLoadOperationShape(item) && !HasLoadComponentBindingShape(item);
        }

        private static bool HasComponentPatchPayload(JObject item)
        {
            return item != null && item.Properties().Any(property =>
                !string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasStandaloneComponentShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            return item["inputTermsPerDay"] != null ||
                   item["outputTermsPerDay"] != null ||
                   item["teamProfiles"] != null ||
                   item["passengerStopId"] != null ||
                   item["passengerStop"] != null ||
                   item["timetableCode"] != null ||
                   item["basePopulation"] != null ||
                   item["canOverhaul"] != null ||
                   HasLoadOperationShape(item);
        }

        private static bool HasLoadComponentBindingShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            // Only real track-span keys make a component a standalone load
            // operation. A bare loadId is NOT a binding: a spanless load-op
            // block (e.g. a Production-Tweaks patch that only adjusts
            // storageChangeRate/maxStorage/carTransferRate on an existing
            // base-game loader) is a partial field-merge onto the component
            // that already owns the spans, not a brand-new loader. Counting
            // loadId here forces such patches to convert as full loaders,
            // which then fail the "loader requires at least one track span"
            // validation rule and fault the whole package.
            return item["trackSpanIds"] != null ||
                   item["trackSpans"] != null ||
                   item["spans"] != null;
        }

        private static bool HasLoadOperationShape(JObject item)
        {
            if (item == null)
            {
                return false;
            }

            return item["loadId"] != null ||
                   item["LoadId"] != null ||
                   item["load"] != null ||
                   item["convertedLoadId"] != null ||
                   item["convertedLoad"] != null ||
                   item["maxStorage"] != null ||
                   item["MaxStorage"] != null ||
                   item["storageChangeRate"] != null ||
                   item["StorageChangeRate"] != null ||
                   item["carTransferRate"] != null ||
                   item["CarTransferRate"] != null ||
                   item["costPerUnit"] != null ||
                   item["notBeforeHour"] != null ||
                   item["notAfterHour"] != null ||
                   item["fillPercentage"] != null ||
                   item["bookReasons"] != null ||
                   item["title"] != null ||
                   item["orderAroundEmpties"] != null ||
                   item["orderAroundLoaded"] != null;
        }

        private static string InferComponentType(string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            var normalizedIdType = NormalizeComponentType(id);
            if (!string.IsNullOrWhiteSpace(normalizedIdType) &&
                FuseIndustryComponentTypes.IsKnown(normalizedIdType))
            {
                return normalizedIdType;
            }

            if (item != null)
            {
                if (item["inputTermsPerDay"] != null || item["outputTermsPerDay"] != null)
                {
                    return FuseIndustryComponentTypes.Formulaic;
                }

                if (item["teamProfiles"] != null)
                {
                    return FuseIndustryComponentTypes.TeamTrack;
                }

                if (item["passengerStopId"] != null || item["passengerStop"] != null || item["timetableCode"] != null || item["basePopulation"] != null)
                {
                    return FuseIndustryComponentTypes.PassengerStop;
                }

                if (item["canOverhaul"] != null)
                {
                    return FuseIndustryComponentTypes.RepairTrack;
                }
            }

            if (inferenceContext != null)
            {
                if (inferenceContext.IsInputOnly(id))
                {
                    return FuseIndustryComponentTypes.Unloader;
                }

                if (inferenceContext.IsOutputOnly(id))
                {
                    return FuseIndustryComponentTypes.Loader;
                }
            }

            return FuseIndustryComponentTypes.Loader;
        }

        private static string InferLoadIdFromComponentId(string id, string normalizedType, JObject item)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                !FuseIndustryComponentTypes.UsesLoadId(normalizedType) ||
                string.Equals(normalizedType, FuseIndustryComponentTypes.PassengerStop, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedType, FuseIndustryComponentTypes.RepairTrack, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (item == null ||
                (item["maxStorage"] == null &&
                 item["storageChangeRate"] == null &&
                 item["carTransferRate"] == null &&
                 item["costPerUnit"] == null &&
                 item["orderAroundEmpties"] == null &&
                 item["orderAroundLoaded"] == null))
            {
                return null;
            }

            return id.Trim();
        }

        private static JObject ConvertComponents(JObject sourceComponents)
        {
            var components = new JObject();
            if (sourceComponents == null)
            {
                return components;
            }

            var inferenceContext = BuildComponentTypeInferenceContext(sourceComponents);
            foreach (var component in sourceComponents.Properties())
            {
                // Legacy SC convention: setting a component's value to null
                // requests deletion of the matching runtime sub-component.
                // The old <c>Where(p => p.Value is JObject)</c> filter
                // discarded these sentinels before the apply path could see
                // them — so removal mods such as CollieSylvaRemoval (which
                // nulls every component on sylva-tannery / sylva-paperboard /
                // sylva-interchange to delete them) became silent no-ops and
                // the vanilla components lingered in the industry list.
                // We can't pass a JSON <c>null</c> straight through because
                // <see cref="FUSE.Authoring.Serialization.FuseSerializer.GetSettings"/>
                // sets <c>NullValueHandling.Ignore</c>, which would drop the
                // entry during deserialization. Convert the null into an
                // explicit <c>{ "remove": true }</c> sentinel that the
                // <see cref="FUSE.Authoring.Data.Operations.FuseIndustryComponent.Remove"/>
                // flag picks up at apply time.
                if (component.Value == null || component.Value.Type == JTokenType.Null)
                {
                    components[component.Name] = new JObject { ["remove"] = true };
                    continue;
                }

                if (!(component.Value is JObject obj))
                {
                    continue;
                }

                if (IsLegacyDirectiveKey(component.Name))
                {
                    ConvertDirectiveComponents(obj, components, inferenceContext);
                    continue;
                }

                AddConvertedComponent(components, component.Name, obj, inferenceContext);
            }

            return components;
        }

        private static ComponentTypeInferenceContext BuildComponentTypeInferenceContext(JObject sourceComponents)
        {
            var context = new ComponentTypeInferenceContext();
            CollectComponentTypeInferenceTerms(sourceComponents, context);
            return context;
        }

        private static void CollectComponentTypeInferenceTerms(JObject sourceComponents, ComponentTypeInferenceContext context)
        {
            if (sourceComponents == null || context == null)
            {
                return;
            }

            foreach (var component in sourceComponents.Properties().Where(p => p.Value is JObject))
            {
                var item = (JObject)component.Value;
                if (IsLegacyDirectiveKey(component.Name))
                {
                    CollectComponentTypeInferenceTerms(item, context);
                    continue;
                }

                AddFormulaLoadTermIds(item["inputTermsPerDay"], context.AddInputLoad);
                AddFormulaLoadTermIds(item["outputTermsPerDay"], context.AddOutputLoad);
            }
        }

        private static void AddFormulaLoadTermIds(JToken terms, Action<string> add)
        {
            var obj = terms as JObject;
            if (obj == null || add == null)
            {
                return;
            }

            foreach (var term in obj.Properties())
            {
                add(term.Name);
            }
        }

        private static void ConvertDirectiveComponents(JObject directive, JObject components, ComponentTypeInferenceContext inferenceContext)
        {
            if (directive == null || components == null)
            {
                return;
            }

            foreach (var child in directive.Properties().Where(p => p.Value is JObject))
            {
                if (IsLegacyDirectiveKey(child.Name))
                {
                    ConvertDirectiveComponents((JObject)child.Value, components, inferenceContext);
                    continue;
                }

                AddConvertedComponent(components, child.Name, (JObject)child.Value, inferenceContext);
            }
        }

        private static void AddConvertedComponent(JObject components, string id, JObject item, ComponentTypeInferenceContext inferenceContext)
        {
            if (components == null || item == null)
            {
                return;
            }

            var componentId = UniqueObjectKey(
                string.IsNullOrWhiteSpace(id) ? "component" : id,
                components);
            components[componentId] = ConvertComponent(componentId, item, inferenceContext);
        }

        private static string NormalizeComponentType(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "load":
                case "loader":
                    return "loader";
                case "unload":
                case "unloader":
                    return "unloader";
                case "formula":
                case "formulaic":
                    return "formulaic";
                case "repair":
                case "repairtrack":
                    return "repairTrack";
                case "teamtrack":
                case "team_track":
                    return "teamTrack";
                case "interchange":
                    return "interchange";
                case "passengerstop":
                case "passenger_stop":
                    return "passengerStop";
                default:
                    return string.IsNullOrWhiteSpace(value) ? "loader" : value.Trim();
            }
        }

        private static bool IsLegacyDirectiveKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.TrimStart().StartsWith("$", StringComparison.Ordinal);
        }

        private static bool IsTurntableHandler(string handler)
        {
            return !string.IsNullOrWhiteSpace(handler) && TurntableHandlers.Contains(handler.Trim());
        }

        private static bool IsMapLabelHandler(string handler)
        {
            return !string.IsNullOrWhiteSpace(handler) &&
                   (string.Equals(handler.Trim(), MapLabelHandler, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(handler.Trim(), "AlinasMapMod.MapLabelBuilder", StringComparison.OrdinalIgnoreCase));
        }

        private static JObject ConvertCustomComponentFields(string type, JObject item)
        {
            var fields = item["fields"] is JObject fieldObject
                ? (JObject)fieldObject.DeepClone()
                : new JObject();
            if (FuseIndustryComponentTypes.IsKnown(type))
            {
                return fields.HasValues ? fields : null;
            }

            AddCustomComponentFields(fields, item["extraData"] as JObject ?? item["ExtraData"] as JObject);
            AddCustomComponentFields(fields, item);
            return fields.HasValues ? fields : null;
        }

        private static void AddCustomComponentFields(JObject fields, JObject source)
        {
            if (fields == null || source == null)
            {
                return;
            }

            foreach (var property in source.Properties())
            {
                if (property.Value == null ||
                    property.Value.Type == JTokenType.Null ||
                    ComponentSchemaKeys.Contains(property.Name) ||
                    IsLegacyDirectiveKey(property.Name) ||
                    fields[property.Name] != null)
                {
                    continue;
                }

                fields[property.Name] = property.Value.DeepClone();
            }
        }
    }
}
