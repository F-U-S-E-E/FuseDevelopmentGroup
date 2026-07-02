using FUSE.Editor.Overlays;
using FUSE.Editor.Screen.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FUSE.Editor.Track.Tools
{
    /// <summary>
    /// The idle / inspection tool. Turns on the FUSE entity markers for
    /// the active mod and lets the user click them to surface their
    /// metadata in the Properties panel. No gizmo, no scene mutation.
    /// </summary>
    internal sealed class FuseSelectTool : IFuseEditorTool
    {
        public const string ToolId = "fuse.editor.tool.select";

        public string Id => ToolId;
        public string LabelKey => "fuse.editor.tool.select";
        public string IconGlyph => "▢";
        public bool IsAvailable => true;
        public string UnavailableReason => null;

        GUIStyle _style;

        GUIStyle SelectionRectStyle { get {
                if (_style == null)
                {
                    _style = new GUIStyle(GUI.skin.box);

                    _style.normal.background = CreateBorderTexture(Color.white);
                    _style.border = new RectOffset(2, 2, 2, 2);
                    _style.padding = new RectOffset(4, 4, 4, 4);
                }
                return _style;
            }}

        public Vector2 StartPosition = Vector2.zero;

        public Rect currentRect = Rect.zero;

        public void OnActivate()
        {
            
        }

        public void OnDeactivate()
        {
            
        }

        public void Tick() {
            Vector2 mousePos = Mouse.current.position.ReadValue();

            FuseOverlayManager.Instance.SelectionSystem.UpdateHoverFromMouse(mousePos);

            // Cleanup stale previews
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                FuseOverlayManager.Instance.TrySelectPreviewAtMouse(mousePos);
            }

            if (Keyboard.current.altKey.wasPressedThisFrame)
            {
                StartPosition = Mouse.current.position.ReadValue();
            }
            if (StartPosition != Vector2.zero)
            {
                Vector2 endPosition = Mouse.current.position.ReadValue();
                currentRect = FromDragPoints(StartPosition, endPosition);

            }

            if (Keyboard.current.altKey.wasReleasedThisFrame && currentRect != Rect.zero)
            {
                FuseOverlayManager.Instance.TrySelectPreviewsInRectangle(currentRect);

                StartPosition = Vector2.zero;
                currentRect = Rect.zero;
            }
        }

        public void Draw()
        {
            if (StartPosition != Vector2.zero)
            {
                // Draw border using GUI.Box with a transparent style
                // Need to flip Y coordinate because Mouse position uses bottom-left origin,
                // but IMGUI uses top-left origin
                Rect guiRect = new Rect(
                    currentRect.x,
                    UnityEngine.Device.Screen.height - currentRect.y - currentRect.height,
                    currentRect.width,
                    currentRect.height
                );

                GUI.color = new Color(1, 1, 1, 0.7f);
                GUI.Box(guiRect, "", SelectionRectStyle);
                GUI.color = Color.white;
            }
        }

        Rect FromDragPoints(Vector2 P1, Vector2 P2)
        {
            Vector2 D = P1 - P2;
            Rect R = new Rect();
            if (D.x < 0)
                R.x = P1.x;
            else
                R.x = P2.x;
            if (D.y < 0)
                R.y = P1.y;
            else
                R.y = P2.y;
            R.width = Mathf.Abs(D.x);
            R.height = Mathf.Abs(D.y);
            return R;
        }

        private static Texture2D CreateBorderTexture(Color borderColor)
        {
            Texture2D tex = new Texture2D(3, 3, TextureFormat.RGBA32, false);

            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    bool border = x == 0 || x == 2 || y == 0 || y == 2;
                    tex.SetPixel(x, y, border ? borderColor : new Color(0, 0, 0, 0));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            return tex;
        }
    }
}
