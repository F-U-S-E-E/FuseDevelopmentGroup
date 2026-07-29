using System;
using System.Collections.Generic;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Entries;
using FUSE.Profiler.Infrastructure;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>
    /// The registry of selectable profiling entries, grouped by category tab.
    /// Multiple entries may be active at once (activating one does not
    /// deactivate the others); instrumentation is applied lazily on first
    /// activation and torn down globally by the cleanup pass.
    /// </summary>
    internal static class EntryCatalog
    {
        private static readonly object Gate = new object();
        private static readonly List<ProfilerEntry> Entries = new List<ProfilerEntry>();
        private static bool _builtInsRegistered;

        internal static IReadOnlyList<ProfilerEntry> All
        {
            get
            {
                lock (Gate)
                {
                    return Entries.ToArray();
                }
            }
        }

        internal static void EnsureBuiltInsRegistered()
        {
            lock (Gate)
            {
                if (_builtInsRegistered)
                {
                    return;
                }

                foreach (var entry in RailroaderEntries.CreateBuiltIns())
                {
                    Entries.Add(entry);
                }

                _builtInsRegistered = true;
            }

            ProfilerLog.Info($"FUSE.Profiler registered {Entries.Count} built-in profiling entr(y/ies).");
        }

        internal static ProfilerEntry FindById(string id)
        {
            lock (Gate)
            {
                for (var i = 0; i < Entries.Count; i++)
                {
                    if (string.Equals(Entries[i].Id, id, StringComparison.Ordinal))
                    {
                        return Entries[i];
                    }
                }
            }

            return null;
        }

        internal static ProfilerEntry Register(ProfilerEntry entry)
        {
            lock (Gate)
            {
                for (var i = 0; i < Entries.Count; i++)
                {
                    if (string.Equals(Entries[i].Id, entry.Id, StringComparison.Ordinal))
                    {
                        return Entries[i];
                    }
                }

                Entries.Add(entry);
            }

            return entry;
        }

        internal static List<ProfilerEntry> ForCategory(ProfilerCategory category)
        {
            var result = new List<ProfilerEntry>();
            lock (Gate)
            {
                for (var i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].Category == category)
                    {
                        result.Add(Entries[i]);
                    }
                }
            }

            return result;
        }

        internal static void SetActive(ProfilerEntry entry, bool active)
        {
            if (!active)
            {
                entry.Active = false;
                return;
            }

            // Activate immediately so probes record as soon as the patches
            // land; instrumentation itself completes on a worker.
            entry.Active = true;
            MethodInstrumenter.EnsureInstrumented(entry);
        }

        /// <summary>
        /// Deactivate everything and forget patch state. Called by cleanup
        /// after <see cref="MethodInstrumenter.RemoveAll"/> so entries can be
        /// re-instrumented next time the profiler opens. Dynamic (non-built-in)
        /// entries created by the search panel are removed entirely.
        /// </summary>
        internal static void ResetAfterCleanup()
        {
            lock (Gate)
            {
                for (var i = Entries.Count - 1; i >= 0; i--)
                {
                    var entry = Entries[i];
                    entry.Active = false;
                    entry.Patched = false;
                    entry.InstrumentedCount = 0;
                    lock (entry.FailedTargets)
                    {
                        entry.FailedTargets.Clear();
                    }

                    if (entry.Category == ProfilerCategory.Custom || entry.Category == ProfilerCategory.Mods)
                    {
                        Entries.RemoveAt(i);
                    }
                }

                _builtInsRegistered = Entries.Count > 0;
            }
        }
    }
}
