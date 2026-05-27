using System;
using FUSE.Editor.Screen.UI;
using FUSE.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// New-node placement tool. While active, a viewport click outside
    /// any IMGUI panel raycasts from the camera and spawns a TrackNode +
    /// FuseNode authoring entry at the hit point. Markers stay visible
    /// so the user can see what's already placed without leaving the
    /// tool.
    /// </summary>
    /// <remarks>
    /// Plain class (not a MonoBehaviour) so tool lifetime tracks the
    /// registry rather than a backing GameObject. Per-frame input
    /// detection runs through <see cref="Tick"/> which
    /// <see cref="FuseEditor"/>'s Update calls on the active tool.
    /// Node ids are auto-generated with a stable
    /// <c>fuse-node-&lt;timestamp&gt;-&lt;short-guid&gt;</c> prefix so
    /// creations from the editor never collide with each other or with
    /// mod-supplied ids.
    /// </remarks>
    internal sealed class FusePlaceTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.place";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.place";
        public string IconGlyph => "✚";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        public void OnActivate()
        {
            FuseNodeEditorController.ShowMarkersForActiveMod();
        }

        public void OnDeactivate()
        {
            FuseNodeEditorController.DeselectCurrent();
            FuseNodeEditorController.ClearMarkers();
        }

        public void OnNodeSelected(FuseNodeMarker marker)
        {
            // Placement doesn't engage on selection — the user is
            // placing new nodes, not editing existing ones.
        }

        public void Tick()
        {
            // FUSE uses Unity.InputSystem rather than the legacy Input
            // static class — Mouse.current is the new-input-system
            // equivalent of Input.GetMouseButtonDown(0).
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // Don't fire when the click landed on a Unity-UI overlay.
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject())
            {
                return;
            }

            // GUIUtility.hotControl != 0 means an IMGUI control is
            // currently capturing input this frame — Tick is called from
            // a MonoBehaviour Update, but pure Unity-UI hit-tests miss
            // OnGUI surfaces. Combining both gates keeps clicks on the
            // FuseEditorScreen panels from spawning ghost nodes.
            if (GUIUtility.hotControl != 0)
            {
                return;
            }

            var id = GenerateNodeId();
            if (!FuseNodeEditorController.TryCreateNodeAtCameraRaycast(id, out var error))
            {
                FuseLog.Info($"FUSE place tool: {error ?? "node creation failed"}");
            }
        }

        private static string GenerateNodeId()
        {
            var stamp = DateTime.UtcNow.ToString("yyMMddHHmmss");
            var slug = Guid.NewGuid().ToString("N").Substring(0, 4);
            return $"fuse-node-{stamp}-{slug}";
        }
    }
}
