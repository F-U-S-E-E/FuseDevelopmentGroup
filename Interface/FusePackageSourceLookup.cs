using System;
using System.IO;
using FUSE.Data;
using FUSE.Loading;

namespace FUSE.Interface
{
    /// <summary>
    /// Reverse lookup from a runtime item id back to the FUSE-loaded sub-package that
    /// defined it, so the debug overlays can show "this came from AspenCrazyMap/acm-edits.json"
    /// without dumping the runtime graph or scanning JSON files by hand.
    /// </summary>
    internal static class FusePackageSourceLookup
    {
        public enum ItemKind
        {
            Scenery,
            SceneClone,
            Segment,
            Node,
            Span,
            Spliney
        }

        public readonly struct Source
        {
            public Source(string packageId, string folderName, string fileName)
            {
                PackageId = packageId;
                FolderName = folderName;
                FileName = fileName;
            }

            public string PackageId { get; }
            public string FolderName { get; }
            public string FileName { get; }

            public string Display
            {
                get
                {
                    if (string.IsNullOrEmpty(FolderName))
                    {
                        return string.IsNullOrEmpty(FileName) ? "<unknown>" : FileName;
                    }

                    return string.IsNullOrEmpty(FileName) ? FolderName : FolderName + "/" + FileName;
                }
            }
        }

        public static bool TryGetSource(ItemKind kind, string id, out Source source)
        {
            source = default;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            System.Collections.Generic.IReadOnlyList<FuseLoadedMod> mods;
            try
            {
                mods = FuseModLoader.GetLoadedModsInOrder();
            }
            catch
            {
                return false;
            }

            if (mods == null)
            {
                return false;
            }

            for (var index = 0; index < mods.Count; index++)
            {
                var mod = mods[index];
                var definition = mod?.Definition;
                if (definition == null)
                {
                    continue;
                }

                if (!ContainsId(definition, kind, id))
                {
                    continue;
                }

                source = new Source(
                    definition.Id,
                    ExtractFolderName(mod.FolderPath),
                    NormalizeDefinitionPath(mod.DefinitionPath));
                return true;
            }

            return false;
        }

        private static bool ContainsId(FuseModDefinition definition, ItemKind kind, string id)
        {
            switch (kind)
            {
                case ItemKind.Scenery:
                    return definition.World?.Scenery != null && definition.World.Scenery.ContainsKey(id);
                case ItemKind.SceneClone:
                    return definition.World?.SceneClones != null && definition.World.SceneClones.ContainsKey(id);
                case ItemKind.Spliney:
                    return definition.World?.Splineys != null && definition.World.Splineys.ContainsKey(id);
                case ItemKind.Segment:
                    return definition.Tracks?.Segments != null && definition.Tracks.Segments.ContainsKey(id);
                case ItemKind.Node:
                    return definition.Tracks?.Nodes != null && definition.Tracks.Nodes.ContainsKey(id);
                case ItemKind.Span:
                    return definition.Tracks?.Spans != null && definition.Tracks.Spans.ContainsKey(id);
                default:
                    return false;
            }
        }

        private static string ExtractFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return null;
            }

            try
            {
                return new DirectoryInfo(folderPath).Name;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeDefinitionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            // Legacy converter records source files as 'legacy://<filename>'.
            const string LegacyPrefix = "legacy://";
            if (path.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(LegacyPrefix.Length);
            }

            const string FusePrefix = "fuse://";
            if (path.StartsWith(FusePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(FusePrefix.Length);
            }

            return path;
        }
    }
}
