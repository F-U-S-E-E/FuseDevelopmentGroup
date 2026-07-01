using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Central palette + style factory for the FUSE editor IMGUI surface.
    /// Modelled after Arma 3 EDEN's near-black dark theme with a warm
    /// orange accent. Every other UI file pulls its colors from
    /// <see cref="Palette"/> and its <see cref="GUIStyle"/> instances
    /// from <see cref="Styles"/> — no inlined Color literals in
    /// drawing code, no repeated style construction.
    /// </summary>
    /// <remarks>
    /// The class lazy-initializes its styles on first access via
    /// <see cref="EnsureCreated"/> so the textures aren't built until
    /// the editor screen is actually rendered (Unity rejects texture
    /// creation in static field initializers when called before the
    /// engine's graphics layer is up).
    /// </remarks>
    internal static class FuseEditorTheme
    {
        /// <summary>
        /// Color tokens. Names group by purpose; values target the
        /// EDEN reference's near-black surfaces and warm accents.
        /// </summary>
        public static class Palette
        {
            // Backgrounds — three depth layers. EDEN's app chrome is
            // very dark (~ #0d0e10); panels sit one step up so they
            // separate visually without losing the dark mood.
            public static readonly Color BackgroundDeep = new Color(0.05f, 0.06f, 0.07f, 0.96f);
            public static readonly Color BackgroundPrimary = new Color(0.08f, 0.09f, 0.10f, 0.96f);
            public static readonly Color BackgroundSecondary = new Color(0.11f, 0.12f, 0.14f, 0.95f);
            public static readonly Color BackgroundTertiary = new Color(0.14f, 0.15f, 0.17f, 0.94f);

            // Text — primary at near-white, secondary muted, disabled
            // gray, accent for selected / active tabs.
            public static readonly Color TextPrimary = new Color(0.92f, 0.93f, 0.95f, 1f);
            public static readonly Color TextSecondary = new Color(0.70f, 0.72f, 0.75f, 1f);
            public static readonly Color TextDisabled = new Color(0.42f, 0.44f, 0.47f, 1f);
            public static readonly Color TextAccent = new Color(0.98f, 0.84f, 0.42f, 1f);

            // Accents — EDEN's signature warm orange for the PLAY CTA
            // and active-tool highlighting; cool blue for informational
            // states.
            public static readonly Color AccentWarm = new Color(0.86f, 0.41f, 0.10f, 1f);
            public static readonly Color AccentWarmStrong = new Color(0.96f, 0.48f, 0.12f, 1f);
            public static readonly Color AccentCool = new Color(0.28f, 0.55f, 0.82f, 1f);

            // Borders + highlights — translucent overlays for hover /
            // selection states so they layer cleanly over any panel.
            public static readonly Color BorderDivider = new Color(0.22f, 0.24f, 0.27f, 1f);
            public static readonly Color BorderStrong = new Color(0.32f, 0.34f, 0.38f, 1f);
            public static readonly Color HighlightHover = new Color(1f, 1f, 1f, 0.06f);
            public static readonly Color HighlightSelected = new Color(0.96f, 0.48f, 0.12f, 0.22f);

            // Axis colors — warm red / green / blue triplet for the
            // world-orientation gizmo and any future axis-coded
            // affordance (e.g. a "constrain to X" toggle). Hex sources:
            // #E04A4A / #5BC04A / #4A87E0. Matches Unity / Blender /
            // EDEN's convention so the visual language is consistent
            // with adjacent CAD tools.
            public static readonly Color AxisX = new Color(0.878f, 0.290f, 0.290f, 1f);
            public static readonly Color AxisY = new Color(0.357f, 0.753f, 0.290f, 1f);
            public static readonly Color AxisZ = new Color(0.290f, 0.529f, 0.878f, 1f);
        }

        // Layout tokens — kept here so any region can read them
        // without re-deriving from screen-local constants.
        public static class Metrics
        {
            public const float MenuBarHeight = 24f;
            public const float ToolbarHeight = 32f;
            public const float TabStripHeight = 26f;
            public const float BottomBarHeight = 28f;
            public const float LeftPanelWidth = 280f;
            public const float RightPanelWidth = 340f;
            public const float DividerThickness = 1f;
            public const float Padding = 6f;
            public const float TightPadding = 3f;
            public const float ToolbarButtonSize = 24f;
            public const float ToolbarGroupGap = 6f;
        }

        // -----------------------------------------------------------------
        // Style factory
        // -----------------------------------------------------------------

        private static bool _initialized;

        // All styles live as static fields so call sites can take a
        // reference once per frame. The factory builds them once on
        // first call to EnsureCreated; tests reset via Reset() so
        // they can re-init with a deterministic baseline.
        private static GUIStyle _menuBar;
        private static GUIStyle _menuItem;
        private static GUIStyle _menuItemActive;
        private static GUIStyle _toolbar;
        private static GUIStyle _toolbarButton;
        private static GUIStyle _toolbarButtonActive;
        private static GUIStyle _panel;
        private static GUIStyle _panelDeep;
        private static GUIStyle _tabBar;
        private static GUIStyle _tab;
        private static GUIStyle _tabActive;
        private static GUIStyle _categoryHeader;
        private static GUIStyle _treeRow;
        private static GUIStyle _treeRowSelected;
        private static GUIStyle _propertyLabel;
        private static GUIStyle _propertyValue;
        private static GUIStyle _bottomBar;
        private static GUIStyle _bottomBarText;
        private static GUIStyle _playCta;
        private static GUIStyle _searchField;
        private static GUIStyle _tooltipBox;
        private static GUIStyle _toolbarDropdownLabel;
        private static GUIStyle _toolbarDropdownItem;
        private static GUIStyle _toolbarDropdownItemActive;

        // Cache one solid texture per palette entry so repeated style
        // creation doesn't leak per-call textures.
        private static readonly System.Collections.Generic.Dictionary<Color, Texture2D> SolidTextures
            = new System.Collections.Generic.Dictionary<Color, Texture2D>();

        public static GUIStyle MenuBar => Ensure(ref _menuBar, CreateMenuBarStyle);
        public static GUIStyle MenuItem => Ensure(ref _menuItem, CreateMenuItemStyle);
        public static GUIStyle MenuItemActive => Ensure(ref _menuItemActive, CreateMenuItemActiveStyle);
        public static GUIStyle Toolbar => Ensure(ref _toolbar, CreateToolbarStyle);
        public static GUIStyle ToolbarButton => Ensure(ref _toolbarButton, CreateToolbarButtonStyle);
        public static GUIStyle ToolbarButtonActive => Ensure(ref _toolbarButtonActive, CreateToolbarButtonActiveStyle);
        public static GUIStyle Panel => Ensure(ref _panel, CreatePanelStyle);
        public static GUIStyle PanelDeep => Ensure(ref _panelDeep, CreatePanelDeepStyle);
        public static GUIStyle TabBar => Ensure(ref _tabBar, CreateTabBarStyle);
        public static GUIStyle Tab => Ensure(ref _tab, CreateTabStyle);
        public static GUIStyle TabActive => Ensure(ref _tabActive, CreateTabActiveStyle);
        public static GUIStyle CategoryHeader => Ensure(ref _categoryHeader, CreateCategoryHeaderStyle);
        public static GUIStyle TreeRow => Ensure(ref _treeRow, CreateTreeRowStyle);
        public static GUIStyle TreeRowSelected => Ensure(ref _treeRowSelected, CreateTreeRowSelectedStyle);
        public static GUIStyle PropertyLabel => Ensure(ref _propertyLabel, CreatePropertyLabelStyle);
        public static GUIStyle PropertyValue => Ensure(ref _propertyValue, CreatePropertyValueStyle);
        public static GUIStyle BottomBar => Ensure(ref _bottomBar, CreateBottomBarStyle);
        public static GUIStyle BottomBarText => Ensure(ref _bottomBarText, CreateBottomBarTextStyle);
        public static GUIStyle PlayCta => Ensure(ref _playCta, CreatePlayCtaStyle);
        public static GUIStyle SearchField => Ensure(ref _searchField, CreateSearchFieldStyle);
        public static GUIStyle TooltipBox => Ensure(ref _tooltipBox, CreateTooltipBoxStyle);
        public static GUIStyle ToolbarDropdownLabel => Ensure(ref _toolbarDropdownLabel, CreateToolbarDropdownLabelStyle);
        public static GUIStyle ToolbarDropdownItem => Ensure(ref _toolbarDropdownItem, CreateToolbarDropdownItemStyle);
        public static GUIStyle ToolbarDropdownItemActive => Ensure(ref _toolbarDropdownItemActive, CreateToolbarDropdownItemActiveStyle);

        /// <summary>
        /// Forces all styles to be built. Call once at editor screen
        /// startup if you'd rather pay the cost up front than on first
        /// render of each region.
        /// </summary>
        public static void EnsureCreated()
        {
            if (_initialized) return;
            _ = MenuBar; _ = MenuItem; _ = MenuItemActive;
            _ = Toolbar; _ = ToolbarButton; _ = ToolbarButtonActive;
            _ = Panel; _ = PanelDeep;
            _ = TabBar; _ = Tab; _ = TabActive;
            _ = CategoryHeader; _ = TreeRow; _ = TreeRowSelected;
            _ = PropertyLabel; _ = PropertyValue;
            _ = BottomBar; _ = BottomBarText; _ = PlayCta;
            _ = SearchField; _ = TooltipBox;
            _ = ToolbarDropdownLabel; _ = ToolbarDropdownItem; _ = ToolbarDropdownItemActive;
            _initialized = true;
        }

        /// <summary>
        /// Drops cached styles + textures. Useful in xUnit tests that
        /// poke the static palette; never called at runtime.
        /// </summary>
        public static void Reset()
        {
            _initialized = false;
            _menuBar = null; _menuItem = null; _menuItemActive = null;
            _toolbar = null; _toolbarButton = null; _toolbarButtonActive = null;
            _panel = null; _panelDeep = null;
            _tabBar = null; _tab = null; _tabActive = null;
            _categoryHeader = null; _treeRow = null; _treeRowSelected = null;
            _propertyLabel = null; _propertyValue = null;
            _bottomBar = null; _bottomBarText = null; _playCta = null;
            _searchField = null; _tooltipBox = null;
            _toolbarDropdownLabel = null; _toolbarDropdownItem = null; _toolbarDropdownItemActive = null;
            foreach (var tex in SolidTextures.Values)
            {
                if (tex != null) Object.DestroyImmediate(tex);
            }
            SolidTextures.Clear();
        }

        /// <summary>
        /// Cached 1×1 solid texture for the supplied color. Used by
        /// every style background. Textures are created once and
        /// retained across the editor's lifetime.
        /// </summary>
        public static Texture2D SolidTexture(Color color)
        {
            if (SolidTextures.TryGetValue(color, out var existing) && existing != null)
            {
                return existing;
            }
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            SolidTextures[color] = tex;
            return tex;
        }

        private static GUIStyle Ensure(ref GUIStyle slot, System.Func<GUIStyle> factory)
        {
            if (slot == null) slot = factory();
            return slot;
        }

        // -----------------------------------------------------------------
        // Style builders. Each method composes a fresh GUIStyle from
        // Palette / Metrics. Inline new Color() literals are
        // deliberately absent — palette is the single source.
        // -----------------------------------------------------------------

        public static GUIStyle CreateMenuBarStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundDeep), textColor = Palette.TextPrimary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 0, 0),
            border = new RectOffset(),
        };

        public static GUIStyle CreateMenuItemStyle()
        {
            var style = new GUIStyle
            {
                normal = { background = SolidTexture(Palette.BackgroundDeep), textColor = Palette.TextPrimary },
                hover = { background = SolidTexture(Palette.BackgroundSecondary), textColor = Palette.TextPrimary },
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 2, 2),
            };
            return style;
        }

        public static GUIStyle CreateMenuItemActiveStyle()
        {
            var style = new GUIStyle(CreateMenuItemStyle());
            style.normal.background = SolidTexture(Palette.BackgroundSecondary);
            style.normal.textColor = Palette.TextAccent;
            return style;
        }

        public static GUIStyle CreateToolbarStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundPrimary) },
            padding = new RectOffset((int)Metrics.Padding, (int)Metrics.Padding, 2, 2),
        };

        public static GUIStyle CreateToolbarButtonStyle()
        {
            var style = new GUIStyle
            {
                normal = { background = SolidTexture(Palette.BackgroundPrimary), textColor = Palette.TextPrimary },
                hover = { background = SolidTexture(Palette.HighlightHover), textColor = Palette.TextPrimary },
                active = { background = SolidTexture(Palette.HighlightSelected), textColor = Palette.TextAccent },
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 2, 2),
                border = new RectOffset(),
            };
            return style;
        }

        public static GUIStyle CreateToolbarButtonActiveStyle()
        {
            var style = new GUIStyle(CreateToolbarButtonStyle());
            style.normal.background = SolidTexture(Palette.HighlightSelected);
            style.normal.textColor = Palette.TextAccent;
            return style;
        }

        public static GUIStyle CreatePanelStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundPrimary) },
            padding = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(),
        };

        public static GUIStyle CreatePanelDeepStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundDeep) },
            padding = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(),
        };

        public static GUIStyle CreateTabBarStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundDeep) },
            padding = new RectOffset(0, 0, 0, 0),
            border = new RectOffset(),
        };

        public static GUIStyle CreateTabStyle()
        {
            return new GUIStyle
            {
                normal = { background = SolidTexture(Palette.BackgroundDeep), textColor = Palette.TextSecondary },
                hover = { background = SolidTexture(Palette.BackgroundSecondary), textColor = Palette.TextPrimary },
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 4, 4),
            };
        }

        public static GUIStyle CreateTabActiveStyle()
        {
            var style = new GUIStyle(CreateTabStyle());
            style.normal.background = SolidTexture(Palette.BackgroundPrimary);
            style.normal.textColor = Palette.TextPrimary;
            style.fontStyle = FontStyle.Bold;
            return style;
        }

        public static GUIStyle CreateCategoryHeaderStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextAccent },
            fontStyle = FontStyle.Bold,
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 3, 3),
        };

        public static GUIStyle CreateTreeRowStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextPrimary },
            hover = { background = SolidTexture(Palette.HighlightHover), textColor = Palette.TextPrimary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 4, 2, 2),
        };

        public static GUIStyle CreateTreeRowSelectedStyle()
        {
            var style = new GUIStyle(CreateTreeRowStyle());
            style.normal.background = SolidTexture(Palette.HighlightSelected);
            style.normal.textColor = Palette.TextAccent;
            style.fontStyle = FontStyle.Bold;
            return style;
        }

        public static GUIStyle CreatePropertyLabelStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextSecondary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
        };

        public static GUIStyle CreatePropertyValueStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextPrimary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
        };

        public static GUIStyle CreateBottomBarStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundDeep) },
            padding = new RectOffset((int)Metrics.Padding, (int)Metrics.Padding, 2, 2),
        };

        public static GUIStyle CreateBottomBarTextStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextSecondary },
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 0, 0),
        };

        public static GUIStyle CreatePlayCtaStyle()
        {
            var style = new GUIStyle
            {
                normal = { background = SolidTexture(Palette.AccentWarm), textColor = Color.white },
                hover = { background = SolidTexture(Palette.AccentWarmStrong), textColor = Color.white },
                active = { background = SolidTexture(Palette.AccentWarmStrong), textColor = Color.white },
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 4, 4),
            };
            return style;
        }

        public static GUIStyle CreateSearchFieldStyle()
        {
            // Inherit GUI.skin.textField but apply palette colors.
            // GUI.skin isn't available at class-init time (no graphics
            // context yet); inline minimal styling instead.
            return new GUIStyle
            {
                normal = { background = SolidTexture(Palette.BackgroundDeep), textColor = Palette.TextPrimary },
                focused = { background = SolidTexture(Palette.BackgroundDeep), textColor = Palette.TextPrimary },
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 3, 3),
                border = new RectOffset(2, 2, 2, 2),
            };
        }

        public static GUIStyle CreateTooltipBoxStyle() => new GUIStyle
        {
            normal = { background = SolidTexture(Palette.BackgroundTertiary), textColor = Palette.TextPrimary },
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 4, 4),
            wordWrap = true,
        };

        public static GUIStyle CreateToolbarDropdownLabelStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextPrimary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
        };

        public static GUIStyle CreateToolbarDropdownItemStyle() => new GUIStyle
        {
            normal = { textColor = Palette.TextPrimary },
            hover = { background = SolidTexture(Palette.HighlightHover), textColor = Palette.TextPrimary },
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 2, 2),
        };

        public static GUIStyle CreateToolbarDropdownItemActiveStyle()
        {
            var style = new GUIStyle(CreateToolbarDropdownItemStyle());
            style.normal.background = SolidTexture(Palette.HighlightSelected);
            style.normal.textColor = Palette.TextAccent;
            style.fontStyle = FontStyle.Bold;
            return style;
        }

        // -----------------------------------------------------------------
        // Convenience drawing helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Paints a thin horizontal divider in
        /// <see cref="Palette.BorderDivider"/> across the rect.
        /// </summary>
        public static void DrawHorizontalDivider(Rect rect)
        {
            var prev = GUI.color;
            GUI.color = Palette.BorderDivider;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, Metrics.DividerThickness), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>
        /// Paints a thin vertical divider in
        /// <see cref="Palette.BorderDivider"/>. Used between toolbar
        /// groups and between panel sections.
        /// </summary>
        public static void DrawVerticalDivider(Rect rect)
        {
            var prev = GUI.color;
            GUI.color = Palette.BorderDivider;
            GUI.DrawTexture(new Rect(rect.x, rect.y, Metrics.DividerThickness, rect.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>
        /// Fills the rect with the supplied palette color. Cheaper
        /// than constructing a GUIStyle when all you want is a solid
        /// background panel.
        /// </summary>
        public static void DrawSolid(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
