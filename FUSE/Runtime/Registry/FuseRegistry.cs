using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;

namespace FUSE.Runtime.Registry
{
    /// <summary>
    /// Tracks which FUSE package owns each runtime resource. Exclusive claims permit
    /// at most one owner per (kind, id); shared claims are reference-counted across
    /// all owners. Claims are released when a package is unloaded or its definition
    /// is overwritten.
    ///
    /// Idempotency: re-claiming a resource by its current owner succeeds without
    /// emitting a conflict — supports normal reapply flows.
    /// </summary>
    public static class FuseRegistry
    {
        private const int MaxConflictHistory = 1024;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> ExclusiveOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> SharedOwners =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FuseRegistryConflict> ConflictHistory =
            new List<FuseRegistryConflict>();

        public static IReadOnlyList<FuseRegistryConflict> Conflicts
        {
            get
            {
                lock (Sync)
                {
                    return ConflictHistory.ToArray();
                }
            }
        }

        public static int ExclusiveClaimCount
        {
            get
            {
                lock (Sync)
                {
                    return ExclusiveOwners.Count;
                }
            }
        }

        public static int SharedClaimCount
        {
            get
            {
                lock (Sync)
                {
                    return SharedOwners.Count;
                }
            }
        }

        public static bool TryClaim(FuseClaimKind kind, string id, string packageId)
        {
            return TryClaim(kind, id, packageId, out _);
        }

        public static bool TryClaim(FuseClaimKind kind, string id, string packageId, out string existingOwner)
        {
            return TryClaim(kind, id, packageId, false, out existingOwner);
        }

        public static bool TryClaim(FuseClaimKind kind, string id, string packageId, bool suppressConflictRecord, out string existingOwner)
        {
            existingOwner = null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var key = MakeKey(kind, id);
            lock (Sync)
            {
                if (FuseClaimKindPolicy.IsShared(kind))
                {
                    if (!SharedOwners.TryGetValue(key, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        SharedOwners[key] = set;
                    }

                    set.Add(packageId);
                    return true;
                }

                if (ExclusiveOwners.TryGetValue(key, out var owner))
                {
                    if (string.Equals(owner, packageId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    existingOwner = owner;
                    if (!suppressConflictRecord)
                    {
                        RecordConflictLocked(kind, id, owner, packageId);
                    }

                    return false;
                }

                ExclusiveOwners[key] = packageId;
                return true;
            }
        }

        public static bool Release(FuseClaimKind kind, string id, string packageId)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var key = MakeKey(kind, id);
            lock (Sync)
            {
                if (FuseClaimKindPolicy.IsShared(kind))
                {
                    if (!SharedOwners.TryGetValue(key, out var set) || !set.Remove(packageId))
                    {
                        return false;
                    }

                    if (set.Count == 0)
                    {
                        SharedOwners.Remove(key);
                    }

                    return true;
                }

                if (!ExclusiveOwners.TryGetValue(key, out var owner) ||
                    !string.Equals(owner, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                ExclusiveOwners.Remove(key);
                return true;
            }
        }

        public static int ReleaseAllForPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return 0;
            }

            var released = 0;
            lock (Sync)
            {
                var exclusiveKeys = ExclusiveOwners
                    .Where(kvp => string.Equals(kvp.Value, packageId, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key)
                    .ToArray();
                foreach (var key in exclusiveKeys)
                {
                    ExclusiveOwners.Remove(key);
                    released++;
                }

                var emptyShared = new List<string>();
                foreach (var kvp in SharedOwners)
                {
                    if (kvp.Value.Remove(packageId))
                    {
                        released++;
                        if (kvp.Value.Count == 0)
                        {
                            emptyShared.Add(kvp.Key);
                        }
                    }
                }

                foreach (var key in emptyShared)
                {
                    SharedOwners.Remove(key);
                }
            }

            return released;
        }

        public static string GetExclusiveOwner(FuseClaimKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || FuseClaimKindPolicy.IsShared(kind))
            {
                return null;
            }

            lock (Sync)
            {
                return ExclusiveOwners.TryGetValue(MakeKey(kind, id), out var owner) ? owner : null;
            }
        }

        public static IReadOnlyCollection<string> GetSharedOwners(FuseClaimKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !FuseClaimKindPolicy.IsShared(kind))
            {
                return Array.Empty<string>();
            }

            lock (Sync)
            {
                if (SharedOwners.TryGetValue(MakeKey(kind, id), out var set))
                {
                    return set.ToArray();
                }

                return Array.Empty<string>();
            }
        }

        public static IEnumerable<KeyValuePair<FuseClaimKind, string>> GetClaimsForPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return Array.Empty<KeyValuePair<FuseClaimKind, string>>();
            }

