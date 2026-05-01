namespace RAIL.Loading
{
    /// <summary>
    /// Per-package outcome of a single ApplyLoadedDefinitions pass. Captures the
    /// summary numbers needed for the aggregate apply report so individual
    /// packages can be flagged "applied / skipped / errored" at a glance.
    /// </summary>
    internal sealed class PackageApplyOutcome
    {
        public string PackageId { get; private set; } = string.Empty;
        public string Reason { get; private set; } = string.Empty;
        public int Applied { get; private set; }
        public int Skipped { get; private set; }
        public int Errored { get; private set; }
        public int CreatedObjects { get; private set; }
        public int UpdatedObjects { get; private set; }
        public int RemovedObjects { get; private set; }
        public int SkippedObjects { get; private set; }
        public int Warnings { get; private set; }
        public int Errors { get; private set; }

        public static PackageApplyOutcome ForSkipped(string packageId, string reason)
        {
            return new PackageApplyOutcome
            {
                PackageId = packageId ?? string.Empty,
                Reason = reason ?? string.Empty,
                Skipped = 1
            };
        }

        public static PackageApplyOutcome ForErrored(string packageId, string reason)
        {
            return new PackageApplyOutcome
            {
                PackageId = packageId ?? string.Empty,
                Reason = reason ?? string.Empty,
                Errored = 1
            };
        }

        public static PackageApplyOutcome FromReport(string packageId, RailApplyReport report, int applied, int skipped, int errored)
        {
            var outcome = new PackageApplyOutcome
            {
                PackageId = packageId ?? string.Empty,
                Reason = report?.Reason ?? string.Empty,
                Applied = applied,
                Skipped = skipped,
                Errored = errored
            };

            if (report != null)
            {
                outcome.CreatedObjects = report.CreatedObjects?.Count ?? 0;
                outcome.UpdatedObjects = report.UpdatedObjects?.Count ?? 0;
                outcome.RemovedObjects = report.RemovedObjects?.Count ?? 0;
                outcome.SkippedObjects = report.SkippedObjects?.Count ?? 0;
                outcome.Warnings = report.Warnings?.Count ?? 0;
                outcome.Errors = report.Errors?.Count ?? 0;
            }

            return outcome;
        }
    }
}
