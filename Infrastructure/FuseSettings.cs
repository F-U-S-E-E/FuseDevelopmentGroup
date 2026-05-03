using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseSettings
    {
        public const bool DefaultEnableExperimentalEarlyScenePathSuppression = false;
        public const float ExperimentalEarlyScenePathSuppressionTimeoutSeconds = 8f;

        public static bool EnableExperimentalEarlyScenePathSuppression { get; private set; } =
            DefaultEnableExperimentalEarlyScenePathSuppression;

        public static void Load(UnityModManager.ModEntry modEntry)
        {
            EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;

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

                FuseLog.Info(
                    "FUSE settings loaded: " +
                    $"EnableExperimentalEarlyScenePathSuppression={EnableExperimentalEarlyScenePathSuppression} " +
                    $"timeoutSeconds={ExperimentalEarlyScenePathSuppressionTimeoutSeconds}.");
            }
            catch (Exception ex)
            {
                EnableExperimentalEarlyScenePathSuppression = DefaultEnableExperimentalEarlyScenePathSuppression;
                FuseLog.Warning($"FUSE failed to parse Info.json settings; experimental early scene-path suppression remains disabled: {ex.Message}");
            }
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
    }
}
