using System;
using System.Collections.Generic;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Visibility + metadata store for every <see cref="FuseEditorWindowKind"/>.
    /// Modelled after Axiom's <c>EditorWindowType</c> enum which carries
    /// state per value via instance fields; C# enums can't host state
    /// directly, so this static class is the equivalent.
    ///
    /// State persists for the lifetime of the editor session. Tests should
    /// call <see cref="ResetToDefaults"/> in their setup and serialise via
    /// xUnit's <c>[Collection]</c> attribute so they don't race.
    /// </summary>
    internal static class FuseEditorWindowRegistry
    {
        private sealed class State
        {
            public readonly string NameKey;
            public readonly bool OpenByDefault;

            /// <summary>
            /// Important panels are always listed in the windows toggle
            /// menu; non-important panels (e.g. modal-style operation
            /// dialogs) are reached through their parent flow instead.
            /// Currently every kind is important, but the field is
            /// reserved for future use to keep the menu uncluttered as
            /// operation windows land.
            /// </summary>
            public readonly bool Important;

            public bool IsOpen;

            public State(string nameKey, bool openByDefault, bool important)
            {
                NameKey = nameKey;
                OpenByDefault = openByDefault;
                Important = important;
                IsOpen = openByDefault;
            }
        }

        private static readonly Dictionary<FuseEditorWindowKind, State> States = BuildDefaults();

        private static Dictionary<FuseEditorWindowKind, State> BuildDefaults()
        {
            return new Dictionary<FuseEditorWindowKind, State>
            {
                [FuseEditorWindowKind.EntityTree] = new State(
                    nameKey: "fuse.editor.window.entity_tree",
                    openByDefault: true,
                    important: true),
                [FuseEditorWindowKind.Locations] = new State(
                    nameKey: "fuse.editor.window.locations",
                    openByDefault: true,
                    important: true),
                [FuseEditorWindowKind.Properties] = new State(
                    nameKey: "fuse.editor.window.properties",
                    openByDefault: true,
                    important: true),
                [FuseEditorWindowKind.Assets] = new State(
                    nameKey: "fuse.editor.window.assets",
                    openByDefault: true,
                    important: true),
                [FuseEditorWindowKind.ToolStrip] = new State(
                    nameKey: "fuse.editor.window.tool_strip",
                    // Default off — the new top icon toolbar replaces
                    // it as the primary gizmo surface, but power users
                    // can opt back in from View → Tool Strip.
                    openByDefault: false,
                    important: true),
            };
        }

        public static bool IsOpen(FuseEditorWindowKind kind)
        {
            return States.TryGetValue(kind, out var state) && state.IsOpen;
        }

        public static void SetOpen(FuseEditorWindowKind kind, bool open)
        {
            if (States.TryGetValue(kind, out var state))
            {
                state.IsOpen = open;
            }
        }

        public static void Toggle(FuseEditorWindowKind kind)
        {
            if (States.TryGetValue(kind, out var state))
            {
                state.IsOpen = !state.IsOpen;
            }
        }

        public static string NameKey(FuseEditorWindowKind kind)
        {
            return States.TryGetValue(kind, out var state) ? state.NameKey : kind.ToString();
        }

        public static bool IsImportant(FuseEditorWindowKind kind)
        {
            return States.TryGetValue(kind, out var state) && state.Important;
        }

        public static bool OpenByDefault(FuseEditorWindowKind kind)
        {
            return States.TryGetValue(kind, out var state) && state.OpenByDefault;
        }

        /// <summary>
        /// All known window kinds in canonical enum order. Iteration order
        /// is stable across calls so a windows-toggle menu built from this
        /// list keeps the same row order between renders.
        /// </summary>
        public static IEnumerable<FuseEditorWindowKind> All()
        {
            return (FuseEditorWindowKind[])Enum.GetValues(typeof(FuseEditorWindowKind));
        }

        /// <summary>
        /// Restores every registered kind's <see cref="State.IsOpen"/> to
        /// its <see cref="State.OpenByDefault"/>. Used by tests to reset
        /// shared state between cases; can also be exposed in a future
        /// "Reset window layout" command on the editor's Windows menu.
        /// </summary>
        public static void ResetToDefaults()
        {
            foreach (var state in States.Values)
            {
                state.IsOpen = state.OpenByDefault;
            }
        }
    }
}
