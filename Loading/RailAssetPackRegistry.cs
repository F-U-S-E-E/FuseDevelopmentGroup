using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RAIL.Infrastructure;
using UnityEngine;

namespace RAIL.Loading
{
    internal static class RailAssetPackRegistry
    {
        private const string RailAssetPacksProperty = "RailAssetPacks";

        private static bool _mountComplete;

        public static int MountAllAvailableAssetPacks()
        {
            if (_mountComplete)
            {
                return 0;
            }

            var modsRoot = RailDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                RailLog.Warning("RAIL could not locate the Unity Mod Manager Mods folder for asset pack discovery.");
                return 0;
            }

            var mountedCount = 0;
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    mountedCount += MountAssetPacksFromPackage(packagePath);
                }
                catch (Exception ex)
                {
                    RailLog.Exception($"RAIL failed to mount asset packs from '{packagePath}'", ex);
                }
            }

            _mountComplete = true;
            if (mountedCount > 0)
            {
                RailLog.Info($"RAIL mounted {mountedCount} asset pack(s).");
            }

            return mountedCount;
        }

        public static void Reset()
        {
            _mountComplete = false;
        }

        private static int MountAssetPacksFromPackage(string packagePath)
        {
            var infoPath = Path.Combine(packagePath, "Info.json");
            if (!File.Exists(infoPath))
            {
                return 0;
            }

            JObject info;
            try
            {
                info = JObject.Parse(File.ReadAllText(infoPath));
            }
            catch
            {
                return 0;
            }

            var sourceRoots = EnumerateAssetPackRoots(info[RailAssetPacksProperty]).ToArray();
            if (sourceRoots.Length == 0)
            {
                return 0;
            }

            var mountedCount = 0;
            for (var index = 0; index < sourceRoots.Length; index++)
            {
                mountedCount += MountAssetPackSource(packagePath, sourceRoots[index]);
            }

            return mountedCount;
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
            var packageRoot = Path.GetFullPath(packagePath);
            var sourcePath = Path.GetFullPath(Path.Combine(packageRoot, relativeSource));
            if (!sourcePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                RailLog.Warning($"RAIL ignored asset pack source outside package root: '{relativeSource}'.");
                return 0;
            }

            if (!Directory.Exists(sourcePath))
            {
                RailLog.Warning($"RAIL asset pack source '{relativeSource}' was not found in '{packagePath}'.");
                return 0;
            }

            if (IsAssetPackFolder(sourcePath))
            {
                return MountAssetPackFolder(sourcePath);
            }

            var mountedCount = 0;
            foreach (var child in Directory.GetDirectories(sourcePath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!IsAssetPackFolder(child))
                {
                    continue;
                }

                mountedCount += MountAssetPackFolder(child);
            }

            if (mountedCount == 0)
            {
                RailLog.Warning($"RAIL asset pack source '{relativeSource}' did not contain any valid asset pack folders.");
            }

            return mountedCount;
        }

        private static bool IsAssetPackFolder(string folderPath)
        {
            return File.Exists(Path.Combine(folderPath, "Bundle")) &&
                   File.Exists(Path.Combine(folderPath, "Catalog.json")) &&
                   File.Exists(Path.Combine(folderPath, "Definitions.json"));
        }

        private static int MountAssetPackFolder(string sourcePath)
        {
            var packId = Path.GetFileName(sourcePath);
            var destinationRoot = Path.Combine(Application.persistentDataPath, "AssetPacks");
            var destinationPath = Path.Combine(destinationRoot, packId);
            Directory.CreateDirectory(destinationPath);

            var copiedCount = CopyDirectoryIfChanged(sourcePath, destinationPath);
            RailLog.Info(copiedCount > 0
                ? $"RAIL mounted asset pack '{packId}' to '{destinationPath}' ({copiedCount} file(s) updated)."
                : $"RAIL asset pack '{packId}' already mounted at '{destinationPath}'.");
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

                if (!NeedsCopy(sourceFile, destinationFile))
                {
                    continue;
                }

                File.Copy(sourceFile, destinationFile, true);
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
                copiedCount++;
            }

            return copiedCount;
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
