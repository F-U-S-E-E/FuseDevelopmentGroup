using System;
using System.IO;
using FUSE.Infrastructure;
using Newtonsoft.Json;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// User-level editor preferences (not per-mod). Persists as a JSON
    /// file at <c>&lt;persistentDataPath&gt;/FUSE.Editor/settings.json</c>
    /// so the preferences travel with the user across mod packages and
    /// editor sessions.
    /// </summary>
    /// <remarks>
    /// Mirrors the lazy-load / save-on-change pattern used by
    /// <c>FuseEditorBookmarkRegistry</c>; the differences are that this
    /// store is user-level rather than per-mod, and there's no
    /// "current mod" context to switch on.
    /// </remarks>
    internal static class FuseEditorSettings
    {
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 2.5f;
        public const float DefaultUiScale = 1.0f;

        // No-op tolerance for the UiScale setter. float.Epsilon (~1.4e-45)
        // is effectively exact equality, which never trips on real
        // slider input and so re-serialised the JSON on every set. A
        // 1e-4 band is far finer than any visible scale step (the slider
        // moves in ~1e-3 increments) yet still skips redundant writes
        // when a value round-trips to itself.
        private const float UiScaleEpsilon = 1e-4f;

        // Settings filename + sub-folder. Kept under a FUSE.Editor/
        // subdirectory of persistentDataPath so a future settings
        // shape (panel positions, hotkey overrides, etc.) can co-
        // locate without polluting Railroader's own data dir.
        public const string SettingsFolderName = "FUSE.Editor";
        public const string SettingsFileName = "settings.json";

        private sealed class SettingsPayload
        {
            [JsonProperty("uiScale")]
            public float UiScale { get; set; } = DefaultUiScale;
        }

        private static SettingsPayload _state;
        private static string _settingsPathOverride;

        /// <summary>
        /// Override the on-disk settings path. Used by tests so they
        /// can point at a temp directory without polluting the real
        /// persistentDataPath. Pass <c>null</c> to restore the default.
        /// </summary>
        internal static void SetPathOverride(string fullPath)
        {
            _settingsPathOverride = fullPath;
            _state = null; // force reload on next access
        }

        public static float UiScale
        {
            get
            {
                EnsureLoaded();
                return _state.UiScale;
            }
            set
            {
                EnsureLoaded();
                var clamped = Mathf.Clamp(value, MinUiScale, MaxUiScale);
                if (Math.Abs(_state.UiScale - clamped) < UiScaleEpsilon)
                {
                    return;
                }
                _state.UiScale = clamped;
                Save();
            }
        }

        /// <summary>
        /// Drops the in-memory cache so the next property access
        /// reloads from disk. Useful in tests; not needed at runtime.
        /// </summary>
        public static void Reset()
        {
            _state = null;
        }

        // -----------------------------------------------------------------
        // Persistence
        // -----------------------------------------------------------------

        private static void EnsureLoaded()
        {
            if (_state != null) return;

            var path = SettingsFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _state = new SettingsPayload();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<SettingsPayload>(json);
                _state = loaded ?? new SettingsPayload();
                // Clamp in case the on-disk value is out of bounds
                // (manual edit, migration from a wider range, etc.).
                _state.UiScale = Mathf.Clamp(_state.UiScale, MinUiScale, MaxUiScale);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor settings: failed to read '{path}'; reverting to defaults.", ex);
                _state = new SettingsPayload();
            }
        }

        private static void Save()
        {
            var path = SettingsFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                var json = JsonConvert.SerializeObject(_state, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor settings: failed to save to '{path}'.", ex);
            }
        }

        private static string SettingsFilePath()
        {
            if (!string.IsNullOrEmpty(_settingsPathOverride))
            {
                return _settingsPathOverride;
            }
            return ResolveUnityPersistentSettingsPath();
        }

        /// <summary>
        /// Isolated Unity-touching helper. Unity's
        /// <see cref="Application.persistentDataPath"/> JIT-fails on
        /// pure xUnit runs ("ECall methods must be packaged into a
        /// system module"). Keeping the call here in its own method
        /// means xUnit tests that set a path override never JIT this
        /// method body, so the engine call never triggers.
        /// </summary>
        private static string ResolveUnityPersistentSettingsPath()
        {
            try
            {
                var root = Application.persistentDataPath;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.Combine(root, SettingsFolderName, SettingsFileName);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
