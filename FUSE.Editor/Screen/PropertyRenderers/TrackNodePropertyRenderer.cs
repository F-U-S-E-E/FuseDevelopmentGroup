using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityResources = UnityEngine.Resources;
using Track;
using FUSE.Editor;

namespace FUSE.Editor.Screen.PropertyRenderers
{
    /// <summary>
    /// Renders TrackNode reference properties with a clickable button that opens a selection window.
    /// Allows filtering by ID, or selecting directly from the scene via the selection system.
    /// </summary>
    public class TrackNodePropertyRenderer : IPropertyRenderer
    {
        private const float RowHeight = 22f;
        private const float LabelWidth = 96f;
        private const float Padding = 6f;
        private const float ButtonHeight = 22f;

        private static TrackNodeSelectionWindow _selectionWindow;
        private static Dictionary<int, TrackNode> _pendingSelections = new Dictionary<int, TrackNode>();

        public bool CanRender(Type propertyType)
        {
            // Check if this is a TrackNode type or string property that represents a TrackNode ID
            return propertyType == typeof(TrackNode) || 
                   (propertyType == typeof(string) && propertyType.Name == "TrackNode");
        }

        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var labelRect = new Rect(rect.x, rect.y, LabelWidth, RowHeight);
            var buttonRect = new Rect(rect.x + LabelWidth, rect.y, rect.width - LabelWidth - Padding, ButtonHeight);

            GUI.Label(labelRect, "  " + propertyName, labelStyle);

            // Get current TrackNode ID for display
            string currentNodeId = "<None>";
            TrackNode currentNode = currentValue as TrackNode;
            if (currentNode != null)
            {
                currentNodeId = currentNode.id ?? "<Unknown>";
            }

            // Display button with current node ID
            if (GUI.Button(buttonRect, currentNodeId, valueStyle))
            {
                OpenSelectionWindow(propertyName, currentNode);
            }

            // Check for pending selection
            int stateKey = propertyType.GetHashCode() ^ propertyName.GetHashCode();
            if (_pendingSelections.TryGetValue(stateKey, out TrackNode selectedNode))
            {
                _pendingSelections.Remove(stateKey);

                if (selectedNode != currentNode)
                {
                    return (true, selectedNode);
                }
            }

            return (false, currentValue);
        }

        private void OpenSelectionWindow(string propertyName, TrackNode currentNode)
        {
            if (_selectionWindow == null)
            {
                _selectionWindow = new TrackNodeSelectionWindow();
            }

            _selectionWindow.Open(propertyName, currentNode);
        }

        /// <summary>
        /// Called by the selection window when a node is selected.
        /// </summary>
        internal static void SetSelection(int stateKey, TrackNode selectedNode)
        {
            _pendingSelections[stateKey] = selectedNode;
        }

        /// <summary>
        /// Called by the selection window to initiate scene selection mode.
        /// </summary>
        internal static void InitiateSceneSelection()
        {
            var selection = FuseEditor.Instance?.EntitySelection;
            if (selection != null)
            {
                selection.RequestSelection(typeof(TrackNode));
            }
        }

        /// <summary>
        /// Draws the selection window if it's open. Call from the editor's OnGUI.
        /// </summary>
        public static void DrawSelectionWindow()
        {
            if (_selectionWindow != null)
            {
                _selectionWindow.OnGUI();
            }
        }

