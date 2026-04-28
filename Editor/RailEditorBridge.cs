namespace RAIL.Editor
{
    public static class RailEditorBridge
    {
        public static IRailSelectionProvider SelectionProvider { get; set; }
        public static IRailEditorProvider EditorProvider { get; private set; }
        public static bool IsEditorActive { get; set; }

        public static void RegisterEditorProvider(IRailEditorProvider provider)
        {
            EditorProvider = provider;
        }

        public static void ClearEditorProvider(IRailEditorProvider provider)
        {
            if (ReferenceEquals(EditorProvider, provider))
            {
                EditorProvider = null;
            }
        }
    }
}
