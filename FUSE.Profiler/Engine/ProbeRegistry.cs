using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FUSE.Profiler.Engine
{
    /// <summary>
    /// The session-wide probe table. Creation is thread-safe (instrumented
    /// methods and the UI both race to create probes); per-cycle sampling
    /// iterates a cached snapshot array so the hot per-frame path does not
    /// allocate an enumerator or fight the dictionary.
    /// </summary>
    internal static class ProbeRegistry
    {
        private static readonly ConcurrentDictionary<string, ProbeRing> Probes =
            new ConcurrentDictionary<string, ProbeRing>(StringComparer.Ordinal);

        private static readonly object SnapshotLock = new object();
        private static volatile ProbeRing[] _snapshot = Array.Empty<ProbeRing>();
        private static volatile bool _snapshotStale;

        internal static int Count => Probes.Count;

        internal static ProbeRing GetOrAdd(string key, string label, ProbeCadence cadence, string groupKey = null)
        {
            if (Probes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var created = Probes.GetOrAdd(key, _ => new ProbeRing(key, label, cadence, groupKey));
            _snapshotStale = true;
            return created;
        }

        internal static bool TryGet(string key, out ProbeRing probe)
        {
            return Probes.TryGetValue(key, out probe);
        }

        /// <summary>
        /// Close the cycle for every probe on the given clock. Main thread
        /// only, once per frame / per sim tick.
        /// </summary>
        internal static void CloseCycles(ProbeCadence cadence)
        {
            var probes = CurrentSnapshot();
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                if (probe.Cadence == cadence)
                {
                    probe.CloseCycle();
                }
            }
        }

        /// <summary>Stable snapshot for the stats worker.</summary>
        internal static ProbeRing[] SnapshotProbes()
        {
            return CurrentSnapshot();
        }

        internal static void Clear()
        {
            Probes.Clear();
            _snapshot = Array.Empty<ProbeRing>();
            _snapshotStale = false;
        }

        private static ProbeRing[] CurrentSnapshot()
        {
            // Fast path: no allocation, no lock, on every ordinary cycle.
            if (!_snapshotStale)
            {
                return _snapshot;
            }

            // Rare (only after a probe was added). Claim the stale flag
            // BEFORE enumerating: a probe added concurrently with the copy
            // then re-marks stale and the next cycle picks it up — clearing
            // the flag after the copy could lose that add forever.
            lock (SnapshotLock)
            {
                if (_snapshotStale)
                {
                    _snapshotStale = false;
                    var list = new List<ProbeRing>(Probes.Count);
                    foreach (var pair in Probes)
                    {
                        list.Add(pair.Value);
                    }

                    _snapshot = list.ToArray();
                }
            }

            return _snapshot;
        }
    }
}
