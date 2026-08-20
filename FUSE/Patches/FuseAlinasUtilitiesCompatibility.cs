using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;

namespace FUSE.Patches
{
    /// <summary>
    /// Keeps the supported, separately-installed Alina Utilities mod usable
    /// when its RailLoader and UMM entry points are both discovered during the
    /// same startup. Older builds can leave the RailLoader singleton alive
    /// without an <c>IModdingContext</c>; its Settings getter then throws from
    /// camera-distance, damage, and main-menu patches before it can fall back
    /// to the UMM settings object.
    ///
    /// FUSE does not replace Alina Utilities. It only supplies the missing
    /// settings reference on that partially-bound instance. The original mod
    /// continues to own its settings UI and feature patches.
    /// </summary>
    internal static class FuseAlinasUtilitiesCompatibility
    {
        private const string PluginTypeName = "AlinasUtils.AlinasUtilsPlugin";
        private const string UmmModTypeName = "AlinasUtils.UMM.Mod";
        private const string SettingsFieldName = "_settings";

        private static readonly HashSet<object> RepairedInstances =
            new HashSet<object>(ReferenceEqualityComparer.Instance);

        internal static string EnsureInstalled()
        {
            var foundAssembly = false;
            var foundShared = false;
            var repaired = 0;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                Type pluginType;
                try
                {
                    pluginType = assembly.GetType(PluginTypeName, false);
                }
                catch
                {
                    continue;
                }

                if (pluginType == null)
                {
                    continue;
                }

                foundAssembly = true;
                try
                {
                    var shared = ReadSharedInstance(pluginType);
                    if (shared == null)
                    {
                        continue;
                    }

                    foundShared = true;
                    var settingsField = pluginType.GetField(
                        SettingsFieldName,
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (settingsField == null || settingsField.GetValue(shared) != null)
                    {
                        continue;
                    }

                    var settings = ReadUmmSettings(assembly, settingsField.FieldType) ??
                                   CreateDefaultSettings(settingsField.FieldType);
                    if (settings == null)
                    {
                        continue;
                    }

                    settingsField.SetValue(shared, settings);
                    if (RepairedInstances.Add(shared))
                    {
                        repaired++;
                        FuseLog.Info(
                            "FUSE repaired a partially-bound Alina Utilities legacy instance by " +
                            "connecting it to the mod's working settings object. Alina Utilities " +
                            "remains installed and owns its original features.");
                    }
                }
                catch (Exception ex)
                {
                    // Recovered distributions can contain more than one assembly
                    // with an AlinasUtils identity. One stale or incompatible copy
                    // must not prevent the compatible copy from being repaired.
                    FuseLog.Warning(
                        $"FUSE could not inspect one Alina Utilities assembly " +
                        $"assembly='{assembly.GetName().Name}': {ex.Message}");
                }
            }

            if (repaired > 0)
            {
                return $"repaired ({repaired})";
            }

            if (!foundAssembly)
            {
                return "idle (not present)";
            }

            return foundShared ? "ready" : "idle (no legacy instance)";
        }

        internal static bool IsTargetAssemblyName(string assemblyName)
        {
            return !string.IsNullOrWhiteSpace(assemblyName) &&
                   assemblyName.StartsWith("AlinasUtils", StringComparison.OrdinalIgnoreCase);
        }

        internal static object ResolveSettingsForTests(
            object currentSettings,
            object ummSettings,
            Func<object> createDefault)
        {
            if (currentSettings != null)
            {
                return currentSettings;
            }

            return ummSettings ?? createDefault?.Invoke();
        }

        private static object ReadSharedInstance(Type pluginType)
        {
            try
            {
                var baseType = pluginType.BaseType;
                var property = baseType?.GetProperty(
                    "Shared",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return property?.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static object ReadUmmSettings(Assembly assembly, Type settingsType)
        {
            try
            {
                var modType = assembly.GetType(UmmModTypeName, false);
                var property = modType?.GetProperty(
                    "Settings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var value = property?.GetValue(null, null);
                return value != null && settingsType.IsInstanceOfType(value) ? value : null;
            }
            catch
            {
                return null;
            }
        }

        private static object CreateDefaultSettings(Type settingsType)
        {
            try
            {
                return settingsType == null ? null : Activator.CreateInstance(settingsType);
            }
            catch
            {
                return null;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance =
                new ReferenceEqualityComparer();

            bool IEqualityComparer<object>.Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            int IEqualityComparer<object>.GetHashCode(object value)
            {
                return value == null
                    ? 0
                    : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
