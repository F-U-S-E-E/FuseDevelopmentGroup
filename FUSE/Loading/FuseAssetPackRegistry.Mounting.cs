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

        private static int MountAssetPacksFromPackage(string packagePath)
        {
            var infoPath = Path.Combine(packagePath, "Info.json");
            if (!File.Exists(infoPath))
            {
                return MountAssetPackFolders(packagePath, EnumerateFallbackAssetPackFolders(packagePath));
            }

            JObject info;
            try
            {
                info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                FuseLog.Exception($"FUSE ignored asset pack declaration in '{packagePath}' because Info.json could not be parsed", ex);
                return 0;
            }

            var sourceRoots = EnumerateAssetPackRoots(info[FuseAssetPacksProperty]).ToArray();
            var folders = EnumerateAssetPackFoldersFromRoots(packagePath, sourceRoots)
                .Concat(EnumerateFallbackAssetPackFolders(packagePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return MountAssetPackFolders(packagePath, folders);
        }

        private static int MountAssetPackFolders(string packagePath, IEnumerable<string> folders)
        {
            var mountedCount = 0;
            foreach (var folder in folders ?? Enumerable.Empty<string>())
            {
                try
                {
                    mountedCount += MountAssetPackFolder(folder);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE failed to mount asset pack folder '{folder}' from '{packagePath}'", ex);
                }
            }

            return mountedCount;
        }

        internal static IEnumerable<string> EnumerateAvailableAssetPackFolders()
        {
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShouldInspectPackage(packagePath))
                {
                    continue;
                }

                foreach (var folder in EnumerateAssetPackFoldersForPackage(packagePath))
                {
                    if (seen.Add(folder))
                    {
                        yield return folder;
                    }
                }
            }
        }

        private static bool ShouldInspectPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return false;
            }

            var packageId = TryReadPackageId(packagePath);
            if (FuseUmmState.TryGetDisabledReason(packagePath, packageId, out _))
            {
                return false;
            }

            return FuseModSetService.IsPackageEnabledByActiveSet(packageId, packagePath);
        }

        private static string TryReadPackageId(string packagePath)
        {
            try
            {
                var infoPath = Path.Combine(packagePath, "Info.json");
                if (File.Exists(infoPath))
                {
                    var info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                    var id = (string)info["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }

                var definitionPath = Path.Combine(packagePath, "Definition.json");
                if (File.Exists(definitionPath))
                {
                    var definition = FuseLegacyDataConverter.ReadLegacyObject(definitionPath);
                    var id = (string)definition["id"] ?? (string)definition["Id"];
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.Trim();
                    }
                }
            }
            catch
            {
                // Asset-pack discovery can still fall back to folder name.
            }

            return Path.GetFileName(packagePath);
        }

        private static IEnumerable<string> EnumerateAssetPackFoldersForPackage(string packagePath)
        {
            JObject info = null;
            var infoPath = Path.Combine(packagePath, "Info.json");
            if (File.Exists(infoPath))
            {
                try
                {
                    info = FuseLegacyDataConverter.ReadLegacyObject(infoPath);
                }
                catch
                {
                    info = null;
                }
            }

            foreach (var folder in EnumerateAssetPackFoldersFromRoots(packagePath, EnumerateAssetPackRoots(info?[FuseAssetPacksProperty])))
            {
                yield return folder;
            }

            foreach (var folder in EnumerateFallbackAssetPackFolders(packagePath))
            {
                yield return folder;
            }
        }

        private static IEnumerable<string> EnumerateAssetPackRoots(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var value = (string)token;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                yield break;
            }

            if (token.Type != JTokenType.Array)
            {
                yield break;
            }

            foreach (var item in token.Children())
            {
                if (item.Type != JTokenType.String)
                {
                    continue;
                }

                var value = (string)item;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static int MountAssetPackSource(string packagePath, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(relativeSource))
            {
                FuseLog.Warning($"FUSE ignored blank asset pack source in '{packagePath}'.");
                return 0;
            }

            var packageRoot = Path.GetFullPath(packagePath);
            var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                FuseLog.Warning($"FUSE ignored asset pack source outside package root: '{relativeSource}'.");
                return 0;
            }

            if (!Directory.Exists(sourcePath))
            {
                FuseLog.Warning($"FUSE asset pack source '{relativeSource}' was not found in '{packagePath}'.");
                return 0;
            }

            if (IsAssetPackFolder(sourcePath))
            {
                return MountAssetPackFolder(sourcePath);
            }

            var assetPackFolders = EnumerateAssetPackFolders(sourcePath).ToArray();
            var mountedCount = 0;
            foreach (var child in assetPackFolders)
            {
                mountedCount += MountAssetPackFolder(child);
            }

            if (mountedCount == 0)
            {
                FuseLog.Warning($"FUSE asset pack source '{relativeSource}' did not contain any valid asset pack folders.");
            }

            return mountedCount;
        }

        private static IEnumerable<string> EnumerateAssetPackFoldersFromRoots(string packagePath, IEnumerable<string> relativeSources)
        {
            foreach (var relativeSource in relativeSources ?? Enumerable.Empty<string>())
            {
                foreach (var folder in EnumerateAssetPackFoldersFromRoot(packagePath, relativeSource))
                {
                    yield return folder;
                }
            }
        }

        private static IEnumerable<string> EnumerateAssetPackFoldersFromRoot(string packagePath, string relativeSource)
        {
            if (string.IsNullOrWhiteSpace(relativeSource))
            {
                yield break;
            }

            var packageRoot = Path.GetFullPath(packagePath);
            var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(sourcePath))
            {
                yield break;
            }

            if (IsAssetPackFolder(sourcePath))
            {
                yield return sourcePath;
                yield break;
            }

            foreach (var folder in EnumerateAssetPackFolders(sourcePath))
            {
                yield return folder;
            }
        }

        /// <summary>
        /// Walks a single mod-package folder and returns the asset-pack
        /// folders inside it in the order they should be registered with
        /// <see cref="Model.Database.PrefabStore"/>. Root-level packs come
        /// first, then anything nested under <c>SCAssetPacks/</c>.
        /// Exposed as <c>internal</c> so the discovery-order test in
        /// <c>FUSE.Tests</c> can assert the ordering directly — the
        /// ordering is the load-time fix for the duplicate-CAB bundle
        /// failure and must not silently regress.
        /// </summary>
        internal static IEnumerable<string> EnumerateFallbackAssetPackFolders(string packagePath)
        {
            // Discovery order is load-bearing. PrefabStore.AssetPackContainingIdentifier
            // returns the FIRST store whose Definitions claim a given
            // identifier, and only that store's bundle is ever loaded —
            // so the order we feed pack folders into _stores decides
            // which bundle file actually gets loaded when two packs in
            // the same mod publish the same identifier under different
            // folders. Empirically, mods with both a root-level pack
            // and an SCAssetPacks/<sameName> pack ship the modern build
            // at the root and keep the SCAssetPacks/ copy as a legacy
            // artifact; the two bundles share an internal CAB name but
            // contain DIFFERENT versions of the same assets, so loading
            // the wrong one for a "modern" car identifier renders the
            // legacy mesh into the modern car's hierarchy and produces
            // visibly broken cars. Yielding root packs first keeps
            // modern saves on the modern bundle.
            var scAssetPacks = Path.Combine(packagePath, "SCAssetPacks");

            foreach (var folder in EnumerateRootAssetPackFolders(packagePath, scAssetPacks))
            {
                yield return folder;
            }

            if (Directory.Exists(scAssetPacks))
            {
                if (IsAssetPackFolder(scAssetPacks))
                {
                    yield return scAssetPacks;
                }
                else
                {
                    foreach (var folder in EnumerateAssetPackFolders(scAssetPacks))
                    {
                        yield return folder;
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateRootAssetPackFolders(string packagePath, string scAssetPacksPath)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !Directory.Exists(packagePath))
            {
                yield break;
            }

            var scRoot = Directory.Exists(scAssetPacksPath)
                ? Path.GetFullPath(scAssetPacksPath)
                : null;

            foreach (var child in Directory.GetDirectories(packagePath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var fullChild = Path.GetFullPath(child);
                if (!string.IsNullOrWhiteSpace(scRoot) &&
                    string.Equals(fullChild, scRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsAssetPackFolder(fullChild))
                {
                    yield return fullChild;
                    continue;
                }

                foreach (var folder in EnumerateAssetPackFolders(fullChild))
                {
                    yield return folder;
                }
            }
        }

        private static IEnumerable<string> EnumerateAssetPackFolders(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                yield break;
            }

            foreach (var child in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (IsAssetPackFolder(child))
                {
                    yield return child;
                }
            }
        }

        private static bool IsAssetPackFolder(string folderPath)
        {
            return File.Exists(Path.Combine(folderPath, "Bundle")) &&
                   File.Exists(Path.Combine(folderPath, "Catalog.json"));
        }

        private static int MountAssetPackFolder(string sourcePath)
        {
            var packId = GetDestinationPackId(sourcePath);
            var destinationRoot = Path.Combine(Application.persistentDataPath, "AssetPacks");
            var destinationPath = Path.Combine(destinationRoot, packId);
            Directory.CreateDirectory(destinationPath);

            var copiedCount = CopyDirectoryIfChanged(sourcePath, destinationPath);
            FuseLog.Info(copiedCount > 0
                ? $"FUSE mounted asset pack '{packId}' to '{destinationPath}' ({copiedCount} file(s) updated)."
                : $"FUSE asset pack '{packId}' already mounted at '{destinationPath}'.");
            return 1;
        }

        private static int CopyDirectoryIfChanged(string sourcePath, string destinationPath)
        {
            var copiedCount = 0;
            foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativeDirectory = GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativeDirectory));
            }

            foreach (var sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativeFile = GetRelativePath(sourcePath, sourceFile);
                var destinationFile = Path.Combine(destinationPath, relativeFile);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                copiedCount += CopyAssetPackFileIfChanged(sourceFile, destinationFile);
            }

            return copiedCount;
        }

        private static int CopyAssetPackFileIfChanged(string sourceFile, string destinationFile)
        {
            // Definitions.json is copied verbatim like any other pack file. FUSE no
            // longer pre-strips "unsupported" component kinds here: the game's native
            // loader (which reads these mirrored files) binds every kind its loaded mods
            // register, and the default direct-store path quarantines only genuinely
            // unbindable components at load time (see TryLoadSanitizedDirectContainer).
            if (!NeedsCopy(sourceFile, destinationFile))
            {
                return 0;
            }

            File.Copy(sourceFile, destinationFile, true);
            File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
            return 1;
        }

        private static string GetDestinationPackId(string sourcePath)
        {
            var baseId = SanitizePackId(Path.GetFileName(sourcePath));
            var fullSource = Path.GetFullPath(sourcePath);
            if (!MountedAssetPackSourcesById.TryGetValue(baseId, out var existingSource))
            {
                MountedAssetPackSourcesById[baseId] = fullSource;
                return baseId;
            }

            if (string.Equals(existingSource, fullSource, StringComparison.OrdinalIgnoreCase))
            {
                return baseId;
            }

            var parentId = SanitizePackId(Path.GetFileName(Path.GetDirectoryName(sourcePath)));
            var candidate = string.IsNullOrWhiteSpace(parentId) ? baseId : parentId + "_" + baseId;
            var unique = candidate;
            var index = 2;
            while (MountedAssetPackSourcesById.TryGetValue(unique, out existingSource) &&
                   !string.Equals(existingSource, fullSource, StringComparison.OrdinalIgnoreCase))
            {
                unique = candidate + "_" + index;
                index++;
            }

            MountedAssetPackSourcesById[unique] = fullSource;
            FuseLog.Warning(
                $"FUSE mounted duplicate asset pack folder '{baseId}' from '{sourcePath}' as '{unique}' " +
                "so it does not overwrite another pack with the same folder name.");
            return unique;
        }

        private static string SanitizePackId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "AssetPack";
            }

            var chars = value.Trim()
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.'
                    ? ch
                    : '_')
                .ToArray();
            var result = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "AssetPack" : result;
        }

        private static string ToDirectStoreIdentifier(string sourcePath)
        {
            return DirectStorePrefix + Uri.EscapeDataString(Path.GetFullPath(sourcePath));
        }

        private static bool NeedsCopy(string sourceFile, string destinationFile)
        {
            if (!File.Exists(destinationFile))
            {
                return true;
            }

            var sourceInfo = new FileInfo(sourceFile);
            var destinationInfo = new FileInfo(destinationFile);
            if (sourceInfo.Length != destinationInfo.Length)
            {
                return true;
            }

            return sourceInfo.LastWriteTimeUtc > destinationInfo.LastWriteTimeUtc.AddSeconds(1);
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            var rootUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(rootPath)));
            var fullUri = new Uri(Path.GetFullPath(fullPath));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
