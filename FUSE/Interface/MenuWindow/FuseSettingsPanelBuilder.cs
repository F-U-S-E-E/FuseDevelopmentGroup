using FUSE.Infrastructure;
using Helpers;
using System;
using System.Collections.Generic;
using UI.Builder;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Interface.MenuWindow
{
    internal struct FuseSettingsPanelBuilder
    {
        private enum PageId
        {
            General,
        }

        private class Page(PageId id)
        {
            public PageId Id { get; } = id;
        }

        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            if (selectedItem.Value == null)
            {
                selectedItem.Value = "general";
            }

            List<UIPanelBuilder.ListItem<Page>> list = [];
            list.Add(new UIPanelBuilder.ListItem<Page>("general", new Page(PageId.General), "Settings", "General"));

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, Page page)
            {
                if (page == null)
                {
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState("Select a page");
                    builder.AddExpandingVerticalSpacer();
                }
                else
                {
                    builder.VScrollView(delegate (UIPanelBuilder builder)
                    {
                        switch (page.Id)
                        {
                            case PageId.General:
                                BuildGeneralSettingsPage(builder);
                                break;
                            default:
                                builder.AddLabel("Unknown page.");
                                break;
                        }
                    }, new RectOffset(0, 4, 0, 0));
                }
            });
        }

        private static void BuildGeneralSettingsPage(UIPanelBuilder builder)
        {
            builder.AddTitle("General Settings", "");

            builder.FieldLabelWidth = 200f;

            builder.Spacing = 6f;

            builder.AddSection("Reporting");

            builder.AddField("Verbose Reporting", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.VerboseApplyReportDetails,
                () =>
                {
                    FuseSettings.SetVerboseApplyReportDetails(!FuseSettings.VerboseApplyReportDetails);
                    builder.Rebuild();
                }));

            builder.AddSection("Debug Overlays");

            builder.AddField("Track Probe", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowTrackDebugOverlay,
                () =>
                {
                    FuseSettings.SetShowTrackDebugOverlay(!FuseSettings.ShowTrackDebugOverlay);
                    builder.Rebuild();
                }));

            builder.AddField("Track Span Paths", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowTrackDebugSpanPaths,
                () =>
                {
                    FuseSettings.SetShowTrackDebugSpanPaths(!FuseSettings.ShowTrackDebugSpanPaths);
                    builder.Rebuild();
                }));

            builder.AddField("Scenery Probe", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowSceneryDebugOverlay,
                () =>
                {
                    FuseSettings.SetShowSceneryDebugOverlay(!FuseSettings.ShowSceneryDebugOverlay);
                    builder.Rebuild();
                }));

            builder.AddField("Scenery Details", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowSceneryDebugAdvanced,
                () =>
                {
                    FuseSettings.SetShowSceneryDebugAdvanced(!FuseSettings.ShowSceneryDebugAdvanced);
                    builder.Rebuild();
                }));

            builder.AddField("Track Span Paths", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowTrackDebugSpanPaths,
                () =>
                {
                    FuseSettings.SetShowTrackDebugSpanPaths(!FuseSettings.ShowTrackDebugSpanPaths);
                    builder.Rebuild();
                }));

            builder.AddSection("World Labels");

            builder.AddLabel("Color-coded labels on every visible entity.");
            builder.Spacer(8f);

            builder.AddField("World Labels", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowWorldLabelsOverlay,
                () =>
                {
                    FuseSettings.SetShowWorldLabelsOverlay(!FuseSettings.ShowWorldLabelsOverlay);
                    builder.Rebuild();
                }));

            builder.AddField("Scenery Labels", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.WorldLabelsShowScenery,
                () =>
                {
                    FuseSettings.SetWorldLabelsShowScenery(!FuseSettings.WorldLabelsShowScenery);
                    builder.Rebuild();
                }));

            builder.AddField("", builder.AddLabelMarkup($"<color={FuseWorldLabelsOverlay.FuseSceneryColor.HexString()}>FUSE Scenery</color> / <color={FuseWorldLabelsOverlay.VanillaSceneryColor.HexString()}>Vanilla Scenery"));

            builder.AddField("Scenery Clones", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.WorldLabelsShowSceneClones,
                () =>
                {
                    FuseSettings.SetWorldLabelsShowSceneClones(!FuseSettings.WorldLabelsShowSceneClones);
                    builder.Rebuild();
                }));

            builder.AddField("", $"<color={FuseWorldLabelsOverlay.SceneCloneColor.HexString()}>Scene Clone");

            builder.AddField("Scenery Labels", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.WorldLabelsShowIndustries,
                () =>
                {
                    FuseSettings.SetWorldLabelsShowIndustries(!FuseSettings.WorldLabelsShowIndustries);
                    builder.Rebuild();
                }));

            builder.AddField("", $"<color={FuseWorldLabelsOverlay.IndustryColor.HexString()}>Industries");

            builder.AddField("Track Nodes", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.WorldLabelsShowTrackNodes,
                () =>
                {
                    FuseSettings.SetWorldLabelsShowTrackNodes(!FuseSettings.WorldLabelsShowTrackNodes);
                    builder.Rebuild();
                }));

            builder.AddField("", $"<color={FuseWorldLabelsOverlay.TrackNodeColor.HexString()}>Track Nodes");

            builder.AddField("Track Segments", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.WorldLabelsShowTrackSegments,
                () =>
                {
                    FuseSettings.SetWorldLabelsShowTrackSegments(!FuseSettings.WorldLabelsShowTrackSegments);
                    builder.Rebuild();
                }));

            builder.AddField("", $"<color={FuseWorldLabelsOverlay.TrackSegmentColor.HexString()}>Track Segments");

            builder.AddSection("Experimental");

            builder.AddField("Early Suppression", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableExperimentalEarlyScenePathSuppression,
                () =>
                {
                    FuseSettings.SetEnableExperimentalEarlyScenePathSuppression(!FuseSettings.EnableExperimentalEarlyScenePathSuppression);
                    builder.Rebuild();
                }));

            builder.Spacer(32f);
        }

        private static RectTransform BuildToggleBoxWithButton(UIPanelBuilder builder, bool enabled, Action action)
        {
            var rowRect = builder.HStack(row =>
            {
                row.AddToggle(() => enabled, (val) =>
                {
                    action();
                    row.Rebuild();
                });
                row.AddButtonSelectable(enabled ? "Enabled" : "Disabled", enabled, () =>
                {
                    action();
                    row.Rebuild();
                });
            });
            var hlg = rowRect.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return rowRect;
        }
    }
}
