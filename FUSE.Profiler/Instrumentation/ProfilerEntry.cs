using System;
using System.Collections.Generic;
using FUSE.Profiler.Engine;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>The tab a profiler entry belongs to.</summary>
    internal enum ProfilerCategory
    {
        Physics,
        Culling,
        Scenery,
        Track,
        Operations,
        UiEvents,
        KeyValue,
        Mods,
        Custom,
    }

    /// <summary>
    /// How one target method is specified before resolution. Specs use the
    /// Harmony string form "Namespace.Type:Method"; coroutine targets resolve
    /// to the compiler-generated iterator MoveNext so the measured time is
    /// where the coroutine body actually runs.
    /// </summary>
    internal readonly struct TargetSpec
    {
        internal TargetSpec(string methodSpec, bool coroutine = false, string label = null)
        {
            MethodSpec = methodSpec;
            Coroutine = coroutine;
            Label = label;
        }

        internal string MethodSpec { get; }
        internal bool Coroutine { get; }
        internal string Label { get; }
    }

    /// <summary>
    /// A selectable profiling unit: a named set of target methods that gets
    /// instrumented on first activation and gated by <see cref="Active"/>
    /// afterwards. Entries are plain instances — activation state is checked
    /// through the per-method binding at call time, so no generated types or
    /// static fields are needed.
    /// </summary>
    internal sealed class ProfilerEntry
    {
        internal ProfilerEntry(string id, string label, ProfilerCategory category, Func<IEnumerable<TargetSpec>> targetProvider, string tooltip = null)
        {
            Id = id;
            Label = label;
            Category = category;
            TargetProvider = targetProvider;
            Tooltip = tooltip;
        }

        internal string Id { get; }
        internal string Label { get; }
        internal string Tooltip { get; }
        internal ProfilerCategory Category { get; }
        internal Func<IEnumerable<TargetSpec>> TargetProvider { get; }

        internal ProbeCadence Cadence =>
            Category == ProfilerCategory.Physics ? ProbeCadence.SimTick : ProbeCadence.Frame;

        /// <summary>
        /// Whether this entry's probes record. Volatile-adjacent: written by
        /// the UI thread, read inside instrumented calls — a stale read for a
        /// frame is harmless.
        /// </summary>
        internal bool Active;

        /// <summary>Set once instrumentation has been applied (or attempted).</summary>
        internal bool Patched;

        /// <summary>True while a background patching task runs for this entry.</summary>
        internal bool PatchInFlight;

        internal int InstrumentedCount;

        /// <summary>Targets that failed to resolve or patch, for the UI.</summary>
        internal readonly List<string> FailedTargets = new List<string>();
    }
}
