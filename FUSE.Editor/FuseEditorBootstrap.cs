using FUSE.Authoring.Editor;

namespace FUSE.Editor
{
    /// <summary>
    /// Single entry point invoked once by FUSE.dll's FuseEditorAssemblyLoader
    /// after it locates FUSE.Editor.dll on disk and calls
    /// Assembly.LoadFrom(...).Initialize(). Registers an IFuseEditorLifecycle
    /// implementation with FuseEditorBridge so all subsequent FUSE -> editor
    /// calls flow through the typed bridge.
    /// </summary>
    public static class FuseEditorBootstrap
    {
        private static readonly FuseEditorLifecycle Lifecycle = new FuseEditorLifecycle();
        private static bool _registered;

        public static void Initialize()
        {
            if (_registered)
            {
                return;
            }

            FuseEditorBridge.RegisterLifecycleProvider(Lifecycle);
            _registered = true;
        }
    }

    internal sealed class FuseEditorLifecycle : IFuseEditorLifecycle
    {
        public void OnFuseLoaded()
        {
            FuseEditor.OnFuseLoad();
        }

        public void OnFuseUnloaded()
        {
            FuseEditor.OnFuseUnload();
        }

        public void EnterEditor()
        {
            FuseEditor.Instance?.Enter();
        }
    }
}
