using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Unity-IMGUI shaped wrappers around the patterns we want to reuse
    /// across every editor panel. Modelled after Axiom's
    /// <c>com.moulberry.axiom.editor.ImGuiHelper</c>: a single static
    /// surface that hides the "do the right thing" boilerplate so each
    /// panel's draw code stays readable.
    ///
    /// Substrate is Unity OnGUI / IMGUI today. The contract here is
    /// stable enough that a future move to Dear ImGui (via imgui-cs or
    /// a custom backend) would only change the implementations, not the
    /// call sites in panels.
    /// </summary>
    internal static class FuseEditorUiHelper
    {
        public readonly struct Label
        {
            public readonly string Key;
            public readonly string Title;
            public readonly string Description;

            public Label(string key, string title, string description)
            {
                Key = key;
                Title = title;
                Description = description;
            }

            public bool HasDescription => !string.IsNullOrEmpty(Description);

            /// <summary>
            /// Convenience accessor matching the Axiom convention: a
            /// <see cref="GUIContent"/> with the title as the visible text
            /// and the description as the hover tooltip (or null when no
            /// description was registered).
            /// </summary>
            public GUIContent ToContent() => new GUIContent(Title, Description);
        }

        /// <summary>
        /// Builds a <see cref="Label"/> from a string key by consulting
        /// <see cref="FuseEditorStrings"/>. The same pattern Axiom uses:
        /// every label resolves through the registry, descriptions are
        /// optional, and missing entries render as the literal key.
        /// </summary>
        public static Label TranslateLabel(string key)
        {
            return new Label(
                key: key,
                title: FuseEditorStrings.Get(key),
                description: FuseEditorStrings.TryGetDescription(key));
        }

        /// <summary>
        /// Renders a button that draws and behaves normally; if the key
        /// has a registered description, the tooltip surfaces on hover.
        /// </summary>
        public static bool Button(Rect rect, string key, GUIStyle style)
        {
            var label = TranslateLabel(key);
            return GUI.Button(rect, label.ToContent(), style);
        }

        /// <summary>
        /// Renders a disabled button — gray, non-clickable — and attaches
        /// <paramref name="reason"/> as the hover tooltip explaining why
        /// the action is unavailable. This is the Axiom
        /// <c>disabledMenuItem(label, reasonText)</c> idiom applied to
        /// buttons: the user never has to guess why a control is gray.
        /// </summary>
        /// <remarks>
        /// Tooltip surfacing on disabled IMGUI controls works because
        /// Unity captures <see cref="GUIContent.tooltip"/> at hit-test
        /// time (mouse-over rect), independently of <see cref="GUI.enabled"/>.
        /// Always returns <c>false</c> so call sites can use it
        /// interchangeably with <see cref="Button"/>.
        /// </remarks>
        public static bool DisabledButton(Rect rect, string key, string reason, GUIStyle style)
        {
            var label = TranslateLabel(key);
            var tooltip = string.IsNullOrEmpty(reason) ? label.Description : reason;
            var prev = GUI.enabled;
            GUI.enabled = false;
            GUI.Button(rect, new GUIContent(label.Title, tooltip), style);
            GUI.enabled = prev;
            return false;
        }

        /// <summary>
        /// Renders a small "(?)" help marker next to the current cursor
        /// position. Hovering surfaces <paramref name="description"/> as
        /// a tooltip via Unity's <see cref="GUI.tooltip"/> pipeline.
        /// Useful next to a label whose meaning isn't obvious at a glance.
        /// </summary>
        public static void HelpMarker(Rect rect, string description, GUIStyle style)
        {
            if (string.IsNullOrEmpty(description))
            {
                return;
            }

            var prev = GUI.enabled;
            GUI.enabled = false;
            GUI.Label(rect, new GUIContent("(?)", description), style);
            GUI.enabled = prev;
        }

        /// <summary>
        /// Renders the tooltip queued by Unity IMGUI for the current
        /// frame. Call this once at the end of <c>OnGUI</c> after every
        /// panel has drawn so the last-hovered control's tooltip is the
        /// one that shows. <paramref name="boxStyle"/> controls the
        /// tooltip pill's background; pass <c>null</c> to use
        /// <see cref="GUI.skin.box"/>.
        /// </summary>
        /// <param name="logicalScreen">
        /// The LOGICAL screen bounds (post-UI-scale) the editor draws
        /// into. The mouse position IMGUI reports is in this same
        /// logical space because the caller's <c>GUI.matrix</c> scale is
        /// in effect, so the on-screen clamp must compare against these
        /// logical dimensions — NOT <see cref="UnityEngine.Screen"/>'s
        /// device pixels. Pass <c>default</c> to fall back to device
        /// pixels (correct only at 1.0x UI scale).
        /// </param>
        public static void RenderHoverTooltip(GUIStyle boxStyle = null, Rect logicalScreen = default)
        {
            var tip = GUI.tooltip;
            if (string.IsNullOrEmpty(tip))
            {
                return;
            }

            // Wrap the text to a maximum width so long descriptions
            // become multi-line rather than running off the screen.
            const float maxWidth = 320f;
            const float padding = 6f;
            var style = boxStyle ?? GUI.skin.box;
            var content = new GUIContent(tip);
            var size = style.CalcSize(content);
            var width = Mathf.Min(size.x, maxWidth);
            var height = style.CalcHeight(content, width);

            var mouse = Event.current?.mousePosition ?? Vector2.zero;
            var x = mouse.x + 14f;
            var y = mouse.y + 18f;

            // Clamp against the LOGICAL screen size when the caller
            // supplied it (the mouse coords are logical under the UI-
            // scale matrix); fall back to device pixels otherwise.
            var boundsW = logicalScreen.width > 0f ? logicalScreen.width : UnityEngine.Screen.width;
            var boundsH = logicalScreen.height > 0f ? logicalScreen.height : UnityEngine.Screen.height;

            // Keep the tooltip on screen — flip to the left / above the
            // cursor if it would clip the bottom-right corner.
            if (x + width + padding > boundsW)
            {
                x = mouse.x - width - 8f;
            }
            if (y + height + padding > boundsH)
            {
                y = mouse.y - height - 8f;
            }

            GUI.Box(new Rect(x, y, width + padding, height + padding), tip, style);
        }

        /// <summary>
        /// Draws a horizontal separator with a centered label embedded
        /// in the line. Useful for breaking long panels into named
        /// sections — same idea as Axiom's <c>separatorWithText</c>.
        /// </summary>
        public static void SeparatorWithText(Rect rect, string text, GUIStyle labelStyle, Color lineColor)
        {
            var labelContent = new GUIContent(text);
            var labelSize = labelStyle.CalcSize(labelContent);
            var midY = rect.y + rect.height * 0.5f;
            var gap = 8f;

            var prevColor = GUI.color;
            GUI.color = lineColor;

            // Left line segment
            var leftEnd = rect.x + (rect.width - labelSize.x) * 0.5f - gap;
            if (leftEnd > rect.x)
            {
                GUI.DrawTexture(new Rect(rect.x, midY, leftEnd - rect.x, 1f), Texture2D.whiteTexture);
            }

            // Right line segment
            var rightStart = rect.x + (rect.width + labelSize.x) * 0.5f + gap;
            var rightEnd = rect.x + rect.width;
            if (rightEnd > rightStart)
            {
                GUI.DrawTexture(new Rect(rightStart, midY, rightEnd - rightStart, 1f), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;

            var labelRect = new Rect(rect.x + (rect.width - labelSize.x) * 0.5f,
                                     rect.y + (rect.height - labelSize.y) * 0.5f,
                                     labelSize.x,
                                     labelSize.y);
            GUI.Label(labelRect, labelContent, labelStyle);
        }
    }
}
