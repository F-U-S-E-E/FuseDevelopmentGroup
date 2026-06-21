namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Contract for one entry in the viewport tool strip — Select, Move,
    /// Rotate, Place, etc. Modelled after Axiom's <c>Tool</c> base class
    /// (the project ships ~30 of them under <c>tools/</c>): each tool is
    /// a self-contained "verb" the user invokes against the active
    /// selection, the toolbar is data-driven from the registry, and tools
    /// can declare themselves unavailable with a reason that surfaces as
    /// the disabled-button tooltip.
    /// </summary>
    /// <remarks>
    /// Tools live in <see cref="FUSE.Editor"/>'s Unity-bound assembly
    /// because they manipulate the scene (spawning markers, attaching
    /// gizmos, raycasting). Pure-logic state (which tool is active,
    /// iteration order, registration) lives in
    /// <see cref="FuseEditorToolRegistry"/> and is xUnit-testable via
    /// fake tools.
    /// </remarks>
    internal interface IFuseEditorTool
    {
        /// <summary>
        /// Stable string identifier. Used to deduplicate registrations
        /// and as the keybind-registry hook later. Convention:
        /// <c>fuse.editor.tool.&lt;kind&gt;</c> matching the i18n key
        /// suffix.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// i18n label key, e.g. <c>fuse.editor.tool.move</c>. The matching
        /// <c>.description</c> companion populates the tooltip when the
        /// tool button is hovered.
        /// </summary>
        string LabelKey { get; }

        /// <summary>
        /// Single character (or short Unicode glyph cluster) painted on
        /// the tool button. Until we ship a real icon font this is a
        /// stand-in — emoji-style glyphs, arrows, etc. — chosen so the
        /// buttons are recognizable even without color cues.
        /// </summary>
        string IconGlyph { get; }

        /// <summary>
        /// <c>false</c> means the toolbar should paint the button gray
        /// and surface <see cref="UnavailableReason"/> as a hover tooltip
        /// explaining why. The user never has to guess why a button is
        /// disabled — same pattern Axiom uses for permission-gated tools.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Human-readable reason surfaced when <see cref="IsAvailable"/>
        /// is <c>false</c>. Should be a complete sentence; the disabled
        /// tooltip pipeline doesn't add extra punctuation.
        /// </summary>
        string UnavailableReason { get; }

        /// <summary>
        /// Called when the registry transitions this tool to the active
        /// slot. Implementations should spawn any tool-specific overlays
        /// (gizmos, markers) and start subscribing to the events they
        /// need.
        /// </summary>
        void OnActivate();

        /// <summary>
        /// Called when the registry transitions away from this tool (the
        /// user clicked a different tool, or <c>Reset</c> ran). Must tear
        /// down anything <see cref="OnActivate"/> spawned so the next
        /// tool starts on a clean scene.
        /// </summary>
        void OnDeactivate();

        /// <summary>
        /// Per-frame tick fired by <see cref="FuseEditor"/> while this
        /// tool is active. Tools that respond to raw input (e.g. Place
        /// detects a viewport click) hook this; passive tools no-op.
        /// </summary>
        void Tick();
    }
}
