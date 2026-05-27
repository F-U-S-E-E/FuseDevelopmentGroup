namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Identifier for every toggleable panel the editor surface can show.
    /// Modelled after Axiom's <c>EditorWindowType</c>: one enum entry per
    /// panel means adding a panel later requires touching exactly two
    /// places — this enum and the per-panel draw site. Panel state
    /// (visibility, name key, importance) lives in
    /// <see cref="FuseEditorWindowRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Fixed bars (top bar, bottom status bar) are deliberately NOT in
    /// this enum — they're framing chrome and always render. Only panels
    /// the user can hide belong here.
    /// </remarks>
    internal enum FuseEditorWindowKind
    {
        /// <summary>Left-hand collapsible category/bucket tree of entities in the active mod.</summary>
        EntityTree,

        /// <summary>Left-hand bookmarked-camera positions list (tab alongside <see cref="EntityTree"/>).</summary>
        Locations,

        /// <summary>Right-hand attributes panel for the current selection.</summary>
        Properties,

        /// <summary>Right-hand placement palette (tab alongside <see cref="Properties"/>; F1–F6 filters).</summary>
        Assets,

        /// <summary>
        /// Bottom-of-viewport tool strip (Select / Move / Rotate / Scale / Place).
        /// The EDEN-style overhaul moved the gizmo buttons into the top icon toolbar,
        /// but this kind is kept so a user can re-enable the bottom strip as a fallback
        /// surface if they prefer it.
        /// </summary>
        ToolStrip,
    }
}
