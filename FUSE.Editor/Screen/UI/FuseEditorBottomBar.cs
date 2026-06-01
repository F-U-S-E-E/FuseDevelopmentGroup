using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Bottom status bar. Left cluster shows live camera position
    /// (X / Y / Z + altitude) plus a build-version stamp; right
    /// cluster carries a "Play mod" toggle option and the prominent
    /// orange Play CTA.
    /// </summary>
    /// <remarks>
    /// Coordinates read from <see cref="Camera.main"/> every frame.
    /// If the main camera isn't available yet (early MapDidLoad,
    /// before the strategy camera takes over), the readouts show
    /// "—" so the bar layout never collapses.
    /// </remarks>
    internal static class FuseEditorBottomBar
    {
        public sealed class Options
        {
            public bool PlaceWithSnap { get; set; } = true;
            public Action OnPlayClicked { get; set; }
            public Func<bool> CanPlay { get; set; }
            public string CannotPlayReasonKey { get; set; }
        }

        public static void Draw(Rect rect, Options options)
        {
            GUI.Box(rect, GUIContent.none, FuseEditorTheme.BottomBar);

            DrawLeftCluster(rect);
            DrawRightCluster(rect, options ?? new Options());
        }

        private static void DrawLeftCluster(Rect rect)
        {
            var (x, y, z, alt) = ReadCameraPosition();
            var version = ReadBuildVersion();

            var labelStyle = FuseEditorTheme.BottomBarText;
            var height = rect.height - 4f;

            // Component widths chosen to match the EDEN reference's
            // proportions: each axis ~85px, altitude ~110px, version
            // floats after a small gap.
            const float AxisWidth = 95f;
            const float AltWidth = 120f;
            const float Gap = 8f;
            float cx = rect.x + FuseEditorTheme.Metrics.Padding;

            GUI.Label(new Rect(cx, rect.y + 2f, AxisWidth, height),
                $"X  {Format(x)} m", labelStyle);
            cx += AxisWidth;
            GUI.Label(new Rect(cx, rect.y + 2f, AxisWidth, height),
                $"Y  {Format(y)} m", labelStyle);
            cx += AxisWidth;
            GUI.Label(new Rect(cx, rect.y + 2f, AxisWidth, height),
                $"Z  {Format(z)} m", labelStyle);
            cx += AxisWidth + Gap;
            GUI.Label(new Rect(cx, rect.y + 2f, AltWidth, height),
                $"alt  {Format(alt)} m", labelStyle);
            cx += AltWidth + Gap;
            GUI.Label(new Rect(cx, rect.y + 2f, 100f, height),
                version, labelStyle);
        }

        private static void DrawRightCluster(Rect rect, Options options)
        {
            // Right-anchored layout: PLAY CTA on the far right, the
            // snap toggle to its left.
            const float PlayWidth = 200f;
            const float ToggleWidth = 170f;
            const float Gap = 10f;
            float height = rect.height - 6f;

            var playRect = new Rect(
                rect.x + rect.width - FuseEditorTheme.Metrics.Padding - PlayWidth,
                rect.y + 3f, PlayWidth, height);

            var canPlay = options.CanPlay?.Invoke() ?? true;
            var playLabel = FuseEditorUiHelper.TranslateLabel("fuse.editor.bottombar.play");
            var playGlyph = FuseEditorIcons.Get(FuseEditorIconKind.Play).GlyphFallback;
            var playContent = new GUIContent($"{playGlyph}  {playLabel.Title}",
                                              canPlay ? playLabel.Description
                                                       : (options.CannotPlayReasonKey == null
                                                          ? playLabel.Description
                                                          : FuseEditorUiHelper.TranslateLabel(options.CannotPlayReasonKey).Title));
            if (canPlay)
            {
                if (GUI.Button(playRect, playContent, FuseEditorTheme.PlayCta))
                {
                    options.OnPlayClicked?.Invoke();
                }
            }
            else
            {
                var prev = GUI.enabled;
                GUI.enabled = false;
                GUI.Button(playRect, playContent, FuseEditorTheme.PlayCta);
                GUI.enabled = prev;
            }

            var toggleRect = new Rect(playRect.x - Gap - ToggleWidth, rect.y + 3f,
                                       ToggleWidth, height);
            var toggleLabel = FuseEditorUiHelper.TranslateLabel("fuse.editor.bottombar.place_with_snap");
            options.PlaceWithSnap = GUI.Toggle(toggleRect, options.PlaceWithSnap,
                new GUIContent(toggleLabel.Title, toggleLabel.Description),
                FuseEditorTheme.BottomBarText);
        }

        private static (float x, float y, float z, float alt) ReadCameraPosition()
        {
            var camera = Camera.main;
            if (camera == null) return (float.NaN, float.NaN, float.NaN, float.NaN);
            var pos = camera.transform.position;
            // Altitude is just Y in Railroader's coordinate space;
            // surfacing it separately matches EDEN's readout and keeps
            // the "alt" label parseable for users who skim the bar.
            return (pos.x, pos.y, pos.z, pos.y);
        }

        private static string Format(float v)
        {
            if (float.IsNaN(v)) return "—";
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string ReadBuildVersion()
        {
            // Read from the executing assembly's informational /
            // file version. Falls back to the assembly version when
            // the informational attribute isn't set.
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null && !string.IsNullOrEmpty(info.InformationalVersion))
                {
                    return "v" + info.InformationalVersion;
                }
                return "v" + assembly.GetName().Version;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