        public float GetPropertyHeight(Type propertyType) => RowHeight;
    }

    /// <summary>
    /// Window for selecting track nodes with search/filter functionality and scene selection.
    /// </summary>
    internal sealed class TrackNodeSelectionWindow
    {
        private string _searchFilter = "";
        private int _stateKey;
        private TrackNode _currentNode;
        private Vector2 _scrollPosition;
        private bool _isOpen;
        private bool _selectionModeActive;

        private const float WindowWidth = 400f;
        private const float WindowHeight = 500f;
        private const float CompactWindowHeight = 100f;  // Small window for scene selection
        private const float ItemHeight = 24f;
        private const float SearchFieldHeight = 24f;
        private const float ButtonHeight = 24f;
        private const float Padding = 8f;

        public void Open(string propertyName, TrackNode currentNode)
        {
            _currentNode = currentNode;
            _stateKey = typeof(TrackNode).GetHashCode() ^ propertyName.GetHashCode();
            _searchFilter = "";
            _scrollPosition = Vector2.zero;
            _isOpen = true;
            _selectionModeActive = false;
        }

        public void OnGUI()
        {
            if (!_isOpen)
                return;

            // Create a centered window
            var screenSize = new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height);

            // Use compact window when in selection mode, positioned in top-right
            if (_selectionModeActive)
            {
                var windowRect = new Rect(
                    screenSize.x - WindowWidth - 20f,  // Right side with margin
                    20f,  // Top with margin
                    WindowWidth,
                    CompactWindowHeight
                );
                GUI.Box(windowRect, "", "window");
                DrawCompactWindow(windowRect);
            }
            else
            {
                var windowRect = new Rect(
                    (screenSize.x - WindowWidth) / 2f,
                    (screenSize.y - WindowHeight) / 2f,
                    WindowWidth,
                    WindowHeight
                );
                GUI.Box(windowRect, "", "window");
                DrawWindow(windowRect);
            }
        }

        private void DrawWindow(Rect windowRect)
        {
            var contentRect = new Rect(windowRect.x + Padding, windowRect.y + Padding,
                                       windowRect.width - Padding * 2, windowRect.height - Padding * 2);

            // Title
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 20f),
                      "Select Track Node", GUI.skin.box);
            var y = contentRect.y + 24f;

            // Search field
            GUI.Label(new Rect(contentRect.x, y, 50f, SearchFieldHeight), "Search:");
            _searchFilter = GUI.TextField(new Rect(contentRect.x + 60f, y, contentRect.width - 60f, SearchFieldHeight),
                                          _searchFilter);
            y += SearchFieldHeight + Padding;

            // Get all track nodes
            var allNodes = FindAllTrackNodes();

            // Filter nodes
            var filteredNodes = string.IsNullOrEmpty(_searchFilter)
                ? allNodes
                : allNodes.Where(n => n.id.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Calculate list height
            float listHeight = contentRect.height - (y - contentRect.y) - (ButtonHeight + Padding) * 2 - Padding;
            var listRect = new Rect(contentRect.x, y, contentRect.width, listHeight);

            // Draw node list
            DrawNodeList(listRect, filteredNodes);
            y = listRect.yMax + Padding;

            // Buttons row
            float buttonWidth = (contentRect.width - Padding) / 2f;

            // "Select from Scene" button
            var sceneButtonRect = new Rect(contentRect.x, y, buttonWidth, ButtonHeight);
            if (GUI.Button(sceneButtonRect, "Select from Scene"))
            {
                _selectionModeActive = true;
                TrackNodePropertyRenderer.InitiateSceneSelection();
            }

            // "Close" button
            var closeButtonRect = new Rect(contentRect.x + buttonWidth + Padding, y, buttonWidth, ButtonHeight);
            if (GUI.Button(closeButtonRect, "Close"))
            {
                _isOpen = false;
                _selectionModeActive = false;
            }
        }

        private void DrawCompactWindow(Rect windowRect)
        {
            var contentRect = new Rect(windowRect.x + Padding, windowRect.y + Padding,
                                       windowRect.width - Padding * 2, windowRect.height - Padding * 2);

            // Title with instruction
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 20f),
                      "Click a node in the viewport", GUI.skin.box);
            var y = contentRect.y + 24f;

            // Status
            GUI.Label(new Rect(contentRect.x, y, contentRect.width, 20f),
                      "Selecting...", GUI.skin.label);
            y += 24f;

            // Cancel button
            if (GUI.Button(new Rect(contentRect.x, y, contentRect.width, ButtonHeight), "Cancel"))
            {
                _selectionModeActive = false;
                var selection = FuseEditor.Instance?.EntitySelection;
                if (selection != null)
                {
                    selection.SelectionRequestType = null;
                }
            }

            // Check if a scene selection was made
            var entitySelection = FuseEditor.Instance?.EntitySelection;
            if (entitySelection != null && entitySelection.TryGetRequestedSelection(typeof(TrackNode), out var handler))
            {
                var selectedNode = handler.Entity as TrackNode;
                if (selectedNode != null)
                {
                    TrackNodePropertyRenderer.SetSelection(_stateKey, selectedNode);
                    _isOpen = false;
                    _selectionModeActive = false;
                }
            }
        }

        private void DrawNodeList(Rect listRect, List<TrackNode> nodes)
        {
            GUI.Box(listRect, "", "box");

            var viewRect = new Rect(0f, 0f, listRect.width - 16f, nodes.Count * ItemHeight);
            _scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition, viewRect);

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var itemRect = new Rect(0f, i * ItemHeight, viewRect.width, ItemHeight);

                // Highlight current selection
                if (node == _currentNode)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 1f); // Blue highlight
                    GUI.Box(itemRect, "", "box");
                    GUI.backgroundColor = Color.white;
                }

                if (GUI.Button(itemRect, node.id, "label"))
                {
                    // Selection made
                    TrackNodePropertyRenderer.SetSelection(_stateKey, node);
                    _isOpen = false;
                    _selectionModeActive = false;
                }
            }

            GUI.EndScrollView();

            if (nodes.Count == 0)
            {
                GUI.Label(new Rect(listRect.x + 10f, listRect.y + 10f, listRect.width - 20f, ItemHeight),
                          string.IsNullOrEmpty(_searchFilter) ? "No track nodes found." : "No matching track nodes.");
            }
        }

        private List<TrackNode> FindAllTrackNodes()
        {
            var nodes = new List<TrackNode>();
            var allGameObjects = UnityResources.FindObjectsOfTypeAll<TrackNode>();

            foreach (var node in allGameObjects)
            {
                if (node != null && !string.IsNullOrEmpty(node.id))
                {
                    nodes.Add(node);
                }
            }

            // Sort by ID for consistent display
            return nodes.OrderBy(n => n.id).ToList();
        }
    }
}
