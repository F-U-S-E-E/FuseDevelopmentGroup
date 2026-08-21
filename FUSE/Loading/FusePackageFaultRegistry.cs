using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FUSE.Infrastructure;
using Newtonsoft.Json;

namespace FUSE.Loading
{
    internal sealed class FusePackageFault
    {
        public FusePackageFault(string packageId, string stage, string message, string details)
            : this(packageId, stage, message, details, string.Empty, string.Empty, string.Empty, 0, 0, string.Empty)
        {
        }

        public FusePackageFault(
            string packageId,
            string stage,
            string message,
            string details,
            string folderPath,
            string sourceFile,
            string jsonPath,
            int lineNumber,
            int linePosition,
            string suggestedAction,
            string packageName = null,
            string validationCode = null,
            string expectedShape = null,
            string receivedValue = null)
        {
            PackageId = packageId ?? string.Empty;
            PackageName = string.IsNullOrWhiteSpace(packageName)
                ? InferPackageName(PackageId, folderPath)
                : packageName.Trim();
            Stage = stage ?? string.Empty;
            Message = message ?? string.Empty;
            Details = details ?? string.Empty;
            FolderPath = folderPath ?? string.Empty;
            SourceFile = sourceFile ?? string.Empty;
            RelativeSourceFile = MakeRelativeSourceFile(FolderPath, SourceFile);
            JsonPath = jsonPath ?? string.Empty;
            LineNumber = Math.Max(0, lineNumber);
            LinePosition = Math.Max(0, linePosition);
            SuggestedAction = suggestedAction ?? string.Empty;
            ValidationCode = validationCode ?? string.Empty;
            ExpectedShape = expectedShape ?? string.Empty;
            ReceivedValue = receivedValue ?? string.Empty;
            TimestampUtc = DateTime.UtcNow;
        }

        public string PackageId { get; }
        public string PackageName { get; }
        public string Stage { get; }
        public string Message { get; }
        public string Details { get; }
        public string FolderPath { get; }
        public string SourceFile { get; }
        public string RelativeSourceFile { get; }
        public string JsonPath { get; }
        public int LineNumber { get; }
        public int LinePosition { get; }
        public string SuggestedAction { get; }
        public string ValidationCode { get; }
        public string ExpectedShape { get; }
        public string ReceivedValue { get; }
        public DateTime TimestampUtc { get; }

        public override string ToString()
        {
            return
                $"package='{PackageId}' packageName='{PackageName}' stage='{Stage}' message='{Message}' " +
                $"folder='{FolderPath}' file='{SourceFile}' relativeFile='{RelativeSourceFile}' jsonPath='{JsonPath}' " +
                $"line={LineNumber} position={LinePosition} code='{ValidationCode}' expected='{ExpectedShape}' " +
                $"received='{ReceivedValue}' action='{SuggestedAction}' details='{Details}'";
        }

        private static string InferPackageName(string packageId, string folderPath)
        {
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                try
                {
                    var folderName = Path.GetFileName(folderPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(folderName))
                        return folderName;
                }
                catch (Exception ex) when (
                    ex is ArgumentException ||
                    ex is NotSupportedException ||
                    ex is PathTooLongException)
                {
                    // Keep the package id fallback; diagnostics must not fail
                    // while formatting an already-broken path.
                    return packageId ?? string.Empty;
                }
            }
            return packageId ?? string.Empty;
        }

        private static string MakeRelativeSourceFile(string folderPath, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
                return string.Empty;
            if (string.IsNullOrWhiteSpace(folderPath))
                return Path.GetFileName(sourceFile) ?? sourceFile;
            try
            {
                var root = Path.GetFullPath(folderPath).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var file = Path.GetFullPath(sourceFile);
                return file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(root.Length)
                    : Path.GetFileName(file);
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
            {
                return Path.GetFileName(sourceFile) ?? sourceFile;
            }
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

        public static void RecordFault(
            string packageId,
            string stage,
            string message,
            Exception exception = null,
            string folderPath = null,
            string sourceFile = null,
            string jsonPath = null,
            int lineNumber = 0,
            int linePosition = 0,
            string suggestedAction = null,
            string packageName = null,
            string validationCode = null,
            string expectedShape = null,
            string receivedValue = null)
        {
            packageId = NormalizePackageId(packageId);
            ExtractJsonLocation(exception, ref jsonPath, ref lineNumber, ref linePosition);
            if (string.IsNullOrWhiteSpace(folderPath) && !string.IsNullOrWhiteSpace(sourceFile))
            {
                try
                {
                    folderPath = Path.GetDirectoryName(sourceFile);
                }
                catch (Exception ex) when (
                    ex is ArgumentException ||
                    ex is NotSupportedException ||
                    ex is PathTooLongException)
                {
                    folderPath = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(suggestedAction) && IsJsonException(exception))
            {
                suggestedAction =
                    "Correct the JSON at the reported location, validate the file against the bundled FUSE schema, then reload the package.";
            }
            if (IsJsonException(exception))
            {
                if (string.IsNullOrWhiteSpace(expectedShape))
                    expectedShape = "Valid JSON matching the declared FUSE or package manifest schema.";
                if (string.IsNullOrWhiteSpace(receivedValue))
                    receivedValue = exception.GetBaseException().Message;
            }

            var details = exception == null ? string.Empty : exception.ToString();
            var fault = new FusePackageFault(
                packageId,
                stage,
                message,
                details,
                folderPath,
                sourceFile,
                jsonPath,
                lineNumber,
                linePosition,
                suggestedAction,
                packageName,
                validationCode,
                expectedShape,
                receivedValue);

            if (!Faults.TryGetValue(packageId, out var packageFaults))
            {
                packageFaults = new List<FusePackageFault>();
                Faults[packageId] = packageFaults;
            }

            if (packageFaults.Any(existing =>
                string.Equals(existing.Stage, fault.Stage, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Message, fault.Message, StringComparison.Ordinal) &&
                string.Equals(existing.SourceFile, fault.SourceFile, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.JsonPath, fault.JsonPath, StringComparison.Ordinal)))
            {
                return;
            }

            packageFaults.Add(fault);
            FuseLog.Error($"FUSE package fault recorded: {fault}");
        }

        private static void ExtractJsonLocation(
            Exception exception,
            ref string jsonPath,
            ref int lineNumber,
            ref int linePosition)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is JsonReaderException reader)
                {
                    jsonPath = string.IsNullOrWhiteSpace(jsonPath) ? reader.Path : jsonPath;
                    lineNumber = lineNumber > 0 ? lineNumber : reader.LineNumber;
                    linePosition = linePosition > 0 ? linePosition : reader.LinePosition;
                    return;
                }

                if (current is JsonSerializationException serialization)
                {
                    jsonPath = string.IsNullOrWhiteSpace(jsonPath) ? serialization.Path : jsonPath;
                    lineNumber = lineNumber > 0 ? lineNumber : serialization.LineNumber;
                    linePosition = linePosition > 0 ? linePosition : serialization.LinePosition;
                    return;
                }
            }
        }

        private static bool IsJsonException(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is JsonException)
                {
                    return true;
                }
            }

            return false;
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
                   (reason.IndexOf("mixinto dependency missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("mixinto conflict matched", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
