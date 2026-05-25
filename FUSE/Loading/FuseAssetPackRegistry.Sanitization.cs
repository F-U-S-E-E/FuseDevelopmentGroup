using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Model.Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Loading
{
    internal static partial class FuseAssetPackRegistry
    {

        private static int CopySanitizedDefinitionsFile(string assetPackRoot, string sourceFile, string destinationFile)
        {
            string outputText;
            var removedByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                outputText = SanitizeDefinitionsJson(File.ReadAllText(sourceFile), removedByKind);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception(
                    $"FUSE could not sanitize asset pack Definitions.json '{sourceFile}'; copying original file", ex);
                if (!NeedsCopy(sourceFile, destinationFile))
                {
                    return 0;
                }

                File.Copy(sourceFile, destinationFile, true);
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
                return 1;
            }

            if (removedByKind.Count > 0)
            {
                var packId = Path.GetFileName(assetPackRoot);
                var summary = string.Join(", ", removedByKind.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => $"{item.Key}={item.Value}"));
                FuseLog.Info(
                    $"FUSE sanitized asset pack '{packId}' Definitions.json by removing unsupported component kind(s): {summary}. " +
                    "The source mod files were not modified.");
            }

            if (File.Exists(destinationFile) && string.Equals(File.ReadAllText(destinationFile), outputText, StringComparison.Ordinal))
            {
                return 0;
            }

            File.WriteAllText(destinationFile, outputText);
            File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            return 1;
        }

        private static string SanitizeDefinitionsJson(string sourceText, Dictionary<string, int> removedByKind)
        {
            var root = JObject.Parse(sourceText);
            var objects = root["objects"] as JArray;
            if (objects == null)
            {
                return sourceText;
            }

            foreach (var objectToken in objects.OfType<JObject>())
            {
                var components = objectToken["definition"]?["components"] as JArray;
                if (components == null)
                {
                    continue;
                }

                for (var index = components.Count - 1; index >= 0; index--)
                {
                    var component = components[index] as JObject;
                    var kind = GetStringProperty(component, "kind");
                    if (!string.IsNullOrWhiteSpace(kind) && SupportedDefinitionComponentKinds.Contains(kind))
                    {
                        continue;
                    }

                    components.RemoveAt(index);
                    var key = string.IsNullOrWhiteSpace(kind) ? "<missing>" : kind;
                    removedByKind[key] = removedByKind.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }

            return removedByKind.Count == 0
                ? sourceText
                : root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static string GetStringProperty(JObject obj, string propertyName)
        {
            if (obj == null)
            {
                return null;
            }

            return obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out var token)
                ? (string)token
                : null;
        }
    }
}
