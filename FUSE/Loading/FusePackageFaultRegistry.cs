using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;

namespace FUSE.Loading
{
    internal sealed class FusePackageFault
    {
        public FusePackageFault(string packageId, string stage, string message, string details)
        {
            PackageId = packageId ?? string.Empty;
            Stage = stage ?? string.Empty;
            Message = message ?? string.Empty;
            Details = details ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
        }

        public string PackageId { get; }
        public string Stage { get; }
        public string Message { get; }
        public string Details { get; }
        public DateTime TimestampUtc { get; }

        public override string ToString()
        {
            return $"package='{PackageId}' stage='{Stage}' message='{Message}' details='{Details}'";
        }
    }

    internal static class FusePackageFaultRegistry
    {
        private static readonly Dictionary<string, List<FusePackageFault>> Faults =
            new Dictionary<string, List<FusePackageFault>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> DisabledPackages =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> SkippedPackages =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LoadedPackages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> AppliedPackages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Reset()
        {
            Faults.Clear();
            DisabledPackages.Clear();
            SkippedPackages.Clear();
            LoadedPackages.Clear();
            AppliedPackages.Clear();
        }

        public static void ClearPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            Faults.Remove(packageId);
            DisabledPackages.Remove(packageId);
            SkippedPackages.Remove(packageId);
            LoadedPackages.Remove(packageId);
            AppliedPackages.Remove(packageId);
        }

        public static void RecordFault(string packageId, string stage, string message, Exception exception = null)
        {
            packageId = NormalizePackageId(packageId);
            var details = exception == null ? string.Empty : exception.ToString();
            var fault = new FusePackageFault(packageId, stage, message, details);

            if (!Faults.TryGetValue(packageId, out var packageFaults))
            {
                packageFaults = new List<FusePackageFault>();
                Faults[packageId] = packageFaults;
            }

            if (packageFaults.Any(existing =>
                string.Equals(existing.Stage, fault.Stage, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Message, fault.Message, StringComparison.Ordinal)))
            {
                return;
            }

            packageFaults.Add(fault);
            FuseLog.Error($"FUSE package fault recorded: {fault}");
        }

        public static void MarkDisabled(string packageId, string reason)
        {
            packageId = NormalizePackageId(packageId);
            DisabledPackages[packageId] = string.IsNullOrWhiteSpace(reason) ? "disabled by manifest" : reason;
        }

        public static void MarkSkipped(string packageId, string reason)
        {
            packageId = NormalizePackageId(packageId);
            SkippedPackages[packageId] = string.IsNullOrWhiteSpace(reason) ? "skipped" : reason;
        }

        public static void MarkLoadedFromDisk(string packageId)
        {
            LoadedPackages.Add(NormalizePackageId(packageId));
        }

        public static void MarkAppliedToRuntime(string packageId)
        {
            AppliedPackages.Add(NormalizePackageId(packageId));
        }

        public static bool IsFaulted(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && Faults.ContainsKey(packageId);
        }

        public static bool IsDisabled(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && DisabledPackages.ContainsKey(packageId);
        }

        public static string[] GetFaultedPackageIds()
        {
            return Faults.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string[] GetLoadedPackageIds()
        {
            return LoadedPackages.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string[] GetAppliedPackageIds()
        {
            return AppliedPackages.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static IReadOnlyDictionary<string, string> GetSkippedPackages()
        {
            return SkippedPackages
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        }

        public static IReadOnlyDictionary<string, string> GetDisabledPackages()
        {
            return DisabledPackages
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        }

        public static FusePackageFault[] GetFaults()
        {
            return Faults.Values
                .SelectMany(items => items)
                .OrderBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Stage, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Message, StringComparer.Ordinal)
                .ToArray();
        }

        public static int FaultCount => Faults.Values.Sum(items => items.Count);
        public static int WarningCount => DisabledPackages.Count + SkippedPackages.Count(item => !IsOptionalSkipReason(item.Value));

        public static bool IsOptionalSkipReason(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason) &&
                   (reason.StartsWith("mixinto dependency missing", StringComparison.OrdinalIgnoreCase) ||
                    reason.StartsWith(FuseMapSession.InactiveSkipReasonPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public static void LogFinalReport(string reason, int residentDefinitionCount)
        {
            var faultedPackageIds = GetFaultedPackageIds();
            FuseLog.Info(
                $"FUSE final package report reason='{reason ?? "unspecified"}' " +
                $"loadedPackages={LoadedPackages.Count} appliedPackages={AppliedPackages.Count} " +
                $"skippedPackages={SkippedPackages.Count} faultedPackages={faultedPackageIds.Length} " +
                $"disabledPackages={DisabledPackages.Count} residentDefinitions={residentDefinitionCount} " +
                $"warnings={WarningCount} errors={FaultCount}.");

            LogSet("loaded", LoadedPackages);
            LogSet("applied", AppliedPackages);
            LogMap("disabled", DisabledPackages);
            LogMap("skipped", SkippedPackages);

            foreach (var packageId in faultedPackageIds)
            {
                if (!Faults.TryGetValue(packageId, out var packageFaults))
                {
                    continue;
                }

                foreach (var fault in packageFaults)
                {
                    FuseLog.Error($"FUSE final package report fault: {fault}");
                }
            }
        }

        private static void LogSet(string label, IEnumerable<string> values)
        {
            foreach (var value in values.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                FuseLog.Info($"FUSE final package report {label}: package='{value}'.");
            }
        }

        private static void LogMap(string label, IDictionary<string, string> values)
        {
            foreach (var entry in values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var line = $"FUSE final package report {label}: package='{entry.Key}' reason='{entry.Value}'.";
                if (string.Equals(label, "skipped", StringComparison.OrdinalIgnoreCase) &&
                    IsOptionalSkipReason(entry.Value))
                {
                    FuseLog.Info(line);
                }
                else
                {
                    FuseLog.Warning(line);
                }
            }
        }

        private static string NormalizePackageId(string packageId)
        {
            return string.IsNullOrWhiteSpace(packageId) ? "<unknown>" : packageId.Trim();
        }
    }
}
