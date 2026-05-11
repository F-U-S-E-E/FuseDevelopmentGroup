using System;

namespace FUSE.Registry
{
    /// <summary>
    /// A recorded attempt by a package to claim a resource that is already
    /// owned exclusively by another package.
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

        public override string ToString()
        {
            return $"target='{Target ?? Kind.ToString()}' kind='{Kind}' id='{Id}' owner='{OwnerPackageId}' attempted='{AttemptedPackageId}' resolution='{Resolution}' at={AtUtc:o}";
        }
    }
}
