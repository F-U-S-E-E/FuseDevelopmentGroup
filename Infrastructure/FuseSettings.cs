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
        public const float ExperimentalEarlyScenePathSuppressionTimeoutSeconds = 8f;

        public static bool EnableExperimentalEarlyScenePathSuppression { get; private set; } =
            DefaultEnableExperimentalEarlyScenePathSuppression;

        public static bool MirrorInfoToPlayerLog { get; private set; } = DefaultMirrorInfoToPlayerLog;

        public static bool MirrorAssetPacksToLocalLow { get; private set; } = DefaultMirrorAssetPacksToLocalLow;

        public static bool VerboseApplyReportDetails { get; private set; } = DefaultVerboseApplyReportDetails;

        public static bool BlockNonHostMultiplayerClientWorldApply { get; private set; } =
            DefaultBlockNonHostMultiplayerClientWorldApply;

        public static bool ShowAdvancedHealthDetails { get; private set; } = DefaultShowAdvancedHealthDetails;

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
            MirrorInfoToPlayerLog = DefaultMirrorInfoToPlayerLog;
            MirrorAssetPacksToLocalLow = DefaultMirrorAssetPacksToLocalLow;
            VerboseApplyReportDetails = DefaultVerboseApplyReportDetails;
            BlockNonHostMultiplayerClientWorldApply = DefaultBlockNonHostMultiplayerClientWorldApply;
            ShowAdvancedHealthDetails = DefaultShowAdvancedHealthDetails;
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
