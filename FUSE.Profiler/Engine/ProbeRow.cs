using System;
using System.Collections.Generic;

namespace FUSE.Profiler.Engine
{
    /// <summary>How the results list is ordered.</summary>
    internal enum ProbeSortMode
    {
        Average,
        Max,
        Percent,
        Calls,
        Total,
        Name,
    }

    /// <summary>
    /// One immutable row of the rolled-up results list, produced by the stats
    /// worker and handed to the UI as a complete replacement list.
    /// </summary>
    internal sealed class ProbeRow
    {
        internal ProbeRow(
            string key,
            string label,
            string groupKey,
            ProbeCadence cadence,
            double averageMs,
            double maxMs,
            double totalMs,
            long calls,
            int maxCallsPerCycle,
            double percentOfFrame)
        {
            Key = key;
            Label = label;
            GroupKey = groupKey;
            Cadence = cadence;
            AverageMs = averageMs;
            MaxMs = maxMs;
            TotalMs = totalMs;
            Calls = calls;
            MaxCallsPerCycle = maxCallsPerCycle;
            PercentOfFrame = percentOfFrame;
        }

        internal string Key { get; }
        internal string Label { get; }
        internal string GroupKey { get; }
        internal ProbeCadence Cadence { get; }
        internal double AverageMs { get; }
        internal double MaxMs { get; }
        internal double TotalMs { get; }
        internal long Calls { get; }
        internal int MaxCallsPerCycle { get; }

        /// <summary>
        /// Share of the measured whole-frame time over the same window, in
        /// percent — or a negative value when no frame baseline applies
        /// (sim-tick probes), which the UI renders as "—".
        /// </summary>
        internal double PercentOfFrame { get; }

        internal static Comparison<ProbeRow> ComparisonFor(ProbeSortMode mode)
        {
            switch (mode)
            {
                case ProbeSortMode.Max:
                    return (a, b) => b.MaxMs.CompareTo(a.MaxMs);
                case ProbeSortMode.Percent:
                    return (a, b) => b.PercentOfFrame.CompareTo(a.PercentOfFrame);
                case ProbeSortMode.Calls:
                    return (a, b) => b.Calls.CompareTo(a.Calls);
                case ProbeSortMode.Total:
                    return (a, b) => b.TotalMs.CompareTo(a.TotalMs);
                case ProbeSortMode.Name:
                    return (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                case ProbeSortMode.Average:
                default:
                    return (a, b) => b.AverageMs.CompareTo(a.AverageMs);
            }
        }

        /// <summary>
        /// Sort rows for display: pinned keys first (stable within the pinned
        /// block), then the rest by the selected mode.
        /// </summary>
        internal static void SortForDisplay(List<ProbeRow> rows, ProbeSortMode mode, HashSet<string> pinnedKeys)
        {
            var comparison = ComparisonFor(mode);
            if (pinnedKeys == null || pinnedKeys.Count == 0)
            {
                rows.Sort(comparison);
                return;
            }

            rows.Sort((a, b) =>
            {
                var aPinned = pinnedKeys.Contains(a.Key);
                var bPinned = pinnedKeys.Contains(b.Key);
                if (aPinned != bPinned)
                {
                    return aPinned ? -1 : 1;
                }

                return comparison(a, b);
            });
        }
    }
}
