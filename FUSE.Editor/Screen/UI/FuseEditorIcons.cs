using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Names every icon the editor UI references. The enum member's
    /// name is the PNG file name on disk: <c>IconKind.Save</c> maps to
    /// <c>Resources/FuseEditor/Icons/Save.png</c>. Add a new icon by
    /// adding a member here + dropping a PNG with the matching name.
    /// </summary>
    internal enum FuseEditorIconKind
    {
        // File ops
        New,
        Open,
        Save,

        // History
        Undo,
        Redo,

        // Gizmo / tools
        Select,
        Move,
        Rotate,
        Scale,
        Place,

        // View toggles
        Grid,
        Camera,
        Weather,

        // Panel headers / tabs
        EntityTree,
        Properties,
        Locations,
        Assets,

        // Entity kinds (F1..F4 + reserved F5/F6)
        Track,
        Switch,
        Scenery,
        Mandela,
        PlaceholderA,
        PlaceholderB,

        // Common verbs / glyphs
        Play,
        Stop,
        Eye,
        EyeOff,
        Trash,
        Plus,
        Close,
        ChevronDown,
        ChevronRight,
        Search,
    }

    /// <summary>
    /// Bound texture + glyph for one icon kind. Drawing code consults
    /// <see cref="Texture"/> first; if it's null (because the PNG
    /// isn't present yet) the matching <see cref="GlyphFallback"/>
    /// is rendered instead. This keeps the editor laid out and
    /// readable while an artist's icon set is in flight.
    /// </summary>
    internal readonly struct FuseEditorIcon
    {
        public FuseEditorIcon(FuseEditorIconKind kind, Texture2D texture, string glyphFallback)
        {
            Kind = kind;
            Texture = texture;
            GlyphFallback = glyphFallback ?? string.Empty;
        }

        public FuseEditorIconKind Kind { get; }
        public Texture2D Texture { get; }
        public string GlyphFallback { get; }

        public bool HasTexture => Texture != null;
    }

    /// <summary>
    /// Icon registry with texture-first / glyph-fallback semantics.
    /// Modelled to be artist-replaceable: dropping a new PNG under
    /// <c>Resources/FuseEditor/Icons/&lt;Kind&gt;.png</c> takes effect
    /// on the next editor session without any code change.
    /// </summary>
    /// <remarks>
    /// Textures load lazily on first request — Unity rejects
    /// <c>Resources.Load</c> calls from static field initializers
    /// because the engine's graphics layer comes up after class
    /// load. Glyph fallbacks are baked into a static map so the
    /// registry remains usable for tests that run without a Unity
    /// graphics context.
    /// </remarks>
    internal static class FuseEditorIcons
    {
        // Icons live as loose PNGs next to FUSE.Editor.dll, under an
        // <c>Icons/</c> subfolder. The mod-loader deploys our DLL to
        // <GameDir>/Mods/FUSE/, so any PNG dropped at
        // <GameDir>/Mods/FUSE/Icons/<Kind>.png becomes the new icon
        // on the next editor session. This file-based path beats
        // Resources.Load for a mod because the asset doesn't have to
        // be pre-packed into a Unity Resources/ folder at build time —
        // an artist (or anyone) can just drop a fresh PNG in.
        public const string IconsSubfolder = "Icons";

        private static readonly Dictionary<FuseEditorIconKind, FuseEditorIcon> Cache =
            new Dictionary<FuseEditorIconKind, FuseEditorIcon>();

        // Unicode glyph fallbacks. Picked for legibility at 16px when
        // the texture isn't available. Use plain ASCII / common Unicode
        // shapes rather than emoji so they render consistently across
        // platforms in the default Unity skin font.
        private static readonly Dictionary<FuseEditorIconKind, string> Glyphs =
            new Dictionary<FuseEditorIconKind, string>
            {
                [FuseEditorIconKind.New] = "+",
                [FuseEditorIconKind.Open] = "▢",        // ▢
                [FuseEditorIconKind.Save] = "↓",        // ↓
                [FuseEditorIconKind.Undo] = "↶",        // ↶
                [FuseEditorIconKind.Redo] = "↷",        // ↷
                [FuseEditorIconKind.Select] = "▲",      // ▲
                [FuseEditorIconKind.Move] = "✥",        // ✥ (4-pointed star approx)
                [FuseEditorIconKind.Rotate] = "↻",      // ↻
                [FuseEditorIconKind.Scale] = "⛶",       // ⛶
                [FuseEditorIconKind.Place] = "⊞",       // ⊞
                [FuseEditorIconKind.Grid] = "▦",        // ▦
                [FuseEditorIconKind.Camera] = "◎",      // ◎
                [FuseEditorIconKind.Weather] = "☁",     // ☁
                [FuseEditorIconKind.EntityTree] = "☰",  // ☰
                [FuseEditorIconKind.Properties] = "≡",  // ≡
                [FuseEditorIconKind.Locations] = "⚑",   // ⚑
                [FuseEditorIconKind.Assets] = "▣",      // ▣
                [FuseEditorIconKind.Track] = "☲",       // ☲ (parallel-line bigram)
                [FuseEditorIconKind.Switch] = "⊣",      // ⊣
                [FuseEditorIconKind.Scenery] = "♣",     // ♣
                [FuseEditorIconKind.Mandela] = "◇",     // ◇
                [FuseEditorIconKind.PlaceholderA] = "…",// …
                [FuseEditorIconKind.PlaceholderB] = "…",// …
                [FuseEditorIconKind.Play] = "▶",        // ▶
                [FuseEditorIconKind.Stop] = "■",        // ■
                [FuseEditorIconKind.Eye] = "◉",         // ◉
                [FuseEditorIconKind.EyeOff] = "⦸",      // ⦸
                [FuseEditorIconKind.Trash] = "✕",       // ✕
                [FuseEditorIconKind.Plus] = "+",
                [FuseEditorIconKind.Close] = "✕",       // ✕
                [FuseEditorIconKind.ChevronDown] = "▾", // ▾
                [FuseEditorIconKind.ChevronRight] = "▸",// ▸
                [FuseEditorIconKind.Search] = "⚲",      // ⚲ (looks like a magnifier handle)
            };

        /// <summary>
        /// Returns the bound icon for <paramref name="kind"/>. The
        /// first call triggers a lazy <see cref="Resources.Load"/>;
        /// subsequent calls return the cached entry. Always returns
        /// a usable struct — the texture may be null, but the glyph
        /// fallback is never empty.
        /// </summary>
        public static FuseEditorIcon Get(FuseEditorIconKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached))
            {
                return cached;
            }

            var texture = TryLoadTexture(kind);
            var glyph = Glyphs.TryGetValue(kind, out var g) ? g : "?";
            var icon = new FuseEditorIcon(kind, texture, glyph);
            Cache[kind] = icon;
            return icon;
        }

        /// <summary>
        /// Draws the icon at <paramref name="rect"/>. Paints the
        /// texture if available, otherwise the Unicode glyph
        /// fallback using <paramref name="glyphStyle"/> (defaults to
        /// the toolbar button style when null).
        /// </summary>
        public static void Draw(Rect rect, FuseEditorIconKind kind, GUIStyle glyphStyle = null, Color? tint = null)
        {
            var icon = Get(kind);
            if (icon.HasTexture)
            {
                var prevColor = GUI.color;
                if (tint.HasValue) GUI.color = tint.Value;
                GUI.DrawTexture(rect, icon.Texture, ScaleMode.ScaleToFit);
                GUI.color = prevColor;
                return;
            }

            // Glyph fallback path. Apply tint via GUI.color since
            // most styles don't expose textColor as a per-call knob.
            var style = glyphStyle ?? FuseEditorTheme.ToolbarButton;
            var prev = GUI.color;
            if (tint.HasValue) GUI.color = tint.Value;
            GUI.Label(rect, icon.GlyphFallback, style);
            GUI.color = prev;
        }

        /// <summary>
        /// Drops every cached icon. Used by tests that need to
        /// re-exercise the load path.
        /// </summary>
        public static void Reset()
        {
            Cache.Clear();
        }

        private static Texture2D TryLoadTexture(FuseEditorIconKind kind)
        {
            // We must NOT touch any Unity engine type in this method
            // — its body gets JIT'd on first call, and the JIT fails
            // outright (ECall not packaged) when xUnit runs outside
            // a Unity graphics context. Keep this method pure
            // System.IO so unit tests can exercise it; defer the
            // texture work to TryDecodeTexture, which only gets JIT'd
            // when bytes are actually available (never in tests, since
            // no PNGs sit next to the test runner).
            try
            {
                var folder = ResolveIconsFolder();
                if (string.IsNullOrEmpty(folder)) return null;

                var path = System.IO.Path.Combine(folder, kind + ".png");
                if (!System.IO.File.Exists(path)) return null;

                var bytes = System.IO.File.ReadAllBytes(path);
                return TryDecodeTexture(bytes);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static Texture2D TryDecodeTexture(byte[] bytes)
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                // LoadImage is an extension in UnityEngine.ImageConversion;
                // call it as a static so we don't depend on the using
                // being imported. Resizes the texture to the PNG's
                // dimensions; the 2×2 init is a placeholder.
                if (!UnityEngine.ImageConversion.LoadImage(texture, bytes))
                {
                    Object.Destroy(texture);
                    return null;
                }
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves the <c>Icons/</c> folder next to FUSE.Editor.dll.
        /// Returns null when the assembly's location can't be
        /// determined (e.g. running from a memory stream).
        /// </summary>
        private static string ResolveIconsFolder()
        {
            try
            {
                var assemblyPath = typeof(FuseEditorIcons).Assembly.Location;
                if (string.IsNullOrEmpty(assemblyPath)) return null;
                var assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                if (string.IsNullOrEmpty(assemblyDir)) return null;
                return System.IO.Path.Combine(assemblyDir, IconsSubfolder);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
