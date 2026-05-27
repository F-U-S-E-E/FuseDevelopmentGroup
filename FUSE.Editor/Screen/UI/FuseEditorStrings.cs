using System.Collections.Generic;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Centralized label store for the editor UI. Modelled after Axiom's
    /// i18n key + ".description" companion convention: every user-facing
    /// string has a stable key, and an optional matching <c>.description</c>
    /// key carries a tooltip companion. The runtime is monolingual today,
    /// but adopting the key shape now lets us swap in a real translator
    /// later (per-locale JSON, language-pack mod, etc.) without rewriting
    /// every call site.
    ///
    /// Keys use the dotted path convention <c>fuse.editor.&lt;area&gt;.&lt;leaf&gt;</c>
    /// for predictable grouping. The optional companion key is
    /// <c>&lt;key&gt;.description</c>.
    /// </summary>
    internal static class FuseEditorStrings
    {
        private static readonly Dictionary<string, string> Entries = new Dictionary<string, string>
        {
            // Top bar
            ["fuse.editor.topbar.title"]              = "FUSE Editor",
            ["fuse.editor.topbar.save"]               = "Save",
            ["fuse.editor.topbar.save.description"]   = "Save changes to the active mod's definition. (Coming soon — persistence is wired in a follow-up.)",
            ["fuse.editor.topbar.play"]               = "Play Mod ▶",
            ["fuse.editor.topbar.play.description"]   = "Launch the active mod in a normal sandbox session. Returning from preview restores the editor to its current state. (Coming soon.)",
            ["fuse.editor.topbar.exit"]               = "Exit Editor",
            ["fuse.editor.topbar.exit.description"]   = "Close the editor and return to the main menu. Unsaved changes will be lost.",
            ["fuse.editor.topbar.mod_label"]          = "Mod",
            ["fuse.editor.topbar.no_mod_selected"]    = "(no mod selected)",
            ["fuse.editor.topbar.windows"]            = "Windows ▾",
            ["fuse.editor.topbar.windows.description"] = "Show or hide editor panels.",

            // Bookmark bar
            ["fuse.editor.bookmarks.add"]                 = "+",
            ["fuse.editor.bookmarks.add.description"]     = "Save the current camera position as a new view.",
            ["fuse.editor.bookmarks.delete"]              = "✕",
            ["fuse.editor.bookmarks.delete.description"]  = "Delete the currently-selected bookmark.",
            ["fuse.editor.bookmarks.delete.no_selection"] = "Select a bookmark tab first.",
            ["fuse.editor.bookmarks.empty_hint"]          = "No saved views. Click + to bookmark the current camera position.",
            ["fuse.editor.bookmarks.tooltip"]             = "Click to teleport. The active tab's name is editable inline.",

            // Window kinds — listed in the Windows toggle popup.
            ["fuse.editor.window.entity_tree"]                  = "Entity Tree",
            ["fuse.editor.window.entity_tree.description"]      = "Left-hand collapsible list of entities in the active mod, grouped by category.",
            ["fuse.editor.window.properties"]                   = "Properties",
            ["fuse.editor.window.properties.description"]       = "Right-hand attribute editors for the current selection.",
            ["fuse.editor.window.tool_strip"]                   = "Tool Strip",
            ["fuse.editor.window.tool_strip.description"]       = "Bottom-of-viewport row of Select / Move / Rotate / Scale / Place buttons.",

            // Entity tree
            ["fuse.editor.entities.header"]                       = "Entities",
            ["fuse.editor.entities.empty_hint"]                   = "Click a marker in the scene to select a node.",
            ["fuse.editor.entities.category.tracks"]              = "Tracks",
            ["fuse.editor.entities.category.world"]               = "World",
            ["fuse.editor.entities.category.operations"]          = "Operations",
            ["fuse.editor.entities.bucket.nodes"]                 = "Nodes",
            ["fuse.editor.entities.bucket.segments"]              = "Segments",
            ["fuse.editor.entities.bucket.spans"]                 = "Spans",
            ["fuse.editor.entities.bucket.areas"]                 = "Areas",
            ["fuse.editor.entities.bucket.scenery"]               = "Scenery",
            ["fuse.editor.entities.bucket.splineys"]              = "Splineys",
            ["fuse.editor.entities.bucket.map_labels"]            = "MapLabels",
            ["fuse.editor.entities.bucket.telegraphs"]            = "Telegraphs",
            ["fuse.editor.entities.bucket.industries"]            = "Industries",
            ["fuse.editor.entities.bucket.loads"]                 = "Loads",
            ["fuse.editor.entities.bucket.stations"]              = "Stations",
            ["fuse.editor.entities.bucket.turntables"]            = "Turntables",

            // Mod browser (shown when no mod is active)
            ["fuse.editor.modbrowser.title"]                       = "FUSE Mod Browser",
            ["fuse.editor.modbrowser.subtitle"]                    = "Pick a FUSE mod to edit, create a new one, or convert a legacy mod.",
            ["fuse.editor.modbrowser.tab.existing"]                = "Existing",
            ["fuse.editor.modbrowser.tab.new"]                     = "Create New",
            ["fuse.editor.modbrowser.tab.legacy"]                  = "Legacy",
            ["fuse.editor.modbrowser.existing.empty"]              = "No FUSE mods currently loaded. Create a new one or convert a legacy mod.",
            ["fuse.editor.modbrowser.existing.edit"]               = "Edit",
            ["fuse.editor.modbrowser.existing.edit.description"]   = "Open this mod in the editor.",
            ["fuse.editor.modbrowser.new.id"]                      = "Mod Id",
            ["fuse.editor.modbrowser.new.id.description"]          = "Reverse-DNS style identifier (e.g. alex.mymod). Used as the folder name and stable across renames.",
            ["fuse.editor.modbrowser.new.name"]                    = "Display Name",
            ["fuse.editor.modbrowser.new.author"]                  = "Author",
            ["fuse.editor.modbrowser.new.create"]                  = "Create Mod",
            ["fuse.editor.modbrowser.new.create.description"]      = "Scaffold a new FUSE mod folder. Restart Railroader to load it after creation.",
            ["fuse.editor.modbrowser.legacy.empty"]                = "No legacy mods detected.",
            ["fuse.editor.modbrowser.legacy.convert"]              = "Convert",
            ["fuse.editor.modbrowser.legacy.convert.description"]  = "Convert this legacy mod to the FUSE format. Output goes to a sibling '*.FUSE' folder. Restart Railroader after conversion to load the new FUSE mod.",

            // Properties panel
            ["fuse.editor.properties.header"]            = "Properties",
            ["fuse.editor.properties.empty_hint"]        = "Select an entity from the tree to see its properties.",
            ["fuse.editor.properties.kind"]              = "Kind",
            ["fuse.editor.properties.id"]                = "Id",
            ["fuse.editor.properties.position"]          = "Position",
            ["fuse.editor.properties.rotation"]          = "Rotation",
            ["fuse.editor.properties.group"]             = "Group",
            ["fuse.editor.properties.tags"]              = "Tags",
            ["fuse.editor.properties.delete"]            = "Delete Node",
            ["fuse.editor.properties.delete.description"] = "Remove this node from the active mod's definition. The change persists immediately.",

            // Viewport tool strip
            ["fuse.editor.tool.select"]                  = "Select",
            ["fuse.editor.tool.select.description"]      = "Click an entity in the world to inspect it.",
            ["fuse.editor.tool.move"]                    = "Move",
            ["fuse.editor.tool.move.description"]        = "Translate the selected entity along the world axes.",
            ["fuse.editor.tool.rotate"]                  = "Rotate",
            ["fuse.editor.tool.rotate.description"]      = "Rotate the selected entity around its origin.",
            ["fuse.editor.tool.scale"]                   = "Scale",
            ["fuse.editor.tool.scale.description"]       = "Scale the selected entity. (Not all entity kinds support scaling.)",
            ["fuse.editor.tool.place"]                   = "Place",
            ["fuse.editor.tool.place.description"]       = "Drop a new entity of the selected kind at the camera's forward raycast hit.",
        };

        /// <summary>
        /// Looks up a string by key. Returns the key itself when the entry
        /// is missing — this matches the Minecraft / Bohemia convention
        /// where missing keys render as the literal key, making them easy
        /// to spot during development.
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return Entries.TryGetValue(key, out var value) ? value : key;
        }

        /// <summary>
        /// Returns the description companion for <paramref name="key"/>
        /// (i.e. <c>&lt;key&gt;.description</c>), or <c>null</c> if no
        /// description has been registered. Used by
        /// <see cref="FuseEditorUiHelper.Label"/> to populate tooltip
        /// text without forcing every call site to test for existence.
        /// </summary>
        public static string TryGetDescription(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return Entries.TryGetValue(key + ".description", out var value) ? value : null;
        }
    }
}
