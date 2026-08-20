using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FUSE.Converter.Conversion
{
    /// <summary>
    /// Port of <c>convert_requirement</c>, <c>convert_requirements</c>,
    /// <c>fuse_package_id</c>, <c>legacy_load_after</c>,
    /// <c>mixinto_metadata</c>, and <c>extract_file_reference</c>.
    /// These translate the Railloader Definition.json metadata into
    /// the FUSE Info.json equivalents (Requirements / LoadAfter chains,
    /// mixinto metadata per fragment file).
    /// </summary>
    internal static class LegacyDefinitionConverter
    {
        private static readonly Regex FileReferencePattern =
            new Regex(@"^\s*file\((.+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Port of <c>extract_file_reference</c>. Legacy mixinto
        /// definitions reference their data files via
        /// <c>file("Some/Path.json")</c>; this strips the wrapping
        /// and surrounding quotes.
        /// </summary>
        public static string ExtractFileReference(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var match = FileReferencePattern.Match(value);
            if (!match.Success) return string.Empty;
            var inner = match.Groups[1].Value.Trim();
            return inner.Trim('"').Trim('\'');
        }

        /// <summary>
        /// Port of <c>convert_requirement</c>. Strips out core
        /// legacy requirement ids (railroader, strangecustoms, fuse,
        /// ...) since FUSE itself satisfies them; everything else
        /// becomes a <c>{ id, notBefore, notAfter }</c> object.
        /// </summary>
        public static JObject ConvertRequirement(JToken item)
        {
            return ConvertReference(item, true);
        }

        private static JObject ConvertReference(JToken item, bool filterReplacementCapabilities)
        {
            if (item == null) return null;

            if (item.Type == JTokenType.String)
            {
                var requirementId = item.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(requirementId)) return null;
                if (filterReplacementCapabilities && LegacyConverterConstants.IsCoreLegacyRequirement(requirementId)) return null;
                return new JObject { ["id"] = requirementId };
            }

            if (!(item is JObject obj)) return null;

            var id = (obj.Value<string>("id") ?? obj.Value<string>("Id") ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id)) return null;
            if (filterReplacementCapabilities && LegacyConverterConstants.IsCoreLegacyRequirement(id)) return null;

            var result = new JObject
            {
                ["id"] = id,
                ["notBefore"] = obj["notBefore"]?.DeepClone() ?? obj["NotBefore"]?.DeepClone(),
                ["notAfter"] = obj["notAfter"]?.DeepClone() ?? obj["NotAfter"]?.DeepClone(),
            };
            return JsonCleanHelper.CleanObject(result);
        }

        /// <summary>
        /// Port of <c>convert_requirements</c>. Filters out core
        /// requirements and packs the rest into a JArray of
        /// requirement objects. Empty list when input isn't an
        /// array.
        /// </summary>
        public static JArray ConvertRequirements(JToken value)
        {
            return ConvertReferences(value, true);
        }

        private static JArray ConvertReferences(JToken value, bool filterReplacementCapabilities)
        {
            var result = new JArray();
            if (!(value is JArray arr)) return result;

            foreach (var item in arr)
            {
                var converted = ConvertReference(item, filterReplacementCapabilities);
                if (converted != null)
                {
                    result.Add(converted);
                }
            }
            return result;
        }

        public static JArray LegacyConflictsWith(string modFolder)
        {
            var path = Path.Combine(modFolder ?? string.Empty, "Definition.json");
            if (!File.Exists(path))
            {
                return new JArray();
            }

            try
            {
                var definition = LegacyJsonReader.ReadJson(path) as JObject;
                var converted = ConvertReferences(
                    definition?["conflictsWith"] ?? definition?["ConflictsWith"],
                    false);
                return new JArray(converted.OfType<JObject>().Select(reference =>
                    JsonCleanHelper.CleanObject(new JObject
                    {
                        ["Id"] = reference["id"]?.DeepClone(),
                        ["NotBefore"] = reference["notBefore"]?.DeepClone(),
                        ["NotAfter"] = reference["notAfter"]?.DeepClone()
                    })));
            }
            catch (Exception)
            {
                return new JArray();
            }
        }

        /// <summary>
        /// Port of <c>fuse_package_id</c>. A legacy requirement id
        /// like "MyMod" becomes "MyMod.FUSE" (the converter shipped
        /// alongside the original package); ids already ending in
        /// ".FUSE" pass through unchanged and legacy ".RAIL" suffixes
        /// are replaced. Core legacy requirements
        /// return null so they get dropped from the LoadAfter chain.
        /// </summary>
        public static string FusePackageId(string requirementId)
        {
            var text = (requirementId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (LegacyConverterConstants.IsCoreLegacyRequirement(text)) return null;
            return ConvertedPackageId(text);
        }

        internal static string ConvertedPackageId(string packageId)
        {
            var text = (packageId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.EndsWith(".FUSE", StringComparison.OrdinalIgnoreCase)) return text;
            if (text.EndsWith(".RAIL", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - ".RAIL".Length).Trim();
            }
            if (string.IsNullOrEmpty(text)) return null;
            return text + ".FUSE";
        }

        /// <summary>
        /// Port of <c>legacy_load_after</c>. Reads Definition.json
        /// from the supplied folder and returns the ordered, dedup'd
        /// list of FUSE package ids the converted mod should load
        /// after (built from the requires + loadAfter lists, with
        /// core ids filtered out).
        /// </summary>
        public static List<string> LegacyLoadAfter(string modFolder)
        {
            var dependencies = LegacyDependencies(modFolder);
            var result = new List<string>(dependencies.Requires);
            result.AddRange(dependencies.LoadAfter);
            return Deduplicate(result);
        }

        /// <summary>
        /// Reads hard requirements separately from advisory ordering. The old
        /// converter flattened both into LoadAfter, which made a missing required
        /// package look optional to FUSE.
        /// </summary>
        public static (List<string> Requires, List<string> LoadAfter) LegacyDependencies(string modFolder)
        {
            var required = new List<string>();
            var loadAfter = new List<string>();
            var path = Path.Combine(modFolder ?? string.Empty, "Definition.json");
            if (!File.Exists(path)) return (required, loadAfter);

            JObject definition;
            try
            {
                definition = LegacyJsonReader.ReadJson(path) as JObject;
                if (definition == null) return (required, loadAfter);
            }
            catch (Exception)
            {
                return (required, loadAfter);
            }

            var requirements =
                definition["requires"] as JArray
                ?? definition["Requires"] as JArray
                ?? definition["requirements"] as JArray
                ?? definition["Requirements"] as JArray
                ?? new JArray();

            foreach (var requirement in ConvertRequirements(requirements))
            {
                var packageId = FusePackageId((requirement as JObject)?.Value<string>("id"));
                if (!string.IsNullOrEmpty(packageId))
                {
                    required.Add(packageId);
                }
            }

            var loadAfterToken = definition["loadAfter"] ?? definition["LoadAfter"];
            if (loadAfterToken is JArray la)
            {
                foreach (var item in la)
                {
                    var id = item.Type == JTokenType.String
                        ? item.Value<string>()
                        : (item as JObject)?.Value<string>("id") ?? (item as JObject)?.Value<string>("Id");
                    var packageId = FusePackageId(id);
                    if (!string.IsNullOrEmpty(packageId)) loadAfter.Add(packageId);
                }
            }
            else if (loadAfterToken?.Type == JTokenType.String)
            {
                var packageId = FusePackageId(loadAfterToken.Value<string>());
                if (!string.IsNullOrEmpty(packageId))
                {
                    loadAfter.Add(packageId);
                }
            }

            foreach (var id in EnumerateMixintoRequirementIds(
                         definition["mixintos"] ?? definition["Mixintos"]))
            {
                var packageId = FusePackageId(id);
                if (!string.IsNullOrEmpty(packageId))
                {
                    loadAfter.Add(packageId);
                }
            }

            return (Deduplicate(required), Deduplicate(loadAfter));
        }

        private static IEnumerable<string> EnumerateMixintoRequirementIds(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                yield break;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var id in EnumerateMixintoRequirementIds(item))
                    {
                        yield return id;
                    }
                }

                yield break;
            }

            if (!(value is JObject obj))
            {
                yield break;
            }

            foreach (var requirement in ConvertRequirements(obj["requires"] ?? obj["Requires"]).OfType<JObject>())
            {
                var id = requirement.Value<string>("id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }
            }

            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, "requires", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var id in EnumerateMixintoRequirementIds(property.Value))
                {
                    yield return id;
                }
            }
        }

        private static List<string> Deduplicate(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            foreach (var item in values ?? Array.Empty<string>())
            {
                if (seen.Add(item))
                {
                    ordered.Add(item);
                }
            }
            return ordered;
        }

        /// <summary>
        /// Port of <c>mixinto_metadata</c>. A legacy
        /// Definition.json can declare mixintos: targets that
        /// instruct Strange Customs to merge a separate JSON file
        /// into a runtime object at load time. Converting them
        /// preserves the target + the source file name so the FUSE
        /// loader can re-apply the merge.
        /// </summary>
        /// <returns>
        /// Tuple of (metadata-by-source-file, ordered-source-file-names).
        /// The ordered list is used by the source-file sorter to bias
        /// mixinto fragments earlier in the load order.
        /// </returns>
        public static (Dictionary<string, JObject> Metadata, List<string> OrderedFiles) MixintoMetadata(string modFolder)
        {
            var metadata = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var orderedFiles = new List<string>();

            var path = Path.Combine(modFolder ?? string.Empty, "Definition.json");
            if (!File.Exists(path)) return (metadata, orderedFiles);

            JObject definition;
            try
            {
                definition = LegacyJsonReader.ReadJson(path) as JObject;
                if (definition == null) return (metadata, orderedFiles);
            }
            catch (Exception)
            {
                return (metadata, orderedFiles);
            }

            void Record(string target, string reference, JArray requirements, JArray conflictsWith)
            {
                var referencedFile = ExtractFileReference(reference);
                if (string.IsNullOrEmpty(referencedFile)) return;

                var sourceFile = Path.GetFileName(referencedFile);
                var key = sourceFile.ToLowerInvariant();
                if (!metadata.ContainsKey(key))
                {
                    orderedFiles.Add(key);
                }
                metadata[key] = JsonCleanHelper.CleanObject(new JObject
                {
                    ["target"] = (target ?? string.Empty).Trim(),
                    ["sourceFile"] = sourceFile,
                    ["requires"] = requirements ?? new JArray(),
                    ["conflictsWith"] = conflictsWith ?? new JArray(),
                });
            }

            void VisitTarget(string target, JToken value)
            {
                if (value == null) return;

                if (value.Type == JTokenType.String)
                {
                    Record(target, value.Value<string>(), null, null);
                    return;
                }

                if (value is JArray arr)
                {
                    foreach (var item in arr) VisitTarget(target, item);
                    return;
                }

                if (!(value is JObject obj)) return;

                var requirements = ConvertRequirements(obj["requires"] ?? obj["Requires"]);
                var conflictsWith = ConvertReferences(obj["conflictsWith"] ?? obj["ConflictsWith"], false);
                var reference = obj.Value<string>("mixinto") ?? obj.Value<string>("Mixinto");
                Record(target, reference, requirements, conflictsWith);
            }

            var mixintos = definition["mixintos"] as JObject ?? definition["Mixintos"] as JObject;
            if (mixintos != null)
            {
                foreach (var prop in mixintos.Properties())
                {
                    VisitTarget(prop.Name, prop.Value);
                }
            }

            return (metadata, orderedFiles);
        }
    }
}
