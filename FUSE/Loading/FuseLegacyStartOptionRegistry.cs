using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;

namespace FUSE.Loading
{
    /// <summary>
    /// Promotes the enabled feature list preserved by the legacy start
    /// converter into the progression's Company-start feature patch. Without
    /// this bridge, the progression disables its starter track groups before
    /// Railroader tries to place the starter equipment.
    /// </summary>
    internal static class FuseLegacyStartOptionRegistry
    {
        private const string ExtensionKey = "legacyStartOption";

        internal static void MergeEnabledFeaturesIntoProgressions(
            IEnumerable<FuseLoadedMod> loadedMods)
        {
            var loaded = (loadedMods ?? Enumerable.Empty<FuseLoadedMod>())
                .Where(item => item?.Definition != null)
                .ToArray();
            var featuresByProgression = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in loaded)
            {
                if (!TryReadStartFeatures(
                        item,
                        out var progressionId,
                        out var featureIds))
                {
                    continue;
                }

                if (!featuresByProgression.TryGetValue(
                        progressionId,
                        out var combined))
                {
                    combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    featuresByProgression.Add(progressionId, combined);
                }

                combined.UnionWith(featureIds);
            }

            foreach (var entry in featuresByProgression)
            {
                var progression = loaded
                    .Select(item => item.Definition.Progression?.Progressions)
                    .Where(progressions => progressions != null)
                    .SelectMany(progressions => progressions)
                    .Where(candidate => string.Equals(
                        candidate.Key,
                        entry.Key,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(candidate => candidate.Value)
                    .FirstOrDefault(candidate => candidate != null);
                if (progression == null)
                {
                    FuseLog.Warning(
                        $"FUSE legacy start progression '{entry.Key}' was not found; " +
                        "its enabledFeatures could not be promoted to enableFeaturesAtStart.");
                    continue;
                }

                progression.EnableFeaturesAtStart = MergeAdditions(
                    progression.EnableFeaturesAtStart,
                    entry.Value);
                FuseLog.Info(
                    $"FUSE promoted legacy start features progression='{entry.Key}' " +
                    $"features=[{string.Join(",", entry.Value)}].");
            }
        }

        private static bool TryReadStartFeatures(
            FuseLoadedMod loaded,
            out string progressionId,
            out string[] featureIds)
        {
            progressionId = string.Empty;
            featureIds = Array.Empty<string>();
            var definition = loaded?.Definition;
            if (definition?.Extensions == null ||
                !definition.Extensions.TryGetValue(ExtensionKey, out var raw) ||
                raw == null)
            {
                return false;
            }

            try
            {
                var payload = raw as JObject ?? JObject.FromObject(raw);
                progressionId = payload.Value<string>("progressionId")?.Trim() ??
                                string.Empty;
                featureIds = payload["enabledFeatures"]?.Values<string>()
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>();
                return !string.IsNullOrWhiteSpace(progressionId) &&
                       featureIds.Length > 0;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not read legacy start option package='{definition.Id ?? string.Empty}': {ex.Message}");
                return false;
            }
        }

        private static FuseStringPatch MergeAdditions(
            FuseStringPatch existing,
            IEnumerable<string> additions)
        {
            var normalized = (additions ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (existing?.Set != null)
            {
                return FuseStringPatch.FromSet(
                    existing.Set.Concat(normalized)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            var patch = existing?.Patch != null
                ? new Dictionary<string, bool>(existing.Patch, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in normalized)
            {
                patch[id] = true;
            }

            return FuseStringPatch.FromPatch(patch);
        }
    }
}
