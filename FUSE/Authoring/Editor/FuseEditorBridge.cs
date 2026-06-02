using System;

namespace FUSE.Authoring.Editor
{
    public static class FuseEditorBridge
    {
        public static IFuseSelectionProvider SelectionProvider { get; set; }
        public static IFuseEditorProvider EditorProvider { get; private set; }
        public static IFuseEditorLifecycle LifecycleProvider { get; private set; }
        public static bool IsEditorActive { get; set; }

        public static void RegisterEditorProvider(IFuseEditorProvider provider)
        {
            EditorProvider = provider;
        }

        public static void ClearEditorProvider(IFuseEditorProvider provider)
        {
            if (ReferenceEquals(EditorProvider, provider))
            {
                EditorProvider = null;
            }
        }

        public static void RegisterLifecycleProvider(IFuseEditorLifecycle provider)
        {
            LifecycleProvider = provider;
        }

        public static void ClearLifecycleProvider(IFuseEditorLifecycle provider)
        {
            if (ReferenceEquals(LifecycleProvider, provider))
            {
                LifecycleProvider = null;
            }
        }

        public static void NotifyFuseLoaded()
        {
            LifecycleProvider?.OnFuseLoaded();
        }

        public static void NotifyFuseUnloaded()
        {
            LifecycleProvider?.OnFuseUnloaded();
        }

        /// <summary>
        /// Invoked by the FUSE-side main-menu patch when the user clicks
        /// the FUSE Editor button. The lifecycle provider (FUSE.Editor.dll)
        /// is responsible for spawning the editor surface; FUSE.dll just
        /// signals the request.
        /// </summary>
        public static void NotifyEnterEditor()
        {
            LifecycleProvider?.EnterEditor();
        }

        /// <summary>
        /// Set by the FUSE-side main-menu patch immediately before it
        /// invokes <c>GlobalGameManager.Launch</c> for an editor session.
        /// FUSE.Editor's MapDidLoad handler consumes the flag and brings
        /// up the editor surface only when this is true — so a normal
        /// sandbox/company load doesn't accidentally trigger the editor.
        /// </summary>
        public static bool EditorSessionPending { get; set; }

        /// <summary>
        /// Fired by the editor side (FUSE.Editor.dll) when the user clicks
        /// Exit Editor (or otherwise dismisses the editor surface). FUSE
        /// subscribes from its main-menu patch to clean up any session
        /// state and, eventually, route back to the main menu via
        /// GlobalGameManager.ReturnToMainMenu.
        /// </summary>
        public static event Action EditorExited;

        public static void NotifyEditorExited()
        {
            EditorExited?.Invoke();
        }
    }
}
