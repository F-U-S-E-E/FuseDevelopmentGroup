using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RAIL.Infrastructure;

namespace RAIL.Patches
{
    /// <summary>
    /// Replaces Harmony PatchAll with per-class application. A single failing
    /// patch class records a warning and is skipped; remaining patches still
    /// apply, so RAIL itself stays loaded.
    /// </summary>
    public static class RailPatchResilience
    {
        private static readonly List<RailPatchInfo> AppliedPatches = new List<RailPatchInfo>();
        private static readonly List<RailPatchInfo> FailedPatches = new List<RailPatchInfo>();

        public static IReadOnlyList<RailPatchInfo> Applied => AppliedPatches;
        public static IReadOnlyList<RailPatchInfo> Failed => FailedPatches;

        public static int ApplyAll(Harmony harmony, Assembly assembly)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            AppliedPatches.Clear();
            FailedPatches.Clear();

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                RailLog.Warning(
                    $"RAIL patch resilience could not enumerate every type in '{assembly.FullName}'; " +
                    $"continuing with {ex.Types.Length - ex.LoaderExceptions.Length} loadable type(s).");
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (!HasHarmonyPatchAttribute(type))
                {
                    continue;
                }

                var info = new RailPatchInfo
                {
                    TypeName = type.FullName ?? type.Name,
                    AssemblyName = type.Assembly.GetName().Name
                };

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    AppliedPatches.Add(info);
                    RailLog.Info($"RAIL applied Harmony patch class '{info.TypeName}'.");
                }
                catch (Exception ex)
                {
                    info.FailureReason = ex.Message;
                    FailedPatches.Add(info);
                    RailLog.Warning(
                        $"RAIL skipped Harmony patch class '{info.TypeName}' after exception: {ex.Message}. " +
                        "Remaining patches will still be applied.");
                }
            }

            RailLog.Info(
                $"RAIL patch application complete: applied={AppliedPatches.Count} failed={FailedPatches.Count}.");
            return AppliedPatches.Count;
        }

        private static bool HasHarmonyPatchAttribute(Type type)
        {
            try
            {
                return type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class RailPatchInfo
    {
        public string TypeName { get; set; }
        public string AssemblyName { get; set; }
        public string FailureReason { get; set; }

        public bool Failed => !string.IsNullOrEmpty(FailureReason);
    }
}
