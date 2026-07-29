using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Profiler.Infrastructure;
using HarmonyLib;
using UnityModManagerNet;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>
    /// Maps code back to the mod that shipped it, and enumerates other mods'
    /// Harmony patches so their cost can be measured and rolled up per mod —
    /// the attribution answer "which mod is the stutter" is this file's job.
    /// </summary>
    internal static class ModAttribution
    {
        /// <summary>Harmony ids owned by the profiler itself — never measured.</summary>
        private static readonly HashSet<string> OwnHarmonyIds = new HashSet<string>(StringComparer.Ordinal)
        {
            MethodInstrumenter.HarmonyId,
            "FUSE.Profiler.static",
        };

        private static Dictionary<Assembly, string> _assemblyToMod;

        internal readonly struct ForeignPatch
        {
            internal ForeignPatch(MethodBase patchMethod, MethodBase targetMethod, string kind, string ownerId, string modName)
            {
                PatchMethod = patchMethod;
                TargetMethod = targetMethod;
                Kind = kind;
                OwnerId = ownerId;
                ModName = modName;
            }

            internal MethodBase PatchMethod { get; }
            internal MethodBase TargetMethod { get; }
            internal string Kind { get; }
            internal string OwnerId { get; }
            internal string ModName { get; }
        }

        /// <summary>
        /// Best-effort mod name for an assembly: the UnityModManager entry
        /// that loaded it, falling back to the assembly's simple name.
        /// </summary>
        internal static string ModNameFor(Assembly assembly)
        {
            if (assembly == null)
            {
                return "<unknown>";
            }

            var map = _assemblyToMod;
            if (map == null)
            {
                map = BuildAssemblyMap();
                _assemblyToMod = map;
            }

            return map.TryGetValue(assembly, out var name) ? name : assembly.GetName().Name;
        }

        /// <summary>Drop the cached map (mods can toggle mid-session).</summary>
        internal static void InvalidateMap()
        {
            _assemblyToMod = null;
        }

        /// <summary>
        /// Every prefix/postfix/finalizer currently installed by someone other
        /// than the profiler. Transpilers are excluded: a transpiler has no
        /// runtime body of its own to time (measuring transpiler-inserted IL
        /// is a later feature).
        /// </summary>
        internal static List<ForeignPatch> EnumerateForeignPatches()
        {
            var result = new List<ForeignPatch>();
            IEnumerable<MethodBase> patchedMethods;
            try
            {
                patchedMethods = Harmony.GetAllPatchedMethods();
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler could not enumerate patched methods", ex);
                return result;
            }

            foreach (var target in patchedMethods)
            {
                Patches info;
                try
                {
                    info = Harmony.GetPatchInfo(target);
                }
                catch
                {
                    continue;
                }

                if (info == null)
                {
                    continue;
                }

                Collect(result, target, info.Prefixes, "prefix");
                Collect(result, target, info.Postfixes, "postfix");
                Collect(result, target, info.Finalizers, "finalizer");
            }

            return result;
        }

        private static void Collect(List<ForeignPatch> into, MethodBase target, IReadOnlyCollection<Patch> patches, string kind)
        {
            if (patches == null)
            {
                return;
            }

            foreach (var patch in patches)
            {
                if (patch == null || OwnHarmonyIds.Contains(patch.owner))
                {
                    continue;
                }

                var patchMethod = patch.PatchMethod;
                if (patchMethod == null || !MethodResolver.IsProfilable(patchMethod))
                {
                    continue;
                }

                var modName = ModNameFor(patchMethod.DeclaringType?.Assembly);
                into.Add(new ForeignPatch(patchMethod, target, kind, patch.owner, modName));
            }
        }

        private static Dictionary<Assembly, string> BuildAssemblyMap()
        {
            var map = new Dictionary<Assembly, string>();
            try
            {
                var entries = UnityModManager.modEntries;
                if (entries != null)
                {
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (entry?.Assembly == null)
                        {
                            continue;
                        }

                        var name = entry.Info?.DisplayName;
                        if (string.IsNullOrEmpty(name))
                        {
                            name = entry.Info?.Id ?? entry.Assembly.GetName().Name;
                        }

                        map[entry.Assembly] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler could not build the assembly→mod map", ex);
            }

            return map;
        }
    }
}