            lock (Sync)
            {
                var result = new List<KeyValuePair<FuseClaimKind, string>>();
                foreach (var kvp in ExclusiveOwners)
                {
                    if (string.Equals(kvp.Value, packageId, StringComparison.OrdinalIgnoreCase) &&
                        TryParseKey(kvp.Key, out var kind, out var id))
                    {
                        result.Add(new KeyValuePair<FuseClaimKind, string>(kind, id));
                    }
                }

                foreach (var kvp in SharedOwners)
                {
                    if (kvp.Value.Contains(packageId) && TryParseKey(kvp.Key, out var kind, out var id))
                    {
                        result.Add(new KeyValuePair<FuseClaimKind, string>(kind, id));
                    }
                }

                return result;
            }
        }

        public static IReadOnlyCollection<string> GetClaimedIds(FuseClaimKind kind)
        {
            lock (Sync)
            {
                IEnumerable<string> source = FuseClaimKindPolicy.IsShared(kind)
                    ? SharedOwners.Keys
                    : ExclusiveOwners.Keys;

                return source
                    .Where(key => TryParseKey(key, out var parsedKind, out _) && parsedKind == kind)
                    .Select(key =>
                    {
                        _ = TryParseKey(key, out _, out var id);
                        return id;
                    })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();
            }
        }

        public static FuseRegistryTransaction BeginReapplyTransaction(string packageId)
        {
            return new FuseRegistryTransaction(packageId);
        }

        public static void Reset()
        {
            lock (Sync)
            {
                ExclusiveOwners.Clear();
                SharedOwners.Clear();
                ConflictHistory.Clear();
            }
        }

        public static void ClearConflictHistory()
        {
            lock (Sync)
            {
                ConflictHistory.Clear();
            }
        }

        /// <summary>
        /// Records a conflict discovered while constructing a merged final
        /// state, before either package can create a normal runtime claim. This
        /// is used for delete-vs-definition collisions where the later delete
        /// removes the earlier package from the plan entirely.
        /// </summary>
        internal static void RecordPlannedConflict(
            FuseClaimKind kind,
            string id,
            string ownerPackageId,
            string attemptedPackageId,
            string resolution)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(ownerPackageId) ||
                string.IsNullOrWhiteSpace(attemptedPackageId) ||
                string.Equals(ownerPackageId, attemptedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (Sync)
            {
                if (ConflictHistory.Any(existing =>
                    existing.Kind == kind &&
                    string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    ((string.Equals(existing.OwnerPackageId, ownerPackageId, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(existing.AttemptedPackageId, attemptedPackageId, StringComparison.OrdinalIgnoreCase)) ||
                     (string.Equals(existing.OwnerPackageId, attemptedPackageId, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(existing.AttemptedPackageId, ownerPackageId, StringComparison.OrdinalIgnoreCase)))))
                {
                    return;
                }

                RecordConflictLocked(
                    kind,
                    id,
                    ownerPackageId,
                    attemptedPackageId,
                    "merge package plan",
                    string.IsNullOrWhiteSpace(resolution)
                        ? "merged plan collision"
                        : resolution);
            }
        }

        private static void RecordConflictLocked(FuseClaimKind kind, string id, string owner, string attempted)
        {
            RecordConflictLocked(
                kind,
                id,
                owner,
                attempted,
                "claim runtime object",
                "claim skipped; existing owner retained");
        }

        private static void RecordConflictLocked(
            FuseClaimKind kind,
            string id,
            string owner,
            string attempted,
            string operation,
            string resolution)
        {
            ConflictHistory.Add(new FuseRegistryConflict
            {
                Kind = kind,
                Target = kind.ToString(),
                Id = id,
                OwnerPackageId = owner,
                AttemptedPackageId = attempted,
                Resolution = resolution,
                AtUtc = DateTime.UtcNow
            });

            if (ConflictHistory.Count > MaxConflictHistory)
            {
                ConflictHistory.RemoveRange(0, ConflictHistory.Count - MaxConflictHistory);
            }

            FuseLog.Warning(
                $"FUSE registry conflict package='{attempted}' operation='{operation}' " +
                $"target='{kind}' kind='{kind}' id='{id}' owner='{owner}' " +
                $"resolution='{resolution}'.");
        }

        private static string MakeKey(FuseClaimKind kind, string id)
        {
            return ((int)kind).ToString() + "\0" + id;
        }

        private static bool TryParseKey(string key, out FuseClaimKind kind, out string id)
        {
            kind = default;
            id = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var separator = key.IndexOf('\0');
            if (separator < 0)
            {
                return false;
            }

            if (!int.TryParse(key.Substring(0, separator), out var kindValue))
            {
                return false;
            }

            kind = (FuseClaimKind)kindValue;
            id = key.Substring(separator + 1);
            return true;
        }
    }
}
