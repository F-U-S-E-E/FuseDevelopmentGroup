using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using FUSE.Compatibility;
using FUSE.Interface.Console;
using FUSE.Runtime.Events;
using FUSE.Infrastructure;
using FUSE.Interface;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Patches;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;
using FUSE.Authoring.Editor;
using FUSE.Interface.MenuWindow;

namespace FUSE
{
#if DEBUG
    [EnableReloading]
#endif
    public static class FusePlugin
    {
        private const string HarmonyId = "FUSE";
        private const string ConverterVersion = "0.2.0";
#if DEBUG
        private const string BuildConfiguration = "Debug";
#else
        private const string BuildConfiguration = "Release";
#endif

        private static Harmony _harmony;
        private static bool _isLoaded;
        private static FuseLifecycle _lifecycle;

        public static UnityModManager.ModEntry ModEntry { get; private set; }

        public static bool IsLoaded => _isLoaded;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            FuseLog.Initialize(modEntry?.Logger);

            if (modEntry == null)
            {
                FuseLog.Error("FUSE failed to load because Unity Mod Manager did not provide a mod entry.");
                return false;
            }

            if (_isLoaded)
            {
                FuseLog.Warning("FUSE Load was called while FUSE is already loaded; ignoring duplicate load request.");
                return true;
            }

            try
            {
                FuseLegacySupportAssemblyShim.Initialize();
                WarnIfLegacyRailloaderInstallPresent();
                LogStartupVersions(modEntry);
                FuseSettings.Load(modEntry);
                FuseNativeLeakDiagnostic.Initialize(FuseSettings.EnableNativeLeakStackTraces);
                FuseAssetPackRegistry.MountAllAvailableAssetPacks();

                _harmony = new Harmony(HarmonyId);
                FusePatchResilience.ApplyAll(_harmony, Assembly.GetExecutingAssembly());
                FuseEarlyLoader.SetPatchAvailable(FusePatchResilience.Applied.Any(patch =>
                    string.Equals(patch.TypeName, "FUSE.Patches.FuseEarlyLoaderSceneManagerPatch", StringComparison.Ordinal)));
                _lifecycle = new FuseLifecycle();
                _lifecycle.Register();
                // Console handler may not exist yet during early load; the lifecycle
                // re-attempts registration on the first map load.
                FuseConsoleRegistrar.TryRegisterAll();

                FuseEditorAssemblyLoader.TryInitialize(modEntry.Path);
                FuseEditorBridge.NotifyFuseLoaded();
                FuseOrphanedCarWindow.Ensure();
                FuseMenuWindow.Ensure();
                FuseTrackDebugOverlay.Ensure();
                FuseSceneryDebugOverlay.Ensure();
                FuseWorldLabelsOverlay.Ensure();
                FuseLoadingScreen.Ensure();
                FuseFrameSpikeDiagnostic.EnsureStarted();
                FuseRuntimePump.EnsureStarted();
                FuseSceneryLoadFailurePatch.EnsureGameLogHook();
                FuseModExceptionLogHook.Install();
                FuseThirdPartyGuardInstaller.EnsureInstalled();
                FuseUmmInjector.ScheduleInjection(modEntry.Path, ReadInfoJsonString(Path.Combine(modEntry.Path ?? string.Empty, "Info.json"), "Version"));
                FuseLegacyAssemblyHost.EnsureStartupHost();

                modEntry.OnUnload = OnUnload;
                _isLoaded = true;
                FuseEvents.RaiseFuseLoaded();
                FuseLog.Info("FUSE loaded.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to load", ex);
                Shutdown();
                return false;
            }
        }

