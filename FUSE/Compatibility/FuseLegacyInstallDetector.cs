using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Compatibility
{
    // Detects when the legacy Railloader install is left in place alongside
    // FUSE. The bad state is Railloader.dll, Railloader.Injector.dll,
    // Railloader.Interchange.dll, and/or StrangeCustoms.dll
    // sitting in Railroader_Data\Managed\: Unity's assembly loader resolves
    // them before FuseLegacySupportAssemblyShim's AssemblyResolve hook fires,
    // so legacy mod IL binds to the real old-loader types instead of FUSE's
    // shim types. FUSE's plugin host then silently rejects those plugin types
    // because their base type isn't the shim Railloader.PluginBase.
    internal static class FuseLegacyInstallDetector
    {
        private const string LegacyRailloaderDll = "Railloader.dll";
        private const string LegacyInjectorDll = "Railloader.Injector.dll";
        private const string LegacyInterchangeDll = "Railloader.Interchange.dll";
        private const string LegacyStrangeCustomsDll = "StrangeCustoms.dll";

        internal static IReadOnlyList<string> DetectConflictingFiles()
        {
            var results = new List<string>();
            ProbeManagedDirectory(results);
            ProbeLoadedAssemblies(results);
            return results;
        }

        private static void ProbeManagedDirectory(List<string> results)
        {
            try
            {
                var dataPath = Application.dataPath;
                if (string.IsNullOrWhiteSpace(dataPath))
                {
                    return;
                }

                var managedDir = Path.Combine(dataPath, "Managed");
                AddIfFileExists(results, Path.Combine(managedDir, LegacyRailloaderDll));
                AddIfFileExists(results, Path.Combine(managedDir, LegacyInjectorDll));
                AddIfFileExists(results, Path.Combine(managedDir, LegacyInterchangeDll));
                AddIfFileExists(results, Path.Combine(managedDir, LegacyStrangeCustomsDll));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE legacy install detector could not probe the Managed directory: "
                    + ex.GetBaseException().Message);
            }
        }

        private static void ProbeLoadedAssemblies(List<string> results)
        {
            Assembly shimAssembly;
            try
            {
                shimAssembly = typeof(Railloader.IModDefinition).Assembly;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE legacy install detector could not resolve the shim assembly reference; "
                    + "loaded-assembly probe will be skipped: " + ex.GetBaseException().Message);
                return;
            }

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE legacy install detector could not enumerate loaded assemblies: "
                    + ex.GetBaseException().Message);
                return;
            }

            foreach (var assembly in assemblies)
            {
                if (assembly == null || ReferenceEquals(assembly, shimAssembly))
                {
                    continue;
                }

                string assemblyName;
                try
                {
                    assemblyName = assembly.GetName().Name;
                }
                catch
                {
                    continue;
                }

                if (!IsLegacyLoaderAssemblyName(assemblyName))
                {
                    continue;
                }

                string location = null;
                try
                {
                    if (!assembly.IsDynamic)
                    {
                        location = assembly.Location;
                    }
                }
                catch
                {
                    location = null;
                }

                AddUnique(
                    results,
                    string.IsNullOrWhiteSpace(location)
                        ? assemblyName + " (loaded, location unavailable)"
                        : location);
            }
        }

        internal static bool IsLegacyLoaderAssemblyName(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            return assemblyName.StartsWith("Railloader", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Equals("StrangeCustoms", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddIfFileExists(List<string> results, string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    AddUnique(results, path);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy install detector could not test path '{path}': "
                    + ex.GetBaseException().Message);
            }
        }

        private static void AddUnique(List<string> results, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var existing in results)
            {
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            results.Add(value);
        }
    }
}
