using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Infrastructure;
using HarmonyLib;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>
    /// Applies and removes the measurement wrappers.
    ///
    /// Threading model: workers do the slow parts (spec resolution, type and
    /// assembly scans) and enqueue per-method patch actions; the actual
    /// Harmony.Patch calls run ONLY on the main thread, drained from
    /// <see cref="DrainPatchQueue"/> under a per-frame time budget. That
    /// keeps IL recompilation off worker threads while the main thread may
    /// be executing the very method being patched, and it makes teardown
    /// race-free by construction: <see cref="RemoveAll"/> (main thread)
    /// bumps the generation, empties the queue, and unpatches — a stale
    /// worker can only enqueue actions that no-op their generation check.
    ///
    /// A method is Harmony-patched at most once; multiple entries share the
    /// wrapper through a per-method binding list (a method can legitimately
    /// belong to a built-in entry, a custom sweep, and the foreign-patch
    /// rollup at the same time).
    /// </summary>
    internal static class MethodInstrumenter
    {
        internal const string HarmonyId = "FUSE.Profiler.instrumentation";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        private sealed class BindingPair
        {
            internal ProbeRing Ring;
            internal ProfilerEntry Entry;
        }

        private static readonly ConcurrentDictionary<MethodBase, BindingPair[]> Bindings =
            new ConcurrentDictionary<MethodBase, BindingPair[]>();

        private static readonly ConcurrentQueue<Action> PatchQueue = new ConcurrentQueue<Action>();

        private static readonly HarmonyMethod WrapperPrefix =
            new HarmonyMethod(typeof(MethodInstrumenter), nameof(ProbePrefix)) { priority = Priority.First };

        private static readonly HarmonyMethod WrapperPostfix =
            new HarmonyMethod(typeof(MethodInstrumenter), nameof(ProbePostfix)) { priority = Priority.Last };

        private static int _generation;

        /// <summary>
        /// The managed id of the Unity main thread; set by the host at load.
        /// Wrappers ignore calls on other threads (rings are main-thread
        /// only), and the patch queue asserts it drains here.
        /// </summary>
        internal static int MainThreadId = -1;

        internal static int Generation => Volatile.Read(ref _generation);

        internal static int InstrumentedMethodCount => Bindings.Count;

        internal static int PendingPatchCount => PatchQueue.Count;

        /// <summary>
        /// Resolve an entry's target specs on a worker, then enqueue the
        /// per-method patching for the main thread. Safe to call repeatedly.
        /// </summary>
        internal static void EnsureInstrumented(ProfilerEntry entry)
        {
            if (entry.Patched || entry.PatchInFlight)
            {
                return;
            }

            entry.PatchInFlight = true;
            var generation = Generation;
            Task.Run(() =>
            {
                try
                {
                    var resolved = new List<(MethodBase Method, string Label)>();
                    foreach (var spec in entry.TargetProvider())
                    {
                        var method = MethodResolver.Resolve(spec, out var error);
                        if (method == null)
                        {
                            lock (entry.FailedTargets)
                            {
                                entry.FailedTargets.Add(error);
                            }

                            continue;
                        }

                        resolved.Add((method, spec.Label ?? DescribeMethod(method)));
                    }

                    foreach (var target in resolved)
                    {
                        EnqueueInstrumentation(
                            entry,
                            target.Method,
                            entry.Id + "|" + MethodKey(target.Method),
                            target.Label,
                            groupKey: null,
                            generation);
                    }

                    EnqueueCompletion(entry, generation);
                }
                catch (Exception ex)
                {
                    ProfilerLog.Exception($"FUSE.Profiler failed resolving entry '{entry.Id}'", ex);
                    entry.PatchInFlight = false;
                }
            });
        }

        /// <summary>
        /// Queue one already-resolved method for instrumentation under an
        /// entry. Runs (and generation-checks) on the main thread.
        /// </summary>
        internal static void EnqueueInstrumentation(
            ProfilerEntry entry,
            MethodBase method,
            string probeKey,
            string probeLabel,
            string groupKey,
            int generation)
        {
            PatchQueue.Enqueue(() =>
            {
                if (Generation != generation)
                {
                    return;
                }

                PatchOne(entry, method, probeKey, probeLabel, groupKey);
            });
        }

        /// <summary>
        /// Queue the "entry finished instrumenting" marker behind its patch
        /// actions, so UI state flips only after the last one ran.
        /// </summary>
        internal static void EnqueueCompletion(ProfilerEntry entry, int generation)
        {
            PatchQueue.Enqueue(() =>
            {
                entry.PatchInFlight = false;
                if (Generation != generation)
                {
                    return;
                }

                entry.Patched = true;
                int failed;
                lock (entry.FailedTargets)
                {
                    failed = entry.FailedTargets.Count;
                }

                ProfilerLog.Info(
                    $"FUSE.Profiler instrumented entry '{entry.Id}': {entry.InstrumentedCount} method(s)" +
                    (failed > 0 ? $", {failed} target(s) failed" : "") + ".");
            });
        }

        /// <summary>
        /// Main-thread pump: apply queued patch actions until the budget is
        /// spent. Called by the host every frame; a large assembly sweep
        /// spreads over multiple frames instead of freezing one.
        /// </summary>
        internal static void DrainPatchQueue(double budgetMilliseconds)
        {
            if (PatchQueue.IsEmpty)
            {
                return;
            }

            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < budgetMilliseconds && PatchQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ProfilerLog.Exception("FUSE.Profiler patch action failed", ex);
                }
            }
        }

        /// <summary>
        /// Main-thread teardown: invalidate every queued and in-flight
        /// worker's generation, drop pending actions, remove every wrapper,
        /// and clear the binding table.
        /// </summary>
        internal static void RemoveAll()
        {
            Interlocked.Increment(ref _generation);
            while (PatchQueue.TryDequeue(out _))
            {
            }

            try
            {
                Harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler unpatch-all failed", ex);
            }

            Bindings.Clear();
        }

        /// <summary>Readable "Type:Name(ParamType, …)" form for labels/keys.</summary>
        internal static string DescribeMethod(MethodBase method)
        {
            var type = method.DeclaringType;
            var sb = new StringBuilder();
            sb.Append(type != null ? type.FullName : "<global>").Append(':').Append(method.Name);
            var parameters = method.GetParameters();
            sb.Append('(');
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(parameters[i].ParameterType.Name);
            }

            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// Probe-key form of a method identity. Signature-qualified so
        /// overloads get distinct rings (a shared ring across overloads
        /// nests Enter/Exit and mixes their numbers).
        /// </summary>
        internal static string MethodKey(MethodBase method)
        {
            return DescribeMethod(method);
        }

        private static void PatchOne(ProfilerEntry entry, MethodBase method, string probeKey, string probeLabel, string groupKey)
        {
            var existing = Bindings.TryGetValue(method, out var pairs) ? pairs : null;
            if (existing != null)
            {
                for (var i = 0; i < existing.Length; i++)
                {
                    if (ReferenceEquals(existing[i].Entry, entry))
                    {
                        return;
                    }
                }
            }

            var ring = ProbeRegistry.GetOrAdd(probeKey, probeLabel, entry.Cadence, groupKey);
            var pair = new BindingPair { Ring = ring, Entry = entry };

            if (existing == null)
            {
                // First entry on this method: bind, then patch. Queue and
                // teardown both run on the main thread, so no patch can land
                // after RemoveAll within the same generation.
                Bindings[method] = new[] { pair };
                try
                {
                    Harmony.Patch(method, prefix: WrapperPrefix, postfix: WrapperPostfix);
                }
                catch (Exception ex)
                {
                    Bindings.TryRemove(method, out _);
                    lock (entry.FailedTargets)
                    {
                        entry.FailedTargets.Add(DescribeMethod(method) + " — " + ex.Message);
                    }

                    return;
                }
            }
            else
            {
                var grown = new BindingPair[existing.Length + 1];
                Array.Copy(existing, grown, existing.Length);
                grown[existing.Length] = pair;
                Bindings[method] = grown;
            }

            entry.InstrumentedCount++;
        }

        private static void ProbePrefix(MethodBase __originalMethod)
        {
            if (!ProfilerSession.Recording ||
                Environment.CurrentManagedThreadId != MainThreadId)
            {
                return;
            }

            if (Bindings.TryGetValue(__originalMethod, out var pairs))
            {
                for (var i = 0; i < pairs.Length; i++)
                {
                    if (pairs[i].Entry.Active)
                    {
                        pairs[i].Ring.Enter();
                    }
                }
            }
        }

        private static void ProbePostfix(MethodBase __originalMethod)
        {
            if (Environment.CurrentManagedThreadId != MainThreadId)
            {
                return;
            }

            // No Recording/Active gate on exit: the depth counter makes a
            // spurious Exit a no-op, while skipping a real one (because a
            // flag flipped mid-call) would wedge the interval until the next
            // cycle close.
            if (Bindings.TryGetValue(__originalMethod, out var pairs))
            {
                for (var i = 0; i < pairs.Length; i++)
                {
                    pairs[i].Ring.Exit();
                }
            }
        }
    }
}
