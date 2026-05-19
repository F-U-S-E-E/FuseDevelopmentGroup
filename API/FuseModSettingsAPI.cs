using System;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using Newtonsoft.Json.Linq;

namespace FUSE.API
{
    public static class FuseModSettingsAPI
    {
        public static JToken GetValue(string packageId, string settingKey)
        {
            TryResolveSetting(packageId, settingKey, out var definition, out var setting);
            return FuseModSettingsStore.GetValue(definition?.Id ?? packageId, settingKey, setting);
        }

        public static string GetString(string packageId, string settingKey)
        {
            return FuseModSettingsStore.FormatValue(GetValue(packageId, settingKey));
        }

        public static bool GetBool(string packageId, string settingKey)
        {
            TryResolveSetting(packageId, settingKey, out var definition, out var setting);
            return FuseModSettingsStore.GetBoolValue(definition, settingKey, setting);
        }

        public static double GetNumber(string packageId, string settingKey)
        {
            TryResolveSetting(packageId, settingKey, out var definition, out var setting);
            return FuseModSettingsStore.GetNumberValue(definition, settingKey, setting);
        }

        public static void SetValue(string packageId, string settingKey, JToken value)
        {
            if (!TryResolveSetting(packageId, settingKey, out var definition, out var setting))
            {
                return;
            }

            FuseModSettingsStore.SetValue(definition, settingKey, setting, value);
        }

        public static void ResetValue(string packageId, string settingKey)
        {
            if (!TryResolveSetting(packageId, settingKey, out var definition, out var setting))
            {
                return;
            }

            FuseModSettingsStore.ResetValue(definition, settingKey, setting);
        }

        private static bool TryResolveSetting(
            string packageId,
            string settingKey,
            out FuseModDefinition definition,
            out FuseModSettingDefinition setting)
        {
            definition = null;
            setting = null;
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(settingKey))
            {
                return false;
            }

            definition = FuseModLoader.GetLoadedDefinition(packageId);
            if (definition?.Settings == null)
            {
                return false;
            }

            return definition.Settings.TryGetValue(settingKey, out setting);
        }
    }
}
