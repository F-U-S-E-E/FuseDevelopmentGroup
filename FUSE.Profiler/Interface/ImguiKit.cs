using UnityEngine;

namespace FUSE.Profiler.Interface
{
    /// <summary>
    /// Small IMGUI helpers: cached solid textures, styles, and a GL-based
    /// polyline for the graph panel. Everything is lazily created inside
    /// OnGUI (Unity requires GUI state to be touched only there).
    /// </summary>
    internal static class ImguiKit
    {
        private static Texture2D _solidDark;
        private static Texture2D _solidPanel;
        private static Texture2D _solidAccent;
        private static Material _lineMaterial;
        private static GUIStyle _header;
        private static GUIStyle _cell;
        private static GUIStyle _cellRight;
        private static GUIStyle _rowButton;
        private static bool _stylesReady;

        internal static readonly Color GraphColor = new Color(0.35f, 0.85f, 1f, 1f);
        internal static readonly Color GraphMaxColor = new Color(1f, 0.55f, 0.25f, 0.9f);

        internal static GUIStyle Header { get { EnsureStyles(); return _header; } }
        internal static GUIStyle Cell { get { EnsureStyles(); return _cell; } }
        internal static GUIStyle CellRight { get { EnsureStyles(); return _cellRight; } }
        internal static GUIStyle RowButton { get { EnsureStyles(); return _rowButton; } }

        internal static Texture2D SolidDark => _solidDark != null ? _solidDark : (_solidDark = MakeSolid(new Color(0.08f, 0.08f, 0.1f, 0.96f)));
        internal static Texture2D SolidPanel => _solidPanel != null ? _solidPanel : (_solidPanel = MakeSolid(new Color(0.16f, 0.16f, 0.2f, 1f)));
        internal static Texture2D SolidAccent => _solidAccent != null ? _solidAccent : (_solidAccent = MakeSolid(new Color(0.25f, 0.45f, 0.65f, 1f)));

        internal static void FillRect(Rect rect, Texture2D texture)
        {
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        }

        /// <summary>
        /// Draw a polyline through <paramref name="values"/> inside
        /// <paramref name="area"/> (GUI-space rect), scaled so
        /// <paramref name="maxValue"/> touches the top. Repaint-only; GL in
        /// screen space, so callers must not put it inside a scroll view.
        /// </summary>
        internal static void DrawPolyline(Rect area, double[] values, int count, double maxValue, Color color)
        {
            if (Event.current.type != EventType.Repaint || count < 2 || maxValue <= 0d)
            {
                return;
            }

            if (!EnsureLineMaterial())
            {
                return;
            }

            var topLeft = GUIUtility.GUIToScreenPoint(new Vector2(area.x, area.y));

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.LINE_STRIP);
            GL.Color(color);
            var stepX = area.width / (count - 1);
            for (var i = 0; i < count; i++)
            {
                var normalized = (float)(values[i] / maxValue);
                if (normalized > 1f)
                {
                    normalized = 1f;
                }

                var x = topLeft.x + stepX * i;
                var y = topLeft.y + area.height * (1f - normalized);
                GL.Vertex3(x, y, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        internal static void DrawHorizontalRule(Rect area, float normalizedHeight, Color color)
        {
            if (Event.current.type != EventType.Repaint || !EnsureLineMaterial())
            {
                return;
            }

            var topLeft = GUIUtility.GUIToScreenPoint(new Vector2(area.x, area.y));
            var y = topLeft.y + area.height * (1f - normalizedHeight);

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.LINES);
            GL.Color(color);
            GL.Vertex3(topLeft.x, y, 0f);
            GL.Vertex3(topLeft.x + area.width, y, 0f);
            GL.End();
            GL.PopMatrix();
        }

        private static bool EnsureLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return true;
            }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }

            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            return true;
        }

        private static void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _header = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
            };
            _cell = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _cellRight = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
            };
            _rowButton = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _stylesReady = true;
        }

        private static Texture2D MakeSolid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
