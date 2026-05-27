using System;
using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Editor.Bookmarks;
using FUSE.Editor.Mods;
using FUSE.Editor.Screen.UI;
using FUSE.Infrastructure;
using FUSE.Loading;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Editor.Screen
{
    /// <summary>
    /// IMGUI mockup of the EDEN-inspired editor surface. Rendered on top
    /// of whatever Unity scene is active (main menu or loaded map) so the
    /// editor can be entered without depending on a
    /// <c>ProgrammaticWindowCreator</c> being present. The layout —
    /// top bar / left entity tree / center viewport / right properties /
    /// bottom status — is a deliberate skeleton; real interaction
    /// (selection, gizmos, persistence) plugs in over the next iterations.
    /// </summary>
    /// <remarks>
    /// EDEN cues that drove this layout:
    /// <list type="bullet">
    ///   <item>top-left: file/Save actions; top-right: Play + Exit/Help</item>
    ///   <item>left panel: entity tree with collapsible categories +
    ///         collections (Arma calls these "Layers")</item>
    ///   <item>center: 3D viewport dominates the screen</item>
    ///   <item>right panel: attributes of the current selection</item>
    ///   <item>bottom: status bar with mod name, save state, entity count</item>
    ///   <item>Play preview state reverts on return — we'll mirror that
    ///         contract once real preview lands</item>
    /// </list>
    /// </remarks>
    internal sealed class FuseEditorScreen : MonoBehaviour
    {
        public event Action ExitRequested;
        // Note: Play wiring lands when the preview-state-revert contract is
        // implemented; the disabled top-bar button surfaces an explanatory
        // tooltip until then.

        private const float TopBarHeight = 36f;
        private const float BookmarkBarHeight = 26f;
        private const float BottomBarHeight = 24f;
        private const float LeftPanelWidth = 280f;
        private const float RightPanelWidth = 320f;
        private const float Padding = 6f;
        private const float WindowsPopupWidth = 220f;
        private const float WindowsPopupRowHeight = 22f;
        private const float WindowsButtonWidth = 110f;
        private const float BookmarkTabWidth = 140f;
        private const float BookmarkAddButtonWidth = 28f;

        /// <summary>Y coordinate where panels and the viewport start.</summary>
        private const float ContentTop = TopBarHeight + BookmarkBarHeight;

        private Vector2 _entityTreeScroll;
        private Vector2 _propertiesScroll;
        private string _selectedEntityKind = "Node";
        private string _selectedEntityId;
        private bool _windowsPopupOpen;
        private Rect _windowsButtonRect;
        private readonly HashSet<string> _expandedCategories = new HashSet<string>(StringComparer.Ordinal);

        // Per-field IMGUI buffers for the Properties panel's inline
        // float editors. The user's in-flight typing lives in the
        // buffer; we reseed from the model on selection change so the
        // user always sees the committed value of the new selection.
        private string _posXBuffer = string.Empty;
        private string _posYBuffer = string.Empty;
        private string _posZBuffer = string.Empty;
        private string _rotXBuffer = string.Empty;
        private string _rotYBuffer = string.Empty;
        private string _rotZBuffer = string.Empty;
        private string _groupBuffer = string.Empty;
        private string _tagsBuffer = string.Empty;
        private string _lastBufferedEntityId;

        private GUIStyle _topBarStyle;
        private GUIStyle _panelStyle;
        private GUIStyle _categoryHeaderStyle;
        private GUIStyle _entityRowStyle;
        private GUIStyle _entityRowSelectedStyle;
        private GUIStyle _viewportStyle;
        private GUIStyle _statusBarStyle;
        private GUIStyle _propertyLabelStyle;
        private GUIStyle _propertyValueStyle;
        private GUIStyle _toolButtonStyle;
        private GUIStyle _toolButtonActiveStyle;
        private GUIStyle _windowsPopupRowStyle;
        private GUIStyle _windowsPopupRowSelectedStyle;
        private GUIStyle _bookmarkBarStyle;
        private GUIStyle _bookmarkTabStyle;
        private GUIStyle _bookmarkTabActiveStyle;
        private Vector2 _bookmarkBarScroll;

        // Active-tab rename buffer. Resyncs from the registry whenever
        // ActiveIndex changes so a tab switch doesn't carry over typed
        // input from the previous tab.
        private string _activeBookmarkNameBuffer = string.Empty;
        private int _lastActiveBookmarkIndex = -1;

        // Mod browser state (when no mod is active).
        private int _modBrowserTab; // 0 = existing, 1 = new, 2 = legacy
        private Vector2 _modBrowserScroll;
        private string _newModIdBuffer = string.Empty;
        private string _newModNameBuffer = string.Empty;
        private string _newModAuthorBuffer = string.Empty;
        private string _newModStatusMessage;
        private List<FuseEditorModEntry> _legacyCatalogCache;
        private int _legacyCatalogRefreshedFrame = -1;
        private bool _stylesInitialized;

        private static float CurrentLeftPanelWidth =>
            FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.EntityTree) ? LeftPanelWidth : 0f;

        private static float CurrentRightPanelWidth =>
            FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Properties) ? RightPanelWidth : 0f;

        private void OnEnable()
        {
            // Default-expand the top-level categories so first impression
            // shows the structure rather than three collapsed headers.
            _expandedCategories.Add("Tracks");
            _expandedCategories.Add("World");
            _expandedCategories.Add("Operations");
        }

        private void OnGUI()
        {
            EnsureStyles();

            var screenRect = new Rect(0f, 0f, UnityEngine.Screen.width, UnityEngine.Screen.height);

            // No full-screen dim — the loaded world (Bushnell & Whittier
            // map + editor-mode camera) shows through the center viewport
            // gap. Top / left / right / bottom panels each paint their
            // own opaque backgrounds so they stay legible against the
            // scene behind them.
            DrawTopBar(screenRect);
            DrawBookmarkBar(screenRect);

            // Mod browser gates the rest of the editor: until a FUSE mod
            // is active, the user can't meaningfully select entities or
            // edit attributes. The browser fills the content area in
            // place of the side panels + viewport.
            if (FuseEditor.Instance?.ActiveMod == null)
            {
                DrawModBrowser(screenRect);
            }
            else
            {
                if (FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.EntityTree))
                {
                    DrawLeftPanel(screenRect);
                }
                DrawCenterViewport(screenRect);
                if (FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.Properties))
                {
                    DrawRightPanel(screenRect);
                }
            }

            DrawBottomBar(screenRect);

            // Windows popup paints after the framing chrome so it overlays
            // panels but stays behind the tooltip pass.
            if (_windowsPopupOpen)
            {
                DrawWindowsPopup();
            }

            // Tooltip pass goes last so it paints over every other panel.
            // Reads GUI.tooltip captured by the most-recently-hovered
            // GUIContent across the whole frame.
            FuseEditorUiHelper.RenderHoverTooltip(_panelStyle);

            // Dismiss the windows popup on a click outside its rect — the
            // standard implicit-modal idiom in Unity IMGUI.
            HandleWindowsPopupDismissal();
        }

        private void DrawTopBar(Rect screen)
        {
            var rect = new Rect(0f, 0f, screen.width, TopBarHeight);
            GUI.Box(rect, GUIContent.none, _topBarStyle);

            // Title + active mod label
            var titleRect = new Rect(Padding, 0f, 360f, TopBarHeight);
            var titleLabel = FuseEditorStrings.Get("fuse.editor.topbar.title");
            var modLabel = FuseEditorStrings.Get("fuse.editor.topbar.mod_label");
            var activeModId = FuseEditor.Instance?.ActiveMod?.Definition?.Id
                              ?? FuseEditorStrings.Get("fuse.editor.topbar.no_mod_selected");
            GUI.Label(titleRect, $"{titleLabel}    •    {modLabel}: {activeModId}", _propertyLabelStyle);

            // Right-aligned cluster: Windows ▾ | Save | Play | Exit
            const float buttonWidth = 96f;
            var buttonY = (TopBarHeight - 22f) * 0.5f;
            float x = screen.width - Padding - buttonWidth;

            // Exit Editor — enabled, surfaces its description on hover.
            if (FuseEditorUiHelper.Button(
                    new Rect(x, buttonY, buttonWidth, 22f),
                    "fuse.editor.topbar.exit",
                    _toolButtonStyle))
            {
                ExitRequested?.Invoke();
            }

            x -= buttonWidth + Padding;
            FuseEditorUiHelper.DisabledButton(
                new Rect(x, buttonY, buttonWidth, 22f),
                "fuse.editor.topbar.play",
                reason: null,
                style: _toolButtonStyle);

            x -= buttonWidth + Padding;
            FuseEditorUiHelper.DisabledButton(
                new Rect(x, buttonY, buttonWidth, 22f),
                "fuse.editor.topbar.save",
                reason: null,
                style: _toolButtonStyle);

            // Windows toggle button — opens a small popup with one
            // checkbox per registered FuseEditorWindowKind. Same shape as
            // Axiom's main-menu-bar "Window" dropdown, scaled down to a
            // single button + popup for Unity IMGUI.
            x -= WindowsButtonWidth + Padding;
            _windowsButtonRect = new Rect(x, buttonY, WindowsButtonWidth, 22f);
            if (FuseEditorUiHelper.Button(_windowsButtonRect, "fuse.editor.topbar.windows", _toolButtonStyle))
            {
                _windowsPopupOpen = !_windowsPopupOpen;
            }
        }

        private void DrawBookmarkBar(Rect screen)
        {
            var rect = new Rect(0f, TopBarHeight, screen.width, BookmarkBarHeight);
            GUI.Box(rect, GUIContent.none, _bookmarkBarStyle);

            // Keep the rename buffer in sync with the registry: when the
            // active tab changes (via teleport, delete, add) reseed from
            // the new bookmark's Name so we don't carry over the
            // previous tab's typed string.
            SyncActiveBookmarkBuffer();

            var bookmarks = FuseEditorBookmarkRegistry.All;
            var contentY = rect.y + (BookmarkBarHeight - 22f) * 0.5f;

            // Reserve space for the right-aligned action buttons: ✕ then +.
            var actionsWidth = (BookmarkAddButtonWidth * 2f) + Padding * 2f;
            var available = screen.width - actionsWidth;

            if (bookmarks.Count == 0)
            {
                var hint = FuseEditorStrings.Get("fuse.editor.bookmarks.empty_hint");
                var hintRect = new Rect(Padding, rect.y, available - Padding, BookmarkBarHeight);
                GUI.Label(hintRect, hint, _propertyLabelStyle);
            }
            else
            {
                DrawBookmarkTabs(rect, available, bookmarks);
            }

            // Right-aligned cluster: ✕ delete | + add. Delete is gray
            // when no bookmark is currently active; + is always enabled
            // so the user can capture the live camera position.
            var addRect = new Rect(
                screen.width - BookmarkAddButtonWidth - Padding,
                contentY,
                BookmarkAddButtonWidth,
                22f);
            if (FuseEditorUiHelper.Button(addRect, "fuse.editor.bookmarks.add", _toolButtonStyle))
            {
                TryCaptureBookmarkFromCamera();
            }

            var deleteRect = new Rect(
                addRect.x - BookmarkAddButtonWidth - Padding,
                contentY,
                BookmarkAddButtonWidth,
                22f);
            if (FuseEditorBookmarkRegistry.ActiveIndex < 0)
            {
                FuseEditorUiHelper.DisabledButton(
                    deleteRect,
                    "fuse.editor.bookmarks.delete",
                    reason: FuseEditorStrings.Get("fuse.editor.bookmarks.delete.no_selection"),
                    style: _toolButtonStyle);
            }
            else if (FuseEditorUiHelper.Button(deleteRect, "fuse.editor.bookmarks.delete", _toolButtonStyle))
            {
                TryDeleteActiveBookmark();
            }
        }

        private void DrawBookmarkTabs(Rect barRect, float availableWidth, System.Collections.Generic.IReadOnlyList<Bookmarks.FuseEditorBookmark> bookmarks)
        {
            var totalNeeded = bookmarks.Count * (BookmarkTabWidth + Padding);
            var viewRect = new Rect(0f, 0f, Mathf.Max(totalNeeded, availableWidth), BookmarkBarHeight);
            var clipRect = new Rect(0f, barRect.y, availableWidth, BookmarkBarHeight);
            _bookmarkBarScroll = GUI.BeginScrollView(clipRect, _bookmarkBarScroll, viewRect, false, false);

            var tabX = Padding;
            var activeIndex = FuseEditorBookmarkRegistry.ActiveIndex;
            for (int i = 0; i < bookmarks.Count; i++)
            {
                var bookmark = bookmarks[i];
                var tabRect = new Rect(tabX, (BookmarkBarHeight - 22f) * 0.5f, BookmarkTabWidth, 22f);
                var isActive = activeIndex == i;
                var tooltip = FuseEditorStrings.Get("fuse.editor.bookmarks.tooltip");

                if (isActive)
                {
                    // Active tab renders as an inline-editable TextField
                    // so the user can rename without a separate dialog.
                    var newName = GUI.TextField(tabRect, _activeBookmarkNameBuffer ?? string.Empty, _bookmarkTabActiveStyle);
                    if (!string.Equals(newName, _activeBookmarkNameBuffer, StringComparison.Ordinal))
                    {
                        _activeBookmarkNameBuffer = newName;
                        // Registry.Rename rejects null/whitespace, so an
                        // empty buffer doesn't wipe the saved name —
                        // user gets the previous name back on next render
                        // via SyncActiveBookmarkBuffer.
                        FuseEditorBookmarkRegistry.Rename(i, newName);
                    }
                }
                else
                {
                    if (GUI.Button(tabRect, new GUIContent(bookmark.Name, tooltip), _bookmarkTabStyle))
                    {
                        TryTeleportToBookmark(i);
                    }
                }

                tabX += BookmarkTabWidth + Padding;
            }

            GUI.EndScrollView();
        }

        private void SyncActiveBookmarkBuffer()
        {
            var idx = FuseEditorBookmarkRegistry.ActiveIndex;
            if (idx == _lastActiveBookmarkIndex)
            {
                return;
            }

            var active = FuseEditorBookmarkRegistry.Active;
            _activeBookmarkNameBuffer = active?.Name ?? string.Empty;
            _lastActiveBookmarkIndex = idx;
        }

        private static void TryDeleteActiveBookmark()
        {
            var idx = FuseEditorBookmarkRegistry.ActiveIndex;
            if (idx < 0)
            {
                return;
            }

            FuseEditorBookmarkRegistry.RemoveAt(idx);
            // RemoveAt clears ActiveIndex when the deleted entry was the
            // active one; SyncActiveBookmarkBuffer picks the change up on
            // the next render.
        }

        private static void TryCaptureBookmarkFromCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                FuseLog.Info("FUSE editor bookmarks: Camera.main was null; nothing captured.");
                return;
            }

            var name = $"View {FuseEditorBookmarkRegistry.All.Count + 1}";
            var bookmark = FuseEditorBookmark.FromCamera(name, cam);
            var index = FuseEditorBookmarkRegistry.Add(bookmark);
            if (index >= 0)
            {
                FuseEditorBookmarkRegistry.SetActive(index);
            }
        }

        private static void TryTeleportToBookmark(int index)
        {
            if (index < 0 || index >= FuseEditorBookmarkRegistry.All.Count)
            {
                return;
            }

            FuseEditorBookmarkRegistry.SetActive(index);

            var bookmark = FuseEditorBookmarkRegistry.All[index];
            var selector = CameraSelector.shared;
            if (selector != null)
            {
                try
                {
                    selector.ZoomToPoint(bookmark.PositionVector);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception($"FUSE editor bookmarks: failed to teleport to '{bookmark.Name}'.", ex);
                }
            }
            else
            {
                FuseLog.Info($"FUSE editor bookmarks: CameraSelector.shared was null; skipping teleport to '{bookmark.Name}'.");
            }
        }

        private void DrawWindowsPopup()
        {
            var kinds = new List<FuseEditorWindowKind>();
            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                if (FuseEditorWindowRegistry.IsImportant(kind))
                {
                    kinds.Add(kind);
                }
            }

            var popupHeight = kinds.Count * WindowsPopupRowHeight + Padding * 2f;
            var popupRect = new Rect(
                _windowsButtonRect.x,
                _windowsButtonRect.y + _windowsButtonRect.height + 2f,
                WindowsPopupWidth,
                popupHeight);

            // Background panel — opaque so it covers anything below.
            GUI.Box(popupRect, GUIContent.none, _panelStyle);

            for (int i = 0; i < kinds.Count; i++)
            {
                var kind = kinds[i];
                var rowRect = new Rect(
                    popupRect.x + Padding,
                    popupRect.y + Padding + i * WindowsPopupRowHeight,
                    popupRect.width - Padding * 2f,
                    WindowsPopupRowHeight);

                var isOpen = FuseEditorWindowRegistry.IsOpen(kind);
                var nameKey = FuseEditorWindowRegistry.NameKey(kind);
                var label = FuseEditorUiHelper.TranslateLabel(nameKey);
                var marker = isOpen ? "✔" : "  ";
                var rowStyle = isOpen ? _windowsPopupRowSelectedStyle : _windowsPopupRowStyle;
                var content = new GUIContent($"  {marker}  {label.Title}", label.Description);
                if (GUI.Button(rowRect, content, rowStyle))
                {
                    FuseEditorWindowRegistry.Toggle(kind);
                }
            }
        }

        private void HandleWindowsPopupDismissal()
        {
            if (!_windowsPopupOpen)
            {
                return;
            }

            var ev = Event.current;
            if (ev == null)
            {
                return;
            }

            if (ev.type != EventType.MouseDown)
            {
                return;
            }

            // Skip dismissal if the click hit the toggle button itself
            // (it'll close via the button's own toggle handler).
            if (_windowsButtonRect.Contains(ev.mousePosition))
            {
                return;
            }

            // Skip dismissal if the click hit the popup body — the user
            // wants to toggle multiple checkboxes without re-opening.
            if (ComputeWindowsPopupRect().Contains(ev.mousePosition))
            {
                return;
            }

            _windowsPopupOpen = false;
        }

        private Rect ComputeWindowsPopupRect()
        {
            int importantCount = 0;
            foreach (var kind in FuseEditorWindowRegistry.All())
            {
                if (FuseEditorWindowRegistry.IsImportant(kind))
                {
                    importantCount++;
                }
            }

            var popupHeight = importantCount * WindowsPopupRowHeight + Padding * 2f;
            return new Rect(
                _windowsButtonRect.x,
                _windowsButtonRect.y + _windowsButtonRect.height + 2f,
                WindowsPopupWidth,
                popupHeight);
        }

        private void DrawLeftPanel(Rect screen)
        {
            var panelRect = new Rect(0f, ContentTop, LeftPanelWidth, screen.height - ContentTop - BottomBarHeight);
            GUI.Box(panelRect, GUIContent.none, _panelStyle);

            var headerRect = new Rect(panelRect.x + Padding, panelRect.y + Padding, panelRect.width - Padding * 2, 20f);
            GUI.Label(headerRect, "Entities", _categoryHeaderStyle);

            var contentRect = new Rect(panelRect.x + Padding,
                                       panelRect.y + Padding + 24f,
                                       panelRect.width - Padding * 2,
                                       panelRect.height - Padding * 2 - 24f);

            var rowHeight = 20f;
            var contentHeight = ComputeEntityTreeHeight(rowHeight);
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, contentHeight);

            _entityTreeScroll = GUI.BeginScrollView(contentRect, _entityTreeScroll, viewRect);
            DrawEntityTree(viewRect, rowHeight);
            GUI.EndScrollView();
        }

        private float ComputeEntityTreeHeight(float rowHeight)
        {
            var snapshot = GetCurrentDefinitionSnapshot();
            var height = 0f;

            foreach (var category in snapshot.Categories)
            {
                height += rowHeight;
                if (_expandedCategories.Contains(category.Name))
                {
                    foreach (var bucket in category.Buckets)
                    {
                        height += rowHeight;
                        if (_expandedCategories.Contains(category.Name + "/" + bucket.Name))
                        {
                            height += bucket.EntityIds.Count * rowHeight;
                        }
                    }
                }
            }

            return height;
        }

        private void DrawEntityTree(Rect viewRect, float rowHeight)
        {
            var snapshot = GetCurrentDefinitionSnapshot();
            var y = 0f;

            foreach (var category in snapshot.Categories)
            {
                var categoryExpanded = _expandedCategories.Contains(category.Name);
                var totalInCategory = TotalEntities(category);

                var categoryRect = new Rect(0f, y, viewRect.width, rowHeight);
                var prefix = categoryExpanded ? "▼" : "▶";
                if (GUI.Button(categoryRect, $"  {prefix}  {category.Name}    ({totalInCategory})", _categoryHeaderStyle))
                {
                    ToggleExpanded(category.Name);
                }
                y += rowHeight;

                if (!categoryExpanded) continue;

                foreach (var bucket in category.Buckets)
                {
                    var bucketKey = category.Name + "/" + bucket.Name;
                    var bucketExpanded = _expandedCategories.Contains(bucketKey);
                    var bucketRect = new Rect(16f, y, viewRect.width - 16f, rowHeight);
                    var bucketPrefix = bucketExpanded ? "▼" : "▶";
                    if (GUI.Button(bucketRect, $"  {bucketPrefix}  {bucket.Name}    ({bucket.EntityIds.Count})", _entityRowStyle))
                    {
                        ToggleExpanded(bucketKey);
                    }
                    y += rowHeight;

                    if (!bucketExpanded) continue;

                    foreach (var entityId in bucket.EntityIds)
                    {
                        var rowRect = new Rect(36f, y, viewRect.width - 36f, rowHeight);
                        var isSelected = string.Equals(_selectedEntityId, entityId, StringComparison.Ordinal)
                                         && string.Equals(_selectedEntityKind, bucket.Name, StringComparison.Ordinal);
                        var style = isSelected ? _entityRowSelectedStyle : _entityRowStyle;
                        if (GUI.Button(rowRect, "  " + entityId, style))
                        {
                            _selectedEntityKind = bucket.Name;
                            _selectedEntityId = entityId;

                            // Reach into the runtime: for Nodes,
                            // pre-spawn markers if needed (so the user
                            // doesn't have to switch tools), find the
                            // marker for this id, select it through the
                            // controller (which also drives the active
                            // tool's OnNodeSelected → gizmo engage), and
                            // pan the camera there.
                            HandleEntityTreeRowClick(bucket.Name, entityId);
                        }
                        y += rowHeight;
                    }
                }
            }
        }

        private static void HandleEntityTreeRowClick(string bucketName, string entityId)
        {
            // Only Nodes have runtime markers + a concrete position to
            // pan to today. Other entity kinds (Segments, Spans, Scenery)
            // will get their own selection hooks once their tools land.
            if (!string.Equals(bucketName, "Nodes", StringComparison.Ordinal))
            {
                return;
            }

            var mod = FuseEditor.Instance?.ActiveMod;
            var nodes = mod?.Definition?.Tracks?.Nodes;
            if (nodes == null || !nodes.TryGetValue(entityId, out var fuseNode) || fuseNode == null)
            {
                return;
            }

            // Camera focus — always pan the camera, even when no marker-
            // spawning tool is active, so tree-click is useful for
            // navigation alone.
            try
            {
                var selector = global::CameraSelector.shared;
                selector?.ZoomToPoint(fuseNode.Position);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to pan camera to node '{entityId}'.", ex);
            }

            // Marker selection — only meaningful when Select/Move/Rotate/
            // Place is active (those tools are what spawn markers). When
            // no marker exists for this id, the panel still updates from
            // the cached selection state above; the user just doesn't
            // get a gizmo until they switch to a marker-active tool.
            if (Track.FuseNodeEditorController.TryFindMarker(entityId, out var marker))
            {
                Track.FuseNodeEditorController.SelectMarker(marker);
                FuseEditorToolRegistry.Active?.OnNodeSelected(marker);
            }
        }

        private void ToggleExpanded(string key)
        {
            if (!_expandedCategories.Remove(key))
            {
                _expandedCategories.Add(key);
            }
        }

        private static int TotalEntities(CategorySnapshot category)
        {
            var total = 0;
            foreach (var bucket in category.Buckets)
            {
                total += bucket.EntityIds.Count;
            }
            return total;
        }

        private void DrawCenterViewport(Rect screen)
        {
            // Viewport stretches into whatever horizontal room the side
            // panels aren't using. Closing one panel grows the viewport
            // by that panel's width; closing both gives full-screen 3D.
            var viewportX = CurrentLeftPanelWidth;
            var viewportY = ContentTop;
            var viewportW = screen.width - CurrentLeftPanelWidth - CurrentRightPanelWidth;
            var viewportH = screen.height - ContentTop - BottomBarHeight;
            var rect = new Rect(viewportX, viewportY, viewportW, viewportH);

            if (!FuseEditorWindowRegistry.IsOpen(FuseEditorWindowKind.ToolStrip))
            {
                return;
            }

            // No background fill — the real 3D world renders through this
            // gap. Only paint the tool strip overlay along the bottom of
            // the viewport. Each button is driven by an IFuseEditorTool in
            // the registry: available tools render enabled (with active
            // state highlighted), unavailable tools render disabled with
            // their UnavailableReason as the tooltip.
            const float toolStripHeight = 28f;
            var tools = new Rect(rect.x + Padding * 4, rect.y + rect.height - toolStripHeight - Padding * 2,
                                 rect.width - Padding * 8, toolStripHeight);
            const float buttonWidth = 90f;
            var bx = tools.x;
            var by = tools.y + (toolStripHeight - 22f) * 0.5f;

            var registered = FuseEditorToolRegistry.All;
            for (int i = 0; i < registered.Count; i++)
            {
                var tool = registered[i];
                var buttonRect = new Rect(bx, by, buttonWidth, 22f);
                DrawToolButton(buttonRect, tool);
                bx += buttonWidth + Padding;
            }
        }

        private void DrawToolButton(Rect rect, FUSE.Editor.Screen.UI.IFuseEditorTool tool)
        {
            var label = FuseEditorUiHelper.TranslateLabel(tool.LabelKey);
            var displayText = string.IsNullOrEmpty(tool.IconGlyph)
                ? label.Title
                : $"{tool.IconGlyph}  {label.Title}";

            if (!tool.IsAvailable)
            {
                // Disabled with reason — the user sees why the button is
                // gray on hover. UnavailableReason takes precedence over
                // the registered .description.
                var prev = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(rect, new GUIContent(displayText, tool.UnavailableReason ?? label.Description), _toolButtonStyle);
                GUI.enabled = prev;
                return;
            }

            var isActive = FuseEditorToolRegistry.IsActive(tool);
            var style = isActive ? _toolButtonActiveStyle : _toolButtonStyle;
            if (GUI.Button(rect, new GUIContent(displayText, label.Description), style))
            {
                if (isActive)
                {
                    // Re-clicking the active tool drops it (the Axiom
                    // shape: tool button is a sticky toggle).
                    FuseEditorToolRegistry.Deactivate();
                }
                else
                {
                    FuseEditorToolRegistry.SetActive(tool);
                }
            }
        }

        private void DrawRightPanel(Rect screen)
        {
            var panelRect = new Rect(screen.width - RightPanelWidth,
                                     ContentTop,
                                     RightPanelWidth,
                                     screen.height - ContentTop - BottomBarHeight);
            GUI.Box(panelRect, GUIContent.none, _panelStyle);

            var headerRect = new Rect(panelRect.x + Padding, panelRect.y + Padding, panelRect.width - Padding * 2, 20f);
            GUI.Label(headerRect, FuseEditorStrings.Get("fuse.editor.properties.header"), _categoryHeaderStyle);

            var contentRect = new Rect(panelRect.x + Padding,
                                       panelRect.y + Padding + 24f,
                                       panelRect.width - Padding * 2,
                                       panelRect.height - Padding * 2 - 24f);

            if (string.IsNullOrEmpty(_selectedEntityId))
            {
                GUI.Label(contentRect, "  " + FuseEditorStrings.Get("fuse.editor.properties.empty_hint"), _propertyLabelStyle);
                return;
            }

            DrawPropertiesContent(contentRect);
        }

        private void DrawPropertiesContent(Rect contentRect)
        {
            // Resolve the underlying FuseNode for the current selection.
            // When it's not a node, or no mod is selected, fall back to
            // a read-only label-only display.
            var mod = FuseEditor.Instance?.ActiveMod;
            FuseNode node = null;
            var isNode = string.Equals(_selectedEntityKind, "Nodes", StringComparison.Ordinal);
            if (isNode && mod?.Definition?.Tracks?.Nodes != null)
            {
                mod.Definition.Tracks.Nodes.TryGetValue(_selectedEntityId, out node);
            }

            // Buffer reseed on selection change. Without this the
            // previous selection's typing would leak into the new
            // selection's fields on first render.
            if (!string.Equals(_lastBufferedEntityId, _selectedEntityId, StringComparison.Ordinal))
            {
                SeedPositionBuffersFromNode(node);
                _lastBufferedEntityId = _selectedEntityId;
            }

            const float rowHeight = 22f;
            const float labelWidth = 96f;
            const float axisLabelWidth = 16f;

            // Five conceptual rows: Kind, Id, Position, Rotation, Group,
            // Tags. Position takes one row with three sub-fields. The
            // others render as read-only labels.
            var rowsToDraw = 6;
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, rowsToDraw * rowHeight + 8f);
            _propertiesScroll = GUI.BeginScrollView(contentRect, _propertiesScroll, viewRect);

            int row = 0;

            // Kind (read-only)
            DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                 FuseEditorStrings.Get("fuse.editor.properties.kind"),
                                 _selectedEntityKind);

            // Id (read-only)
            DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                 FuseEditorStrings.Get("fuse.editor.properties.id"),
                                 _selectedEntityId);

            // Position — editable when we resolved a FuseNode; otherwise
            // a fallback message in the value slot.
            if (node != null)
            {
                DrawPositionRow(row++, rowHeight, labelWidth, axisLabelWidth, viewRect.width, mod, node);
            }
            else
            {
                DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                     FuseEditorStrings.Get("fuse.editor.properties.position"),
                                     isNode ? "(not loaded)" : "(not editable for this kind)");
            }

            // Rotation — three editable Euler-component fields,
            // matching the Position row's pattern.
            if (node != null)
            {
                DrawRotationRow(row++, rowHeight, labelWidth, axisLabelWidth, viewRect.width, mod, node);
            }
            else
            {
                DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                     FuseEditorStrings.Get("fuse.editor.properties.rotation"),
                                     "—");
            }

            // Group + Tags — editable when a FuseNode is resolved.
            if (node != null)
            {
                DrawGroupRow(row++, rowHeight, labelWidth, viewRect.width, mod, node);
                DrawTagsRow(row++, rowHeight, labelWidth, viewRect.width, mod, node);
            }
            else
            {
                DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                     FuseEditorStrings.Get("fuse.editor.properties.group"), "—");
                DrawPropertyLabelRow(row++, rowHeight, labelWidth, viewRect.width,
                                     FuseEditorStrings.Get("fuse.editor.properties.tags"), "—");
            }

            // Delete affordance — only when a real FuseNode is resolved.
            // No confirmation prompt: re-creation via the Place tool is
            // trivial, and the action is persisted, not destructive of
            // anything beyond the in-mod definition.
            if (node != null && mod != null)
            {
                DrawDeleteButton(row++, rowHeight, viewRect.width, mod);
            }

            GUI.EndScrollView();
        }

        private void DrawDeleteButton(int row, float rowHeight, float totalWidth, FuseLoadedMod mod)
        {
            var y = row * rowHeight + Padding;
            var rect = new Rect(Padding, y, Mathf.Min(160f, totalWidth - Padding * 2f), rowHeight);
            if (FuseEditorUiHelper.Button(rect, "fuse.editor.properties.delete", _toolButtonStyle))
            {
                DeleteSelectedNode(mod);
            }
        }

        private void DeleteSelectedNode(FuseLoadedMod mod)
        {
            if (mod == null || string.IsNullOrEmpty(_selectedEntityId))
            {
                return;
            }

            var nodeId = _selectedEntityId;

            // Destroy the live runtime TrackNode first so the world
            // reflects the removal even if the persist step fails.
            try
            {
                var trackNode = global::Track.Graph.Shared?.GetNode(nodeId);
                if (trackNode != null)
                {
                    UnityEngine.Object.Destroy(trackNode.gameObject);
                    global::Track.Graph.Shared.RebuildCollections();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to destroy live TrackNode '{nodeId}' before persist.", ex);
            }

            try
            {
                if (FUSE.Authoring.Entities.FuseAuthoringPersistenceService.RemoveDefinitionObject(
                        packageId: mod.Definition.Id,
                        kind: "node",
                        objectId: nodeId,
                        reason: "deleted via FUSE editor properties panel"))
                {
                    FuseEditorSaveTracker.MarkSaved();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to persist deletion of node '{nodeId}'.", ex);
            }

            // Drop the selection so the panel returns to the empty hint.
            // Existing marker (if any) is dangling now that the
            // TrackNode is destroyed; let the active tool's next
            // markers-refresh path clean it up.
            _selectedEntityId = null;
            _selectedEntityKind = "Node";
            _lastBufferedEntityId = null;
            Track.FuseNodeEditorController.DeselectCurrent();
        }

        private void DrawPropertyLabelRow(int row, float rowHeight, float labelWidth, float totalWidth,
                                          string label, string value)
        {
            var y = row * rowHeight;
            GUI.Label(new Rect(0f, y, labelWidth, rowHeight), "  " + label, _propertyLabelStyle);
            GUI.Label(new Rect(labelWidth, y, totalWidth - labelWidth, rowHeight), value, _propertyValueStyle);
        }

        private void DrawPositionRow(int row, float rowHeight, float labelWidth, float axisLabelWidth,
                                     float totalWidth, FuseLoadedMod mod, FuseNode node)
        {
            var y = row * rowHeight;
            GUI.Label(new Rect(0f, y, labelWidth, rowHeight),
                      "  " + FuseEditorStrings.Get("fuse.editor.properties.position"),
                      _propertyLabelStyle);

            var fieldsStart = labelWidth;
            var fieldsWidth = totalWidth - labelWidth - Padding;
            var perField = (fieldsWidth - axisLabelWidth * 3f - Padding * 2f) / 3f;

            var x = fieldsStart;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "X", ref _posXBuffer, node.Position.x,
                          newValue => ApplyNodePositionEdit(mod, _selectedEntityId, node, new Vector3(newValue, node.Position.y, node.Position.z)));
            x += axisLabelWidth + perField + Padding;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "Y", ref _posYBuffer, node.Position.y,
                          newValue => ApplyNodePositionEdit(mod, _selectedEntityId, node, new Vector3(node.Position.x, newValue, node.Position.z)));
            x += axisLabelWidth + perField + Padding;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "Z", ref _posZBuffer, node.Position.z,
                          newValue => ApplyNodePositionEdit(mod, _selectedEntityId, node, new Vector3(node.Position.x, node.Position.y, newValue)));
        }

        private void DrawAxisField(Rect labelRect, Rect fieldRect, string axisLabel,
                                   ref string buffer, float committedValue, Action<float> onCommit)
        {
            GUI.Label(labelRect, axisLabel, _propertyLabelStyle);

            var newBuffer = GUI.TextField(fieldRect, buffer ?? string.Empty);
            if (!string.Equals(newBuffer, buffer, StringComparison.Ordinal))
            {
                buffer = newBuffer;
                if (FuseEditorFieldHelper.TryCommitFloat(newBuffer, committedValue, out var parsed))
                {
                    onCommit?.Invoke(parsed);
                }
            }
        }

        private void SeedPositionBuffersFromNode(FuseNode node)
        {
            if (node == null)
            {
                _posXBuffer = _posYBuffer = _posZBuffer = string.Empty;
                _rotXBuffer = _rotYBuffer = _rotZBuffer = string.Empty;
                _groupBuffer = _tagsBuffer = string.Empty;
                return;
            }

            _posXBuffer = FuseEditorFieldHelper.FormatFloat(node.Position.x);
            _posYBuffer = FuseEditorFieldHelper.FormatFloat(node.Position.y);
            _posZBuffer = FuseEditorFieldHelper.FormatFloat(node.Position.z);

            _rotXBuffer = FuseEditorFieldHelper.FormatFloat(node.Rotation.x);
            _rotYBuffer = FuseEditorFieldHelper.FormatFloat(node.Rotation.y);
            _rotZBuffer = FuseEditorFieldHelper.FormatFloat(node.Rotation.z);

            _groupBuffer = node.GroupId ?? string.Empty;
            _tagsBuffer = FuseEditorFieldHelper.FormatTags(node.Tags);
        }

        private void DrawRotationRow(int row, float rowHeight, float labelWidth, float axisLabelWidth,
                                     float totalWidth, FuseLoadedMod mod, FuseNode node)
        {
            var y = row * rowHeight;
            GUI.Label(new Rect(0f, y, labelWidth, rowHeight),
                      "  " + FuseEditorStrings.Get("fuse.editor.properties.rotation"),
                      _propertyLabelStyle);

            var fieldsStart = labelWidth;
            var fieldsWidth = totalWidth - labelWidth - Padding;
            var perField = (fieldsWidth - axisLabelWidth * 3f - Padding * 2f) / 3f;

            var x = fieldsStart;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "X", ref _rotXBuffer, node.Rotation.x,
                          newValue => ApplyNodeRotationEdit(mod, _selectedEntityId, node, new Vector3(newValue, node.Rotation.y, node.Rotation.z)));
            x += axisLabelWidth + perField + Padding;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "Y", ref _rotYBuffer, node.Rotation.y,
                          newValue => ApplyNodeRotationEdit(mod, _selectedEntityId, node, new Vector3(node.Rotation.x, newValue, node.Rotation.z)));
            x += axisLabelWidth + perField + Padding;

            DrawAxisField(new Rect(x, y, axisLabelWidth, rowHeight),
                          new Rect(x + axisLabelWidth, y, perField, rowHeight),
                          "Z", ref _rotZBuffer, node.Rotation.z,
                          newValue => ApplyNodeRotationEdit(mod, _selectedEntityId, node, new Vector3(node.Rotation.x, node.Rotation.y, newValue)));
        }

        private void DrawGroupRow(int row, float rowHeight, float labelWidth, float totalWidth,
                                  FuseLoadedMod mod, FuseNode node)
        {
            var y = row * rowHeight;
            GUI.Label(new Rect(0f, y, labelWidth, rowHeight),
                      "  " + FuseEditorStrings.Get("fuse.editor.properties.group"),
                      _propertyLabelStyle);

            var fieldRect = new Rect(labelWidth, y, totalWidth - labelWidth - Padding, rowHeight);
            var newBuffer = GUI.TextField(fieldRect, _groupBuffer ?? string.Empty);
            if (!string.Equals(newBuffer, _groupBuffer, StringComparison.Ordinal))
            {
                _groupBuffer = newBuffer;
                ApplyNodeGroupEdit(mod, _selectedEntityId, node, newBuffer);
            }
        }

        private void DrawTagsRow(int row, float rowHeight, float labelWidth, float totalWidth,
                                 FuseLoadedMod mod, FuseNode node)
        {
            var y = row * rowHeight;
            GUI.Label(new Rect(0f, y, labelWidth, rowHeight),
                      "  " + FuseEditorStrings.Get("fuse.editor.properties.tags"),
                      _propertyLabelStyle);

            var fieldRect = new Rect(labelWidth, y, totalWidth - labelWidth - Padding, rowHeight);
            var newBuffer = GUI.TextField(fieldRect, _tagsBuffer ?? string.Empty);
            if (!string.Equals(newBuffer, _tagsBuffer, StringComparison.Ordinal))
            {
                _tagsBuffer = newBuffer;
                ApplyNodeTagsEdit(mod, _selectedEntityId, node, FuseEditorFieldHelper.ParseTags(newBuffer));
            }
        }

        /// <summary>
        /// Commits a Group edit. Empty / whitespace input clears the
        /// group (stored as null) so the user can detach a node from
        /// its group by emptying the field.
        /// </summary>
        private static void ApplyNodeGroupEdit(FuseLoadedMod mod, string nodeId, FuseNode node, string newGroup)
        {
            if (mod == null || node == null || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            node.GroupId = string.IsNullOrWhiteSpace(newGroup) ? null : newGroup.Trim();
            PersistNodeEdit(mod, nodeId, node, "group edited via FUSE editor properties panel");
        }

        /// <summary>
        /// Commits a Tags edit. The Tags array is replaced atomically
        /// with whatever parsed out of the buffer; empty input clears
        /// the array (empty rather than null so consumers can iterate
        /// without a guard).
        /// </summary>
        private static void ApplyNodeTagsEdit(FuseLoadedMod mod, string nodeId, FuseNode node, string[] newTags)
        {
            if (mod == null || node == null || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            node.Tags = newTags ?? System.Array.Empty<string>();
            PersistNodeEdit(mod, nodeId, node, "tags edited via FUSE editor properties panel");
        }

        /// <summary>
        /// Shared persistence helper for non-spatial node edits (Group,
        /// Tags). Spatial edits (Position, Rotation) have their own
        /// helpers that ALSO mirror the live <c>TrackNode.transform</c>;
        /// non-spatial edits don't need that mirror.
        /// </summary>
        private static void PersistNodeEdit(FuseLoadedMod mod, string nodeId, FuseNode node, string reason)
        {
            try
            {
                if (FUSE.Authoring.Entities.FuseAuthoringPersistenceService.SaveDefinitionObject(
                        packageId: mod.Definition.Id,
                        kind: "node",
                        objectId: nodeId,
                        definition: node,
                        reason: reason))
                {
                    FuseEditorSaveTracker.MarkSaved();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to persist edit for node '{nodeId}' (reason: {reason}).", ex);
            }
        }

        /// <summary>
        /// Counterpart to <see cref="ApplyNodePositionEdit"/>: mutates
        /// the in-memory <see cref="FuseNode"/> rotation, mirrors onto
        /// the live <c>TrackNode</c> via <c>Quaternion.Euler</c>, and
        /// persists to disk. Euler degrees are stored as a Vector3 on
        /// FuseNode so the field cell maps 1:1 onto a degree value.
        /// </summary>
        private static void ApplyNodeRotationEdit(FuseLoadedMod mod, string nodeId, FuseNode node, Vector3 newEulerDegrees)
        {
            if (mod == null || node == null || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            node.Rotation = newEulerDegrees;

            try
            {
                var trackNode = global::Track.Graph.Shared?.GetNode(nodeId);
                if (trackNode != null)
                {
                    trackNode.transform.rotation = Quaternion.Euler(newEulerDegrees);
                    global::Track.Graph.Shared.OnNodeDidChange(trackNode);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to mirror rotation edit to live TrackNode '{nodeId}'.", ex);
            }

            try
            {
                if (FUSE.Authoring.Entities.FuseAuthoringPersistenceService.SaveDefinitionObject(
                        packageId: mod.Definition.Id,
                        kind: "node",
                        objectId: nodeId,
                        definition: node,
                        reason: "rotation edited via FUSE editor properties panel"))
                {
                    FuseEditorSaveTracker.MarkSaved();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to persist rotation edit for node '{nodeId}'.", ex);
            }
        }

        /// <summary>
        /// Persists an edit to a node's Position: mutates the in-memory
        /// <see cref="FuseNode"/>, syncs the live <c>TrackNode</c> in the
        /// scene if one exists, and writes the mod definition to disk
        /// via <see cref="Authoring.Entities.FuseAuthoringPersistenceService"/>.
        /// </summary>
        private static void ApplyNodePositionEdit(FuseLoadedMod mod, string nodeId, FuseNode node, Vector3 newPosition)
        {
            if (mod == null || node == null || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            node.Position = newPosition;

            // Mirror onto the live TrackNode so the user sees the marker
            // jump immediately. Marker may be null (no marker-tool active)
            // or the TrackNode may not be in Graph yet — skip silently in
            // those cases.
            try
            {
                var trackNode = global::Track.Graph.Shared?.GetNode(nodeId);
                if (trackNode != null)
                {
                    trackNode.transform.localPosition = newPosition;
                    global::Track.Graph.Shared.OnNodeDidChange(trackNode);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to mirror position edit to live TrackNode '{nodeId}'.", ex);
            }

            try
            {
                if (FUSE.Authoring.Entities.FuseAuthoringPersistenceService.SaveDefinitionObject(
                        packageId: mod.Definition.Id,
                        kind: "node",
                        objectId: nodeId,
                        definition: node,
                        reason: "position edited via FUSE editor properties panel"))
                {
                    FuseEditorSaveTracker.MarkSaved();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to persist position edit for node '{nodeId}'.", ex);
            }
        }

        private void DrawBottomBar(Rect screen)
        {
            var rect = new Rect(0f, screen.height - BottomBarHeight, screen.width, BottomBarHeight);
            GUI.Box(rect, GUIContent.none, _statusBarStyle);

            var snapshot = GetCurrentDefinitionSnapshot();
            var totalEntities = 0;
            foreach (var c in snapshot.Categories)
            {
                totalEntities += TotalEntities(c);
            }

            var activeModId = FuseEditor.Instance?.ActiveMod?.Definition?.Id ?? "(no mod selected)";
            var savedDisplay = FuseEditorSaveTracker.GetDisplayString();
            var labelRect = new Rect(Padding, rect.y, rect.width - Padding * 2, BottomBarHeight);
            GUI.Label(labelRect,
                $"  Editing: {activeModId}    •    Entities: {totalEntities}    •    Last saved: {savedDisplay}",
                _propertyLabelStyle);
        }

        private void DrawModBrowser(Rect screen)
        {
            // Center a fixed-width panel inside the content area. The
            // bookmark bar / status bar still render their full widths;
            // only the mod-picker UI is centered for legibility.
            const float browserWidth = 760f;
            const float browserHeight = 440f;
            var contentTop = ContentTop;
            var contentBottom = screen.height - BottomBarHeight;
            var contentHeight = contentBottom - contentTop;

            var rect = new Rect(
                (screen.width - browserWidth) * 0.5f,
                contentTop + Mathf.Max(0f, (contentHeight - browserHeight) * 0.5f),
                browserWidth,
                Mathf.Min(browserHeight, contentHeight - Padding * 2f));

            GUI.Box(rect, GUIContent.none, _panelStyle);

            var headerRect = new Rect(rect.x + Padding * 2f, rect.y + Padding, rect.width - Padding * 4f, 24f);
            GUI.Label(headerRect, FuseEditorStrings.Get("fuse.editor.modbrowser.title"), _categoryHeaderStyle);

            var subtitleRect = new Rect(rect.x + Padding * 2f, headerRect.yMax, rect.width - Padding * 4f, 20f);
            GUI.Label(subtitleRect, FuseEditorStrings.Get("fuse.editor.modbrowser.subtitle"), _propertyLabelStyle);

            // Tab strip.
            var tabsY = subtitleRect.yMax + Padding;
            const float tabWidth = 140f;
            const float tabHeight = 24f;
            string[] tabKeys =
            {
                "fuse.editor.modbrowser.tab.existing",
                "fuse.editor.modbrowser.tab.new",
                "fuse.editor.modbrowser.tab.legacy",
            };
            for (int i = 0; i < tabKeys.Length; i++)
            {
                var tabRect = new Rect(rect.x + Padding * 2f + i * (tabWidth + Padding), tabsY, tabWidth, tabHeight);
                var isActive = _modBrowserTab == i;
                var style = isActive ? _toolButtonActiveStyle : _toolButtonStyle;
                if (FuseEditorUiHelper.Button(tabRect, tabKeys[i], style) && !isActive)
                {
                    _modBrowserTab = i;
                    if (i == 2)
                    {
                        // Refresh the legacy catalog when the tab opens
                        // so the user always sees the current state of
                        // their Mods/ folder.
                        RefreshLegacyCatalog();
                    }
                }
            }

            var bodyRect = new Rect(
                rect.x + Padding * 2f,
                tabsY + tabHeight + Padding,
                rect.width - Padding * 4f,
                rect.yMax - tabsY - tabHeight - Padding * 3f);

            switch (_modBrowserTab)
            {
                case 0: DrawModBrowserExisting(bodyRect); break;
                case 1: DrawModBrowserNew(bodyRect); break;
                case 2: DrawModBrowserLegacy(bodyRect); break;
            }
        }

        private void DrawModBrowserExisting(Rect rect)
        {
            var loaded = FuseModLoader.GetLoadedModsInOrder();
            if (loaded == null || loaded.Count == 0)
            {
                GUI.Label(rect, "  " + FuseEditorStrings.Get("fuse.editor.modbrowser.existing.empty"), _propertyLabelStyle);
                return;
            }

            const float rowHeight = 28f;
            var viewRect = new Rect(0f, 0f, rect.width - 16f, loaded.Count * rowHeight + Padding);
            _modBrowserScroll = GUI.BeginScrollView(rect, _modBrowserScroll, viewRect);

            const float editButtonWidth = 80f;
            for (int i = 0; i < loaded.Count; i++)
            {
                var mod = loaded[i];
                var rowY = i * rowHeight;
                var info = $"  {mod.Definition?.Name ?? mod.Definition?.Id}  •  {mod.Definition?.Author ?? "(unknown)"}  •  v{mod.Definition?.ModVersion ?? "?"}";
                GUI.Label(new Rect(0f, rowY, viewRect.width - editButtonWidth - Padding, rowHeight), info, _propertyLabelStyle);

                if (FuseEditorUiHelper.Button(
                        new Rect(viewRect.width - editButtonWidth, rowY + (rowHeight - 22f) * 0.5f, editButtonWidth, 22f),
                        "fuse.editor.modbrowser.existing.edit",
                        _toolButtonStyle))
                {
                    FuseEditor.Instance?.SetActiveMod(mod);
                    _newModStatusMessage = null;
                }
            }

            GUI.EndScrollView();
        }

        private void DrawModBrowserNew(Rect rect)
        {
            const float rowHeight = 28f;
            const float labelWidth = 120f;
            const float fieldHeight = 22f;

            var y = rect.y;

            DrawModBrowserField(rect, ref y, rowHeight, labelWidth, fieldHeight,
                                "fuse.editor.modbrowser.new.id", ref _newModIdBuffer);
            DrawModBrowserField(rect, ref y, rowHeight, labelWidth, fieldHeight,
                                "fuse.editor.modbrowser.new.name", ref _newModNameBuffer);
            DrawModBrowserField(rect, ref y, rowHeight, labelWidth, fieldHeight,
                                "fuse.editor.modbrowser.new.author", ref _newModAuthorBuffer);

            y += Padding;

            var createRect = new Rect(rect.x, y, 160f, 26f);
            if (FuseEditorUiHelper.Button(createRect, "fuse.editor.modbrowser.new.create", _toolButtonStyle))
            {
                TryCreateNewMod();
            }

            y += createRect.height + Padding;

            if (!string.IsNullOrEmpty(_newModStatusMessage))
            {
                var msgRect = new Rect(rect.x, y, rect.width, rowHeight);
                GUI.Label(msgRect, "  " + _newModStatusMessage, _propertyLabelStyle);
            }
        }

        private void DrawModBrowserField(Rect parent, ref float y, float rowHeight, float labelWidth,
                                         float fieldHeight, string labelKey, ref string buffer)
        {
            var label = FuseEditorUiHelper.TranslateLabel(labelKey);
            GUI.Label(new Rect(parent.x, y + (rowHeight - fieldHeight) * 0.5f, labelWidth, fieldHeight),
                      new GUIContent("  " + label.Title, label.Description), _propertyLabelStyle);

            buffer = GUI.TextField(
                new Rect(parent.x + labelWidth, y + (rowHeight - fieldHeight) * 0.5f,
                         parent.width - labelWidth - Padding, fieldHeight),
                buffer ?? string.Empty);

            y += rowHeight;
        }

        private void TryCreateNewMod()
        {
            var modsRoot = ResolveModsRootPath();
            if (string.IsNullOrEmpty(modsRoot))
            {
                _newModStatusMessage = "Could not resolve the mods root folder — try restarting Railroader.";
                return;
            }

            var path = FuseEditorModCatalog.CreateNewMod(modsRoot, _newModIdBuffer, _newModNameBuffer, _newModAuthorBuffer);
            if (path == null)
            {
                _newModStatusMessage = "Could not create the mod (id may already exist, be empty, or contain only invalid characters).";
                return;
            }

            _newModStatusMessage = $"Created mod at '{path}'. Restart Railroader and the FUSE editor to load it.";
            _newModIdBuffer = _newModNameBuffer = _newModAuthorBuffer = string.Empty;
        }

        private void DrawModBrowserLegacy(Rect rect)
        {
            if (_legacyCatalogCache == null)
            {
                RefreshLegacyCatalog();
            }

            var entries = _legacyCatalogCache;
            if (entries == null || entries.Count == 0)
            {
                GUI.Label(rect, "  " + FuseEditorStrings.Get("fuse.editor.modbrowser.legacy.empty"), _propertyLabelStyle);
                return;
            }

            const float rowHeight = 28f;
            var viewRect = new Rect(0f, 0f, rect.width - 16f, entries.Count * rowHeight + Padding);
            _modBrowserScroll = GUI.BeginScrollView(rect, _modBrowserScroll, viewRect);

            const float convertButtonWidth = 110f;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rowY = i * rowHeight;
                var kindBadge = entry.Kind == FuseEditorModKind.LegacyRailLoader ? "Railloader" : "MapMod";
                var info = $"  [{kindBadge}]  {entry.DisplayName ?? entry.Id}  •  v{entry.Version ?? "?"}";
                GUI.Label(new Rect(0f, rowY, viewRect.width - convertButtonWidth - Padding, rowHeight),
                          info, _propertyLabelStyle);

                var buttonRect = new Rect(viewRect.width - convertButtonWidth, rowY + (rowHeight - 22f) * 0.5f, convertButtonWidth, 22f);
                if (FuseEditorUiHelper.Button(buttonRect, "fuse.editor.modbrowser.legacy.convert", _toolButtonStyle))
                {
                    TryConvertLegacyMod(entry);
                }
            }

            GUI.EndScrollView();
        }

        private void TryConvertLegacyMod(FuseEditorModEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.FolderPath))
            {
                return;
            }

            // Default output: sibling folder with ".FUSE" suffix so the
            // converted mod sits next to the legacy original in the
            // Mods/ tree. Matches the Python converter's convention.
            var outputFolder = entry.FolderPath + ".FUSE";

            try
            {
                var result = global::FUSE.Converter.FuseLegacyConverter.ConvertMod(entry.FolderPath, outputFolder);
                if (result.Success)
                {
                    var counts = SummariseFragmentCounts(result);
                    _newModStatusMessage =
                        $"Converted '{entry.DisplayName ?? entry.Id}' to '{outputFolder}'  •  {result.WrittenFragments.Count} fragment(s){counts}." +
                        " Restart Railroader to load the new FUSE mod.";
                    FuseLog.Info(_newModStatusMessage);

                    // Surface the most severe report entry so the user
                    // sees if anything notable happened during conversion
                    // (e.g. spans/areas were skipped pending those
                    // converter passes).
                    AppendReportSummary(result);

                    // Refresh so the new .FUSE folder shows up if the
                    // user switches to the Legacy tab again, and so the
                    // legacy entry is re-classified (it stays Legacy —
                    // but the catalog rescans the directory).
                    RefreshLegacyCatalog();
                }
                else
                {
                    var firstError = result.Report.Find(e => e.Level == global::FUSE.Converter.Models.FuseConversionReportLevel.Error);
                    _newModStatusMessage = firstError != null
                        ? $"Conversion failed: {firstError.Message}"
                        : "Conversion failed. Check the FUSE log for details.";
                    FuseLog.Warning(_newModStatusMessage);
                }

                // Surface the status on the Create-New tab too so the
                // user sees it regardless of which tab they're on.
                _modBrowserTab = 1;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor: failed to convert legacy mod '{entry.FolderPath}'.", ex);
                _newModStatusMessage = $"Conversion threw: {ex.Message}";
                _modBrowserTab = 1;
            }
        }

        private static string SummariseFragmentCounts(global::FUSE.Converter.Models.FuseConversionResult result)
        {
            if (result.FragmentCounts == null || result.FragmentCounts.Count == 0)
            {
                return string.Empty;
            }

            var totalNodes = 0;
            var totalSegments = 0;
            foreach (var counts in result.FragmentCounts.Values)
            {
                counts.TryGetValue("nodes", out var n);
                counts.TryGetValue("segments", out var s);
                totalNodes += n;
                totalSegments += s;
            }

            if (totalNodes == 0 && totalSegments == 0)
            {
                return string.Empty;
            }

            return $"  •  {totalNodes} node(s), {totalSegments} segment(s)";
        }

        private void AppendReportSummary(global::FUSE.Converter.Models.FuseConversionResult result)
        {
            if (result.Report == null || result.Report.Count == 0)
            {
                return;
            }

            var warnings = 0;
            string firstSignificant = null;
            foreach (var entry in result.Report)
            {
                if (entry.Level == global::FUSE.Converter.Models.FuseConversionReportLevel.Warning ||
                    entry.Level == global::FUSE.Converter.Models.FuseConversionReportLevel.Info)
                {
                    warnings++;
                    if (firstSignificant == null)
                    {
                        firstSignificant = entry.Message;
                    }
                }
            }

            if (warnings > 0 && !string.IsNullOrEmpty(firstSignificant))
            {
                _newModStatusMessage += $"\n({warnings} note(s): {firstSignificant})";
            }
        }

        private void RefreshLegacyCatalog()
        {
            var modsRoot = ResolveModsRootPath();
            if (string.IsNullOrEmpty(modsRoot))
            {
                _legacyCatalogCache = new List<FuseEditorModEntry>();
                return;
            }

            var all = FuseEditorModCatalog.EnumerateAll(modsRoot);
            var legacy = new List<FuseEditorModEntry>(all.Count);
            foreach (var entry in all)
            {
                if (entry.Kind == FuseEditorModKind.LegacyRailLoader ||
                    entry.Kind == FuseEditorModKind.LegacyMapMod)
                {
                    legacy.Add(entry);
                }
            }
            _legacyCatalogCache = legacy;
            _legacyCatalogRefreshedFrame = Time.frameCount;
        }

        private static string ResolveModsRootPath()
        {
            // Prefer an already-loaded FUSE mod's parent (most reliable
            // signal of where the user's Mods/ folder actually lives,
            // including non-default install paths). Fall back to
            // UnityModManager.modsPath when no FUSE mod has loaded yet.
            var loaded = FuseModLoader.GetLoadedModsInOrder();
            if (loaded != null && loaded.Count > 0 && !string.IsNullOrEmpty(loaded[0].FolderPath))
            {
                var parent = System.IO.Path.GetDirectoryName(loaded[0].FolderPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    return parent;
                }
            }

            try
            {
                return UnityModManager.modsPath;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE editor mod browser: failed to resolve UnityModManager.modsPath.", ex);
                return null;
            }
        }

        private static CategoryTreeSnapshot GetCurrentDefinitionSnapshot()
        {
            // Pull the active mod's authoring layer if one is selected;
            // otherwise return a representative mock so the EDEN structure
            // is visible even before mod selection lands.
            var mod = FuseEditor.Instance?.ActiveMod;
            if (mod?.Definition != null)
            {
                return BuildSnapshot(mod);
            }

            return MockSnapshot();
        }

        private static CategoryTreeSnapshot BuildSnapshot(FuseLoadedMod mod)
        {
            var def = mod.Definition;
            return new CategoryTreeSnapshot
            {
                Categories = new List<CategorySnapshot>
                {
                    new CategorySnapshot
                    {
                        Name = "Tracks",
                        Buckets = new List<BucketSnapshot>
                        {
                            BucketFromDict("Nodes",    def.Tracks?.Nodes),
                            BucketFromDict("Segments", def.Tracks?.Segments),
                            BucketFromDict("Spans",    def.Tracks?.Spans),
                            BucketFromDict("Areas",    def.Tracks?.Areas),
                        }
                    },
                    new CategorySnapshot
                    {
                        Name = "World",
                        Buckets = new List<BucketSnapshot>
                        {
                            BucketFromDict("Scenery",      def.World?.Scenery),
                            BucketFromDict("Splineys",     def.World?.Splineys),
                            BucketFromDict("MapLabels",    def.World?.MapLabels),
                            BucketFromDict("Telegraphs",   def.World?.TelegraphPoles),
                        }
                    },
                    new CategorySnapshot
                    {
                        Name = "Operations",
                        Buckets = new List<BucketSnapshot>
                        {
                            BucketFromDict("Industries", def.Operations?.Industries),
                            BucketFromDict("Loads",      def.Operations?.Loads),
                            BucketFromDict("Stations",   def.Operations?.Stations),
                            BucketFromDict("Turntables", def.Operations?.Turntables),
                        }
                    },
                }
            };
        }

        private static BucketSnapshot BucketFromDict<TValue>(string name, IDictionary<string, TValue> dict)
        {
            var ids = new List<string>();
            if (dict != null)
            {
                foreach (var key in dict.Keys)
                {
                    ids.Add(key);
                }
                ids.Sort(StringComparer.Ordinal);
            }
            return new BucketSnapshot { Name = name, EntityIds = ids };
        }

        private static CategoryTreeSnapshot MockSnapshot()
        {
            // Mock data shown when no mod is selected — communicates the
            // layout via realistic-looking ids so the user reading the
            // mockup understands what each bucket will hold.
            return new CategoryTreeSnapshot
            {
                Categories = new List<CategorySnapshot>
                {
                    new CategorySnapshot
                    {
                        Name = "Tracks",
                        Buckets = new List<BucketSnapshot>
                        {
                            new BucketSnapshot { Name = "Nodes",    EntityIds = new List<string> { "node-A0", "node-A1", "node-A2" } },
                            new BucketSnapshot { Name = "Segments", EntityIds = new List<string> { "seg-main-01", "seg-main-02" } },
                            new BucketSnapshot { Name = "Spans",    EntityIds = new List<string>() },
                            new BucketSnapshot { Name = "Areas",    EntityIds = new List<string> { "area-yard" } },
                        }
                    },
                    new CategorySnapshot
                    {
                        Name = "World",
                        Buckets = new List<BucketSnapshot>
                        {
                            new BucketSnapshot { Name = "Scenery",    EntityIds = new List<string> { "barn-01" } },
                            new BucketSnapshot { Name = "Splineys",   EntityIds = new List<string>() },
                            new BucketSnapshot { Name = "MapLabels",  EntityIds = new List<string> { "yard-label" } },
                            new BucketSnapshot { Name = "Telegraphs", EntityIds = new List<string>() },
                        }
                    },
                    new CategorySnapshot
                    {
                        Name = "Operations",
                        Buckets = new List<BucketSnapshot>
                        {
                            new BucketSnapshot { Name = "Industries", EntityIds = new List<string> { "sawmill-1" } },
                            new BucketSnapshot { Name = "Loads",      EntityIds = new List<string>() },
                            new BucketSnapshot { Name = "Stations",   EntityIds = new List<string>() },
                            new BucketSnapshot { Name = "Turntables", EntityIds = new List<string>() },
                        }
                    },
                }
            };
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _topBarStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = SolidTexture(new Color(0.10f, 0.11f, 0.13f, 0.95f)) },
                border = new RectOffset(0, 0, 0, 0),
            };

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = SolidTexture(new Color(0.13f, 0.14f, 0.16f, 0.95f)) },
                border = new RectOffset(0, 0, 0, 0),
            };

            _categoryHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.86f, 0.78f, 0.50f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _entityRowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.88f, 0.88f, 0.88f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _entityRowSelectedStyle = new GUIStyle(_entityRowStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.96f, 0.82f, 0.36f), background = SolidTexture(new Color(0.20f, 0.21f, 0.24f, 1f)) },
            };

            _viewportStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = SolidTexture(new Color(0.10f, 0.11f, 0.13f, 0.75f)), textColor = new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic,
                fontSize = 12,
            };

            _statusBarStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = SolidTexture(new Color(0.08f, 0.09f, 0.10f, 0.95f)) },
                border = new RectOffset(0, 0, 0, 0),
            };

            _propertyLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _propertyValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.92f, 0.92f, 0.92f) },
                alignment = TextAnchor.MiddleLeft,
            };

            _toolButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
            };

            // Active tool gets a brighter, warmer background so the
            // current mode is obvious without reading the label.
            _toolButtonActiveStyle = new GUIStyle(_toolButtonStyle)
            {
                normal = { background = SolidTexture(new Color(0.55f, 0.40f, 0.18f, 1f)), textColor = new Color(1f, 0.95f, 0.82f) },
                hover = { background = SolidTexture(new Color(0.62f, 0.46f, 0.22f, 1f)), textColor = new Color(1f, 0.97f, 0.86f) },
                active = { background = SolidTexture(new Color(0.50f, 0.37f, 0.16f, 1f)), textColor = new Color(1f, 0.93f, 0.78f) },
            };

            // Windows-toggle popup uses borderless rows so the checkmark
            // column reads as the only state cue. Selected rows are
            // brighter + bold; the row hover state comes from the
            // underlying skin button background.
            _windowsPopupRowStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.88f, 0.88f, 0.88f), background = null },
                hover = { background = SolidTexture(new Color(0.20f, 0.21f, 0.24f, 1f)) },
                active = { background = SolidTexture(new Color(0.20f, 0.21f, 0.24f, 1f)) },
                border = new RectOffset(0, 0, 0, 0),
            };

            _windowsPopupRowSelectedStyle = new GUIStyle(_windowsPopupRowStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.96f, 0.82f, 0.36f) },
            };

            _bookmarkBarStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = SolidTexture(new Color(0.09f, 0.10f, 0.12f, 0.95f)) },
                border = new RectOffset(0, 0, 0, 0),
            };

            _bookmarkTabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 2, 2),
            };

            _bookmarkTabActiveStyle = new GUIStyle(_bookmarkTabStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = SolidTexture(new Color(0.55f, 0.40f, 0.18f, 1f)), textColor = new Color(1f, 0.95f, 0.82f) },
                hover = { background = SolidTexture(new Color(0.62f, 0.46f, 0.22f, 1f)), textColor = new Color(1f, 0.97f, 0.86f) },
                active = { background = SolidTexture(new Color(0.50f, 0.37f, 0.16f, 1f)), textColor = new Color(1f, 0.93f, 0.78f) },
            };

            _stylesInitialized = true;
        }

        private static Texture2D SolidTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private sealed class CategoryTreeSnapshot
        {
            public List<CategorySnapshot> Categories;
        }

        private sealed class CategorySnapshot
        {
            public string Name;
            public List<BucketSnapshot> Buckets;
        }

        private sealed class BucketSnapshot
        {
            public string Name;
            public List<string> EntityIds;
        }
    }
}
