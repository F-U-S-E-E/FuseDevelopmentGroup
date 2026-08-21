using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FUSE.Authoring.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FUSE.Infrastructure
{
    public static class FuseModSettingsStore
    {
        public const string ScopeUser = "user";
        public const string ScopeProfile = "profile";
        public const string ScopeServer = "server";

        private static readonly object Sync = new object();
        private static JObject _root;
        private static bool _loaded;
        private static string _lastStatus = "Mod settings have not been changed in this session.";

        public static string LastStatus
        {
            get
            {
                lock (Sync)
                {
                    return _lastStatus;
                }
            }
        }

        public static string GetStorePath()
        {
            return Path.Combine(Application.persistentDataPath, "FUSE", "mod-settings.json");
        }

        public static JToken GetValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            return GetValue(definition?.Id, key, setting);
        }

        public static JToken GetValue(string packageId, string key, FuseModSettingDefinition setting)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(key))
            {
                return GetDefault(setting);
            }

            lock (Sync)
            {
                EnsureLoaded();
                var scope = NormalizeScope(setting?.Scope);
                var scopeKey = GetCurrentScopeKey(scope);
                var bucket = GetBucket(packageId, scope, scopeKey, create: false);
                var stored = bucket?[key];
                return stored == null ? GetDefault(setting) : CoerceValue(stored, setting);
            }
        }

        public static string GetStringValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var value = GetValue(definition, key, setting);
            return TokenToText(value);
        }

        public static bool GetBoolValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var value = GetValue(definition, key, setting);
            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            bool parsed;
            return bool.TryParse(TokenToText(value), out parsed) && parsed;
        }

        public static double GetNumberValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var value = GetValue(definition, key, setting);
            if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
            {
                return value.Value<double>();
            }

            double parsed;
            return double.TryParse(TokenToText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? Clamp(parsed, setting)
                : 0d;
        }

        public static bool HasStoredValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            if (string.IsNullOrWhiteSpace(definition?.Id) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (Sync)
            {
                EnsureLoaded();
                var scope = NormalizeScope(setting?.Scope);
                var scopeKey = GetCurrentScopeKey(scope);
                return GetBucket(definition.Id, scope, scopeKey, create: false)?[key] != null;
            }
        }

        public static void SetValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting, JToken value)
        {
            SetValueCore(definition, key, setting, value, persist: true);
        }

        internal static void SetValueInMemory(FuseModDefinition definition, string key, FuseModSettingDefinition setting, JToken value)
        {
            SetValueCore(definition, key, setting, value, persist: false);
        }

        private static void SetValueCore(
            FuseModDefinition definition,
            string key,
            FuseModSettingDefinition setting,
            JToken value,
            bool persist)
        {
            if (string.IsNullOrWhiteSpace(definition?.Id) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (Sync)
            {
                EnsureLoaded();
                var scope = NormalizeScope(setting?.Scope);
                var scopeKey = GetCurrentScopeKey(scope);
                var bucket = GetBucket(definition.Id, scope, scopeKey, create: true);
                bucket[key] = CoerceValue(value, setting);
                if (persist && SaveNoLock())
                {
                    _lastStatus = $"Saved setting '{key}' for package '{definition.Id}' ({DescribeScope(scope)}).";
                }
            }
        }

        public static void ResetValue(FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            if (string.IsNullOrWhiteSpace(definition?.Id) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (Sync)
            {
                EnsureLoaded();
                var scope = NormalizeScope(setting?.Scope);
                var scopeKey = GetCurrentScopeKey(scope);
                var bucket = GetBucket(definition.Id, scope, scopeKey, create: false);
                bucket?.Remove(key);
                if (SaveNoLock())
                {
                    _lastStatus = $"Reset setting '{key}' for package '{definition.Id}' ({DescribeScope(scope)}).";
                }
            }
        }

        public static string NormalizeType(string type)
        {
            var value = (type ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "bool":
                case "boolean":
                    return "bool";
                case "enum":
                case "choice":
                case "select":
                    return "enum";
                case "number":
                case "float":
                case "double":
                case "int":
                case "integer":
                    return "number";
                case "path":
                case "file":
                case "folder":
                    return "path";
                case "color":
                case "colour":
                    return "color";
                case "text":
                case "string":
                default:
                    return "text";
            }
        }

        public static string NormalizeScope(string scope)
        {
            var value = (scope ?? string.Empty).Trim().ToLowerInvariant();
            switch (value)
            {
                case "profile":
                case "modset":
                case "mod-set":
                    return ScopeProfile;
                case "server":
                case "shared":
                case "multiplayer":
                    return ScopeServer;
                case "local":
                case "client":
                case "user":
                default:
                    return ScopeUser;
            }
        }

        public static string DescribeScope(FuseModSettingDefinition setting)
        {
            return DescribeScope(NormalizeScope(setting?.Scope));
        }

        public static string DescribeScope(string scope)
        {
            scope = NormalizeScope(scope);
            if (scope == ScopeProfile)
            {
                return "profile:" + GetCurrentScopeKey(scope);
            }

            if (scope == ScopeServer)
            {
                return "server:" + GetCurrentScopeKey(scope);
            }

            return "user";
        }

        public static string FormatValue(JToken value)
        {
            return TokenToText(value);
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            var path = GetStorePath();
            try
            {
                _root = File.Exists(path)
                    ? JObject.Parse(File.ReadAllText(path))
                    : CreateEmptyRoot();
                if (_root["packages"] == null || _root["packages"].Type != JTokenType.Object)
                {
                    _root["packages"] = new JObject();
                }

                _root["schema"] = 1;
            }
            catch (Exception ex)
            {
                _root = CreateEmptyRoot();
                _lastStatus = $"Could not read mod settings store '{path}': {ex.GetBaseException().Message}";
                FuseLog.Warning("FUSE could not read mod settings store: " + ex.GetBaseException().Message);
            }

            _loaded = true;
        }

        private static JObject CreateEmptyRoot()
        {
            return new JObject
            {
                ["schema"] = 1,
                ["packages"] = new JObject()
            };
        }

        private static JObject GetBucket(string packageId, string scope, string scopeKey, bool create)
        {
            var packages = GetObject(_root, "packages", create);
            var package = GetObject(packages, packageId, create);
            var scopeObject = GetObject(package, scope, create);
            return GetObject(scopeObject, scopeKey, create);
        }

        private static JObject GetObject(JObject parent, string key, bool create)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var existing = parent[key] as JObject;
            if (existing != null || !create)
            {
                return existing;
            }

            existing = new JObject();
            parent[key] = existing;
            return existing;
        }

        private static JToken GetDefault(FuseModSettingDefinition setting)
        {
            if (setting?.Default != null)
            {
                return CoerceValue(setting.Default, setting);
            }

            switch (NormalizeType(setting?.Type))
            {
                case "bool":
                    return new JValue(false);
                case "number":
                    return new JValue(Clamp(0d, setting));
                case "enum":
                    var first = FirstEnumValue(setting);
                    return new JValue(first ?? string.Empty);
                case "color":
                case "path":
                case "text":
                default:
                    return new JValue(string.Empty);
            }
        }

        private static JToken CoerceValue(JToken value, FuseModSettingDefinition setting)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
            {
                return GetDefault(setting);
            }

            switch (NormalizeType(setting?.Type))
            {
                case "bool":
                    return new JValue(CoerceBool(value));
                case "number":
                    return new JValue(Clamp(CoerceNumber(value), setting));
                case "enum":
                    return new JValue(CoerceEnum(value, setting));
                case "color":
                case "path":
                case "text":
                default:
                    return new JValue(TokenToText(value));
            }
        }

        private static bool CoerceBool(JToken value)
        {
            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                return Math.Abs(value.Value<double>()) > double.Epsilon;
            }

            bool parsed;
            return bool.TryParse(TokenToText(value), out parsed) && parsed;
        }

        private static double CoerceNumber(JToken value)
        {
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                return value.Value<double>();
            }

            double parsed;
            return double.TryParse(TokenToText(value), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0d;
        }

        private static double Clamp(double value, FuseModSettingDefinition setting)
        {
            if (setting?.Min.HasValue == true)
            {
                value = Math.Max(setting.Min.Value, value);
            }

            if (setting?.Max.HasValue == true)
            {
                value = Math.Min(setting.Max.Value, value);
            }

            return value;
        }

        private static string CoerceEnum(JToken value, FuseModSettingDefinition setting)
        {
            var text = TokenToText(value);
            var values = setting?.Values ?? Array.Empty<string>();
            if (values.Length == 0)
            {
                return text;
            }

            var exact = values.FirstOrDefault(candidate => string.Equals(candidate, text, StringComparison.Ordinal));
            if (exact != null)
            {
                return exact;
            }

            var defaultText = setting?.Default == null ? null : TokenToText(setting.Default);
            exact = values.FirstOrDefault(candidate => string.Equals(candidate, defaultText, StringComparison.Ordinal));
            return exact ?? values[0];
        }

        private static string FirstEnumValue(FuseModSettingDefinition setting)
        {
            var values = setting?.Values ?? Array.Empty<string>();
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string TokenToText(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
            {
                return string.Empty;
            }

            if (value.Type == JTokenType.Object || value.Type == JTokenType.Array)
            {
                return value.ToString(Formatting.None);
            }

            var jValue = value as JValue;
            var formattable = jValue?.Value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        private static string GetCurrentScopeKey(string scope)
        {
            scope = NormalizeScope(scope);
            if (scope == ScopeProfile)
            {
                var setId = FuseModSetService.ActiveSetId;
                return string.IsNullOrWhiteSpace(setId) ? "all-active" : setId.Trim();
            }

            if (scope == ScopeServer)
            {
                var fingerprint = FuseModSetService.GetActiveSetFingerprint();
                return string.IsNullOrWhiteSpace(fingerprint) ? "unknown-profile" : fingerprint.Trim();
            }

            return "default";
        }

        private static bool SaveNoLock()
        {
            try
            {
                var path = GetStorePath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, _root.ToString(Formatting.Indented));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _lastStatus = "Could not save mod settings: " + ex.GetBaseException().Message;
                FuseLog.Exception("FUSE could not save mod settings", ex);
                return false;
            }
        }
    }
}
