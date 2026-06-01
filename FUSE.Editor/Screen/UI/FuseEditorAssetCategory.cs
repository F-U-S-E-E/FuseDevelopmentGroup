using System;
using System.Collections.Generic;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Entity-kind buckets the right panel's F1–F6 selector switches
    /// between. Declaration order is the F-key order:
    /// <c>Tracks</c> = F1, <c>Switches</c> = F2, etc. The two
    /// placeholder slots leave room for future FUSE-domain kinds
    /// (industries / loaders / signals) without renumbering existing
    /// keybindings.
    /// </summary>
    /// <remarks>
    /// Switches aren't a top-level FUSE type — they're track nodes
    /// with <c>flipSwitchStand=true</c>. The selector treats them as
    /// their own bucket so placing/filtering switches feels first-
    /// class in the UI even though the underlying schema folds them
    /// into nodes.
    /// </remarks>
    internal enum FuseEditorAssetCategory
    {
        Tracks = 0,
        Switches = 1,
        Scenery = 2,
        Mandelas = 3,
        PlaceholderA = 4,
        PlaceholderB = 5,
    }

    /// <summary>
    /// Metadata + active-selection state for the asset category
    /// selector. Static so the menu bar's View submenu and the
    /// right-panel's Assets tab share one source of truth.
    /// </summary>
    internal static class FuseEditorAssetCategoryRegistry
    {
        public sealed class CategoryInfo
        {
            public CategoryInfo(FuseEditorAssetCategory kind, string labelKey, FuseEditorIconKind iconKind,
                                bool isAvailable, string unavailableReasonKey)
            {
                Kind = kind;
                LabelKey = labelKey;
                IconKind = iconKind;
                IsAvailable = isAvailable;
                UnavailableReasonKey = unavailableReasonKey;
            }

            public FuseEditorAssetCategory Kind { get; }
            public string LabelKey { get; }
            public FuseEditorIconKind IconKind { get; }

            /// <summary>
            /// <c>false</c> means the F-key button paints as disabled
            /// with <see cref="UnavailableReasonKey"/> in the hover
            /// tooltip. The two placeholder kinds default to this
            /// state so the row visually carries six slots without
            /// pretending they all work.
            /// </summary>
            public bool IsAvailable { get; }
            public string UnavailableReasonKey { get; }
        }

        private static readonly CategoryInfo[] Infos = new[]
        {
            new CategoryInfo(FuseEditorAssetCategory.Tracks, "fuse.editor.assets.tracks",
                             FuseEditorIconKind.Track, isAvailable: true, unavailableReasonKey: null),
            new CategoryInfo(FuseEditorAssetCategory.Switches, "fuse.editor.assets.switches",
                             FuseEditorIconKind.Switch, isAvailable: true, unavailableReasonKey: null),
            new CategoryInfo(FuseEditorAssetCategory.Scenery, "fuse.editor.assets.scenery",
                             FuseEditorIconKind.Scenery, isAvailable: true, unavailableReasonKey: null),
            new CategoryInfo(FuseEditorAssetCategory.Mandelas, "fuse.editor.assets.mandelas",
                             FuseEditorIconKind.Mandela, isAvailable: true, unavailableReasonKey: null),
            new CategoryInfo(FuseEditorAssetCategory.PlaceholderA, "fuse.editor.assets.placeholder_a",
                             FuseEditorIconKind.PlaceholderA, isAvailable: false,
                             unavailableReasonKey: "fuse.editor.assets.placeholder.reason"),
            new CategoryInfo(FuseEditorAssetCategory.PlaceholderB, "fuse.editor.assets.placeholder_b",
                             FuseEditorIconKind.PlaceholderB, isAvailable: false,
                             unavailableReasonKey: "fuse.editor.assets.placeholder.reason"),
        };

        public static FuseEditorAssetCategory Active { get; private set; } = FuseEditorAssetCategory.Tracks;

        /// <summary>
        /// All categories in canonical F1..F6 order. Same ordering as
        /// the enum's declaration so callers can iterate to build the
        /// row directly.
        /// </summary>
        public static IReadOnlyList<CategoryInfo> All => Infos;

        public static CategoryInfo Get(FuseEditorAssetCategory kind) => Infos[(int)kind];

        /// <summary>
        /// Activates the supplied category if it's available;
        /// silently no-ops for placeholder slots so a keyboard
        /// F5/F6 mash doesn't end up in a half-state where the UI
        /// claims a category is selected that has no asset list.
        /// </summary>
        public static void SetActive(FuseEditorAssetCategory kind)
        {
            var info = Get(kind);
            if (!info.IsAvailable) return;
            Active = kind;
        }

        /// <summary>
        /// Resets the active category to the first available kind.
        /// Used by tests and at editor-exit teardown.
        /// </summary>
        public static void Reset()
        {
            Active = FuseEditorAssetCategory.Tracks;
        }
    }
}
