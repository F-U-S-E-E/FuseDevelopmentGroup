namespace FUSE.Authoring.Editor
{
    public static class FuseEditorBridge
    {
        public static IFuseSelectionProvider SelectionProvider { get; set; }
        public static IFuseEditorProvider EditorProvider { get; private set; }
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
    }
}
