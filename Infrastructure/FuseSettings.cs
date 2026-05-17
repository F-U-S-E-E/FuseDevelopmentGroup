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
        public const bool DefaultBlockNonHostMultiplayerClientWorldApply = false;
        public const bool DefaultShowAdvancedHealthDetails = false;
        public const bool DefaultShowTrackDebugOverlay = false;
        public const bool DefaultShowTrackDebugSpanPaths = false;
        public const bool DefaultShowSceneryDebugOverlay = false;
        public const bool DefaultShowSceneryDebugAdvanced = false;
        public const float ExperimentalEarlyScenePathSuppressionTimeoutSeconds = 8f;

        public static bool EnableExperimentalEarlyScenePathSuppression { get; private set; } =
            DefaultEnableExperimentalEarlyScenePathSuppression;

        public static bool MirrorInfoToPlayerLog { get; private set; } = DefaultMirrorInfoToPlayerLog;

        public static bool MirrorAssetPacksToLocalLow { get; private set; } = DefaultMirrorAssetPacksToLocalLow;

        public static bool VerboseApplyReportDetails { get; private set; } = DefaultVerboseApplyReportDetails;

        public static bool BlockNonHostMultiplayerClientWorldApply { get; private set; } =
            DefaultBlockNonHostMultiplayerClientWorldApply;

        public static bool ShowAdvancedHealthDetails { get; private set; } = DefaultShowAdvancedHealthDetails;

        public static bool ShowTrackDebugOverlay { get; private set; } = DefaultShowTrackDebugOverlay;

        public static bool ShowTrackDebugSpanPaths { get; private set; } = DefaultShowTrackDebugSpanPaths;

        public static bool ShowSceneryDebugOverlay { get; private set; } = DefaultShowSceneryDebugOverlay;

        public static bool ShowSceneryDebugAdvanced { get; private set; } = DefaultShowSceneryDebugAdvanced;

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
            MirrorInfoToPlayerLog = DefaultMirrorInfoToPlayerLog;
            MirrorAssetPacksToLocalLow = DefaultMirrorAssetPacksToLocalLow;
            VerboseApplyReportDetails = DefaultVerboseApplyReportDetails;
            BlockNonHostMultiplayerClientWorldApply = DefaultBlockNonHostMultiplayerClientWorldApply;
            ShowAdvancedHealthDetails = DefaultShowAdvancedHealthDetails;
            ShowTrackDebugOverlay = DefaultShowTrackDebugOverlay;
            ShowTrackDebugSpanPaths = DefaultShowTrackDebugSpanPaths;
            ShowSceneryDebugOverlay = DefaultShowSceneryDebugOverlay;
            ShowSceneryDebugAdvanced = DefaultShowSceneryDebugAdvanced;
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
                ApplyUserOverrides();
                FuseLog.MirrorInfoToPlayerLog = MirrorInfoToPlayerLog;

                FuseLog.Info(
                    "FUSE settings loaded: " +
                    $"EnableExperimentalEarlyScenePathSuppression={EnableExperimentalEarlyScenePathSuppression} " +
                    $"MirrorInfoToPlayerLog={MirrorInfoToPlayerLog} " +
                    $"MirrorAssetPacksToLocalLow={MirrorAssetPacksToLocalLow} " +
                    $"VerboseApplyReportDetails={VerboseApplyReportDetails} " +
                    $"BlockNonHostMultiplayerClientWorldApply={BlockNonHostMultiplayerClientWorldApply} " +
                    $"ShowAdvancedHealthDetails={ShowAdvancedHealthDetails} " +
                    $"ShowTrackDebugOverlay={ShowTrackDebugOverlay} " +
                    $"ShowTrackDebugSpanPaths={ShowTrackDebugSpanPaths} " +
                    $"ShowSceneryDebugOverlay={ShowSceneryDebugOverlay} " +
                    $"ShowSceneryDebugAdvanced={ShowSceneryDebugAdvanced} " +
                    $"timeoutSeconds={ExperimentalEarlyScenePathSuppressionTimeoutSeconds}.");
            }
            catch (Exception ex)
            {
                EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
                MirrorInfoToPlayerLog = DefaultMirrorInfoToPlayerLog;
                MirrorAssetPacksToLocalLow = DefaultMirrorAssetPacksToLocalLow;
                VerboseApplyReportDetails = DefaultVerboseApplyReportDetails;
                BlockNonHostMultiplayerClientWorldApply = DefaultBlockNonHostMultiplayerClientWorldApply;
                ShowAdvancedHealthDetails = DefaultShowAdvancedHealthDetails;
                ShowTrackDebugOverlay = DefaultShowTrackDebugOverlay;
                ShowTrackDebugSpanPaths = DefaultShowTrackDebugSpanPaths;
                ShowSceneryDebugOverlay = DefaultShowSceneryDebugOverlay;
                ShowSceneryDebugAdvanced = DefaultShowSceneryDebugAdvanced;
                FuseLog.MirrorInfoToPlayerLog = MirrorInfoToPlayerLog;
                FuseLog.Warning($"FUSE failed to parse Info.json settings; experimental early scene-path suppression remains disabled: {ex.Message}");
            }
        }

        public static void SetVerboseApplyReportDetails(bool enabled)
        {
            VerboseApplyReportDetails = enabled;
            SaveUserOverride(nameof(VerboseApplyReportDetails), enabled);
            FuseLog.Info($"FUSE setting changed: {nameof(VerboseApplyReportDetails)}={enabled}.");
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
