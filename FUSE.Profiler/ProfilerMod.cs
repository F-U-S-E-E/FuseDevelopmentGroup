using System;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Infrastructure;
using FUSE.Profiler.Instrumentation;
using FUSE.Profiler.Interface;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Profiler
{
    /// <summary>UnityModManager entry point.</summary>
    public static class ProfilerMod
    {
        internal static ProfilerSettings Settings { get; private set; } = new ProfilerSettings();

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                Settings = UnityModManager.ModSettings.Load<ProfilerSettings>(modEntry) ?? new ProfilerSettings();
                ProfilerLog.Bind(
                    message => modEntry.Logger.Log(message),
                    message => modEntry.Logger.Warning(message),
                    message => modEntry.Logger.Error(message));

                ApplySettings();

                modEntry.OnToggle = OnToggle;
                modEntry.OnGUI = OnSettingsGui;
                modEntry.OnSaveGUI = entry => Settings.Save(entry);

                ProfilerHost.EnsureStarted();
                ProfilerConsole.Initialize();
                ProfilerLog.Info($"FUSE.Profiler loaded. Toggle with {ProfilerHost.ToggleKey} or /profiler.");
                return true;
            }
            catch (Exception ex)
            {
                modEntry.Logger.Error("FUSE.Profiler failed to load: " + ex);
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            if (enabled)
            {
                ProfilerHost.EnsureStarted();
                ProfilerConsole.Initialize();
            }
            else
            {
                ProfilerRuntime.CleanupNow();
                ProfilerConsole.Shutdown();
                ProfilerHost.Shutdown();
            }

            return true;
        }

        private static void OnSettingsGui(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Stats refreshes per second: " + Settings.UpdatesPerSecond, GUILayout.Width(220f));
            Settings.UpdatesPerSecond = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(Settings.UpdatesPerSecond, 1f, 10f, GUILayout.Width(200f)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Toggle key:", GUILayout.Width(220f));
            Settings.ToggleKeyName = GUILayout.TextField(Settings.ToggleKeyName ?? "F11", GUILayout.Width(120f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Teardown delay after close (s): " + Settings.CleanupDelaySeconds.ToString("0"), GUILayout.Width(220f));
            Settings.CleanupDelaySeconds = GUILayout.HorizontalSlider(Settings.CleanupDelaySeconds, 0f, 120f, GUILayout.Width(200f));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            ApplySettings();
        }

        private static void ApplySettings()
        {
            var updates = Settings.UpdatesPerSecond < 1 ? 1 : Settings.UpdatesPerSecond;
            ProfilerSession.StatsIntervalSeconds = 1f / updates;
            ProfilerRuntime.CleanupDelaySeconds = Settings.CleanupDelaySeconds;
            ProfilerHost.ToggleKey = ParseKey(Settings.ToggleKeyName, KeyCode.F11);
        }

        private static KeyCode ParseKey(string name, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            return Enum.TryParse<KeyCode>(name.Trim(), ignoreCase: true, out var key) ? key : fallback;
        }
    }
}
