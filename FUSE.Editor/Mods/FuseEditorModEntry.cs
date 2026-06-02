namespace FUSE.Editor.Mods
{
    /// <summary>
    /// Classification the mod browser uses to decide whether a folder
    /// is directly editable, must be converted first, or doesn't show
    /// up in the editor at all.
    /// </summary>
    internal enum FuseEditorModKind
    {
        /// <summary>
        /// Folder contains one or more <c>*.fuse.json</c> files. The
        /// editor can open it directly.
        /// </summary>
        FuseMod,

        /// <summary>
        /// Folder has a Railloader-style <c>Definition.json</c> manifest
        /// (manifestVersion / requires: railloader). Conversion needed
        /// before editing.
        /// </summary>
        LegacyRailLoader,

        /// <summary>
        /// Folder has an Alina's-Map-Mod patch file
        /// (<c>mapmod.yaml</c> or similar marker). Conversion needed.
        /// </summary>
        LegacyMapMod,

        /// <summary>
        /// Recognised as a Railroader mod (Info.json present) but
        /// neither FUSE nor a known legacy data shape — likely a
        /// pure-code mod, no editable content.
        /// </summary>
        CodeOnlyMod,

        /// <summary>
        /// Nothing the editor recognises. Folder is in <c>Mods/</c>
        /// but doesn't look like a Railroader mod at all.
        /// </summary>
        Unknown,
    }

    /// <summary>
    /// One row in the mod browser's listing. Holds enough information
    /// to render the row + take action (open, convert, delete).
    /// </summary>
    internal sealed class FuseEditorModEntry
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string FolderPath { get; set; }
        public FuseEditorModKind Kind { get; set; }

        /// <summary>
        /// Optional one-line reason explaining why the editor can't
        /// open this row directly (e.g. "Legacy AMM patch — convert
        /// first"). Populated for non-<see cref="FuseEditorModKind.FuseMod"/>
        /// kinds; null for openable ones.
        /// </summary>
        public string IneligibilityReason { get; set; }
    }
}
