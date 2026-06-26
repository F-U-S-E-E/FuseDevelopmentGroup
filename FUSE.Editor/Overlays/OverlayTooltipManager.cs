using FUSE.Infrastructure;
using UnityEngine;
using UnityEngine.InputSystem;
using EditorHandlerBase = FUSE.Editor.EditorHandler.EditorHandlerBase;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Manages IMGUI tooltips for hovered overlay previews.
    /// Displays handler tooltips using OnGUI when hovering over previews.
    /// </summary>
    public class OverlayTooltipManager : MonoBehaviour
    {
        private OverlaySelectionSystem _selectionSystem;
        private GUIStyle _tooltipStyle;
        private float _tooltipHoverTime = 0.5f; // Delay before showing tooltip
        private float _hoverTimer = 0f;
        private EditorHandlerBase _lastHoveredHandler;
        private Rect _lastMouseRect;

        /// <summary>
        /// Initializes the tooltip manager with a selection system.
        /// </summary>
        public void Initialize(OverlaySelectionSystem selectionSystem)
        {
            _selectionSystem = selectionSystem;
            if (_selectionSystem != null)
            {
                _selectionSystem.OnPreviewHovered += ResetTooltipTimer;
                _selectionSystem.OnPreviewUnhovered += ClearTooltip;
            }
        }

        private void InitializeStyle()
        {
            if (_tooltipStyle != null)
            {
                return; // Already initialized
            }

            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4),
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal =
                {
                    textColor = Color.white,
                    background = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.95f))
                }
            };
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void ResetTooltipTimer(string previewId, OverlaySelectionArea area)
        {
            _hoverTimer = 0f;
        }

        private void ClearTooltip()
        {
            _hoverTimer = 0f;
            _lastHoveredHandler = null;
        }

        private void Update()
        {
            if (_selectionSystem == null)
            {
                return;
            }

            var hoveredHandler = _selectionSystem.GetHoveredHandler();

            if (hoveredHandler != null)
            {
                // Increment timer while hovering
                if (_lastHoveredHandler == hoveredHandler)
                {
                    _hoverTimer += Time.deltaTime;
                }
                else
                {
                    // Handler changed, reset timer
                    _hoverTimer = 0f;
                    _lastHoveredHandler = hoveredHandler;
                }
            }
            else
            {
                _hoverTimer = 0f;
                _lastHoveredHandler = null;
            }
        }

        private void OnGUI()
        {
            if (_selectionSystem == null)
            {
                return;
            }

            // Lazy initialize style on first OnGUI call
            InitializeStyle();

            // Check if enough time has passed to show tooltip
            if (_hoverTimer < _tooltipHoverTime)
            {
                return;
            }

            var hoveredHandler = _selectionSystem.GetHoveredHandler();
            if (hoveredHandler == null)
            {
                return;
            }

            try
            {
                string tooltipText = hoveredHandler.GetTooltip();
                if (string.IsNullOrEmpty(tooltipText))
                {
                    return;
                }

                DrawTooltip(tooltipText);
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"OverlayTooltipManager: Error drawing tooltip: {ex.Message}");
            }
        }

        private void DrawTooltip(string tooltipText)
        {
            // Get mouse position in GUI space
            Vector2 mousePos = Mouse.current.position.ReadValue();
            // Flip Y coordinate for GUI (GUI origin is top-left, but Input origin is bottom-left)
            mousePos.y = UnityEngine.Screen.height - mousePos.y;

            // Calculate tooltip size
            Vector2 tooltipSize = _tooltipStyle.CalcSize(new GUIContent(tooltipText));

            // Add padding for the box
            tooltipSize.x += 16; // Left + right padding
            tooltipSize.y += 8;  // Top + bottom padding

            // Create rect with offset from mouse
            Rect tooltipRect = new Rect(
                mousePos.x + 15, // Offset right from mouse
                mousePos.y + 15, // Offset down from mouse
                tooltipSize.x,
                tooltipSize.y
            );

            // Clamp to screen
            if (tooltipRect.xMax > UnityEngine.Screen.width)
            {
                tooltipRect.x = UnityEngine.Screen.width - tooltipRect.width - 10;
            }

            if (tooltipRect.yMax > UnityEngine.Screen.height)
            {
                tooltipRect.y = UnityEngine.Screen.height - tooltipRect.height - 10;
            }

            // Draw the tooltip box
            GUI.Box(tooltipRect, tooltipText, _tooltipStyle);
        }

        private void OnDestroy()
        {
            if (_selectionSystem != null)
            {
                _selectionSystem.OnPreviewHovered -= ResetTooltipTimer;
                _selectionSystem.OnPreviewUnhovered -= ClearTooltip;
            }
        }
    }
}