        private static void LogStartupVersions(UnityModManager.ModEntry modEntry)
        {
            try
            {
                var infoPath = Path.Combine(modEntry.Path ?? string.Empty, "Info.json");
                var fuseVersion = ReadInfoJsonString(infoPath, "Version") ?? ReadModEntryInfoString(modEntry, "Version") ?? "unknown";
                var supportedRailroaderVersion = ReadInfoJsonString(infoPath, "GameVersion") ?? "unspecified";
                var currentRailroaderVersion = string.IsNullOrWhiteSpace(Application.version) ? "unknown" : Application.version;
                var unityVersion = string.IsNullOrWhiteSpace(Application.unityVersion) ? "unknown" : Application.unityVersion;

                FuseLog.Info(
                    "FUSE startup version report: " +
                    $"fuseVersion='{fuseVersion}' " +
                    $"schemaVersion='{FuseMigration.CurrentVersion}' " +
                    $"converterVersion='{ConverterVersion}' " +
                    $"buildConfiguration='{BuildConfiguration}' " +
                    $"supportedRailroaderVersion='{supportedRailroaderVersion}' " +
                    $"currentRailroaderVersion='{currentRailroaderVersion}' " +
                    $"unityVersion='{unityVersion}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE startup version report failed", ex);
            }
        }

        private static void WarnIfLegacyRailloaderInstallPresent()
        {
            // Detection runs after the AssemblyResolve shim is wired so the
            // loaded-assembly probe can identify a real Railloader assembly
            // by exclusion from the shim assembly identity.
            var conflicts = FuseLegacyInstallDetector.DetectConflictingFiles();
            if (conflicts.Count == 0)
            {
                return;
            }

            foreach (var path in conflicts)
            {
                FuseLog.Error(
                    "FUSE detected a conflicting legacy Railloader file: " + path +
                    ". Delete this file and restart Railroader.");
            }

            FuseLegacyInstallAlert.Ensure(conflicts);
        }

        private static string ReadInfoJsonString(string infoPath, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(infoPath) || !File.Exists(infoPath))
            {
                return null;
            }

            var manifest = JObject.Parse(File.ReadAllText(infoPath));
            var property = manifest.Properties()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            return property?.Value?.Type == JTokenType.String
                ? property.Value.Value<string>()
                : property?.Value?.ToString();
        }

        private static string ReadModEntryInfoString(UnityModManager.ModEntry modEntry, string memberName)
        {
            try
            {
                var info = modEntry?.GetType().GetProperty("Info")?.GetValue(modEntry) ??
                           modEntry?.GetType().GetField("Info")?.GetValue(modEntry);
                if (info == null)
                {
                    return null;
                }

                var member = info.GetType().GetProperty(memberName)?.GetValue(info) ??
                             info.GetType().GetField(memberName)?.GetValue(info);
                return member?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            Shutdown();
            return true;
        }

        private static void Shutdown()
        {
            if (_harmony != null)
            {
                try
                {
                    _harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE failed while unpatching Harmony hooks during shutdown", ex);
                }

                _harmony = null;
            }

            if (_lifecycle != null)
            {
                try
                {
                    _lifecycle.Unregister();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE failed while unregistering lifecycle handlers during shutdown", ex);
                }

                _lifecycle = null;
            }

            FuseSceneryLoadThrottlePatch.Shutdown();
            FuseSceneryLoadFailurePatch.Shutdown();
            FuseModExceptionLogHook.Shutdown();
            FuseLegacyAssemblyHost.Shutdown();
            FuseLegacySupportAssemblyShim.Shutdown();
            FuseRuntimeRebindService.Shutdown();
            FuseMenuWindow.Shutdown();
            FuseTrackDebugOverlay.Shutdown();
            FuseSceneryDebugOverlay.Shutdown();
            FuseWorldLabelsOverlay.Shutdown();
            FuseLoadingScreen.Shutdown();
            FuseFrameSpikeDiagnostic.Shutdown();
            FuseRuntimePump.Shutdown();
            FuseNativeLeakDiagnostic.Shutdown();

            if (_isLoaded)
            {
                FuseEditorBridge.NotifyFuseUnloaded();
                FuseEvents.RaiseFuseUnloaded();
                FuseLog.Info("FUSE unloaded.");
            }

            _isLoaded = false;
            ModEntry = null;
        }
    }
}
