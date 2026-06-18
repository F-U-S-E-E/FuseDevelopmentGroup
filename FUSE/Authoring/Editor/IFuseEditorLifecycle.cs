namespace FUSE.Authoring.Editor
{
    /// <summary>
    /// Contract the FUSE.Editor assembly registers with FuseEditorBridge so
    /// FUSE.dll can hand off plugin load/unload and editor mode transitions
    /// without a hard project reference to FUSE.Editor.dll. All interaction
    /// between the two assemblies flows through this interface plus the
    /// existing provider interfaces in FuseEditorBridge.
    /// </summary>
    public interface IFuseEditorLifecycle
    {
        void OnFuseLoaded();
        void OnFuseUnloaded();

        /// <summary>
        /// Called by FUSE.dll when the user clicks the FUSE Editor entry
        /// in the main menu. Implementations must spawn the editor surface
        /// (currently an IMGUI EDEN-inspired mockup) on top of whatever
        /// scene is loaded. The MAIN menu path is the only entry point —
        /// the pause-menu coupling has been removed.
        /// </summary>
        void EnterEditor();
    }
}
