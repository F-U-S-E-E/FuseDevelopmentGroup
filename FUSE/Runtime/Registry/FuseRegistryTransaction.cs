using System;
using System.Collections.Generic;
using System.Linq;

namespace FUSE.Registry
{
    /// <summary>
    /// Snapshots a package's existing claims, releases them, and lets the caller
    /// re-apply. Commit() makes any new claims final. Disposing without Commit
    /// (or calling Rollback) restores the snapshot — releasing every claim made
    /// during the transaction and re-claiming the prior set. This guarantees that
    /// a partial-failure reapply leaves prior claims intact.
    /// </summary>
    public sealed class FuseRegistryTransaction : IDisposable
    {
        private readonly string _packageId;
        private readonly KeyValuePair<FuseClaimKind, string>[] _snapshot;
        private bool _committed;
        private bool _finished;

        internal FuseRegistryTransaction(string packageId)
        {
            _packageId = packageId ?? string.Empty;
            _snapshot = FuseRegistry.GetClaimsForPackage(_packageId).ToArray();
            FuseRegistry.ReleaseAllForPackage(_packageId);
        }

        public string PackageId => _packageId;

        public int SnapshotSize => _snapshot.Length;

        public void Commit()
        {
            if (_finished)
            {
                return;
            }

            _committed = true;
            _finished = true;
        }

        public void Rollback()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            // Drop any new claims made during the transaction, then restore.
            FuseRegistry.ReleaseAllForPackage(_packageId);
            foreach (var pair in _snapshot)
            {
                FuseRegistry.TryClaim(pair.Key, pair.Value, _packageId);
            }
        }

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            if (!_committed)
            {
                Rollback();
            }
            else
            {
                _finished = true;
            }
        }
    }
}
