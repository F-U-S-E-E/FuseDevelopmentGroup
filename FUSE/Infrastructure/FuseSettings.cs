using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseSettings
    {
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
        public const bool DefaultShowLegacyModsInUmm = true;
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
            SaveUserOverride(nameof(EnableSceneryCullingDiagnostics), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(EnableSceneryCullingDiagnostics)}={enabled}.");
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

        private static void ApplyUserOverrides()
        {
            var path = GetUserSettingsPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var settings = JObject.Parse(File.ReadAllText(path));
                EnableExperimentalEarlyScenePathSuppression =
                    ReadBool(settings, nameof(EnableExperimentalEarlyScenePathSuppression), EnableExperimentalEarlyScenePathSuppression);
                VerboseApplyReportDetails =
                    ReadBool(settings, nameof(VerboseApplyReportDetails), VerboseApplyReportDetails);
                EnableSceneryCullingDiagnostics =
                    ReadBool(settings, nameof(EnableSceneryCullingDiagnostics), EnableSceneryCullingDiagnostics);
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
                FuseLog.Info($"FUSE user setting overrides loaded from '{path}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not read user setting overrides from '{path}': {ex.GetBaseException().Message}");
            }
        }

        private static void SaveUserOverride(string key, bool value)
        {
            try
            {
                var path = GetUserSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                var root = File.Exists(path)
                    ? JObject.Parse(File.ReadAllText(path))
                    : new JObject();
                root[key] = value;
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE could not save user setting override '{key}': {ex.GetBaseException().Message}");
            }
        }
    }
}
