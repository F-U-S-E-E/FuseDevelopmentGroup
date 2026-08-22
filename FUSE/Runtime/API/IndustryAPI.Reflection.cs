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
        private const string LegacyPay4ResourceType =
            "ConfusingSupplements.IndustryComponents.Pay4Resource";
        private const string LegacyCaptiveConversionLoaderType =
            "ConfusingSupplements.IndustryComponents.CaptiveConversionLoader";
        private const string LegacyCaptiveConversionUnloaderType =
            "ConfusingSupplements.IndustryComponents.CaptiveConversionUnloader";

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

            // Route the legacy "Pay4Resource" type name to FUSE's native
            // implementation. The behaviour and configuration surface are
            // reconstructed independently from the public JSON contract
            // documented by packs like Foxy's Kirkland Purchasable Coal
            // Patch — no third-party assembly is required at runtime.
            if (string.Equals(normalized, LegacyPay4ResourceType, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FusePay4ResourceIndustryComponent);
            }

            if (string.Equals(normalized, LegacyCaptiveConversionLoaderType, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FuseCaptiveConversionLoader);
            }

            if (string.Equals(normalized, LegacyCaptiveConversionUnloaderType, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FuseCaptiveConversionUnloader);
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
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE skipped industry component type probe assembly='{assembly.FullName ?? "<unknown>"}' " +
                        $"type='{type}' reason='{ex.Message}'.");
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

            if (component is FuseCaptiveConversionLoader)
            {
                return LegacyCaptiveConversionLoaderType;
            }

            if (component is FuseCaptiveConversionUnloader)
            {
                return LegacyCaptiveConversionUnloaderType;
            }

            if (component is FusePay4ResourceIndustryComponent)
            {
                return LegacyPay4ResourceType;
            }

            return component.GetType().FullName;
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

        private static void ApplyCustomFieldBag(object instance, Dictionary<string, object> fields)
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

                if (!IsUsableTrackSpan(span))
                {
                    FuseLog.Warning(
                        $"FUSE track span '{id}' has a missing or invalid endpoint after graph conflict resolution; " +
                        "it was omitted from the industry component so opening operations UI cannot crash.");
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
                if (!IsUsableTrackSpan(span))
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

        internal static bool IsUsableTrackSpan(TrackSpan span)
        {
            if (span == null)
            {
                return false;
            }

            try
            {
                return span.IsValid;
            }
            catch
            {
                return false;
            }
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
    }
}
