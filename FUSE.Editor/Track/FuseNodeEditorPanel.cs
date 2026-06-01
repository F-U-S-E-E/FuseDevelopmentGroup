using UI.Builder;

namespace FUSE.Editor.Track
{
    /// <summary>
    /// UI Panel rendered under the MainEditorWindow "Tracks" tab. Provides
    /// new-node creation, selection display, and gizmo trigger buttons for
    /// the currently-selected FuseNodeMarker.
    /// </summary>
    internal static class FuseNodeEditorPanel
    {
        private static string _newNodeId = string.Empty;
        private static string _lastError;
        private static bool _markersVisible;

        public static void Build(UIPanelBuilder builder)
        {
            var mod = FuseEditor.Instance != null ? FuseEditor.Instance.ActiveMod : null;
            if (mod == null)
            {
                builder.AddLabel("Select a mod to edit its track nodes.");
                return;
            }

            builder.AddLabel($"Editing tracks for mod: {mod.Definition.Id}");
            builder.AddLabel(string.Empty);

            if (_markersVisible)
            {
                builder.AddButtonCompact("Hide node markers", () =>
                {
                    FuseNodeEditorController.ClearMarkers();
                    _markersVisible = false;
                });
            }
            else
            {
                builder.AddButtonCompact("Show node markers", () =>
                {
                    FuseNodeEditorController.ShowMarkersForActiveMod();
                    _markersVisible = true;
                });
            }

            builder.AddLabel(string.Empty);

            var selected = FuseNodeEditorController.Selected;
            if (selected != null && selected.Node != null)
            {
                builder.AddLabel($"Selected node: {selected.Node.id}");
                builder.AddButtonCompact("Move", selected.BeginMove);
                builder.AddButtonCompact("Rotate", selected.BeginRotate);
                builder.AddButtonCompact("Save & Rebuild", selected.PersistAndRebuild);
                builder.AddButtonCompact("Deselect", FuseNodeEditorController.DeselectCurrent);
            }
            else
            {
                builder.AddLabel("Click a marker in the scene to select a node.");
                builder.AddLabel(string.Empty);
                builder.AddLabel("New node id:");
                builder.AddInputField(_newNodeId, value => _newNodeId = value);
                builder.AddButtonCompact("Create node at camera ray", () =>
                {
                    if (FuseNodeEditorController.TryCreateNodeAtCameraRaycast(_newNodeId, out var error))
                    {
                        _newNodeId = string.Empty;
                        _lastError = null;
                        _markersVisible = true;
                    }
                    else
                    {
                        _lastError = error;
                    }
                });
            }

            if (!string.IsNullOrEmpty(_lastError))
            {
                builder.AddLabel($"Error: {_lastError}");
            }

            builder.AddExpandingVerticalSpacer();
        }
    }
}
