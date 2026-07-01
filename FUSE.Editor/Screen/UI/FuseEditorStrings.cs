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

            // Window kinds — listed in the View menu's toggle list.
            ["fuse.editor.window.entity_tree"]                  = "Entity Tree",
            ["fuse.editor.window.entity_tree.description"]      = "Left-hand collapsible list of entities in the active mod, grouped by category.",
            ["fuse.editor.window.locations"]                    = "Locations",
            ["fuse.editor.window.locations.description"]        = "Bookmarked camera positions for the active mod (Locations tab on the left panel).",
            ["fuse.editor.window.properties"]                   = "Properties",
            ["fuse.editor.window.properties.description"]       = "Right-hand attribute editors for the current selection.",
            ["fuse.editor.window.assets"]                       = "Assets",
            ["fuse.editor.window.assets.description"]           = "Place-mode palette filtered by F1–F6 entity kind (Tracks / Switches / Scenery / Mandelas).",
            ["fuse.editor.window.tool_strip"]                   = "Tool Strip",
            ["fuse.editor.window.tool_strip.description"]       = "Optional bottom-of-viewport gizmo row — the top icon toolbar replaces it as the primary surface.",

            // Top menu bar (Scenario / Edit / View / Attributes / Tools / Settings / Play / Help)
            ["fuse.editor.menu.scenario"]                       = "Scenario",
            ["fuse.editor.menu.scenario.new"]                   = "New Mod…",
            ["fuse.editor.menu.scenario.new.description"]       = "Open the mod browser on the Create New tab.",
            ["fuse.editor.menu.scenario.open"]                  = "Open Mod…",
            ["fuse.editor.menu.scenario.open.description"]      = "Open the mod browser on the Existing Mods tab.",
            ["fuse.editor.menu.scenario.save"]                  = "Save",
            ["fuse.editor.menu.scenario.save.description"]      = "Save changes to the active mod's definition.",
            ["fuse.editor.menu.scenario.exit"]                  = "Exit Editor",
            ["fuse.editor.menu.scenario.exit.description"]      = "Close the editor and return to the main menu.",
            ["fuse.editor.menu.edit"]                           = "Edit",
            ["fuse.editor.menu.edit.undo"]                      = "Undo",
            ["fuse.editor.menu.edit.redo"]                      = "Redo",
            ["fuse.editor.menu.edit.cut"]                       = "Cut",
            ["fuse.editor.menu.edit.copy"]                      = "Copy",
            ["fuse.editor.menu.edit.paste"]                     = "Paste",
            ["fuse.editor.menu.view"]                           = "View",
            ["fuse.editor.menu.attributes"]                     = "Attributes",
            ["fuse.editor.menu.tools"]                          = "Tools",
            ["fuse.editor.menu.settings"]                       = "Settings",
            ["fuse.editor.menu.play"]                           = "Play",
            ["fuse.editor.menu.play.sandbox"]                   = "Play in Sandbox",
            ["fuse.editor.menu.play.sandbox.description"]       = "Launch the active mod in a normal sandbox session.",
            ["fuse.editor.menu.help"]                           = "Help",
            ["fuse.editor.menu.help.about"]                     = "About FUSE Editor",
            ["fuse.editor.menu.coming_soon"]                    = "Coming soon — this menu item is reserved for a future release.",

            // Icon toolbar tooltips
            ["fuse.editor.toolbar.new"]                         = "New",
            ["fuse.editor.toolbar.new.description"]             = "Create a new mod.",
            ["fuse.editor.toolbar.open"]                        = "Open",
            ["fuse.editor.toolbar.open.description"]            = "Open an existing mod.",
            ["fuse.editor.toolbar.save"]                        = "Save",
            ["fuse.editor.toolbar.save.description"]            = "Save changes to the active mod.",
            ["fuse.editor.toolbar.undo"]                        = "Undo",
            ["fuse.editor.toolbar.undo.description"]            = "Undo last change. (Coming soon.)",
            ["fuse.editor.toolbar.redo"]                        = "Redo",
            ["fuse.editor.toolbar.redo.description"]            = "Redo last undone change. (Coming soon.)",
            ["fuse.editor.toolbar.grid"]                        = "Grid",
            ["fuse.editor.toolbar.grid.description"]            = "Toggle the placement grid overlay. (Coming soon.)",
            ["fuse.editor.toolbar.camera"]                      = "Camera",
            ["fuse.editor.toolbar.camera.description"]          = "Reset the camera to the default view.",
            ["fuse.editor.toolbar.origin"]                      = "Origin",
            ["fuse.editor.toolbar.origin.description"]          = "Whether the Gizmo is centered on the primary object or the average of the selected objects.",
            ["fuse.editor.toolbar.origin.object"]               = "Object",
            ["fuse.editor.toolbar.origin.object.description"]   = "Center the Gizmo on the primary object.",
            ["fuse.editor.toolbar.origin.group"]                = "Group",
            ["fuse.editor.toolbar.origin.group.description"]    = "Center the Gizmo on the average of the selected objects.",
            ["fuse.editor.toolbar.transform"]                   = "Transform",
            ["fuse.editor.toolbar.transform.description"]       = "Switch between Global and Local transform space for the Gizmo.",
            ["fuse.editor.toolbar.transform.global"]            = "Global",
            ["fuse.editor.toolbar.transform.global.description"] = "Gizmo axes align with the world coordinate system.",
            ["fuse.editor.toolbar.transform.local"]             = "Local",
            ["fuse.editor.toolbar.transform.local.description"] = "Gizmo axes align with the selected object's local coordinate system.",

            // Settings panel
            ["fuse.editor.settings.title"]                      = "FUSE Editor Settings",
            ["fuse.editor.settings.close"]                      = "✕",
            ["fuse.editor.settings.close.description"]          = "Dismiss the settings panel. Changes apply as you drag.",
            ["fuse.editor.settings.ui_scale"]                   = "UI Scale",
            ["fuse.editor.settings.ui_scale.description"]       = "Open the editor settings panel to adjust UI scale.",
            ["fuse.editor.settings.ui_scale.hint"]              = "Drag the slider to grow or shrink the editor chrome. The 3D viewport, in-world markers, and gizmos are not affected. The setting persists across editor sessions.",

            // Mod browser overlay (close button)
            ["fuse.editor.modbrowser.close"]                    = "✕",
            ["fuse.editor.modbrowser.close.description"]        = "Dismiss the mod browser. The currently-active mod stays active.",

            // Right panel — Assets tab F1–F6 selector
            ["fuse.editor.assets.tracks"]                       = "F1 Tracks",
            ["fuse.editor.assets.tracks.description"]           = "Place track nodes, segments, and spans.",
            ["fuse.editor.assets.switches"]                     = "F2 Switches",
            ["fuse.editor.assets.switches.description"]         = "Place switch-stand nodes (FUSE folds switches into nodes with flipSwitchStand=true).",
            ["fuse.editor.assets.scenery"]                      = "F3 Scenery",
            ["fuse.editor.assets.scenery.description"]          = "Place scenery prefabs from FUSE asset packs.",
            ["fuse.editor.assets.mandelas"]                     = "F4 Mandelas",
            ["fuse.editor.assets.mandelas.description"]         = "Place scene clones (\"mandelas\") that instantiate a base-scene prefab at a new transform.",
            ["fuse.editor.assets.placeholder_a"]                = "F5",
            ["fuse.editor.assets.placeholder_a.description"]    = "Reserved for a future FUSE entity kind.",
            ["fuse.editor.assets.placeholder_b"]                = "F6",
            ["fuse.editor.assets.placeholder_b.description"]    = "Reserved for a future FUSE entity kind.",
            ["fuse.editor.assets.placeholder.reason"]           = "Placeholder slot — a future FUSE entity kind will plug in here.",
            ["fuse.editor.assets.search_placeholder"]           = "Search assets…",
            ["fuse.editor.assets.empty"]                        = "Asset listing for this kind isn't wired up yet.",

            // Bottom bar
            ["fuse.editor.bottombar.play"]                      = "PLAY MOD",
            ["fuse.editor.bottombar.play.description"]          = "Launch the active mod in a sandbox session. Returning from play restores the editor.",
            ["fuse.editor.bottombar.play.no_mod"]               = "Select or create a mod first.",
            ["fuse.editor.bottombar.place_with_snap"]           = "Snap placement to track",
            ["fuse.editor.bottombar.place_with_snap.description"] = "When placing new entities, snap to the nearest track segment.",

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

            // World-orientation axis gizmo (bottom-left compass).
            // Title is the displayed letter (X / Y / Z); description
            // surfaces on hover via the standard tooltip pipeline.
            ["fuse.editor.gizmo.axis_x"]                 = "X",
            ["fuse.editor.gizmo.axis_x.description"]    = "World X axis. The arm rotates with the camera so it always shows the current view orientation.",
            ["fuse.editor.gizmo.axis_y"]                 = "Y",
            ["fuse.editor.gizmo.axis_y.description"]    = "World Y axis (altitude). The arm rotates with the camera so it always shows the current view orientation.",
            ["fuse.editor.gizmo.axis_z"]                 = "Z",
            ["fuse.editor.gizmo.axis_z.description"]    = "World Z axis. The arm rotates with the camera so it always shows the current view orientation.",

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
