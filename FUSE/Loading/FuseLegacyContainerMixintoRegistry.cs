using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AssetPack.Runtime;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Definition;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    // Temporary legacy support for old loader container:<id> mixinto fragments.
    // Native FUSE packages should define assets/cars directly through supported
    // FUSE schemas; this bridge exists only to keep existing installs working
    // during the compatibility window.
    internal static class FuseLegacyContainerMixintoRegistry
    {
        private const string ContainerTargetPrefix = "container:";
        private static readonly char[] PathSeparators = { '.', '/' };
        private static readonly object DiscoveryLock = new object();
        private static readonly object ApplyLock = new object();
        private static Dictionary<string, List<LegacyContainerMixinto>> MixintosByTarget;
        private static readonly HashSet<string> AppliedMixintos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ProcessedContainers = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> WarnedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void Reset()
        {
            lock (DiscoveryLock)
            {
                MixintosByTarget = null;
            }

            lock (ApplyLock)
            {
                AppliedMixintos.Clear();
                ProcessedContainers.Clear();
            }
        }

        internal static void ApplyToContainer(AssetPackRuntimeStore store, Container container)
        {
            if (store == null || container?.Objects == null || container.Objects.Count == 0)
            {
                return;
            }

            var mixintosByTarget = GetMixintosByTarget();
            if (mixintosByTarget.Count == 0)
            {
                return;
            }

            // AssetPackRuntimeStore.Container() is hit on every prefab lookup, so this
            // Postfix runs constantly during scenery apply. Once a container has been
            // visited and all matching mixintos recorded in AppliedMixintos, repeat
            // calls must return immediately - otherwise each prefab load re-reads every
            // mixinto JSON file from disk and re-scans container.Objects per target.
            var containerKey = RuntimeHelpers.GetHashCode(container).ToString("X");
            var processedKey = (store.Identifier ?? string.Empty) + "|" + containerKey;

            int applied;
            lock (ApplyLock)
            {
                if (!ProcessedContainers.Add(processedKey))
                {
                    return;
                }

                applied = 0;
                foreach (var target in mixintosByTarget.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                {
                    var targetItem = FindItem(container, target);
                    if (targetItem == null)
                    {
                        continue;
                    }

                    foreach (var mixinto in mixintosByTarget[target])
                    {
                        applied += ApplyMixinto(store, container, containerKey, targetItem, mixinto);
                    }
                }
            }

            if (applied > 0)
            {
                FuseLog.Info(
                    $"FUSE legacy support applied {applied} container mixinto object(s) to asset store '{store.Identifier}'. " +
                    "This is temporary legacy compatibility, not native FUSE package data.");
            }
        }

        private static int ApplyMixinto(
            AssetPackRuntimeStore store,
            Container container,
            string containerKey,
            ContainerItem defaultTargetItem,
            LegacyContainerMixinto mixinto)
        {
            if (!ShouldApply(mixinto))
            {
                return 0;
            }

            JObject root;
            try
            {
                root = FuseLegacyDataConverter.ReadLegacyObject(mixinto.SourcePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                WarnOnce(mixinto.SourcePath, $"FUSE legacy support could not parse container mixinto '{mixinto.SourcePath}': {ex.Message}");
                return 0;
            }

            var objects = root["objects"] as JArray;
            if (objects == null || objects.Count == 0)
            {
                return 0;
            }

            var applied = 0;
            for (var index = 0; index < objects.Count; index++)
            {
                if (!(objects[index] is JObject patch))
                {
                    continue;
                }

                var applyKey = $"{store.Identifier}|{containerKey}|{mixinto.SourcePath}|{index}";
                if (!AppliedMixintos.Add(applyKey))
                {
                    continue;
                }

                var sourceId = ReadFindIdentifier(patch) ?? mixinto.TargetIdentifier;
                var sourceItem = FindItem(container, sourceId) ?? defaultTargetItem;
                if (sourceItem == null)
                {
                    continue;
                }

                try
                {
                    var patchedItem = BuildPatchedItem(sourceItem, patch);
                    if (patchedItem == null || string.IsNullOrWhiteSpace(patchedItem.Identifier))
                    {
                        continue;
                    }

                    UpsertItem(container, patchedItem);
                    applied++;
                }
                catch (Exception ex)
                {
                    WarnOnce(
                        $"{mixinto.SourcePath}:{index}",
                        $"FUSE legacy support could not apply container mixinto '{mixinto.SourcePath}' object[{index}]: {ex.GetBaseException().Message}");
                }
            }

            return applied;
        }

        private static bool ShouldApply(LegacyContainerMixinto mixinto)
        {
            if (mixinto == null)
            {
                return false;
            }

            var definition = new FuseModDefinition
            {
                Id = mixinto.PackageId,
                Name = mixinto.PackageName,
                ModVersion = mixinto.PackageVersion,
                Mixinto = new FuseMixintoDefinition
                {
                    Target = ContainerTargetPrefix + mixinto.TargetIdentifier,
                    SourceFile = Path.GetFileName(mixinto.SourcePath),
                    Requires = mixinto.Requirements
                }
            };

            var loaded = new FuseLoadedMod(mixinto.PackagePath, mixinto.SourcePath, definition);
            if (FuseModRequirementResolver.ShouldApply(loaded, out var reason))
            {
                return true;
            }

            FuseLog.Info(
                $"FUSE legacy support skipped container mixinto package='{mixinto.PackageId}' " +
                $"sourceFile='{Path.GetFileName(mixinto.SourcePath)}' reason='{reason}'.");
            return false;
        }

        private static ContainerItem BuildPatchedItem(ContainerItem sourceItem, JObject patch)
        {
            var source = SerializeItem(sourceItem);
            var patchCopy = (JObject)patch.DeepClone();

            ApplyLegacyObjectPatch(source, patchCopy);
            NormalizeLegacyAssetPackReferences(source);
            return DeserializeItem(source);
        }

        private static void ApplyLegacyObjectPatch(JObject target, JObject patch)
        {
            if (target == null || patch == null)
            {
                return;
            }

            foreach (var property in patch.Properties().ToArray())
            {
                if (IsLegacyDirective(property.Name))
                {
                    continue;
                }

                if (TryReadReplaceDirective(property.Value, out var replacement))
                {
                    SetProperty(target, property.Name, MaterializeLegacyPatchToken(replacement));
                    continue;
                }

                var targetValue = TryGetProperty(target, property.Name, out var targetProperty)
                    ? targetProperty.Value
                    : null;

                if (property.Value is JObject patchObject && targetValue is JObject targetObject)
                {
                    ApplyLegacyObjectPatch(targetObject, patchObject);
                    continue;
                }

                if (property.Value is JArray patchArray)
                {
                    if (ContainsLegacyArrayDirective(patchArray))
                    {
                        var targetArray = targetValue as JArray;
                        if (targetArray == null)
                        {
                            targetArray = new JArray();
                            SetProperty(target, property.Name, targetArray);
                        }

                        ApplyLegacyArrayPatch(targetArray, patchArray);
                    }
                    else
                    {
                        SetProperty(target, property.Name, MaterializeLegacyPatchToken(patchArray));
                    }

                    continue;
                }

                SetProperty(target, property.Name, MaterializeLegacyPatchToken(property.Value));
            }
        }

        private static void ApplyLegacyArrayPatch(JArray target, JArray patch)
        {
            if (target == null || patch == null)
            {
                return;
            }

            foreach (var item in patch)
            {
                if (!(item is JObject patchObject))
                {
                    target.Add(MaterializeLegacyPatchToken(item));
                    continue;
                }

                if (TryGetLegacyDirective(patchObject, "$add", out var addToken))
                {
                    AddLegacyArrayValue(target, addToken);
                    continue;
                }

                if (TryGetLegacyDirective(patchObject, "$find", out var findToken))
                {
                    var found = FindArrayItem(target, findToken as JArray);
                    if (found == null)
                    {
                        continue;
                    }

                    if (TryGetLegacyDirective(patchObject, "$remove", out var foundRemoveToken) ||
                        TryGetLegacyDirective(patchObject, "$delete", out foundRemoveToken))
                    {
                        if (IsTruthyDirective(foundRemoveToken))
                        {
                            target.Remove(found);
                        }

                        continue;
                    }

                    if (TryReadReplaceDirective(patchObject, out var replacement))
                    {
                        ReplaceArrayItem(target, found, replacement);
                        continue;
                    }

                    if (found is JObject foundObject)
                    {
                        var itemPatch = (JObject)patchObject.DeepClone();
                        RemoveLegacyDirective(itemPatch, "$find");
                        RemoveLegacyDirective(itemPatch, "$clone");
                        ApplyLegacyObjectPatch(foundObject, itemPatch);
                    }

                    continue;
                }

                if (TryGetLegacyDirective(patchObject, "$remove", out var removeToken) ||
                    TryGetLegacyDirective(patchObject, "$delete", out removeToken))
                {
                    RemoveLegacyArrayValue(target, removeToken);
                    continue;
                }

                if (TryReadReplaceDirective(patchObject, out var arrayReplacement))
                {
                    ReplaceLegacyArray(target, arrayReplacement);
                    continue;
                }

                target.Add(MaterializeLegacyPatchToken(patchObject));
            }
        }

        private static void AddLegacyArrayValue(JArray target, JToken value)
        {
            if (target == null || value == null || value.Type == JTokenType.Null)
            {
                return;
            }

            if (value is JArray array)
            {
                foreach (var item in array)
                {
                    target.Add(MaterializeLegacyPatchToken(item));
                }

                return;
            }

            target.Add(MaterializeLegacyPatchToken(value));
        }

        private static void RemoveLegacyArrayValue(JArray target, JToken value)
        {
            if (target == null || value == null)
            {
                return;
            }

            var removeValues = value is JArray array
                ? array.ToArray()
                : new[] { value };

            for (var index = target.Count - 1; index >= 0; index--)
            {
                if (removeValues.Any(removeValue => LegacyTokenEquals(target[index], removeValue)))
                {
                    target.RemoveAt(index);
                }
            }
        }

        private static void ReplaceLegacyArray(JArray target, JToken value)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            AddLegacyArrayValue(target, value);
        }

        private static JToken MaterializeLegacyPatchToken(JToken token)
        {
            if (token == null)
            {
                return JValue.CreateNull();
            }

            if (TryReadReplaceDirective(token, out var replacement))
            {
                return MaterializeLegacyPatchToken(replacement);
            }

            if (token is JObject obj)
            {
                var result = new JObject();
                foreach (var property in obj.Properties())
                {
                    if (IsLegacyDirective(property.Name))
                    {
                        continue;
                    }

                    result[property.Name] = MaterializeLegacyPatchToken(property.Value);
                }

                return result;
            }

            if (token is JArray array)
            {
                var result = new JArray();
                foreach (var item in array)
                {
                    if (item is JObject itemObject && TryGetLegacyDirective(itemObject, "$add", out var addToken))
                    {
                        if (addToken is JArray addArray)
                        {
                            foreach (var addItem in addArray)
                            {
                                result.Add(MaterializeLegacyPatchToken(addItem));
                            }
                        }
                        else
                        {
                            result.Add(MaterializeLegacyPatchToken(addToken));
                        }

                        continue;
                    }

                    result.Add(MaterializeLegacyPatchToken(item));
                }

                return result;
            }

            return token.DeepClone();
        }

        private static bool ContainsLegacyArrayDirective(JArray array)
        {
            return array?.OfType<JObject>().Any(item =>
                TryGetLegacyDirective(item, "$add", out _) ||
                TryGetLegacyDirective(item, "$remove", out _) ||
                TryGetLegacyDirective(item, "$delete", out _) ||
                TryGetLegacyDirective(item, "$find", out _) ||
                TryReadReplaceDirective(item, out _)) == true;
        }

        private static bool TryReadReplaceDirective(JToken token, out JToken replacement)
        {
            replacement = null;
            return token is JObject obj && TryGetLegacyDirective(obj, "$replace", out replacement);
        }

        private static bool TryGetLegacyDirective(JObject obj, string name, out JToken value)
        {
            value = null;
            return obj != null &&
                   obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out value);
        }

        private static bool IsTruthyDirective(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            return true;
        }

        private static bool IsLegacyDirective(string name)
        {
            return string.Equals(name, "$find", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$clone", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$add", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$replace", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$remove", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "$delete", StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveLegacyDirective(JObject obj, string name)
        {
            var property = obj?.Properties()
                .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            property?.Remove();
        }

        private static bool TryGetProperty(JObject obj, string name, out JProperty property)
        {
            property = obj?.Properties()
                .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            return property != null;
        }

        private static void SetProperty(JObject obj, string name, JToken value)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (TryGetProperty(obj, name, out var property))
            {
                property.Value = value ?? JValue.CreateNull();
            }
            else
            {
                obj[name] = value ?? JValue.CreateNull();
            }
        }

        private static JToken FindArrayItem(JArray target, JArray conditions)
        {
            if (target == null || conditions == null || conditions.Count == 0)
            {
                return null;
            }

            return target.FirstOrDefault(item => MatchesFindConditions(item, conditions));
        }

        private static bool MatchesFindConditions(JToken candidate, JArray conditions)
        {
            foreach (var condition in conditions.OfType<JObject>())
            {
                var path = ReadString(condition, "path");
                var actual = string.IsNullOrWhiteSpace(path)
                    ? candidate
                    : SelectLegacyPath(candidate, path);
                var expected = condition["value"];
                var comparison = ReadString(condition, "comp") ?? "equals";
                if (!CompareLegacyFindValue(actual, expected, comparison))
                {
                    return false;
                }
            }

            return true;
        }

        private static JToken SelectLegacyPath(JToken token, string path)
        {
            if (token == null || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var current = token;
            foreach (var part in path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (current is JObject obj)
                {
                    current = obj.Properties()
                        .FirstOrDefault(property => string.Equals(property.Name, part, StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                    continue;
                }

                if (current is JArray array && int.TryParse(part, out var index) && index >= 0 && index < array.Count)
                {
                    current = array[index];
                    continue;
                }

                return null;
            }

            return current;
        }

        private static bool CompareLegacyFindValue(JToken actual, JToken expected, string comparison)
        {
            var normalizedComparison = (comparison ?? "equals").Trim().ToLowerInvariant();
            if (normalizedComparison == "exists")
            {
                return actual != null && actual.Type != JTokenType.Null;
            }

            if (actual == null)
            {
                return normalizedComparison == "notexists";
            }

            if (normalizedComparison == "notequals" || normalizedComparison == "not-equals" || normalizedComparison == "!=")
            {
                return !LegacyTokenEquals(actual, expected);
            }

            if (normalizedComparison == "contains")
            {
                return (actual.ToString() ?? string.Empty).IndexOf(expected?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return LegacyTokenEquals(actual, expected);
        }

        private static bool LegacyTokenEquals(JToken actual, JToken expected)
        {
            if (actual == null || expected == null)
            {
                return actual == expected;
            }

            if (JToken.DeepEquals(actual, expected))
            {
                return true;
            }

            return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static void ReplaceArrayItem(JArray array, JToken existing, JToken replacement)
        {
            var index = array?.IndexOf(existing) ?? -1;
            if (index >= 0)
            {
                array[index] = MaterializeLegacyPatchToken(replacement);
            }
        }

        private static JObject SerializeItem(ContainerItem item)
        {
            var wrapper = new Container
            {
                Objects = new List<ContainerItem> { item }
            };
            var root = JObject.Parse(ContainerSerialization.Serialize(wrapper));
            return (JObject)((JArray)root["objects"])[0];
        }

        // Cached reflection handle to ContainerSerialization's private settings factory.
        // We call it directly through Newtonsoft instead of going through the public
        // ContainerSerialization.Deserialize entry point, because old-loader plugins
        // (notably LegosLibraryOfStuff) Harmony-Postfix that entry point and mutate
        // shared, cached Component instances on every invocation — invoking the
        // entry point during a legacy mixinto patch would re-fire those postfixes
        // and accumulate the mutations (component names prefixed twice, etc.),
        // which manifests as the Component Group toggle on cars going dead.
        // The plugin's first run during the game's primary container load remains
        // unaffected; we only bypass the entry point on the per-item re-deserialize
        // FUSE does after applying the patch JObject.
        private static readonly MethodInfo ContainerSerializerSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static ContainerItem DeserializeItem(JObject item)
        {
            var wrapper = new JObject
            {
                ["objects"] = new JArray(item)
            };

            var text = wrapper.ToString(Formatting.None);

            if (ContainerSerializerSettingsMethod != null)
            {
                try
                {
                    var settings = (JsonSerializerSettings)ContainerSerializerSettingsMethod.Invoke(null, null);
                    var container = JsonConvert.DeserializeObject<Container>(text, settings);
                    container?.Awake();
                    return container?.Objects?.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        "FUSE legacy container mixinto fell back to ContainerSerialization.Deserialize " +
                        $"because the reflection-based bypass failed: {ex.GetBaseException().Message}. " +
                        "Old-loader Deserialize postfixes will re-fire for this item, which may double-apply " +
                        "their edits.");
                }
            }

            return ContainerSerialization.Deserialize(text)?.Objects?.FirstOrDefault();
        }

        private static void NormalizeLegacyAssetPackReferences(JToken token)
        {
            if (token == null)
            {
                return;
            }

            if (token is JObject obj)
            {
                foreach (var property in obj.Properties().ToArray())
                {
                    if (string.Equals(property.Name, "assetPackIdentifier", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.Type == JTokenType.String)
                    {
                        var original = property.Value.Value<string>();
                        var resolved = FuseAssetPackRegistry.ResolveLegacyAssetPackIdentifier(original);
                        if (!string.Equals(original, resolved, StringComparison.Ordinal))
                        {
                            property.Value = resolved;
                        }
                    }
                    else
                    {
                        NormalizeLegacyAssetPackReferences(property.Value);
                    }
                }

                return;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    NormalizeLegacyAssetPackReferences(item);
                }
            }
        }

        private static ContainerItem FindItem(Container container, string identifier)
        {
            if (container?.Objects == null || string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            return container.Objects.FirstOrDefault(item =>
                string.Equals(item?.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        }

        private static void UpsertItem(Container container, ContainerItem item)
        {
            var index = container.Objects.FindIndex(existing =>
                string.Equals(existing?.Identifier, item.Identifier, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                container.Objects[index] = item;
            }
            else
            {
                container.Objects.Add(item);
            }
        }

        private static string ReadFindIdentifier(JObject patch)
        {
            var find = patch?["$find"] as JArray;
            if (find == null)
            {
                return null;
            }

            foreach (var condition in find.OfType<JObject>())
            {
                var path = ReadString(condition, "path");
                var comp = ReadString(condition, "comp");
                if (string.Equals(path, "identifier", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(comp) || string.Equals(comp, "equals", StringComparison.OrdinalIgnoreCase)))
                {
                    return ReadString(condition, "value");
                }
            }

            return null;
        }

        private static Dictionary<string, List<LegacyContainerMixinto>> GetMixintosByTarget()
        {
            lock (DiscoveryLock)
            {
                if (MixintosByTarget != null)
                {
                    return MixintosByTarget;
                }

                MixintosByTarget = DiscoverMixintos()
                    .GroupBy(mixinto => mixinto.TargetIdentifier, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.OrderBy(item => item.PackageOrder)
                            .ThenBy(item => item.DiscoveryOrder)
                            .ThenBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase);
                return MixintosByTarget;
            }
        }

        private static IEnumerable<LegacyContainerMixinto> DiscoverMixintos()
        {
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                yield break;
            }

            var packageOrderByPath = FuseDataPackageDiscovery.GetPackageManifestSnapshots()
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.FolderPath))
                .GroupBy(snapshot => snapshot.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Order, StringComparer.OrdinalIgnoreCase);
            var discoveryOrder = 0;
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var manifest = TryReadManifest(packagePath);
                if (manifest == null || !ShouldInspectPackage(manifest))
                {
                    continue;
                }

                var mixintos = manifest.RawDefinition["mixintos"] as JObject ??
                               manifest.RawDefinition["Mixintos"] as JObject;
                if (mixintos == null)
                {
                    continue;
                }

                foreach (var property in mixintos.Properties())
                {
                    if (!property.Name.StartsWith(ContainerTargetPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var targetIdentifier = property.Name.Substring(ContainerTargetPrefix.Length).Trim();
                    if (string.IsNullOrWhiteSpace(targetIdentifier))
                    {
                        continue;
                    }

                    foreach (var entry in EnumerateMixintoEntries(property.Name, property.Value, null))
                    {
                        var sourcePath = ResolvePackageFile(packagePath, entry.Reference);
                        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                        {
                            WarnOnce(
                                $"{packagePath}:{entry.Reference}",
                                $"FUSE legacy support skipped missing container mixinto '{entry.Reference}' for package '{manifest.Id}'.");
                            continue;
                        }

                        yield return new LegacyContainerMixinto
                        {
                            PackageId = manifest.Id,
                            PackageName = manifest.Name,
                            PackageVersion = manifest.Version,
                            PackagePath = packagePath,
                            PackageOrder = packageOrderByPath.TryGetValue(packagePath, out var packageOrder)
                                ? packageOrder
                                : int.MaxValue,
                            DiscoveryOrder = discoveryOrder++,
                            TargetIdentifier = targetIdentifier,
                            SourcePath = sourcePath,
                            Requirements = ConvertRequirements(entry.Requirements)
                        };
                    }
                }
            }
        }

        private static IEnumerable<MixintoEntry> EnumerateMixintoEntries(string target, JToken token, JArray inheritedRequirements)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var reference = ExtractFileReference(token.Value<string>());
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    yield return new MixintoEntry(reference, inheritedRequirements);
                }

                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var entry in EnumerateMixintoEntries(target, item, inheritedRequirements))
                    {
                        yield return entry;
                    }
                }

                yield break;
            }

            if (!(token is JObject obj))
            {
                yield break;
            }

            var requirements = obj["requires"] as JArray ??
                               obj["Requires"] as JArray ??
                               inheritedRequirements;
            var direct = ReadString(obj, "mixinto", "Mixinto");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                var reference = ExtractFileReference(direct);
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    yield return new MixintoEntry(reference, requirements);
                }
            }

            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, "mixinto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "requires", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var entry in EnumerateMixintoEntries(target, property.Value, requirements))
                {
                    yield return entry;
                }
            }
        }

        private static FuseModRequirement[] ConvertRequirements(JArray requirements)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return Array.Empty<FuseModRequirement>();
            }

            return requirements
                .Select(ConvertRequirement)
                .Where(requirement => requirement != null && !string.IsNullOrWhiteSpace(requirement.Id))
                .ToArray();
        }

        private static FuseModRequirement ConvertRequirement(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return new FuseModRequirement { Id = token.Value<string>() };
            }

            var obj = token as JObject;
            if (obj == null)
            {
                return null;
            }

            return new FuseModRequirement
            {
                Id = ReadString(obj, "id", "Id"),
                NotBefore = ReadString(obj, "notBefore", "NotBefore"),
                NotAfter = ReadString(obj, "notAfter", "NotAfter")
            };
        }

        private static LegacyManifest TryReadManifest(string packagePath)
        {
            var definitionPath = Path.Combine(packagePath, "Definition.json");
            if (!File.Exists(definitionPath))
            {
                return null;
            }

            try
            {
                var definition = FuseLegacyDataConverter.ReadLegacyObject(definitionPath);
                var id = ReadString(definition, "id", "Id") ?? Path.GetFileName(packagePath);
                return new LegacyManifest
                {
                    Id = id,
                    Name = ReadString(definition, "name", "Name") ?? id,
                    Version = ReadString(definition, "version", "Version") ?? string.Empty,
                    PackagePath = packagePath,
                    RawDefinition = definition
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                WarnOnce(packagePath, $"FUSE legacy support ignored '{packagePath}' because Definition.json could not be parsed: {ex.Message}");
                return null;
            }
        }

        private static bool ShouldInspectPackage(LegacyManifest manifest)
        {
            if (manifest == null)
            {
                return false;
            }

            if (string.Equals(manifest.Id, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (FuseUmmState.TryGetDisabledReason(manifest.PackagePath, manifest.Id, out _))
            {
                return false;
            }

            return FuseModSetService.IsPackageEnabledByActiveSet(manifest.Id, manifest.PackagePath);
        }

        private static string ExtractFileReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                text = text.Substring(open + 1, close - open - 1);
            }

            return text.Trim().Trim('"', '\'');
        }

        private static string ResolvePackageFile(string folderPath, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return string.Empty;
            }

            var relative = reference.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(folderPath, relative));
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token) &&
                    token.Type != JTokenType.Null)
                {
                    var text = token.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return null;
        }

        private static void WarnOnce(string key, string message)
        {
            if (!WarnedFiles.Add(key ?? string.Empty))
            {
                return;
            }

            FuseLog.Warning(message);
        }

        private sealed class LegacyManifest
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
            public string PackagePath { get; set; }
            public JObject RawDefinition { get; set; }
        }

        private sealed class LegacyContainerMixinto
        {
            public string PackageId { get; set; }
            public string PackageName { get; set; }
            public string PackageVersion { get; set; }
            public string PackagePath { get; set; }
            public int PackageOrder { get; set; } = int.MaxValue;
            public int DiscoveryOrder { get; set; }
            public string TargetIdentifier { get; set; }
            public string SourcePath { get; set; }
            public FuseModRequirement[] Requirements { get; set; } = Array.Empty<FuseModRequirement>();
        }

        private readonly struct MixintoEntry
        {
            public MixintoEntry(string reference, JArray requirements)
            {
                Reference = reference;
                Requirements = requirements;
            }

            public string Reference { get; }
            public JArray Requirements { get; }
        }
    }
}
