using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Bottom-left world-orientation gizmo (also known as an
    /// "axis gizmo", "orientation gizmo", "navigation gizmo", or
    /// formally as a "gnomon" in CAD). Shows the three world basis
    /// vectors — X / Y / Z — as colored arms that rotate to match the
    /// current camera orientation, so the user always has a fixed
    /// reference for which way is which. Mirrors Arma 3 EDEN's
    /// bottom-left compass widget; Blender, Unity Editor, and Maya
    /// ship the same idea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a VIEW gizmo — pure orientation indicator, non-interactive
    /// in v1. Distinct from FUSE's existing TRANSFORM gizmo (the
    /// on-object handles attached to selections by the Move / Rotate /
    /// Scale tools via <see cref="FUSE.Editor.Track.FuseNodeMarker"/>
    /// + RLD's <c>ObjectTransformGizmo</c>). Same visual vocabulary
    /// — colored arrows for X/Y/Z — but very different roles.
    /// </para>
    /// <para>
    /// Rendered inside <c>FuseEditorScreen.OnGUI</c> after the bottom
    /// bar and before the tooltip pass. The UiScale matrix transform
    /// from the screen's OnGUI is in effect when this draws, so the
    /// widget scales with the rest of the chrome automatically.
    /// </para>
    /// </remarks>
    internal static class FuseEditorAxisGizmo
    {
        // Logical-pixel dimensions. The OnGUI UiScale matrix multiplies
        // these — 1.5× scale renders the widget at ~108 device pixels.
        private const float WidgetSize = 72f;
        private const float PaddingFromCorner = 16f;
        private const float LineThickness = 2f;

        // Arm radius when an axis is perpendicular to the camera (the
        // maximum on-screen length). Slightly less than half the widget
        // so the letter label has room to breathe at the tip.
        private const float ArmLength = WidgetSize * 0.5f - 10f;
        private const float LabelBoxSize = 14f;

        // Opacity for axes pointing INTO the scene (away from the
        // viewer's eye). Matches Blender's and EDEN's depth-cueing
        // convention — dim the "back" of each axis pair so the user
        // reads orientation at a glance.
        private const float DimAlpha = 0.45f;

        private static GUIStyle _labelStyle;

        /// <summary>
        /// Draws the gizmo anchored to the bottom-left of the supplied
        /// viewport rect. Bails silently when <see cref="Camera.main"/>
        /// is null (early load, scene transition) so the editor surface
        /// never flickers a half-drawn widget during boot.
        /// </summary>
        public static void Draw(Rect viewport)
        {
            var camera = Camera.main;
            if (camera == null) return;

            EnsureLabelStyle();

            // Anchor center 16px in from the bottom-left of the viewport.
            var center = new Vector2(
                viewport.x + PaddingFromCorner + WidgetSize * 0.5f,
                viewport.yMax - PaddingFromCorner - WidgetSize * 0.5f);

            // Project world basis vectors into camera-local space.
            // Unity's camera-local convention: +X right, +Y up, +Z
            // forward (the direction the camera looks). So the (x, y)
            // components of inv * worldAxis give the screen-direction,
            // and z carries depth (positive = into the scene).
            var inv = Quaternion.Inverse(camera.transform.rotation);
            var ax = inv * Vector3.right;     // World +X
            var ay = inv * Vector3.up;        // World +Y
            var az = inv * Vector3.forward;   // World +Z

            // Draw in z-sorted order so the axis closest to the viewer
            // paints last (on top). Smaller (more negative) z = closer
            // to the eye. Sort ascending by z means painting the
            // farthest first, the nearest last.
            DrawSortedAxes(center,
                new Axis(ax, FuseEditorTheme.Palette.AxisX, "fuse.editor.gizmo.axis_x"),
                new Axis(ay, FuseEditorTheme.Palette.AxisY, "fuse.editor.gizmo.axis_y"),
                new Axis(az, FuseEditorTheme.Palette.AxisZ, "fuse.editor.gizmo.axis_z"));
        }

        private readonly struct Axis
        {
            public readonly Vector3 CameraSpace;
            public readonly Color Color;
            public readonly string LabelKey;

            public Axis(Vector3 cameraSpace, Color color, string labelKey)
            {
                CameraSpace = cameraSpace;
                Color = color;
                LabelKey = labelKey;
            }
        }

        private static void DrawSortedAxes(Vector2 center, Axis a, Axis b, Axis c)
        {
            // Simple 3-element insertion sort by camera-z descending
            // (farthest first). 3 elements — branchless beats a loop.
            if (a.CameraSpace.z < b.CameraSpace.z) (a, b) = (b, a);
            if (b.CameraSpace.z < c.CameraSpace.z) (b, c) = (c, b);
            if (a.CameraSpace.z < b.CameraSpace.z) (a, b) = (b, a);

            DrawAxis(center, a);
            DrawAxis(center, b);
            DrawAxis(center, c);
        }

        private static void DrawAxis(Vector2 center, Axis axis)
        {
            // Depth cue: positive camera-z means the axis points INTO
            // the scene (away from the viewer's eye) — dim it. Matches
            // Blender's convention; EDEN does the same.
            var depth = axis.CameraSpace.z;
            var alpha = depth > 0f ? DimAlpha : 1f;

            // IMGUI Y grows downward; Unity screen Y grows upward.
            // Negate the y component so a world axis aligned with
            // world-up renders as an arrow pointing UP on screen.
            var dirX = axis.CameraSpace.x;
            var dirY = -axis.CameraSpace.y;

            // The arm's on-screen length scales with the projection's
            // 2D magnitude: an axis perpendicular to view reaches full
            // ArmLength, an axis parallel to view collapses to a point.
            var planar = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            var length = planar * ArmLength;
            var tip = center + new Vector2(dirX, dirY) * ArmLength;

            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            try
            {
                if (length > 0.5f)
                {
                    // Rotate around the widget center so the horizontal
                    // line we draw next renders along the projected
                    // direction. RotateAroundPivot composes with the
                    // outer UiScale matrix already in effect.
                    var angleDeg = Mathf.Atan2(dirY, dirX) * Mathf.Rad2Deg;
                    GUIUtility.RotateAroundPivot(angleDeg, center);

                    GUI.color = new Color(axis.Color.r, axis.Color.g, axis.Color.b, axis.Color.a * alpha);
                    var lineRect = new Rect(center.x, center.y - LineThickness * 0.5f,
                                              length, LineThickness);
                    GUI.DrawTexture(lineRect, FuseEditorTheme.SolidTexture(axis.Color));

                    // Drop the rotation before drawing the label so the
                    // letter stays screen-aligned (legible regardless
                    // of camera roll).
                    GUI.matrix = prevMatrix;
                }

                var translated = FuseEditorUiHelper.TranslateLabel(axis.LabelKey);
                var tooltip = translated.HasDescription ? translated.Description : translated.Title;
                var labelRect = new Rect(
                    tip.x - LabelBoxSize * 0.5f,
                    tip.y - LabelBoxSize * 0.5f,
                    LabelBoxSize, LabelBoxSize);

                // Letter renders in the axis color so the user
                // associates the letter with the arm; alpha mirrors
                // the depth cue.
                _labelStyle.normal.textColor = new Color(axis.Color.r, axis.Color.g, axis.Color.b, alpha);
                GUI.Label(labelRect, new GUIContent(translated.Title, tooltip), _labelStyle);
            }
            finally
            {
                GUI.matrix = prevMatrix;
                GUI.color = prevColor;
            }
        }

        private static void EnsureLabelStyle()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
