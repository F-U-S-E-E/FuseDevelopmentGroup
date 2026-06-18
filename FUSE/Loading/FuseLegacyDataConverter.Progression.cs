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

        private static void ConvertProgression(JObject source, JObject root)
        {
            var progression = source["progression"] as JObject;
            if (progression != null)
            {
                var progressionId = ReadString(progression, "progressionId");
                if (!string.IsNullOrWhiteSpace(progressionId))
                {
                    root["progression"]["progressionId"] = progressionId;
                }

                if (progression["sections"] is JArray sections)
                {
                    foreach (var section in sections)
                    {
                        ((JArray)root["progression"]["sections"]).Add(NormalizeProgressionValue(section));
                    }
                }

                MergeProgressionDictionary(progression["progressions"], root["progression"]["progressions"] as JObject);
                MergeMapFeatureDictionary(progression["mapFeatures"], root["progression"]["mapFeatures"] as JObject);
            }

            MergeProgressionDictionary(source["progressions"], root["progression"]["progressions"] as JObject);
            MergeMapFeatureDictionary(source["mapFeatures"], root["progression"]["mapFeatures"] as JObject);
        }

        private static void MergeProgressionDictionary(JToken source, JObject target)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                target[property.Name] = NormalizeProgressionValue(property.Value);
            }
        }

        private static void MergeMapFeatureDictionary(JToken source, JObject target)
        {
            if (!(source is JObject obj) || target == null)
            {
                return;
            }

            foreach (var property in obj.Properties())
            {
                var value = NormalizeProgressionValue(property.Value);
                if (value is JObject mapFeature &&
                    string.IsNullOrWhiteSpace(ReadString(mapFeature, "displayName")))
                {
                    mapFeature["displayName"] = property.Name;
                }

                target[property.Name] = value;
            }
        }

        private static JToken NormalizeProgressionValue(JToken value)
        {
            if (value is JArray array)
            {
                return new JArray(array.Select(NormalizeProgressionValue));
            }

            if (!(value is JObject obj))
            {
                return value?.DeepClone();
            }

            var result = new JObject();
            foreach (var property in obj.Properties())
            {
                var targetKey = NormalizeProgressionKey(property.Name);
                if (string.Equals(targetKey, "direction", StringComparison.OrdinalIgnoreCase))
                {
                    result[targetKey] = NormalizeDeliveryDirection(property.Value);
                }
                else
                {
                    if (string.Equals(targetKey, "deliveryPhases", StringComparison.OrdinalIgnoreCase))
                    {
                        result[targetKey] = NormalizeDeliveryPhases(property.Value);
                        continue;
                    }

                    // We deliberately do NOT pre-collapse boolean-dictionary
                    // fields here (the SC convention: <c>{ "foo": true,
                    // "bar": false }</c> on tracksEnable / tracksAvail /
                    // prerequisites / etc.). The downstream
                    // <see cref="FUSE.Authoring.Serialization.FuseStringPatchConverter"/>
                    // recognises both the array shape (replace) and the
                    // object shape (merge: keys with true ADD, keys with
                    // false REMOVE) and stores them on
                    // <see cref="FUSE.Authoring.Data.FuseStringPatch.Set"/> vs
                    // <see cref="FUSE.Authoring.Data.FuseStringPatch.Patch"/>
                    // accordingly. <see cref="ApplyMapFeatureDefinition"/>
                    // then merges with whatever the live runtime
                    // <c>MapFeature</c> already has via
                    // <see cref="FuseStringPatch.ApplyTo"/>.
                    //
                    // The previous behaviour here called
                    // <c>BoolDictionaryToArray</c> for the boolean-dict
                    // fields and flattened them into a JSON array of just
                    // the truthy keys, which the converter then read as a
                    // REPLACE set — silently dropping the rest of the
                    // base feature's tracksEnable list. The MaconCounty
                    // mod's <c>"alarka": { "trackGroupsEnableOnUnlock":
                    // { "alext-off": true } }</c> patch was meant to ADD
                    // <c>alext-off</c> on top of the base game's
                    // <c>[s3a]</c>; under the old normalisation it
                    // REPLACED with <c>[alext-off]</c>, dropped s3a, and
                    // left the Alarka branch track group as an unowned
                    // orphan (so the orphan-finaliser had to guess what
                    // to do, and either visibly leaked or wholesale
                    // hid the rails). Leaving the object shape intact
                    // here makes the patch behave the way SC's
                    // documented object-key merge always did.
                    result[targetKey] = NormalizeProgressionValue(property.Value);
                }
            }

            return CleanObject(result);
        }

        private static JToken NormalizeDeliveryPhases(JToken value)
        {
            if (!(value is JArray array))
            {
                return NormalizeProgressionValue(value);
            }

            var result = new JArray();
            foreach (var item in array)
            {
                var normalized = NormalizeProgressionValue(item);
                if (normalized is JObject phase && !phase.HasValues)
                {
                    // Legacy progressions use `{}` as a valid free phase:
                    // zero cost, no deliveries. The generic cleaner treats an
                    // empty object as absent, which collapses `[{}]` into zero
                    // phases and crashes the vanilla milestones UI.
                    phase["cost"] = 0;
                }

                if (normalized != null && normalized.Type != JTokenType.Null)
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        private static string NormalizeProgressionKey(string key)
        {
            var lower = (key ?? string.Empty).ToLowerInvariant();
            switch (lower)
            {
                case "displayname":
                    return "displayName";
                case "name":
                    return "name";
                case "defaultenableinsandbox":
                    return "initiallyEnabled";
                case "prerequisites":
                    return "prerequisiteFeatureIds";
                case "industrycomponent":
                    return "industryComponentId";
                case "load":
                    return "loadId";
                default:
                    return key;
            }
        }

        private static JToken NormalizeDeliveryDirection(JToken value)
        {
            var text = value == null ? string.Empty : value.ToString().Trim().ToLowerInvariant();
            if (text == "0" || text == "loadtoindustry" || text == "toindustry" || text == "to" || text == "import")
            {
                return "loadToIndustry";
            }

            if (text == "1" || text == "loadfromindustry" || text == "fromindustry" || text == "from" || text == "export")
            {
                return "loadFromIndustry";
            }

            return value?.DeepClone();
        }

        private static bool IsBooleanDictionaryArrayField(string key)
        {
            switch (key)
            {
                case "prerequisiteFeatureIds":
                case "prerequisiteSections":
                case "prerequisiteSectionIds":
                case "enableFeaturesOnUnlock":
                case "disableFeaturesOnUnlock":
                case "enableFeaturesOnAvailable":
                case "unlockIncludeIndustries":
                case "unlockExcludeIndustries":
                case "unlockIncludeIndustryComponents":
                case "areasEnableOnUnlock":
                case "gameObjectsEnableOnUnlock":
                case "trackGroupsEnableOnUnlock":
                case "trackGroupsAvailableOnUnlock":
                    return true;
                default:
                    return false;
            }
        }

        private static JArray BoolDictionaryToArray(JObject value)
        {
            var result = new JArray();
            foreach (var property in value.Properties())
            {
                if (property.Value.Type == JTokenType.Boolean && !property.Value.Value<bool>())
                {
                    continue;
                }

                if (property.Value.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(property.Name))
                {
                    result.Add(property.Name);
                }
            }

            return result;
        }
    }
}
