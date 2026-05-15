using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Infrastructure
{
    internal static class FusePerformanceMetrics
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, long> Timings =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Counts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> ApplyPackageTotals =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static string _slowestApplyPhasePackage = string.Empty;
        private static string _slowestApplyPhase = string.Empty;
        private static long _slowestApplyPhaseMilliseconds;

        public static void RecordTiming(string phase, long elapsedMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(phase))
            {
                return;
            }

            lock (Sync)
            {
                Timings[phase.Trim()] = Math.Max(0L, elapsedMilliseconds);
            }
        }

        public static string FormatTiming(string phase)
        {
            lock (Sync)
            {
                return Timings.TryGetValue(phase ?? string.Empty, out var elapsed)
                    ? elapsed + " ms"
                    : "n/a";
            }
        }

        public static void RecordCount(string name, int count)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            lock (Sync)
            {
                Counts[name.Trim()] = Math.Max(0, count);
            }
        }

        public static string FormatCount(string name)
        {
            lock (Sync)
            {
                return Counts.TryGetValue(name ?? string.Empty, out var count)
                    ? count.ToString()
                    : "n/a";
            }
        }

        public static void ResetApplyTimings()
        {
            lock (Sync)
            {
                ApplyPackageTotals.Clear();
                _slowestApplyPhasePackage = string.Empty;
                _slowestApplyPhase = string.Empty;
                _slowestApplyPhaseMilliseconds = 0L;
            }
        }

        public static void RecordApplyPhase(string packageId, string phase, long elapsedMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(phase))
            {
                return;
            }

            var elapsed = Math.Max(0L, elapsedMilliseconds);
            lock (Sync)
            {
                ApplyPackageTotals[packageId] =
                    ApplyPackageTotals.TryGetValue(packageId, out var current) ? current + elapsed : elapsed;

                if (elapsed > _slowestApplyPhaseMilliseconds)
                {
                    _slowestApplyPhasePackage = packageId;
                    _slowestApplyPhase = phase;
                    _slowestApplyPhaseMilliseconds = elapsed;
                }
            }
        }

        public static string FormatSlowestApplyPhase()
        {
            lock (Sync)
            {
                return _slowestApplyPhaseMilliseconds <= 0L
                    ? "n/a"
                    : $"{_slowestApplyPhasePackage} / {_slowestApplyPhase} ({_slowestApplyPhaseMilliseconds} ms)";
            }
        }

        public static string FormatSlowestApplyPackage()
        {
            lock (Sync)
            {
                if (ApplyPackageTotals.Count == 0)
                {
                    return "n/a";
                }

                var slowest = ApplyPackageTotals
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .First();
                return $"{slowest.Key} ({slowest.Value} ms)";
            }
        }

        public static IReadOnlyDictionary<string, long> SnapshotTimings()
        {
            lock (Sync)
            {
                return Timings
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
