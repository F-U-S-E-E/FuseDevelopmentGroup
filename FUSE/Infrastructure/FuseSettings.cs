using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseSettings
    {
        private static readonly object UserSettingsCommitGate = new object();

        public const bool DefaultEnableExperimentalEarlyScenePathSuppression = false;
        public const bool DefaultMirrorInfoToPlayerLog = false;
        public const bool DefaultMirrorAssetPacksToLocalLow = false;
        public const bool DefaultVerboseApplyReportDetails = false;
        public const bool DefaultEnableSceneryCullingDiagnostics = false;
        public const bool DefaultEnableTargetedTerrainInvalidation = false;
        public const bool DefaultBlockNonHostMultiplayerClientWorldApply = false;
        public const bool DefaultShowAdvancedHealthDetails = false;
        public const bool DefaultShowTrackDebugOverlay = false;
        public const bool DefaultShowTrackDebugSpanPaths = false;
        public const bool DefaultShowSceneryDebugOverlay = false;
        public const bool DefaultShowSceneryDebugAdvanced = false;
        // World-Labels overlay defaults. The master toggle is opt-in; per-kind
        // toggles default to a sensible "show the things most authors care
        // about" subset so flipping the master switch shows something useful
        // without paving the screen in track-node labels (~1000+ on a typical
        // map). Authors can opt into the dense kinds individually.
        public const bool DefaultShowWorldLabelsOverlay = false;
        public const bool DefaultWorldLabelsShowScenery = true;
        public const bool DefaultWorldLabelsShowSceneClones = true;
        public const bool DefaultWorldLabelsShowIndustries = true;
        public const bool DefaultWorldLabelsShowTrackNodes = false;
        public const bool DefaultWorldLabelsShowTrackSegments = false;
        // Synthetic UMM rows add every legacy data package to UMM's global mod list.
        // UMM walks that list from several per-frame callbacks, so keep the rows opt-in;
        // FUSE's own Mods page remains the primary place to inspect legacy packages.
        public const bool DefaultShowLegacyModsInUmm = false;
        // FUSE-owned enhanced loading screen (issue #83): replaces the bare stock
        // "Loading…" screen with staged progress + a current-step label that stays
        // up until FUSE's own post-load pipeline finishes. On by default; one switch
        // falls the whole feature back to the untouched stock screen.
        public const bool DefaultEnableEnhancedLoadingScreen = true;
        // When off (default), the per-car Visual Condition slider can only make
        // a car look more worn than its mechanical condition; when on, the
        // visual override applies verbatim so worn cars can look fresh.
        public const bool DefaultDecoupleVisualConditionLimits = false;
        // Spawn-time visual-condition randomization is opt-in; the default
        // range mirrors the legacy behavior players already expect: mostly
        // presentable cars (0.6) up to factory-fresh (1.0).
        public const bool DefaultRandomizeVisualConditionOnSpawn = false;
        public const float DefaultRandomVisualConditionMin = 0.6f;
        public const float DefaultRandomVisualConditionMax = 1f;
        // Frame-spike diagnostic (stutter attribution): off by default — it is a
        // measurement tool, not a fix. The threshold marks the frame duration at
        // which a frame is logged as a spike; 100 ms ≈ a clearly felt hitch at
        // any refresh rate without flagging ordinary frame-time noise.
        public const bool DefaultEnableFrameSpikeDiagnostics = false;
        public const float DefaultFrameSpikeThresholdMs = 100f;
        // Test override for reproducing the constrained-card scenery policy on
        // higher-VRAM hardware. Off by default and persisted as a user setting.
        public const bool DefaultForceConstrainedVramMode = false;
        // Unity's native-allocation leak stacks are process-wide and expensive.
        // Keep them opt-in and restore the host's prior mode when FUSE unloads.
        public const bool DefaultEnableNativeLeakStackTraces = false;
        // Startup check that asks GitHub (the canonical version authority) whether
        // a newer stable FUSE release exists and, if so, surfaces a non-blocking
        // notice. On by default; one switch turns off all network access for it.
        public const bool DefaultEnableUpdateCheck = true;
        // Native replacement for ZAMU FallFromGrace. The defaults preserve the
        // base game's grace calculation exactly (minimum 0, x1, +0) while still
        // enabling the useful due-date row in the car inspector.
        public const int DefaultGraceMinimumDays = 0;
        public const int DefaultGraceMultiplier = 1;
        public const int DefaultGraceAddedDays = 0;
        public const int DefaultInterchangeServiceIntervalMinutes = 150;
        public const bool DefaultInterchangeContinuousService = false;
        public const float DefaultInterchangeNotBeforeHour = 0f;
        public const float DefaultInterchangeNotAfterHour = 24f;
        public const bool DefaultEnableOutboundIndustryRerouting = false;
        public const float DefaultOutboundIndustryRerouteChance = 0.25f;
        public const float DefaultOutboundIndustryFillFactor = 1f;
        public const float DefaultOutboundIndustryPaymentMultiplier = 1f;
        public const bool DefaultOutboundIndustryAllowShortTrips = false;
        public const bool DefaultOutboundIndustryIgnoreOrigin = false;
        public const bool DefaultOutboundIndustryPreventBlocking = false;
        public const int DefaultInterchangeToInterchangeMaximumCars = 30;
        public const bool DefaultForYourConvenienceShowCabooseIcons = false;
        public const bool DefaultForYourConvenienceShowCarTagMph = false;
        public const bool DefaultForYourConvenienceShowCarTagLoads = false;
        // The cold load of a direct (fuseasset://) asset pack goes through the
        // game's public ContainerSerialization.Deserialize so old-loader
        // Harmony postfixes (LegosLibraryOfStuff clone/edit injection) apply
        // to mod packs exactly as they do to natively loaded packs. This is
        // the escape hatch back to FUSE's Newtonsoft-only loader if a field
        // regression ever needs it; it is not a normal user setting.
        public const bool DefaultDirectStoreNativeDeserialize = true;
        public const float ExperimentalEarlyScenePathSuppressionTimeoutSeconds = 8f;

        public static bool EnableExperimentalEarlyScenePathSuppression { get; private set; } =
            DefaultEnableExperimentalEarlyScenePathSuppression;

        public static bool MirrorInfoToPlayerLog { get; private set; } = DefaultMirrorInfoToPlayerLog;

        public static bool MirrorAssetPacksToLocalLow { get; private set; } = DefaultMirrorAssetPacksToLocalLow;

        public static bool VerboseApplyReportDetails { get; private set; } = DefaultVerboseApplyReportDetails;

        public static bool EnableSceneryCullingDiagnostics { get; private set; } = DefaultEnableSceneryCullingDiagnostics;

        // Experimental (issue #76 follow-up): when on, the post-apply terrain rebuild
        // is narrowed to the tiles FUSE actually touched (MapManager.Invalidate) instead
        // of a full MapManager.RebuildAll teardown+reload. Big load-time win but timing-
        // sensitive (masks load async); default OFF until validated in-game. Falls back
        // to the full rebuild whenever no footprint was captured.
        public static bool EnableTargetedTerrainInvalidation { get; private set; } = DefaultEnableTargetedTerrainInvalidation;

        public static bool BlockNonHostMultiplayerClientWorldApply { get; private set; } =
            DefaultBlockNonHostMultiplayerClientWorldApply;

        public static bool ShowAdvancedHealthDetails { get; private set; } = DefaultShowAdvancedHealthDetails;

        public static bool ShowTrackDebugOverlay { get; private set; } = DefaultShowTrackDebugOverlay;

        public static bool ShowTrackDebugSpanPaths { get; private set; } = DefaultShowTrackDebugSpanPaths;

        public static bool ShowSceneryDebugOverlay { get; private set; } = DefaultShowSceneryDebugOverlay;

        public static bool ShowSceneryDebugAdvanced { get; private set; } = DefaultShowSceneryDebugAdvanced;

        public static bool ShowWorldLabelsOverlay { get; private set; } = DefaultShowWorldLabelsOverlay;

        public static bool WorldLabelsShowScenery { get; private set; } = DefaultWorldLabelsShowScenery;

        public static bool WorldLabelsShowSceneClones { get; private set; } = DefaultWorldLabelsShowSceneClones;

        public static bool WorldLabelsShowIndustries { get; private set; } = DefaultWorldLabelsShowIndustries;

        public static bool WorldLabelsShowTrackNodes { get; private set; } = DefaultWorldLabelsShowTrackNodes;

        public static bool WorldLabelsShowTrackSegments { get; private set; } = DefaultWorldLabelsShowTrackSegments;

        public static bool ShowLegacyModsInUmm { get; private set; } = DefaultShowLegacyModsInUmm;

        public static bool EnableEnhancedLoadingScreen { get; private set; } = DefaultEnableEnhancedLoadingScreen;

        public static bool DecoupleVisualConditionLimits { get; private set; } = DefaultDecoupleVisualConditionLimits;

        public static bool RandomizeVisualConditionOnSpawn { get; private set; } = DefaultRandomizeVisualConditionOnSpawn;

        public static float RandomVisualConditionMin { get; private set; } = DefaultRandomVisualConditionMin;

        public static float RandomVisualConditionMax { get; private set; } = DefaultRandomVisualConditionMax;

        public static bool EnableFrameSpikeDiagnostics { get; private set; } = DefaultEnableFrameSpikeDiagnostics;

        public static float FrameSpikeThresholdMs { get; private set; } = DefaultFrameSpikeThresholdMs;

        public static bool ForceConstrainedVramMode { get; private set; } =
            DefaultForceConstrainedVramMode;

        public static bool EnableNativeLeakStackTraces { get; private set; } = DefaultEnableNativeLeakStackTraces;

        public static bool EnableUpdateCheck { get; private set; } = DefaultEnableUpdateCheck;

        public static int GraceMinimumDays { get; private set; } = DefaultGraceMinimumDays;

        public static int GraceMultiplier { get; private set; } = DefaultGraceMultiplier;

        public static int GraceAddedDays { get; private set; } = DefaultGraceAddedDays;

        public static int InterchangeServiceIntervalMinutes { get; private set; } =
            DefaultInterchangeServiceIntervalMinutes;

        public static bool InterchangeContinuousService { get; private set; } =
            DefaultInterchangeContinuousService;

        public static float InterchangeNotBeforeHour { get; private set; } =
            DefaultInterchangeNotBeforeHour;

        public static float InterchangeNotAfterHour { get; private set; } =
            DefaultInterchangeNotAfterHour;

        public static bool EnableOutboundIndustryRerouting { get; private set; } =
            DefaultEnableOutboundIndustryRerouting;

        public static float OutboundIndustryRerouteChance { get; private set; } =
            DefaultOutboundIndustryRerouteChance;

        public static float OutboundIndustryFillFactor { get; private set; } =
            DefaultOutboundIndustryFillFactor;

        public static float OutboundIndustryPaymentMultiplier { get; private set; } =
            DefaultOutboundIndustryPaymentMultiplier;

        public static bool OutboundIndustryAllowShortTrips { get; private set; } =
            DefaultOutboundIndustryAllowShortTrips;

        public static bool OutboundIndustryIgnoreOrigin { get; private set; } =
            DefaultOutboundIndustryIgnoreOrigin;

        public static bool OutboundIndustryPreventBlocking { get; private set; } =
            DefaultOutboundIndustryPreventBlocking;

        public static int InterchangeToInterchangeMaximumCars { get; private set; } =
            DefaultInterchangeToInterchangeMaximumCars;

        public static bool ForYourConvenienceShowCabooseIcons { get; private set; } =
            DefaultForYourConvenienceShowCabooseIcons;

        public static bool ForYourConvenienceShowCarTagMph { get; private set; } =
            DefaultForYourConvenienceShowCarTagMph;

        public static bool ForYourConvenienceShowCarTagLoads { get; private set; } =
            DefaultForYourConvenienceShowCarTagLoads;

        public static bool DirectStoreNativeDeserialize { get; private set; } = DefaultDirectStoreNativeDeserialize;

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
            MirrorInfoToPlayerLog = DefaultMirrorInfoToPlayerLog;
            MirrorAssetPacksToLocalLow = DefaultMirrorAssetPacksToLocalLow;
            VerboseApplyReportDetails = DefaultVerboseApplyReportDetails;
            EnableSceneryCullingDiagnostics = DefaultEnableSceneryCullingDiagnostics;
            EnableTargetedTerrainInvalidation = DefaultEnableTargetedTerrainInvalidation;
            BlockNonHostMultiplayerClientWorldApply = DefaultBlockNonHostMultiplayerClientWorldApply;
            ShowAdvancedHealthDetails = DefaultShowAdvancedHealthDetails;
            ShowTrackDebugOverlay = DefaultShowTrackDebugOverlay;
            ShowTrackDebugSpanPaths = DefaultShowTrackDebugSpanPaths;
            ShowSceneryDebugOverlay = DefaultShowSceneryDebugOverlay;
            ShowSceneryDebugAdvanced = DefaultShowSceneryDebugAdvanced;
            ShowWorldLabelsOverlay = DefaultShowWorldLabelsOverlay;
            WorldLabelsShowScenery = DefaultWorldLabelsShowScenery;
            WorldLabelsShowSceneClones = DefaultWorldLabelsShowSceneClones;
            WorldLabelsShowIndustries = DefaultWorldLabelsShowIndustries;
            WorldLabelsShowTrackNodes = DefaultWorldLabelsShowTrackNodes;
            WorldLabelsShowTrackSegments = DefaultWorldLabelsShowTrackSegments;
            ShowLegacyModsInUmm = DefaultShowLegacyModsInUmm;
            EnableEnhancedLoadingScreen = DefaultEnableEnhancedLoadingScreen;
            DecoupleVisualConditionLimits = DefaultDecoupleVisualConditionLimits;
            RandomizeVisualConditionOnSpawn = DefaultRandomizeVisualConditionOnSpawn;
            RandomVisualConditionMin = DefaultRandomVisualConditionMin;
            RandomVisualConditionMax = DefaultRandomVisualConditionMax;
            EnableFrameSpikeDiagnostics = DefaultEnableFrameSpikeDiagnostics;
            FrameSpikeThresholdMs = DefaultFrameSpikeThresholdMs;
            ForceConstrainedVramMode = DefaultForceConstrainedVramMode;
            EnableNativeLeakStackTraces = DefaultEnableNativeLeakStackTraces;
            EnableUpdateCheck = DefaultEnableUpdateCheck;
            GraceMinimumDays = DefaultGraceMinimumDays;
            GraceMultiplier = DefaultGraceMultiplier;
            GraceAddedDays = DefaultGraceAddedDays;
            InterchangeServiceIntervalMinutes = DefaultInterchangeServiceIntervalMinutes;
            InterchangeContinuousService = DefaultInterchangeContinuousService;
            InterchangeNotBeforeHour = DefaultInterchangeNotBeforeHour;
            InterchangeNotAfterHour = DefaultInterchangeNotAfterHour;
            EnableOutboundIndustryRerouting = DefaultEnableOutboundIndustryRerouting;
            OutboundIndustryRerouteChance = DefaultOutboundIndustryRerouteChance;
            OutboundIndustryFillFactor = DefaultOutboundIndustryFillFactor;
            OutboundIndustryPaymentMultiplier = DefaultOutboundIndustryPaymentMultiplier;
            OutboundIndustryAllowShortTrips = DefaultOutboundIndustryAllowShortTrips;
            OutboundIndustryIgnoreOrigin = DefaultOutboundIndustryIgnoreOrigin;
            OutboundIndustryPreventBlocking = DefaultOutboundIndustryPreventBlocking;
            InterchangeToInterchangeMaximumCars = DefaultInterchangeToInterchangeMaximumCars;
            ForYourConvenienceShowCabooseIcons = DefaultForYourConvenienceShowCabooseIcons;
            ForYourConvenienceShowCarTagMph = DefaultForYourConvenienceShowCarTagMph;
            ForYourConvenienceShowCarTagLoads = DefaultForYourConvenienceShowCarTagLoads;
            DirectStoreNativeDeserialize = DefaultDirectStoreNativeDeserialize;
            FuseLog.MirrorInfoToPlayerLog = MirrorInfoToPlayerLog;

            var infoPath = Path.Combine(modEntry?.Path ?? string.Empty, "Info.json");
            if (string.IsNullOrWhiteSpace(infoPath) || !File.Exists(infoPath))
            {
                FuseLog.Warning("FUSE could not read Info.json settings; experimental early scene-path suppression remains disabled.");
                return;
            }

            try
            {
                var info = JObject.Parse(File.ReadAllText(infoPath));
                var settings = info["Settings"];
                EnableExperimentalEarlyScenePathSuppression =
                    ReadBool(settings, "EnableExperimentalEarlyScenePathSuppression", DefaultEnableExperimentalEarlyScenePathSuppression);
                MirrorInfoToPlayerLog =
                    ReadBool(settings, "MirrorInfoToPlayerLog", DefaultMirrorInfoToPlayerLog);
                MirrorAssetPacksToLocalLow =
                    ReadBool(settings, "MirrorAssetPacksToLocalLow", DefaultMirrorAssetPacksToLocalLow);
                VerboseApplyReportDetails =
                    ReadBool(settings, "VerboseApplyReportDetails", DefaultVerboseApplyReportDetails);
                EnableSceneryCullingDiagnostics =
                    ReadBool(settings, "EnableSceneryCullingDiagnostics", DefaultEnableSceneryCullingDiagnostics);
                EnableTargetedTerrainInvalidation =
                    ReadBool(settings, "EnableTargetedTerrainInvalidation", DefaultEnableTargetedTerrainInvalidation);
                BlockNonHostMultiplayerClientWorldApply =
                    ReadBool(settings, "BlockNonHostMultiplayerClientWorldApply", DefaultBlockNonHostMultiplayerClientWorldApply);
                ShowAdvancedHealthDetails =
                    ReadBool(settings, "ShowAdvancedHealthDetails", DefaultShowAdvancedHealthDetails);
                ShowTrackDebugOverlay =
                    ReadBool(settings, "ShowTrackDebugOverlay", DefaultShowTrackDebugOverlay);
                ShowTrackDebugSpanPaths =
                    ReadBool(settings, "ShowTrackDebugSpanPaths", DefaultShowTrackDebugSpanPaths);
                ShowSceneryDebugOverlay =
                    ReadBool(settings, "ShowSceneryDebugOverlay", DefaultShowSceneryDebugOverlay);
                ShowSceneryDebugAdvanced =
                    ReadBool(settings, "ShowSceneryDebugAdvanced", DefaultShowSceneryDebugAdvanced);
                ShowWorldLabelsOverlay =
                    ReadBool(settings, "ShowWorldLabelsOverlay", DefaultShowWorldLabelsOverlay);
                WorldLabelsShowScenery =
                    ReadBool(settings, "WorldLabelsShowScenery", DefaultWorldLabelsShowScenery);
                WorldLabelsShowSceneClones =
                    ReadBool(settings, "WorldLabelsShowSceneClones", DefaultWorldLabelsShowSceneClones);
                WorldLabelsShowIndustries =
                    ReadBool(settings, "WorldLabelsShowIndustries", DefaultWorldLabelsShowIndustries);
                WorldLabelsShowTrackNodes =
                    ReadBool(settings, "WorldLabelsShowTrackNodes", DefaultWorldLabelsShowTrackNodes);
                WorldLabelsShowTrackSegments =
                    ReadBool(settings, "WorldLabelsShowTrackSegments", DefaultWorldLabelsShowTrackSegments);
                ShowLegacyModsInUmm =
                    ReadBool(settings, "ShowLegacyModsInUmm", DefaultShowLegacyModsInUmm);
                EnableEnhancedLoadingScreen =
                    ReadBool(settings, "EnableEnhancedLoadingScreen", DefaultEnableEnhancedLoadingScreen);
                DecoupleVisualConditionLimits =
                    ReadBool(settings, "DecoupleVisualConditionLimits", DefaultDecoupleVisualConditionLimits);
                RandomizeVisualConditionOnSpawn =
                    ReadBool(settings, "RandomizeVisualConditionOnSpawn", DefaultRandomizeVisualConditionOnSpawn);
                RandomVisualConditionMin = Mathf.Clamp01(
                    ReadFloat(settings, "RandomVisualConditionMin", DefaultRandomVisualConditionMin));
                RandomVisualConditionMax = Mathf.Clamp01(
                    ReadFloat(settings, "RandomVisualConditionMax", DefaultRandomVisualConditionMax));
                EnableFrameSpikeDiagnostics =
                    ReadBool(settings, "EnableFrameSpikeDiagnostics", DefaultEnableFrameSpikeDiagnostics);
                FrameSpikeThresholdMs = ClampFrameSpikeThresholdMs(
                    ReadFloat(settings, "FrameSpikeThresholdMs", DefaultFrameSpikeThresholdMs));
                ForceConstrainedVramMode =
                    ReadBool(settings, "ForceConstrainedVramMode", DefaultForceConstrainedVramMode);
                EnableNativeLeakStackTraces =
                    ReadBool(settings, "EnableNativeLeakStackTraces", DefaultEnableNativeLeakStackTraces);
                EnableUpdateCheck =
                    ReadBool(settings, "EnableUpdateCheck", DefaultEnableUpdateCheck);
                GraceMinimumDays =
                    ReadInt(settings, "GraceMinimumDays", DefaultGraceMinimumDays);
                GraceMultiplier =
                    ReadInt(settings, "GraceMultiplier", DefaultGraceMultiplier);
                GraceAddedDays =
                    ReadInt(settings, "GraceAddedDays", DefaultGraceAddedDays);
                InterchangeServiceIntervalMinutes = NormalizeInterchangeServiceIntervalMinutes(
                    ReadInt(settings, "InterchangeServiceIntervalMinutes", DefaultInterchangeServiceIntervalMinutes));
                InterchangeContinuousService =
                    ReadBool(settings, "InterchangeContinuousService", DefaultInterchangeContinuousService);
                InterchangeNotBeforeHour = ClampInterchangeServiceHour(
                    ReadFloat(settings, "InterchangeNotBeforeHour", DefaultInterchangeNotBeforeHour));
                InterchangeNotAfterHour = ClampInterchangeServiceHour(
                    ReadFloat(settings, "InterchangeNotAfterHour", DefaultInterchangeNotAfterHour));
                EnableOutboundIndustryRerouting =
                    ReadBool(settings, "EnableOutboundIndustryRerouting", DefaultEnableOutboundIndustryRerouting);
                OutboundIndustryRerouteChance = Mathf.Clamp01(
                    ReadFloat(settings, "OutboundIndustryRerouteChance", DefaultOutboundIndustryRerouteChance));
                OutboundIndustryFillFactor = ClampOutboundIndustryFillFactor(
                    ReadFloat(settings, "OutboundIndustryFillFactor", DefaultOutboundIndustryFillFactor));
                OutboundIndustryPaymentMultiplier = ClampOutboundIndustryPaymentMultiplier(
                    ReadFloat(settings, "OutboundIndustryPaymentMultiplier", DefaultOutboundIndustryPaymentMultiplier));
                OutboundIndustryAllowShortTrips =
                    ReadBool(settings, "OutboundIndustryAllowShortTrips", DefaultOutboundIndustryAllowShortTrips);
                OutboundIndustryIgnoreOrigin =
                    ReadBool(settings, "OutboundIndustryIgnoreOrigin", DefaultOutboundIndustryIgnoreOrigin);
                OutboundIndustryPreventBlocking =
                    ReadBool(settings, "OutboundIndustryPreventBlocking", DefaultOutboundIndustryPreventBlocking);
                InterchangeToInterchangeMaximumCars = ClampInterchangeToInterchangeMaximumCars(
                    ReadInt(
                        settings,
                        "InterchangeToInterchangeMaximumCars",
                        DefaultInterchangeToInterchangeMaximumCars));
                ForYourConvenienceShowCabooseIcons = ReadBool(
                    settings,
                    "ForYourConvenienceShowCabooseIcons",
                    DefaultForYourConvenienceShowCabooseIcons);
                ForYourConvenienceShowCarTagMph = ReadBool(
                    settings,
                    "ForYourConvenienceShowCarTagMph",
                    DefaultForYourConvenienceShowCarTagMph);
                ForYourConvenienceShowCarTagLoads = ReadBool(
                    settings,
                    "ForYourConvenienceShowCarTagLoads",
                    DefaultForYourConvenienceShowCarTagLoads);
                DirectStoreNativeDeserialize =
                    ReadBool(settings, "DirectStoreNativeDeserialize", DefaultDirectStoreNativeDeserialize);
                ApplyUserOverrides();
                FuseLog.MirrorInfoToPlayerLog = MirrorInfoToPlayerLog;

                FuseLog.Info(
                    "FUSE settings loaded: " +
                    $"EnableExperimentalEarlyScenePathSuppression={EnableExperimentalEarlyScenePathSuppression} " +
                    $"MirrorInfoToPlayerLog={MirrorInfoToPlayerLog} " +
                    $"MirrorAssetPacksToLocalLow={MirrorAssetPacksToLocalLow} " +
                    $"VerboseApplyReportDetails={VerboseApplyReportDetails} " +
                    $"EnableSceneryCullingDiagnostics={EnableSceneryCullingDiagnostics} " +
                    $"EnableTargetedTerrainInvalidation={EnableTargetedTerrainInvalidation} " +
                    $"BlockNonHostMultiplayerClientWorldApply={BlockNonHostMultiplayerClientWorldApply} " +
                    $"ShowAdvancedHealthDetails={ShowAdvancedHealthDetails} " +
                    $"ShowTrackDebugOverlay={ShowTrackDebugOverlay} " +
                    $"ShowTrackDebugSpanPaths={ShowTrackDebugSpanPaths} " +
                    $"ShowSceneryDebugOverlay={ShowSceneryDebugOverlay} " +
                    $"ShowSceneryDebugAdvanced={ShowSceneryDebugAdvanced} " +
                    $"ShowWorldLabelsOverlay={ShowWorldLabelsOverlay} " +
                    $"WorldLabelsShowScenery={WorldLabelsShowScenery} " +
                    $"WorldLabelsShowSceneClones={WorldLabelsShowSceneClones} " +
                    $"WorldLabelsShowIndustries={WorldLabelsShowIndustries} " +
                    $"WorldLabelsShowTrackNodes={WorldLabelsShowTrackNodes} " +
                    $"WorldLabelsShowTrackSegments={WorldLabelsShowTrackSegments} " +
                    $"ShowLegacyModsInUmm={ShowLegacyModsInUmm} " +
                    $"EnableEnhancedLoadingScreen={EnableEnhancedLoadingScreen} " +
                    $"DecoupleVisualConditionLimits={DecoupleVisualConditionLimits} " +
                    $"RandomizeVisualConditionOnSpawn={RandomizeVisualConditionOnSpawn} " +
                    $"RandomVisualConditionMin={RandomVisualConditionMin} " +
                    $"RandomVisualConditionMax={RandomVisualConditionMax} " +
                    $"EnableFrameSpikeDiagnostics={EnableFrameSpikeDiagnostics} " +
                    $"FrameSpikeThresholdMs={FrameSpikeThresholdMs} " +
                    $"ForceConstrainedVramMode={ForceConstrainedVramMode} " +
                    $"EnableNativeLeakStackTraces={EnableNativeLeakStackTraces} " +
                    $"EnableUpdateCheck={EnableUpdateCheck} " +
                    $"GraceMinimumDays={GraceMinimumDays} " +
                    $"GraceMultiplier={GraceMultiplier} " +
                    $"GraceAddedDays={GraceAddedDays} " +
                    $"InterchangeServiceIntervalMinutes={InterchangeServiceIntervalMinutes} " +
                    $"InterchangeContinuousService={InterchangeContinuousService} " +
                    $"InterchangeNotBeforeHour={InterchangeNotBeforeHour} " +
                    $"InterchangeNotAfterHour={InterchangeNotAfterHour} " +
                    $"EnableOutboundIndustryRerouting={EnableOutboundIndustryRerouting} " +
                    $"OutboundIndustryRerouteChance={OutboundIndustryRerouteChance} " +
                    $"OutboundIndustryFillFactor={OutboundIndustryFillFactor} " +
                    $"OutboundIndustryPaymentMultiplier={OutboundIndustryPaymentMultiplier} " +
                    $"OutboundIndustryAllowShortTrips={OutboundIndustryAllowShortTrips} " +
                    $"OutboundIndustryIgnoreOrigin={OutboundIndustryIgnoreOrigin} " +
                    $"OutboundIndustryPreventBlocking={OutboundIndustryPreventBlocking} " +
                    $"InterchangeToInterchangeMaximumCars={InterchangeToInterchangeMaximumCars} " +
                    $"ForYourConvenienceShowCabooseIcons={ForYourConvenienceShowCabooseIcons} " +
                    $"ForYourConvenienceShowCarTagMph={ForYourConvenienceShowCarTagMph} " +
                    $"ForYourConvenienceShowCarTagLoads={ForYourConvenienceShowCarTagLoads} " +
                    $"DirectStoreNativeDeserialize={DirectStoreNativeDeserialize} " +
                    $"timeoutSeconds={ExperimentalEarlyScenePathSuppressionTimeoutSeconds}.");
            }
            catch (Exception ex)
            {
                EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
                MirrorInfoToPlayerLog = DefaultMirrorInfoToPlayerLog;
                MirrorAssetPacksToLocalLow = DefaultMirrorAssetPacksToLocalLow;
                VerboseApplyReportDetails = DefaultVerboseApplyReportDetails;
                EnableSceneryCullingDiagnostics = DefaultEnableSceneryCullingDiagnostics;
                EnableTargetedTerrainInvalidation = DefaultEnableTargetedTerrainInvalidation;
                BlockNonHostMultiplayerClientWorldApply = DefaultBlockNonHostMultiplayerClientWorldApply;
                ShowAdvancedHealthDetails = DefaultShowAdvancedHealthDetails;
                ShowTrackDebugOverlay = DefaultShowTrackDebugOverlay;
                ShowTrackDebugSpanPaths = DefaultShowTrackDebugSpanPaths;
                ShowSceneryDebugOverlay = DefaultShowSceneryDebugOverlay;
                ShowSceneryDebugAdvanced = DefaultShowSceneryDebugAdvanced;
                ShowWorldLabelsOverlay = DefaultShowWorldLabelsOverlay;
                WorldLabelsShowScenery = DefaultWorldLabelsShowScenery;
                WorldLabelsShowSceneClones = DefaultWorldLabelsShowSceneClones;
                WorldLabelsShowIndustries = DefaultWorldLabelsShowIndustries;
                WorldLabelsShowTrackNodes = DefaultWorldLabelsShowTrackNodes;
                WorldLabelsShowTrackSegments = DefaultWorldLabelsShowTrackSegments;
                ShowLegacyModsInUmm = DefaultShowLegacyModsInUmm;
                EnableEnhancedLoadingScreen = DefaultEnableEnhancedLoadingScreen;
                DecoupleVisualConditionLimits = DefaultDecoupleVisualConditionLimits;
                RandomizeVisualConditionOnSpawn = DefaultRandomizeVisualConditionOnSpawn;
                RandomVisualConditionMin = DefaultRandomVisualConditionMin;
                RandomVisualConditionMax = DefaultRandomVisualConditionMax;
                EnableFrameSpikeDiagnostics = DefaultEnableFrameSpikeDiagnostics;
                FrameSpikeThresholdMs = DefaultFrameSpikeThresholdMs;
                ForceConstrainedVramMode = DefaultForceConstrainedVramMode;
                EnableNativeLeakStackTraces = DefaultEnableNativeLeakStackTraces;
                EnableUpdateCheck = DefaultEnableUpdateCheck;
                GraceMinimumDays = DefaultGraceMinimumDays;
                GraceMultiplier = DefaultGraceMultiplier;
                GraceAddedDays = DefaultGraceAddedDays;
                InterchangeServiceIntervalMinutes = DefaultInterchangeServiceIntervalMinutes;
                InterchangeContinuousService = DefaultInterchangeContinuousService;
                InterchangeNotBeforeHour = DefaultInterchangeNotBeforeHour;
                InterchangeNotAfterHour = DefaultInterchangeNotAfterHour;
                EnableOutboundIndustryRerouting = DefaultEnableOutboundIndustryRerouting;
                OutboundIndustryRerouteChance = DefaultOutboundIndustryRerouteChance;
                OutboundIndustryFillFactor = DefaultOutboundIndustryFillFactor;
                OutboundIndustryPaymentMultiplier = DefaultOutboundIndustryPaymentMultiplier;
                OutboundIndustryAllowShortTrips = DefaultOutboundIndustryAllowShortTrips;
                OutboundIndustryIgnoreOrigin = DefaultOutboundIndustryIgnoreOrigin;
                OutboundIndustryPreventBlocking = DefaultOutboundIndustryPreventBlocking;
                InterchangeToInterchangeMaximumCars = DefaultInterchangeToInterchangeMaximumCars;
                ForYourConvenienceShowCabooseIcons = DefaultForYourConvenienceShowCabooseIcons;
                ForYourConvenienceShowCarTagMph = DefaultForYourConvenienceShowCarTagMph;
                ForYourConvenienceShowCarTagLoads = DefaultForYourConvenienceShowCarTagLoads;
                DirectStoreNativeDeserialize = DefaultDirectStoreNativeDeserialize;
                FuseLog.MirrorInfoToPlayerLog = MirrorInfoToPlayerLog;
                FuseLog.Exception($"FUSE failed to parse Info.json settings; experimental early scene-path suppression remains disabled", ex);
            }
        }

        public static void SetVerboseApplyReportDetails(bool enabled)
        {
            VerboseApplyReportDetails = enabled;
            SaveUserOverride(nameof(VerboseApplyReportDetails), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(VerboseApplyReportDetails)}={enabled}.");
        }

        public static void SetEnableSceneryCullingDiagnostics(bool enabled)
        {
            EnableSceneryCullingDiagnostics = enabled;
            RemoveUserOverride(nameof(EnableSceneryCullingDiagnostics));
            FuseLog.Info(
                $"FUSE session diagnostic changed: {nameof(EnableSceneryCullingDiagnostics)}={enabled}. " +
                "This diagnostic resets when the game restarts.");
        }

        // Transient (non-persisting) toggle used by FuseSceneryBenchmark to capture
        // scenery churn counts during a run without writing a user override. The
        // benchmark restores the prior value when the run finishes.
        internal static void SetSceneryCullingDiagnosticsTransient(bool enabled)
        {
            EnableSceneryCullingDiagnostics = enabled;
        }

        public static void SetEnableTargetedTerrainInvalidation(bool enabled)
        {
            EnableTargetedTerrainInvalidation = enabled;
            SaveUserOverride(nameof(EnableTargetedTerrainInvalidation), enabled);
            FuseLog.Warning(
                $"FUSE experimental setting changed: {nameof(EnableTargetedTerrainInvalidation)}={enabled}. " +
                "Takes effect on the next map load / terrain reload.");
        }

        public static void SetBlockNonHostMultiplayerClientWorldApply(bool enabled)
        {
            BlockNonHostMultiplayerClientWorldApply = enabled;
            SaveUserOverride(nameof(BlockNonHostMultiplayerClientWorldApply), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(BlockNonHostMultiplayerClientWorldApply)}={enabled}.");
        }

        public static void SetEnableExperimentalEarlyScenePathSuppression(bool enabled)
        {
            EnableExperimentalEarlyScenePathSuppression = enabled;
            SaveUserOverride(nameof(EnableExperimentalEarlyScenePathSuppression), enabled);
            FuseLog.Warning(
                $"FUSE experimental setting changed: {nameof(EnableExperimentalEarlyScenePathSuppression)}={enabled}. " +
                "This takes effect on the next map load.");
        }

        public static void SetShowAdvancedHealthDetails(bool enabled)
        {
            ShowAdvancedHealthDetails = enabled;
            SaveUserOverride(nameof(ShowAdvancedHealthDetails), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowAdvancedHealthDetails)}={enabled}.");
        }

        public static void SetShowTrackDebugOverlay(bool enabled)
        {
            ShowTrackDebugOverlay = enabled;
            SaveUserOverride(nameof(ShowTrackDebugOverlay), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowTrackDebugOverlay)}={enabled}.");
        }

        public static void SetShowTrackDebugSpanPaths(bool enabled)
        {
            ShowTrackDebugSpanPaths = enabled;
            SaveUserOverride(nameof(ShowTrackDebugSpanPaths), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowTrackDebugSpanPaths)}={enabled}.");
        }

        public static void SetShowSceneryDebugOverlay(bool enabled)
        {
            ShowSceneryDebugOverlay = enabled;
            SaveUserOverride(nameof(ShowSceneryDebugOverlay), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowSceneryDebugOverlay)}={enabled}.");
        }

        public static void SetShowSceneryDebugAdvanced(bool enabled)
        {
            ShowSceneryDebugAdvanced = enabled;
            SaveUserOverride(nameof(ShowSceneryDebugAdvanced), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowSceneryDebugAdvanced)}={enabled}.");
        }

        public static void SetShowWorldLabelsOverlay(bool enabled)
        {
            ShowWorldLabelsOverlay = enabled;
            SaveUserOverride(nameof(ShowWorldLabelsOverlay), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(ShowWorldLabelsOverlay)}={enabled}.");
        }

        public static void SetWorldLabelsShowScenery(bool enabled)
        {
            WorldLabelsShowScenery = enabled;
            SaveUserOverride(nameof(WorldLabelsShowScenery), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(WorldLabelsShowScenery)}={enabled}.");
        }

        public static void SetWorldLabelsShowSceneClones(bool enabled)
        {
            WorldLabelsShowSceneClones = enabled;
            SaveUserOverride(nameof(WorldLabelsShowSceneClones), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(WorldLabelsShowSceneClones)}={enabled}.");
        }

        public static void SetWorldLabelsShowIndustries(bool enabled)
        {
            WorldLabelsShowIndustries = enabled;
            SaveUserOverride(nameof(WorldLabelsShowIndustries), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(WorldLabelsShowIndustries)}={enabled}.");
        }

        public static void SetWorldLabelsShowTrackNodes(bool enabled)
        {
            WorldLabelsShowTrackNodes = enabled;
            SaveUserOverride(nameof(WorldLabelsShowTrackNodes), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(WorldLabelsShowTrackNodes)}={enabled}.");
        }

        public static void SetWorldLabelsShowTrackSegments(bool enabled)
        {
            WorldLabelsShowTrackSegments = enabled;
            SaveUserOverride(nameof(WorldLabelsShowTrackSegments), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(WorldLabelsShowTrackSegments)}={enabled}.");
        }

        public static void SetEnableEnhancedLoadingScreen(bool enabled)
        {
            EnableEnhancedLoadingScreen = enabled;
            SaveUserOverride(nameof(EnableEnhancedLoadingScreen), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(EnableEnhancedLoadingScreen)}={enabled}. Takes effect on the next map load.");
        }

        public static void SetDecoupleVisualConditionLimits(bool enabled)
        {
            DecoupleVisualConditionLimits = enabled;
            SaveUserOverride(nameof(DecoupleVisualConditionLimits), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(DecoupleVisualConditionLimits)}={enabled}.");
        }

        public static void SetRandomizeVisualConditionOnSpawn(bool enabled)
        {
            RandomizeVisualConditionOnSpawn = enabled;
            SaveUserOverride(nameof(RandomizeVisualConditionOnSpawn), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(RandomizeVisualConditionOnSpawn)}={enabled}.");
        }

        public static void SetRandomVisualConditionMin(float value)
        {
            RandomVisualConditionMin = Mathf.Clamp01(value);
            SaveUserOverride(nameof(RandomVisualConditionMin), RandomVisualConditionMin);
            FuseLog.Info($"FUSE setting changed: {nameof(RandomVisualConditionMin)}={RandomVisualConditionMin}.");
        }

        public static void SetRandomVisualConditionMax(float value)
        {
            RandomVisualConditionMax = Mathf.Clamp01(value);
            SaveUserOverride(nameof(RandomVisualConditionMax), RandomVisualConditionMax);
            FuseLog.Info($"FUSE setting changed: {nameof(RandomVisualConditionMax)}={RandomVisualConditionMax}.");
        }

        public static void SetEnableFrameSpikeDiagnostics(bool enabled)
        {
            EnableFrameSpikeDiagnostics = enabled;
            SaveUserOverride(nameof(EnableFrameSpikeDiagnostics), enabled);
            FuseLog.Info(
                $"FUSE setting changed: {nameof(EnableFrameSpikeDiagnostics)}={enabled} " +
                $"(threshold {FrameSpikeThresholdMs:F0}ms). Takes effect immediately.");
        }

        public static void SetForceConstrainedVramMode(bool enabled)
        {
            ForceConstrainedVramMode = enabled;
            SaveUserOverride(nameof(ForceConstrainedVramMode), enabled);
            FuseLog.Info(
                $"FUSE setting changed: {nameof(ForceConstrainedVramMode)}={enabled}. " +
                "Restart the game before collecting a comparison capture.");
        }

        // The spike-floor clamp: 20ms matches the read-time floor applied to
        // Info.json values (below that, ordinary frames at low fps would log
        // as spikes); 500ms is far past anything worth calling a "spike"
        // rather than a stall.
        internal const float MinFrameSpikeThresholdMs = 20f;
        internal const float MaxFrameSpikeThresholdMs = 500f;

        // Every path that writes FrameSpikeThresholdMs — Info.json load, user
        // override apply, slider preview, and persist — funnels through this
        // clamp so no source can smuggle a value outside the documented range.
        // NaN would sail through Mathf.Clamp (ReadFloat accepts the string
        // "NaN"), so it degrades to the default; infinities clamp normally.
        private static float ClampFrameSpikeThresholdMs(float thresholdMs)
        {
            if (float.IsNaN(thresholdMs))
            {
                return DefaultFrameSpikeThresholdMs;
            }

            return Mathf.Clamp(thresholdMs, MinFrameSpikeThresholdMs, MaxFrameSpikeThresholdMs);
        }

        /// <summary>
        /// Live preview while the settings slider is being dragged: updates
        /// the running value (the spike logger reads it per frame) without
        /// persisting, so a drag does not write the override file once per
        /// slider tick. <see cref="SetFrameSpikeThresholdMs"/> persists on
        /// release.
        /// </summary>
        internal static void PreviewFrameSpikeThresholdMs(float thresholdMs)
        {
            FrameSpikeThresholdMs = ClampFrameSpikeThresholdMs(thresholdMs);
        }

        public static void SetFrameSpikeThresholdMs(float thresholdMs)
        {
            FrameSpikeThresholdMs = ClampFrameSpikeThresholdMs(thresholdMs);
            SaveUserOverride(nameof(FrameSpikeThresholdMs), FrameSpikeThresholdMs);
            FuseLog.Info(
                $"FUSE setting changed: {nameof(FrameSpikeThresholdMs)}={FrameSpikeThresholdMs:F0}ms. " +
                "Takes effect immediately.");
        }

        public static void SetEnableNativeLeakStackTraces(bool enabled)
        {
            EnableNativeLeakStackTraces = enabled;
            SaveUserOverride(nameof(EnableNativeLeakStackTraces), enabled);
            FuseNativeLeakDiagnostic.Apply(enabled);
        }

        public static void SetEnableUpdateCheck(bool enabled)
        {
            EnableUpdateCheck = enabled;
            SaveUserOverride(nameof(EnableUpdateCheck), enabled);
            FuseLog.Info(
                $"FUSE setting changed: {nameof(EnableUpdateCheck)}={enabled}. " +
                "Takes effect on the next game start.");
        }

        public static void SetGraceMinimumDays(int value)
        {
            GraceMinimumDays = value;
            SaveUserOverride(nameof(GraceMinimumDays), value);
            FuseLog.Info($"FUSE legacy gameplay setting changed: {nameof(GraceMinimumDays)}={value}.");
        }

        public static void SetGraceMultiplier(int value)
        {
            GraceMultiplier = value;
            SaveUserOverride(nameof(GraceMultiplier), value);
            FuseLog.Info($"FUSE legacy gameplay setting changed: {nameof(GraceMultiplier)}={value}.");
        }

        public static void SetGraceAddedDays(int value)
        {
            GraceAddedDays = value;
            SaveUserOverride(nameof(GraceAddedDays), value);
            FuseLog.Info($"FUSE legacy gameplay setting changed: {nameof(GraceAddedDays)}={value}.");
        }

        public static void SetInterchangeServiceIntervalMinutes(int value)
        {
            InterchangeServiceIntervalMinutes = NormalizeInterchangeServiceIntervalMinutes(value);
            SaveUserOverride(nameof(InterchangeServiceIntervalMinutes), InterchangeServiceIntervalMinutes);
            FuseLog.Info(
                $"FUSE legacy gameplay setting changed: {nameof(InterchangeServiceIntervalMinutes)}=" +
                $"{InterchangeServiceIntervalMinutes}.");
        }

        public static void SetInterchangeContinuousService(bool enabled)
        {
            InterchangeContinuousService = enabled;
            SaveUserOverride(nameof(InterchangeContinuousService), enabled);
            FuseLog.Info(
                $"FUSE legacy gameplay setting changed: {nameof(InterchangeContinuousService)}={enabled}.");
        }

        public static void SetInterchangeNotBeforeHour(float value)
        {
            InterchangeNotBeforeHour = ClampInterchangeServiceHour(value);
            SaveUserOverride(nameof(InterchangeNotBeforeHour), InterchangeNotBeforeHour);
            FuseLog.Info(
                $"FUSE legacy gameplay setting changed: {nameof(InterchangeNotBeforeHour)}=" +
                $"{InterchangeNotBeforeHour:F2}.");
        }

        public static void SetInterchangeNotAfterHour(float value)
        {
            InterchangeNotAfterHour = ClampInterchangeServiceHour(value);
            SaveUserOverride(nameof(InterchangeNotAfterHour), InterchangeNotAfterHour);
            FuseLog.Info(
                $"FUSE legacy gameplay setting changed: {nameof(InterchangeNotAfterHour)}=" +
                $"{InterchangeNotAfterHour:F2}.");
        }

        internal static int NormalizeInterchangeServiceIntervalMinutes(int value)
        {
            return value <= 0 ? DefaultInterchangeServiceIntervalMinutes : Math.Min(value, 7 * 24 * 60);
        }

        internal static float ClampInterchangeServiceHour(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, 24f);
        }

        public static void SetEnableOutboundIndustryRerouting(bool enabled)
        {
            EnableOutboundIndustryRerouting = enabled;
            SaveUserOverride(nameof(EnableOutboundIndustryRerouting), enabled);
        }

        public static void SetOutboundIndustryRerouteChance(float value)
        {
            OutboundIndustryRerouteChance = Mathf.Clamp01(value);
            SaveUserOverride(nameof(OutboundIndustryRerouteChance), OutboundIndustryRerouteChance);
        }

        public static void SetOutboundIndustryFillFactor(float value)
        {
            OutboundIndustryFillFactor = ClampOutboundIndustryFillFactor(value);
            SaveUserOverride(nameof(OutboundIndustryFillFactor), OutboundIndustryFillFactor);
        }

        public static void SetOutboundIndustryPaymentMultiplier(float value)
        {
            OutboundIndustryPaymentMultiplier = ClampOutboundIndustryPaymentMultiplier(value);
            SaveUserOverride(nameof(OutboundIndustryPaymentMultiplier), OutboundIndustryPaymentMultiplier);
        }

        public static void SetOutboundIndustryAllowShortTrips(bool enabled)
        {
            OutboundIndustryAllowShortTrips = enabled;
            SaveUserOverride(nameof(OutboundIndustryAllowShortTrips), enabled);
        }

        public static void SetOutboundIndustryIgnoreOrigin(bool enabled)
        {
            OutboundIndustryIgnoreOrigin = enabled;
            SaveUserOverride(nameof(OutboundIndustryIgnoreOrigin), enabled);
        }

        public static void SetOutboundIndustryPreventBlocking(bool enabled)
        {
            OutboundIndustryPreventBlocking = enabled;
            SaveUserOverride(nameof(OutboundIndustryPreventBlocking), enabled);
        }

        public static void SetInterchangeToInterchangeMaximumCars(int value)
        {
            InterchangeToInterchangeMaximumCars = ClampInterchangeToInterchangeMaximumCars(value);
            SaveUserOverride(nameof(InterchangeToInterchangeMaximumCars), InterchangeToInterchangeMaximumCars);
        }

        internal static int ClampInterchangeToInterchangeMaximumCars(int value)
        {
            return Mathf.Clamp(value, 0, 200);
        }

        public static void SetForYourConvenienceShowCabooseIcons(bool enabled)
        {
            ForYourConvenienceShowCabooseIcons = enabled;
            SaveUserOverride(nameof(ForYourConvenienceShowCabooseIcons), enabled);
        }

        public static void SetForYourConvenienceShowCarTagMph(bool enabled)
        {
            ForYourConvenienceShowCarTagMph = enabled;
            SaveUserOverride(nameof(ForYourConvenienceShowCarTagMph), enabled);
        }

        public static void SetForYourConvenienceShowCarTagLoads(bool enabled)
        {
            ForYourConvenienceShowCarTagLoads = enabled;
            SaveUserOverride(nameof(ForYourConvenienceShowCarTagLoads), enabled);
        }

        internal static float ClampOutboundIndustryFillFactor(float value)
        {
            return float.IsNaN(value) ? DefaultOutboundIndustryFillFactor : Mathf.Clamp(value, 0.1f, 3f);
        }

        internal static float ClampOutboundIndustryPaymentMultiplier(float value)
        {
            return float.IsNaN(value) ? DefaultOutboundIndustryPaymentMultiplier : Mathf.Clamp(value, 0f, 10f);
        }

        public static string GetUserSettingsPath()
        {
            return Path.Combine(Application.persistentDataPath, "FUSE", "settings.json");
        }

        private static bool ReadBool(JToken settings, string key, bool defaultValue)
        {
            if (settings == null || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            var token = settings[key];
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            bool parsed;
            return bool.TryParse(token.ToString(), out parsed) ? parsed : defaultValue;
        }

        internal static float ReadFloat(JToken settings, string key, float defaultValue)
        {
            if (settings == null || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            var token = settings[key];
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return token.Value<float>();
            }

            float parsed;
            return float.TryParse(
                token.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : defaultValue;
        }

        internal static int ReadInt(JToken settings, string key, int defaultValue)
        {
            if (settings == null || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            var token = settings[key];
            if (token == null)
            {
                return defaultValue;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            int parsed;
            return int.TryParse(
                token.ToString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : defaultValue;
        }

        internal static bool IsSessionOnlyUserSetting(string key)
        {
            return string.Equals(
                key,
                nameof(EnableSceneryCullingDiagnostics),
                StringComparison.Ordinal);
        }

        private static JObject LoadUserSettingsJson(string path)
        {
            return File.Exists(path)
                ? JObject.Parse(File.ReadAllText(path))
                : null;
        }

        // Caller holds UserSettingsCommitGate across the complete
        // load -> mutate -> write transaction.
        private static void WriteUserSettingsJsonUnderLock(string path, JObject root)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                // Write beside the destination, then publish the complete JSON
                // atomically. Readers therefore never observe a truncated live
                // settings file if serialization or I/O is interrupted.
                File.WriteAllText(
                    temporaryPath,
                    root.ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception cleanupException)
                {
                    FuseLog.Warning(
                        $"FUSE could not clean up temporary user settings file '{temporaryPath}': " +
                        cleanupException.GetBaseException().Message);
                }
            }
        }

        private static void ApplyUserOverrides()
        {
            var path = GetUserSettingsPath();
            try
            {
                JObject settings;
                var removedLegacyOverride = false;
                string legacyCleanupError = null;
                lock (UserSettingsCommitGate)
                {
                    settings = LoadUserSettingsJson(path);
                    if (settings == null)
                    {
                        return;
                    }

                    // High-volume culling diagnostics are deliberately session-only.
                    // Remove an older persisted value in the same transaction that
                    // loaded it, so a concurrent UI save cannot be overwritten.
                    removedLegacyOverride = settings.Remove(nameof(EnableSceneryCullingDiagnostics));
                    if (removedLegacyOverride)
                    {
                        try
                        {
                            WriteUserSettingsJsonUnderLock(path, settings);
                        }
                        catch (Exception cleanupException)
                        {
                            // Cleanup is best-effort. Continue applying the already-loaded
                            // overrides so one unwritable legacy key cannot discard every
                            // valid setting that follows it.
                            legacyCleanupError = cleanupException.GetBaseException().Message;
                        }
                    }
                }

                if (removedLegacyOverride)
                {
                    if (legacyCleanupError == null)
                    {
                        FuseLog.Info(
                            "FUSE removed the legacy persisted scenery-culling diagnostic override; " +
                            "enable it explicitly for each diagnostic session.");
                    }
                    else
                    {
                        FuseLog.Warning(
                            "FUSE could not remove the legacy persisted scenery-culling diagnostic override; " +
                            $"continuing with the remaining user settings: {legacyCleanupError}");
                    }
                }

                EnableExperimentalEarlyScenePathSuppression =
                    ReadBool(settings, nameof(EnableExperimentalEarlyScenePathSuppression), EnableExperimentalEarlyScenePathSuppression);
                VerboseApplyReportDetails =
                    ReadBool(settings, nameof(VerboseApplyReportDetails), VerboseApplyReportDetails);
                EnableTargetedTerrainInvalidation =
                    ReadBool(settings, nameof(EnableTargetedTerrainInvalidation), EnableTargetedTerrainInvalidation);
                BlockNonHostMultiplayerClientWorldApply =
                    ReadBool(settings, nameof(BlockNonHostMultiplayerClientWorldApply), BlockNonHostMultiplayerClientWorldApply);
                ShowAdvancedHealthDetails =
                    ReadBool(settings, nameof(ShowAdvancedHealthDetails), ShowAdvancedHealthDetails);
                ShowTrackDebugOverlay =
                    ReadBool(settings, nameof(ShowTrackDebugOverlay), ShowTrackDebugOverlay);
                ShowTrackDebugSpanPaths =
                    ReadBool(settings, nameof(ShowTrackDebugSpanPaths), ShowTrackDebugSpanPaths);
                ShowSceneryDebugOverlay =
                    ReadBool(settings, nameof(ShowSceneryDebugOverlay), ShowSceneryDebugOverlay);
                ShowSceneryDebugAdvanced =
                    ReadBool(settings, nameof(ShowSceneryDebugAdvanced), ShowSceneryDebugAdvanced);
                ShowWorldLabelsOverlay =
                    ReadBool(settings, nameof(ShowWorldLabelsOverlay), ShowWorldLabelsOverlay);
                WorldLabelsShowScenery =
                    ReadBool(settings, nameof(WorldLabelsShowScenery), WorldLabelsShowScenery);
                WorldLabelsShowSceneClones =
                    ReadBool(settings, nameof(WorldLabelsShowSceneClones), WorldLabelsShowSceneClones);
                WorldLabelsShowIndustries =
                    ReadBool(settings, nameof(WorldLabelsShowIndustries), WorldLabelsShowIndustries);
                WorldLabelsShowTrackNodes =
                    ReadBool(settings, nameof(WorldLabelsShowTrackNodes), WorldLabelsShowTrackNodes);
                WorldLabelsShowTrackSegments =
                    ReadBool(settings, nameof(WorldLabelsShowTrackSegments), WorldLabelsShowTrackSegments);
                ShowLegacyModsInUmm =
                    ReadBool(settings, nameof(ShowLegacyModsInUmm), ShowLegacyModsInUmm);
                EnableEnhancedLoadingScreen =
                    ReadBool(settings, nameof(EnableEnhancedLoadingScreen), EnableEnhancedLoadingScreen);
                DecoupleVisualConditionLimits =
                    ReadBool(settings, nameof(DecoupleVisualConditionLimits), DecoupleVisualConditionLimits);
                RandomizeVisualConditionOnSpawn =
                    ReadBool(settings, nameof(RandomizeVisualConditionOnSpawn), RandomizeVisualConditionOnSpawn);
                RandomVisualConditionMin = Mathf.Clamp01(
                    ReadFloat(settings, nameof(RandomVisualConditionMin), RandomVisualConditionMin));
                RandomVisualConditionMax = Mathf.Clamp01(
                    ReadFloat(settings, nameof(RandomVisualConditionMax), RandomVisualConditionMax));
                EnableFrameSpikeDiagnostics =
                    ReadBool(settings, nameof(EnableFrameSpikeDiagnostics), EnableFrameSpikeDiagnostics);
                FrameSpikeThresholdMs = ClampFrameSpikeThresholdMs(
                    ReadFloat(settings, nameof(FrameSpikeThresholdMs), FrameSpikeThresholdMs));
                ForceConstrainedVramMode =
                    ReadBool(settings, nameof(ForceConstrainedVramMode), ForceConstrainedVramMode);
                EnableNativeLeakStackTraces =
                    ReadBool(settings, nameof(EnableNativeLeakStackTraces), EnableNativeLeakStackTraces);
                EnableUpdateCheck =
                    ReadBool(settings, nameof(EnableUpdateCheck), EnableUpdateCheck);
                GraceMinimumDays =
                    ReadInt(settings, nameof(GraceMinimumDays), GraceMinimumDays);
                GraceMultiplier =
                    ReadInt(settings, nameof(GraceMultiplier), GraceMultiplier);
                GraceAddedDays =
                    ReadInt(settings, nameof(GraceAddedDays), GraceAddedDays);
                InterchangeServiceIntervalMinutes = NormalizeInterchangeServiceIntervalMinutes(
                    ReadInt(
                        settings,
                        nameof(InterchangeServiceIntervalMinutes),
                        InterchangeServiceIntervalMinutes));
                InterchangeContinuousService =
                    ReadBool(settings, nameof(InterchangeContinuousService), InterchangeContinuousService);
                InterchangeNotBeforeHour = ClampInterchangeServiceHour(
                    ReadFloat(settings, nameof(InterchangeNotBeforeHour), InterchangeNotBeforeHour));
                InterchangeNotAfterHour = ClampInterchangeServiceHour(
                    ReadFloat(settings, nameof(InterchangeNotAfterHour), InterchangeNotAfterHour));
                EnableOutboundIndustryRerouting =
                    ReadBool(settings, nameof(EnableOutboundIndustryRerouting), EnableOutboundIndustryRerouting);
                OutboundIndustryRerouteChance = Mathf.Clamp01(
                    ReadFloat(settings, nameof(OutboundIndustryRerouteChance), OutboundIndustryRerouteChance));
                OutboundIndustryFillFactor = ClampOutboundIndustryFillFactor(
                    ReadFloat(settings, nameof(OutboundIndustryFillFactor), OutboundIndustryFillFactor));
                OutboundIndustryPaymentMultiplier = ClampOutboundIndustryPaymentMultiplier(
                    ReadFloat(
                        settings,
                        nameof(OutboundIndustryPaymentMultiplier),
                        OutboundIndustryPaymentMultiplier));
                OutboundIndustryAllowShortTrips =
                    ReadBool(settings, nameof(OutboundIndustryAllowShortTrips), OutboundIndustryAllowShortTrips);
                OutboundIndustryIgnoreOrigin =
                    ReadBool(settings, nameof(OutboundIndustryIgnoreOrigin), OutboundIndustryIgnoreOrigin);
                OutboundIndustryPreventBlocking =
                    ReadBool(settings, nameof(OutboundIndustryPreventBlocking), OutboundIndustryPreventBlocking);
                InterchangeToInterchangeMaximumCars = ClampInterchangeToInterchangeMaximumCars(
                    ReadInt(
                        settings,
                        nameof(InterchangeToInterchangeMaximumCars),
                        InterchangeToInterchangeMaximumCars));
                ForYourConvenienceShowCabooseIcons = ReadBool(
                    settings,
                    nameof(ForYourConvenienceShowCabooseIcons),
                    ForYourConvenienceShowCabooseIcons);
                ForYourConvenienceShowCarTagMph = ReadBool(
                    settings,
                    nameof(ForYourConvenienceShowCarTagMph),
                    ForYourConvenienceShowCarTagMph);
                ForYourConvenienceShowCarTagLoads = ReadBool(
                    settings,
                    nameof(ForYourConvenienceShowCarTagLoads),
                    ForYourConvenienceShowCarTagLoads);
                DirectStoreNativeDeserialize =
                    ReadBool(settings, nameof(DirectStoreNativeDeserialize), DirectStoreNativeDeserialize);
                FuseLog.Info($"FUSE user setting overrides loaded from '{path}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not read user setting overrides from '{path}': {ex.GetBaseException().Message}");
            }
        }

        private static void SaveUserOverride(string key, bool value)
        {
            SaveUserOverride(key, new JValue(value));
        }

        private static void SaveUserOverride(string key, float value)
        {
            SaveUserOverride(key, new JValue(value));
        }

        private static void SaveUserOverride(string key, int value)
        {
            SaveUserOverride(key, new JValue(value));
        }

        private static void SaveUserOverride(string key, JValue value)
        {
            if (IsSessionOnlyUserSetting(key))
            {
                RemoveUserOverride(key);
                return;
            }

            try
            {
                var path = GetUserSettingsPath();
                lock (UserSettingsCommitGate)
                {
                    var root = LoadUserSettingsJson(path) ?? new JObject();
                    root[key] = value;
                    WriteUserSettingsJsonUnderLock(path, root);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not save user setting override '{key}': {ex.GetBaseException().Message}");
            }
        }

        private static void RemoveUserOverride(string key)
        {
            try
            {
                var path = GetUserSettingsPath();
                lock (UserSettingsCommitGate)
                {
                    var root = LoadUserSettingsJson(path);
                    if (root == null)
                    {
                        return;
                    }

                    if (!root.Remove(key))
                    {
                        return;
                    }

                    WriteUserSettingsJsonUnderLock(path, root);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not remove user setting override '{key}': {ex.GetBaseException().Message}");
            }
        }
    }
}
