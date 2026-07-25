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

        private sealed class Page(PageId id)
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

            builder.AddSection("Interface");

            builder.AddField("Enhanced Loading Screen", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableEnhancedLoadingScreen,
                () =>
                {
                    FuseSettings.SetEnableEnhancedLoadingScreen(!FuseSettings.EnableEnhancedLoadingScreen);
                    builder.Rebuild();
                }));

            builder.AddLabel("Staged progress and current-step labels during loading; takes effect on the next load.");

            builder.AddSection("Reporting");

            builder.AddField("Verbose Reporting", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.VerboseApplyReportDetails,
                () =>
                {
                    FuseSettings.SetVerboseApplyReportDetails(!FuseSettings.VerboseApplyReportDetails);
                    builder.Rebuild();
                }));

            builder.AddField("Advanced Details", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ShowAdvancedHealthDetails,
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    builder.Rebuild();
                }));

            builder.AddLabel("Shows deeper diagnostics across FUSE pages: advanced mod settings, dependency-graph and asset internals. Combined with Verbose Reporting it also logs per-object progression diagnostics to FUSE.log.");

            builder.AddSection("Performance Diagnostics");

            // Migrated from the retired Health window's Advanced page: this
            // menu window is the "FUSE" surface most users actually open, and a
            // stutter-report toggle nobody can find produces no stutter reports.
            builder.AddField("Frame Spike Log", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableFrameSpikeDiagnostics,
                () =>
                {
                    FuseSettings.SetEnableFrameSpikeDiagnostics(!FuseSettings.EnableFrameSpikeDiagnostics);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                FuseSettings.EnableFrameSpikeDiagnostics
                    ? $"Logging adaptive hitches to FUSE.log (absolute floor {FuseSettings.FrameSpikeThresholdMs:F0}ms; spikes so far: {FuseRuntimeGuardCounters.FrameSpikes}, worst {FuseRuntimeGuardCounters.FrameSpikeWorstMs:F0}ms)."
                    : "For stutter reports: logs frames that exceed both the configured floor and the rolling frame-time baseline, with memory and queue context. Takes effect immediately.");

            builder.AddField("Spike Floor", control: builder.AddSliderQuantized(
                () => FuseSettings.FrameSpikeThresholdMs,
                () => $"{FuseSettings.FrameSpikeThresholdMs:F0}ms",
                FuseSettings.PreviewFrameSpikeThresholdMs,
                5f,
                FuseSettings.MinFrameSpikeThresholdMs,
                250f,
                value =>
                {
                    FuseSettings.SetFrameSpikeThresholdMs(value);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                "Frames shorter than the floor never log as spikes, even when they exceed the rolling " +
                "baseline. 50ms suits subtle-stutter hunts; the 100ms default keeps only severe hitches. " +
                "Takes effect immediately.");

            builder.AddField("Native Allocation Stacks", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableNativeLeakStackTraces,
                () =>
                {
                    FuseSettings.SetEnableNativeLeakStackTraces(!FuseSettings.EnableNativeLeakStackTraces);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                FuseSettings.EnableNativeLeakStackTraces
                    ? $"Unity mode: {FuseNativeLeakDiagnostic.ModeLabel}. Process-wide and expensive; reproduce briefly, then disable. Restart before capture for the cleanest history."
                    : $"Unity mode: {FuseNativeLeakDiagnostic.ModeLabel}. Enables process-wide native-allocation stack traces for leak hunts; substantial CPU and memory overhead.");

            builder.AddField("Scenery Cull Log", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableSceneryCullingDiagnostics,
                () =>
                {
                    FuseSettings.SetEnableSceneryCullingDiagnostics(!FuseSettings.EnableSceneryCullingDiagnostics);
                    builder.Rebuild();
                }));

            builder.AddLabel("Logs every scenery load/unload flip to FUSE.log ('scenery-cull'). Session-only and resets when the game restarts; verbose while moving.");

            builder.AddSection("Experimental");

            builder.AddField("Targeted Terrain Rebuild", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableTargetedTerrainInvalidation,
                () =>
                {
                    FuseSettings.SetEnableTargetedTerrainInvalidation(!FuseSettings.EnableTargetedTerrainInvalidation);
                    builder.Rebuild();
                }));

            builder.AddLabel("After applying packages, re-bake only the terrain tiles FUSE touched instead of a full rebuild. Falls back to the full rebuild when a mask can't be bounded. Takes effect on the next map load.");

            builder.AddField("Early Scene-Path Suppression", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableExperimentalEarlyScenePathSuppression,
                () =>
                {
                    FuseSettings.SetEnableExperimentalEarlyScenePathSuppression(!FuseSettings.EnableExperimentalEarlyScenePathSuppression);
                    builder.Rebuild();
                }));

            builder.AddLabel("Applies scene-path suppressions during the load itself instead of after. Takes effect on the next map load.");

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

            builder.AddField("Industry Labels", control: BuildToggleBoxWithButton(
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
