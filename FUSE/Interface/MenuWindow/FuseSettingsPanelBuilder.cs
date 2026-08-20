using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
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
            LegacyGameplay,
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
            list.Add(new UIPanelBuilder.ListItem<Page>("legacy-gameplay", new Page(PageId.LegacyGameplay), "Settings", "Legacy Gameplay"));

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
                            case PageId.LegacyGameplay:
                                BuildLegacyGameplaySettingsPage(builder);
                                break;
                            default:
                                builder.AddLabel("Unknown page.");
                                break;
                        }
                    }, new RectOffset(0, 4, 0, 0));
                }
            });
        }

        private static void BuildLegacyGameplaySettingsPage(UIPanelBuilder builder)
        {
            builder.AddTitle("Legacy Gameplay Replacements", "");
            builder.FieldLabelWidth = 200f;
            builder.Spacing = 6f;

            builder.AddLabel(
                "FUSE owns these compatibility behaviors so legacy dependencies can be removed. " +
                "Defaults preserve the base game unless a value below is changed.");

            builder.AddSection("Fall From Grace");
            builder.AddLabel(
                "Grace days = max(Minimum, base-game days × Multiplier + Added). " +
                "The 0 / 1 / 0 defaults are identical to the base game. FUSE also shows the due time in the car inspector.");

            AddIntegerField(builder, "Minimum Days", FuseSettings.GraceMinimumDays, FuseSettings.SetGraceMinimumDays);
            AddIntegerField(builder, "Multiplier", FuseSettings.GraceMultiplier, FuseSettings.SetGraceMultiplier);
            AddIntegerField(builder, "Added Days", FuseSettings.GraceAddedDays, FuseSettings.SetGraceAddedDays);

            builder.AddSection("Configurable Interchange Service");
            builder.AddLabel(
                "Replaces C1CD. Controls the extra-service interval and optional daily service window for all interchanges.");

            var intervals = new[] { 5, 15, 30, 45, 60, 90, 120, 150, 180, 240, 300, 360, 480, 720, 1080 };
            var selectedInterval = Array.IndexOf(intervals, FuseSettings.InterchangeServiceIntervalMinutes);
            if (selectedInterval < 0)
            {
                selectedInterval = Array.FindIndex(
                    intervals,
                    value => value >= FuseSettings.InterchangeServiceIntervalMinutes);
                if (selectedInterval < 0)
                {
                    selectedInterval = intervals.Length - 1;
                }
            }

            builder.AddField(
                "Serve Interval",
                builder.AddDropdown(
                    new List<string>(Array.ConvertAll(intervals, FormatServiceInterval)),
                    selectedInterval,
                    index =>
                    {
                        if (index >= 0 && index < intervals.Length)
                        {
                            FuseSettings.SetInterchangeServiceIntervalMinutes(intervals[index]);
                        }
                    }));

            builder.AddField("Continuous Service", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.InterchangeContinuousService,
                () =>
                {
                    FuseSettings.SetInterchangeContinuousService(!FuseSettings.InterchangeContinuousService);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                "When enabled, FUSE schedules the next extra service after every interchange pass, even when no orders remain.");

            builder.AddField("Not Before", control: builder.AddSliderQuantized(
                () => FuseSettings.InterchangeNotBeforeHour,
                () => FormatServiceHour(FuseSettings.InterchangeNotBeforeHour),
                FuseSettings.SetInterchangeNotBeforeHour,
                0.25f,
                0f,
                24f,
                FuseSettings.SetInterchangeNotBeforeHour));

            builder.AddField("Not After", control: builder.AddSliderQuantized(
                () => FuseSettings.InterchangeNotAfterHour,
                () => FormatServiceHour(FuseSettings.InterchangeNotAfterHour),
                FuseSettings.SetInterchangeNotAfterHour,
                0.25f,
                0f,
                24f,
                FuseSettings.SetInterchangeNotAfterHour));

            builder.AddLabel(
                "Use 00:00–24:00 for all-day service. A start later than the end (for example 22:00–06:00) creates an overnight window.");

            builder.AddSection("Outbound Industry Routing");
            builder.AddLabel(
                "Native replacement for AbsoluteMadness and SomeKindOfMadness. It activates automatically when an enabled package requests either legacy id. " +
                "The switch below is an explicit opt-in for profiles that do not declare the old dependency.");

            builder.AddField("Enable Routing", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableOutboundIndustryRerouting,
                () =>
                {
                    FuseSettings.SetEnableOutboundIndustryRerouting(!FuseSettings.EnableOutboundIndustryRerouting);
                    builder.Rebuild();
                }));

            builder.AddField("Route Chance", control: builder.AddSliderQuantized(
                () => FuseSettings.OutboundIndustryRerouteChance,
                () => (FuseSettings.OutboundIndustryRerouteChance * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%",
                FuseSettings.SetOutboundIndustryRerouteChance,
                0.05f,
                0f,
                1f,
                FuseSettings.SetOutboundIndustryRerouteChance));

            builder.AddField("Capacity Target", control: builder.AddSliderQuantized(
                () => FuseSettings.OutboundIndustryFillFactor,
                () => FuseSettings.OutboundIndustryFillFactor.ToString("0.0x", System.Globalization.CultureInfo.InvariantCulture),
                FuseSettings.SetOutboundIndustryFillFactor,
                0.1f,
                0.1f,
                3f,
                FuseSettings.SetOutboundIndustryFillFactor));

            builder.AddField("Payment", control: builder.AddSliderQuantized(
                () => FuseSettings.OutboundIndustryPaymentMultiplier,
                () => FuseSettings.OutboundIndustryPaymentMultiplier.ToString("0.0x", System.Globalization.CultureInfo.InvariantCulture),
                FuseSettings.SetOutboundIndustryPaymentMultiplier,
                0.1f,
                0f,
                10f,
                FuseSettings.SetOutboundIndustryPaymentMultiplier));

            AddOutboundRoutingToggle(
                builder,
                "Allow Short Trips",
                FuseSettings.OutboundIndustryAllowShortTrips,
                FuseSettings.SetOutboundIndustryAllowShortTrips);
            AddOutboundRoutingToggle(
                builder,
                "Ignore Origin",
                FuseSettings.OutboundIndustryIgnoreOrigin,
                FuseSettings.SetOutboundIndustryIgnoreOrigin);
            AddOutboundRoutingToggle(
                builder,
                "Shuffle Orders",
                FuseSettings.OutboundIndustryPreventBlocking,
                FuseSettings.SetOutboundIndustryPreventBlocking);

            builder.AddLabel(
                "If both old packages are requested, the configurable SomeKindOfMadness behavior wins. " +
                "A routing extension can inspect or adjust candidates through FUSE's native outbound-routing event.");

            builder.AddSection("Interchange-to-Interchange Traffic");
            builder.AddLabel(
                "Replaces Interchange2Interchange when an enabled package requests it. " +
                "Each contracted source interchange can create a daily cut for every other enabled interchange.");
            AddIntegerField(
                builder,
                "Maximum Cars / Cut",
                FuseSettings.InterchangeToInterchangeMaximumCars,
                FuseSettings.SetInterchangeToInterchangeMaximumCars);

            builder.AddSection("For Your Convenience");
            builder.AddLabel(
                "These visual additions activate only when an enabled package requests ForYourConvenience. " +
                "The live Industry Dashboard is always available under Tools, and station-map actions are attached without replacing existing icon actions.");
            AddLegacyToggle(
                builder,
                "Caboose Map Icons",
                FuseSettings.ForYourConvenienceShowCabooseIcons,
                FuseSettings.SetForYourConvenienceShowCabooseIcons);
            AddLegacyToggle(
                builder,
                "Car Tag Speed",
                FuseSettings.ForYourConvenienceShowCarTagMph,
                FuseSettings.SetForYourConvenienceShowCarTagMph);
            AddLegacyToggle(
                builder,
                "Car Tag Loads",
                FuseSettings.ForYourConvenienceShowCarTagLoads,
                FuseSettings.SetForYourConvenienceShowCarTagLoads);

            builder.Spacer(32f);
        }

        private static void AddOutboundRoutingToggle(
            UIPanelBuilder builder,
            string label,
            bool enabled,
            Action<bool> setter)
        {
            builder.AddField(label, control: BuildToggleBoxWithButton(
                builder,
                enabled,
                () =>
                {
                    setter(!enabled);
                    builder.Rebuild();
                }));
        }

        private static void AddLegacyToggle(
            UIPanelBuilder builder,
            string label,
            bool enabled,
            Action<bool> setter)
        {
            builder.AddField(label, control: BuildToggleBoxWithButton(
                builder,
                enabled,
                () =>
                {
                    setter(!enabled);
                    builder.Rebuild();
                }));
        }

        private static void AddIntegerField(UIPanelBuilder builder, string label, int value, Action<int> setter)
        {
            builder.AddField(
                label,
                builder.AddInputField(
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    text =>
                    {
                        int parsed;
                        if (int.TryParse(
                            text,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out parsed))
                        {
                            setter(parsed);
                        }
                    }));
        }

        private static string FormatServiceInterval(int minutes)
        {
            return minutes < 60
                ? minutes + " min"
                : (minutes / 60f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " h";
        }

        private static string FormatServiceHour(float hour)
        {
            var totalMinutes = (int)Math.Round(hour * 60f);
            var displayHour = totalMinutes / 60;
            var displayMinute = totalMinutes % 60;
            return $"{displayHour:00}:{displayMinute:00}";
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

            builder.AddSection("Updates");

            builder.AddField("Check for Updates", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.EnableUpdateCheck,
                () =>
                {
                    FuseSettings.SetEnableUpdateCheck(!FuseSettings.EnableUpdateCheck);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                FuseVersionCheck.UpdateAvailable
                    ? $"Update available: FUSE {FuseVersionCheck.LatestVersionText} (you have {FuseVersionCheck.CurrentVersionText}). See the Status page for the download link."
                    : "On startup, asks GitHub whether a newer stable FUSE release exists and shows a notice if so. The request carries no FUSE account, save, or mod-list data — only the normal metadata of an HTTPS request to GitHub's public API. Takes effect on the next game start.");

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

            builder.AddField("Force 8 GB VRAM Mode", control: BuildToggleBoxWithButton(
                builder,
                FuseSettings.ForceConstrainedVramMode,
                () =>
                {
                    FuseSettings.SetForceConstrainedVramMode(!FuseSettings.ForceConstrainedVramMode);
                    builder.Rebuild();
                }));

            builder.AddLabel(
                "For comparison testing on larger GPUs: applies the constrained-card " +
                "one-level texture mip cap while preserving normal scenery distance. " +
                "Restart before capturing results.");

            builder.AddField("Spike Floor", control: builder.AddSliderQuantized(
                () => FuseSettings.FrameSpikeThresholdMs,
                () => $"{FuseSettings.FrameSpikeThresholdMs:F0}ms",
                FuseSettings.PreviewFrameSpikeThresholdMs,
                5f,
                FuseSettings.MinFrameSpikeThresholdMs,
                FuseSettings.MaxFrameSpikeThresholdMs,
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
