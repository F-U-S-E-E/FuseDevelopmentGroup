using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FUSE.Profiler.Infrastructure;
using FUSE.Profiler.Instrumentation;
using HarmonyLib;
using UnityModManagerNet;

namespace FUSE.Profiler.Entries
{
    /// <summary>
    /// Creates entries at runtime from user input (search panel) and from the
    /// live Harmony state (the Mods rollup). Workers only discover and
    /// resolve targets; every Harmony.Patch is enqueued for the main-thread
    /// pump. Re-invoking a factory for the same id re-activates the existing
    /// entry instead of duplicating it.
    /// </summary>
    internal static class RuntimeEntryFactory
    {
        /// <summary>
        /// Profile a single "Namespace.Type:Method" (optionally a coroutine).
        /// </summary>
        internal static ProfilerEntry CreateMethodEntry(string methodSpec, bool coroutine)
        {
            var id = "custom.method." + methodSpec + (coroutine ? "#coroutine" : "");
            var existing = EntryCatalog.FindById(id);
            if (existing != null)
            {
                EntryCatalog.SetActive(existing, true);
                return existing;
            }

            var spec = new TargetSpec(methodSpec, coroutine);
            var entry = new ProfilerEntry(
                id,
                methodSpec + (coroutine ? " (coroutine)" : ""),
                ProfilerCategory.Custom,
                () => new[] { spec });
            EntryCatalog.Register(entry);
            EntryCatalog.SetActive(entry, true);
            return entry;
        }

        /// <summary>Profile every instrumentable method a type declares.</summary>
        internal static ProfilerEntry CreateTypeEntry(string typeName)
        {
            return CreateSweepEntry(
                "custom.type." + typeName,
                typeName + " (all methods)",
                groupKey: null,
                entry =>
                {
                    var type = AccessTools.TypeByName(typeName);
                    if (type == null)
                    {
                        lock (entry.FailedTargets)
                        {
                            entry.FailedTargets.Add(typeName + ": type not found");
                        }

                        return Array.Empty<MethodBase>();
                    }

                    return MethodResolver.ProfilableDeclaredMethods(type).Cast<MethodBase>().ToArray();
                });
        }

        /// <summary>
        /// Profile every instrumentable method in a mod's main assembly —
        /// the tool for "this mod is slow in its own MonoBehaviours".
        /// </summary>
        internal static ProfilerEntry CreateModAssemblyEntry(UnityModManager.ModEntry mod)
        {
            var modName = mod.Info?.DisplayName ?? mod.Info?.Id ?? "<mod>";
            return CreateSweepEntry(
                "custom.assembly." + modName,
                modName + " (whole assembly)",
                modName,
                entry =>
                {
                    var assembly = mod.Assembly;
                    if (assembly == null)
                    {
                        lock (entry.FailedTargets)
                        {
                            entry.FailedTargets.Add(modName + ": mod has no loaded assembly");
                        }

                        return Array.Empty<MethodBase>();
                    }

                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        types = rtle.Types.Where(t => t != null).ToArray();
                    }

                    return types
                        .SelectMany(MethodResolver.ProfilableDeclaredMethods)
                        .Cast<MethodBase>()
                        .ToArray();
                });
        }

        /// <summary>
        /// The Mods rollup: one entry instrumenting every foreign Harmony
        /// prefix/postfix/finalizer in the session, with probes grouped by
        /// owning mod so the UI can aggregate per mod. Tiny patch methods the
        /// Mono JIT inlined into their targets cannot be intercepted and are
        /// undercounted — the whole-assembly sweep covers those.
        /// </summary>
        internal static ProfilerEntry CreateForeignPatchesEntry()
        {
            var id = "mods.harmony-patches";
            var existing = EntryCatalog.FindById(id);
            if (existing != null)
            {
                EntryCatalog.SetActive(existing, true);
                return existing;
            }

            var entry = new ProfilerEntry(
                id,
                "All mods' Harmony patches",
                ProfilerCategory.Mods,
                Array.Empty<TargetSpec>,
                "Every other mod's prefix/postfix/finalizer, attributed to its mod. " +
                "JIT-inlined patch bodies and transpiler-inserted code are not isolated.");
            EntryCatalog.Register(entry);
            entry.Active = true;
            entry.PatchInFlight = true;
            var generation = MethodInstrumenter.Generation;

            Task.Run(() =>
            {
                try
                {
                    foreach (var patch in ModAttribution.EnumerateForeignPatches())
                    {
                        var targetName = patch.TargetMethod != null
                            ? MethodInstrumenter.DescribeMethod(patch.TargetMethod)
                            : "<unknown>";
                        var label = patch.ModName + " • " + patch.Kind + " of " + targetName;
                        var key = entry.Id + "|" + MethodInstrumenter.MethodKey(patch.PatchMethod) + "|" + patch.Kind + "|" + targetName;
                        MethodInstrumenter.EnqueueInstrumentation(entry, patch.PatchMethod, key, label, patch.ModName, generation);
                    }

                    MethodInstrumenter.EnqueueCompletion(entry, generation);
                }
                catch (Exception ex)
                {
                    ProfilerLog.Exception("FUSE.Profiler failed enumerating foreign Harmony patches", ex);
                    entry.PatchInFlight = false;
                }
            });

            return entry;
        }

        private static ProfilerEntry CreateSweepEntry(
            string id,
            string label,
            string groupKey,
            Func<ProfilerEntry, MethodBase[]> discover)
        {
            var existing = EntryCatalog.FindById(id);
            if (existing != null)
            {
                EntryCatalog.SetActive(existing, true);
                return existing;
            }

            var entry = new ProfilerEntry(id, label, ProfilerCategory.Custom, Array.Empty<TargetSpec>);
            EntryCatalog.Register(entry);
            entry.Active = true;
            entry.PatchInFlight = true;
            var generation = MethodInstrumenter.Generation;

            Task.Run(() =>
            {
                try
                {
                    var methods = discover(entry);
                    foreach (var method in methods)
                    {
                        var name = MethodInstrumenter.MethodKey(method);
                        MethodInstrumenter.EnqueueInstrumentation(
                            entry,
                            method,
                            entry.Id + "|" + name,
                            MethodInstrumenter.DescribeMethod(method),
                            groupKey,
                            generation);
                    }

                    MethodInstrumenter.EnqueueCompletion(entry, generation);
                }
                catch (Exception ex)
                {
                    ProfilerLog.Exception($"FUSE.Profiler failed discovering targets for '{id}'", ex);
                    entry.PatchInFlight = false;
                }
            });

            return entry;
        }
    }
}
