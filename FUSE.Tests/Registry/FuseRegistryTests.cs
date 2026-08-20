using System.Linq;
using FUSE.Runtime.Registry;
using Xunit;

namespace FUSE.Tests.Registry
{
    [Collection("FuseRegistry")]
    public class FuseRegistryTests
    {
        public FuseRegistryTests()
        {
            FuseRegistry.Reset();
        }

        [Fact]
        public void TryClaim_Exclusive_FirstClaimSucceeds()
        {
            var ok = FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            Assert.True(ok);
            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));
        }

        [Fact]
        public void TryClaim_Exclusive_SameOwner_IsIdempotent()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            var ok = FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a", out var existingOwner);

            Assert.True(ok);
            Assert.Null(existingOwner);
            Assert.Empty(FuseRegistry.Conflicts);
        }

        [Fact]
        public void TryClaim_Exclusive_DifferentOwner_FailsAndRecordsConflict()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            var ok = FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-b", out var existingOwner);

            Assert.False(ok);
            Assert.Equal("pkg-a", existingOwner);

            var conflict = Assert.Single(FuseRegistry.Conflicts);
            Assert.Equal(FuseClaimKind.Node, conflict.Kind);
            Assert.Equal("node-1", conflict.Id);
            Assert.Equal("pkg-a", conflict.OwnerPackageId);
            Assert.Equal("pkg-b", conflict.AttemptedPackageId);
        }

        [Fact]
        public void TryClaim_Exclusive_WithSuppressConflictRecord_DoesNotRecord()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            var ok = FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-b",
                suppressConflictRecord: true, out var existingOwner);

            Assert.False(ok);
            Assert.Equal("pkg-a", existingOwner);
            Assert.Empty(FuseRegistry.Conflicts);
        }

        [Fact]
        public void RecordPlannedConflict_ReportsDeleteVersusDefinitionWithoutCreatingAClaim()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Node,
                "node-removed",
                "route-a.graph",
                "route-b.patch",
                "later package removal won; earlier package definition suppressed");

            var conflict = Assert.Single(FuseRegistry.Conflicts);
            Assert.Equal(FuseClaimKind.Node, conflict.Kind);
            Assert.Equal("node-removed", conflict.Id);
            Assert.Equal("route-a.graph", conflict.OwnerPackageId);
            Assert.Equal("route-b.patch", conflict.AttemptedPackageId);
            Assert.Contains("removal won", conflict.Resolution);
            Assert.Equal(0, FuseRegistry.ExclusiveClaimCount);
        }

        [Fact]
        public void RecordPlannedConflict_DeduplicatesTheSamePackagePairAcrossReapplyOrder()
        {
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Industry,
                "destination:type=consumer;name=MP1;spans=R2,R3",
                "pkg-a",
                "pkg-b",
                "shared industry destination overlap");
            FuseRegistry.RecordPlannedConflict(
                FuseClaimKind.Industry,
                "destination:type=consumer;name=MP1;spans=R2,R3",
                "pkg-b",
                "pkg-a",
                "shared industry destination overlap");

            Assert.Single(FuseRegistry.Conflicts);
        }

        [Theory]
        [InlineData(null, "pkg")]
        [InlineData("", "pkg")]
        [InlineData("   ", "pkg")]
        [InlineData("id", null)]
        [InlineData("id", "")]
        [InlineData("id", "   ")]
        public void TryClaim_BlankInputs_ReturnFalseAndClaimNothing(string id, string packageId)
        {
            var ok = FuseRegistry.TryClaim(FuseClaimKind.Node, id, packageId);

            Assert.False(ok);
            Assert.Equal(0, FuseRegistry.ExclusiveClaimCount);
        }

        [Fact]
        public void TryClaim_Shared_MultipleOwnersAreAccumulated()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "industry-a", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "industry-a", "pkg-b");

            var owners = FuseRegistry.GetSharedOwners(FuseClaimKind.Industry, "industry-a");

            Assert.Equal(new[] { "pkg-a", "pkg-b" }.OrderBy(s => s),
                         owners.OrderBy(s => s));
            Assert.Empty(FuseRegistry.Conflicts);
        }

        [Fact]
        public void GetExclusiveOwner_OnSharedKind_ReturnsNull()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "industry-a", "pkg-a");

            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Industry, "industry-a"));
        }

        [Fact]
        public void GetSharedOwners_OnExclusiveKind_ReturnsEmpty()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            Assert.Empty(FuseRegistry.GetSharedOwners(FuseClaimKind.Node, "node-1"));
        }

        [Fact]
        public void Release_Exclusive_OnlyByOwner()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            Assert.False(FuseRegistry.Release(FuseClaimKind.Node, "node-1", "pkg-other"));
            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));

            Assert.True(FuseRegistry.Release(FuseClaimKind.Node, "node-1", "pkg-a"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));
        }

        [Fact]
        public void Release_Shared_RemovesOnlyTheGivenOwner()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-b");

            Assert.True(FuseRegistry.Release(FuseClaimKind.Industry, "ind-1", "pkg-a"));

            Assert.Equal(new[] { "pkg-b" }, FuseRegistry.GetSharedOwners(FuseClaimKind.Industry, "ind-1"));
        }

        [Fact]
        public void Release_Shared_LastOwner_RemovesKey()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-a");
            FuseRegistry.Release(FuseClaimKind.Industry, "ind-1", "pkg-a");

            Assert.Empty(FuseRegistry.GetSharedOwners(FuseClaimKind.Industry, "ind-1"));
            Assert.Equal(0, FuseRegistry.SharedClaimCount);
        }

        [Fact]
        public void ReleaseAllForPackage_ReleasesExclusiveAndShared_AndLeavesOthersAlone()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Segment, "seg-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-other");
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-2", "pkg-other");

            var released = FuseRegistry.ReleaseAllForPackage("pkg-a");

            Assert.Equal(3, released);
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Segment, "seg-1"));
            Assert.Equal(new[] { "pkg-other" }, FuseRegistry.GetSharedOwners(FuseClaimKind.Industry, "ind-1"));
            Assert.Equal("pkg-other", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-2"));
        }

        [Fact]
        public void GetClaimsForPackage_ReturnsAllClaimsAcrossKinds()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-a");

            var claims = FuseRegistry.GetClaimsForPackage("pkg-a").ToArray();

            Assert.Equal(2, claims.Length);
            Assert.Contains(claims, c => c.Key == FuseClaimKind.Node && c.Value == "node-1");
            Assert.Contains(claims, c => c.Key == FuseClaimKind.Industry && c.Value == "ind-1");
        }

        [Fact]
        public void Reset_ClearsAllStateIncludingConflicts()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-b"); // conflict
            FuseRegistry.TryClaim(FuseClaimKind.Industry, "ind-1", "pkg-c");

            FuseRegistry.Reset();

            Assert.Equal(0, FuseRegistry.ExclusiveClaimCount);
            Assert.Equal(0, FuseRegistry.SharedClaimCount);
            Assert.Empty(FuseRegistry.Conflicts);
        }

        [Fact]
        public void ClearConflictHistory_RemovesConflictsButKeepsClaims()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-b"); // conflict

            FuseRegistry.ClearConflictHistory();

            Assert.Empty(FuseRegistry.Conflicts);
            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));
        }
    }

    [Collection("FuseRegistry")]
    public class FuseRegistryTransactionTests
    {
        public FuseRegistryTransactionTests()
        {
            FuseRegistry.Reset();
        }

        [Fact]
        public void Begin_SnapshotsAndReleasesExistingClaims()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "node-1", "pkg-a");

            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                Assert.Equal(1, tx.SnapshotSize);
                Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "node-1"));
                tx.Commit();
            }
        }

        [Fact]
        public void Commit_KeepsNewClaims_DiscardsSnapshot()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "old-node", "pkg-a");

            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                FuseRegistry.TryClaim(FuseClaimKind.Node, "new-node", "pkg-a");
                tx.Commit();
            }

            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "new-node"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "old-node"));
        }

        [Fact]
        public void Rollback_RestoresSnapshot_DiscardsNewClaims()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "old-node", "pkg-a");

            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                FuseRegistry.TryClaim(FuseClaimKind.Node, "new-node", "pkg-a");
                tx.Rollback();
            }

            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "old-node"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "new-node"));
        }

        [Fact]
        public void Dispose_WithoutCommit_RollsBack()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "old-node", "pkg-a");

            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                FuseRegistry.TryClaim(FuseClaimKind.Node, "new-node", "pkg-a");
                // No Commit — Dispose should rollback.
            }

            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "old-node"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "new-node"));
        }

        [Fact]
        public void DoubleCommit_IsNoOp()
        {
            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                tx.Commit();
                tx.Commit(); // must not throw
            }
        }

        [Fact]
        public void Rollback_AfterCommit_IsNoOp()
        {
            FuseRegistry.TryClaim(FuseClaimKind.Node, "old-node", "pkg-a");

            using (var tx = FuseRegistry.BeginReapplyTransaction("pkg-a"))
            {
                FuseRegistry.TryClaim(FuseClaimKind.Node, "new-node", "pkg-a");
                tx.Commit();
                tx.Rollback(); // already finished — must not undo the committed state
            }

            Assert.Equal("pkg-a", FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "new-node"));
            Assert.Null(FuseRegistry.GetExclusiveOwner(FuseClaimKind.Node, "old-node"));
        }
    }
}
