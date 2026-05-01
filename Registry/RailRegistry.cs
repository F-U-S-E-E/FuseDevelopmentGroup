using System;
using System.Collections.Generic;
using System.Linq;
using RAIL.Infrastructure;

namespace RAIL.Registry
{
    /// <summary>
    /// Tracks which RAIL package owns each runtime resource. Exclusive claims permit
    /// at most one owner per (kind, id); shared claims are reference-counted across
    /// all owners. Claims are released when a package is unloaded or its definition
    /// is overwritten.
    ///
    /// Idempotency: re-claiming a resource by its current owner succeeds without
    /// emitting a conflict — supports normal reapply flows.
    /// </summary>
    public static class RailRegistry
    {
        private const int MaxConflictHistory = 1024;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> ExclusiveOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> SharedOwners =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<RailRegistryConflict> ConflictHistory =
            new List<RailRegistryConflict>();

        public static IReadOnlyList<RailRegistryConflict> Conflicts
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

        public static bool TryClaim(RailClaimKind kind, string id, string packageId)
        {
            return TryClaim(kind, id, packageId, out _);
        }

        public static bool TryClaim(RailClaimKind kind, string id, string packageId, out string existingOwner)
        {
            existingOwner = null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var key = MakeKey(kind, id);
            lock (Sync)
            {
                if (RailClaimKindPolicy.IsShared(kind))
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
                    RecordConflictLocked(kind, id, owner, packageId);
                    return false;
                }

                ExclusiveOwners[key] = packageId;
                return true;
            }
        }

        public static bool Release(RailClaimKind kind, string id, string packageId)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var key = MakeKey(kind, id);
            lock (Sync)
            {
                if (RailClaimKindPolicy.IsShared(kind))
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

        public static string GetExclusiveOwner(RailClaimKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || RailClaimKindPolicy.IsShared(kind))
            {
                return null;
            }

            lock (Sync)
            {
                return ExclusiveOwners.TryGetValue(MakeKey(kind, id), out var owner) ? owner : null;
            }
        }

        public static IReadOnlyCollection<string> GetSharedOwners(RailClaimKind kind, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !RailClaimKindPolicy.IsShared(kind))
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

        public static IEnumerable<KeyValuePair<RailClaimKind, string>> GetClaimsForPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return Array.Empty<KeyValuePair<RailClaimKind, string>>();
            }

            lock (Sync)
            {
                var result = new List<KeyValuePair<RailClaimKind, string>>();
                foreach (var kvp in ExclusiveOwners)
                {
                    if (string.Equals(kvp.Value, packageId, StringComparison.OrdinalIgnoreCase) &&
                        TryParseKey(kvp.Key, out var kind, out var id))
                    {
                        result.Add(new KeyValuePair<RailClaimKind, string>(kind, id));
                    }
                }

                foreach (var kvp in SharedOwners)
                {
                    if (kvp.Value.Contains(packageId) && TryParseKey(kvp.Key, out var kind, out var id))
                    {
                        result.Add(new KeyValuePair<RailClaimKind, string>(kind, id));
                    }
                }

                return result;
            }
        }

        public static IReadOnlyCollection<string> GetClaimedIds(RailClaimKind kind)
        {
            lock (Sync)
            {
                IEnumerable<string> source = RailClaimKindPolicy.IsShared(kind)
                    ? SharedOwners.Keys
                    : ExclusiveOwners.Keys;

                return source
                    .Where(key => TryParseKey(key, out var parsedKind, out _) && parsedKind == kind)
                    .Select(key =>
                    {
                        TryParseKey(key, out _, out var id);
                        return id;
                    })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToArray();
            }
        }

        public static RailRegistryTransaction BeginReapplyTransaction(string packageId)
        {
            return new RailRegistryTransaction(packageId);
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

        private static void RecordConflictLocked(RailClaimKind kind, string id, string owner, string attempted)
        {
            ConflictHistory.Add(new RailRegistryConflict
            {
                Kind = kind,
                Id = id,
                OwnerPackageId = owner,
                AttemptedPackageId = attempted,
                AtUtc = DateTime.UtcNow
            });

            if (ConflictHistory.Count > MaxConflictHistory)
            {
                ConflictHistory.RemoveRange(0, ConflictHistory.Count - MaxConflictHistory);
            }

            RailLog.Warning(
                $"RAIL registry conflict: package '{attempted}' attempted to claim {kind} '{id}' " +
                $"already owned by '{owner}'.");
        }

        private static string MakeKey(RailClaimKind kind, string id)
        {
            return ((int)kind).ToString() + "\0" + id;
        }

        private static bool TryParseKey(string key, out RailClaimKind kind, out string id)
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

            kind = (RailClaimKind)kindValue;
            id = key.Substring(separator + 1);
            return true;
        }
    }
}
