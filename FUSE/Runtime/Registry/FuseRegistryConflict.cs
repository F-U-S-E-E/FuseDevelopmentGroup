using System;

namespace FUSE.Runtime.Registry
{
    /// <summary>
    /// A recorded attempt by a package to claim a resource that is already
    /// owned by another package. Some records are blocking exclusive claims;
    /// others describe an allowed shared merge that authors still need to see.
    /// </summary>
    public sealed class FuseRegistryConflict
    {
        public FuseClaimKind Kind { get; internal set; }
        public string Target { get; internal set; }
        public string Id { get; internal set; }
        public string OwnerPackageId { get; internal set; }
        public string AttemptedPackageId { get; internal set; }
        public string Resolution { get; internal set; }
        public DateTime AtUtc { get; internal set; }

        /// <summary>
        /// True when the record documents a successful cumulative merge rather
        /// than one package losing ownership or having an operation skipped.
        /// These records remain useful to authors, but are not load-health
        /// failures and must not inflate the user-facing conflict count.
        /// </summary>
        public bool IsCooperativeMerge =>
            Contains("shared industry destination overlap") ||
            Contains("definitions merged into the same runtime location") ||
            Contains("shared industry component removal overlap") ||
            Contains("shared merge");

        private bool Contains(string value)
        {
            return !string.IsNullOrWhiteSpace(Resolution) &&
                   Resolution.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override string ToString()
        {
            return $"target='{Target ?? Kind.ToString()}' kind='{Kind}' id='{Id}' owner='{OwnerPackageId}' attempted='{AttemptedPackageId}' resolution='{Resolution}' at={AtUtc:o}";
        }
    }
}
