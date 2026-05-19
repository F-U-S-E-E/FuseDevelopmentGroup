using System;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    [Collection("FusePackageFaultRegistry")]
    public class FusePackageFaultRegistryTests
    {
        public FusePackageFaultRegistryTests()
        {
            FusePackageFaultRegistry.Reset();
        }

        [Fact]
        public void Reset_ClearsAllCollections()
        {
            FusePackageFaultRegistry.RecordFault("pkg", "stage", "boom");
            FusePackageFaultRegistry.MarkDisabled("pkg", "manual");
            FusePackageFaultRegistry.MarkSkipped("pkg", "reason");
            FusePackageFaultRegistry.MarkLoadedFromDisk("pkg");
            FusePackageFaultRegistry.MarkAppliedToRuntime("pkg");

            FusePackageFaultRegistry.Reset();

            Assert.False(FusePackageFaultRegistry.IsFaulted("pkg"));
            Assert.False(FusePackageFaultRegistry.IsDisabled("pkg"));
            Assert.Empty(FusePackageFaultRegistry.GetLoadedPackageIds());
            Assert.Empty(FusePackageFaultRegistry.GetAppliedPackageIds());
            Assert.Empty(FusePackageFaultRegistry.GetSkippedPackages());
            Assert.Empty(FusePackageFaultRegistry.GetDisabledPackages());
        }

        [Fact]
        public void ClearPackage_RemovesFromAllCollections_ForOnlyThatPackage()
        {
            FusePackageFaultRegistry.RecordFault("pkg-a", "load", "boom");
            FusePackageFaultRegistry.MarkDisabled("pkg-a", "reason");
            FusePackageFaultRegistry.MarkSkipped("pkg-a", "reason");
            FusePackageFaultRegistry.MarkLoadedFromDisk("pkg-a");
            FusePackageFaultRegistry.MarkAppliedToRuntime("pkg-a");

            FusePackageFaultRegistry.RecordFault("pkg-b", "load", "boom-b");
            FusePackageFaultRegistry.MarkLoadedFromDisk("pkg-b");

            FusePackageFaultRegistry.ClearPackage("pkg-a");

            Assert.False(FusePackageFaultRegistry.IsFaulted("pkg-a"));
            Assert.False(FusePackageFaultRegistry.IsDisabled("pkg-a"));
            Assert.DoesNotContain("pkg-a", FusePackageFaultRegistry.GetLoadedPackageIds());
            Assert.DoesNotContain("pkg-a", FusePackageFaultRegistry.GetAppliedPackageIds());

            Assert.True(FusePackageFaultRegistry.IsFaulted("pkg-b"));
            Assert.Contains("pkg-b", FusePackageFaultRegistry.GetLoadedPackageIds());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ClearPackage_BlankInput_IsNoOp(string packageId)
        {
            FusePackageFaultRegistry.RecordFault("pkg", "stage", "msg");
            FusePackageFaultRegistry.ClearPackage(packageId);

            Assert.True(FusePackageFaultRegistry.IsFaulted("pkg"));
        }

        [Fact]
        public void RecordFault_Deduplicates_By_Stage_And_Message()
        {
            FusePackageFaultRegistry.RecordFault("pkg", "load", "duplicate-msg");
            FusePackageFaultRegistry.RecordFault("pkg", "load", "duplicate-msg");
            FusePackageFaultRegistry.RecordFault("pkg", "LOAD", "duplicate-msg"); // stage compare is case-insensitive
            FusePackageFaultRegistry.RecordFault("pkg", "apply", "duplicate-msg"); // different stage
            FusePackageFaultRegistry.RecordFault("pkg", "load", "different-msg");  // different message

            Assert.Equal(3, FusePackageFaultRegistry.FaultCount);
        }

        [Fact]
        public void RecordFault_NullPackageId_NormalizesToUnknown()
        {
            FusePackageFaultRegistry.RecordFault(null, "stage", "msg");

            Assert.True(FusePackageFaultRegistry.IsFaulted("<unknown>"));
            Assert.Contains("<unknown>", FusePackageFaultRegistry.GetFaultedPackageIds());
        }

        [Fact]
        public void RecordFault_WithException_PreservesDetails()
        {
            var exception = new InvalidOperationException("inner-detail");

            FusePackageFaultRegistry.RecordFault("pkg", "stage", "msg", exception);

            var fault = Assert.Single(FusePackageFaultRegistry.GetFaults());
            Assert.Contains("inner-detail", fault.Details);
        }

        [Fact]
        public void MarkDisabled_BlankReason_DefaultsTo_ManifestReason()
        {
            FusePackageFaultRegistry.MarkDisabled("pkg", null);

            var disabled = FusePackageFaultRegistry.GetDisabledPackages();
            Assert.Equal("disabled by manifest", disabled["pkg"]);
        }

        [Fact]
        public void MarkSkipped_BlankReason_DefaultsTo_Skipped()
        {
            FusePackageFaultRegistry.MarkSkipped("pkg", "   ");

            var skipped = FusePackageFaultRegistry.GetSkippedPackages();
            Assert.Equal("skipped", skipped["pkg"]);
        }

        [Theory]
        [InlineData("mixinto dependency missing id='foo'", true)]
        [InlineData("MIXINTO DEPENDENCY MISSING something", true)]
        [InlineData("some other reason", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsOptionalSkipReason_RecognizesMixintoMissingReasons(string reason, bool expected)
        {
            Assert.Equal(expected, FusePackageFaultRegistry.IsOptionalSkipReason(reason));
        }

        [Fact]
        public void WarningCount_Counts_Disabled_Plus_NonOptionalSkips()
        {
            FusePackageFaultRegistry.MarkDisabled("pkg-a", "off");
            FusePackageFaultRegistry.MarkDisabled("pkg-b", "off");
            FusePackageFaultRegistry.MarkSkipped("pkg-c", "user choice");                              // counted
            FusePackageFaultRegistry.MarkSkipped("pkg-d", "mixinto dependency missing id='bar'");      // not counted

            Assert.Equal(3, FusePackageFaultRegistry.WarningCount);
        }

        [Fact]
        public void GetFaultedPackageIds_AreSortedOrdinalIgnoreCase()
        {
            FusePackageFaultRegistry.RecordFault("Gamma", "s", "m");
            FusePackageFaultRegistry.RecordFault("alpha", "s", "m");
            FusePackageFaultRegistry.RecordFault("Beta", "s", "m");

            var ids = FusePackageFaultRegistry.GetFaultedPackageIds();

            Assert.Equal(new[] { "alpha", "Beta", "Gamma" }, ids);
        }

        [Fact]
        public void GetFaults_AreSortedBy_Package_Stage_Message()
        {
            FusePackageFaultRegistry.RecordFault("pkg-b", "apply", "b-apply-msg");
            FusePackageFaultRegistry.RecordFault("pkg-a", "load", "a-load-msg");
            FusePackageFaultRegistry.RecordFault("pkg-a", "apply", "a-apply-msg");

            var faults = FusePackageFaultRegistry.GetFaults();

            Assert.Equal(new[] { "pkg-a", "pkg-a", "pkg-b" }, faults.Select(f => f.PackageId));
            Assert.Equal(new[] { "apply", "load", "apply" }, faults.Select(f => f.Stage));
        }

        [Fact]
        public void PackageIds_AreTrimmedOnRecord()
        {
            FusePackageFaultRegistry.RecordFault("  pkg-trim  ", "stage", "msg");

            Assert.True(FusePackageFaultRegistry.IsFaulted("pkg-trim"));
            Assert.False(FusePackageFaultRegistry.IsFaulted("  pkg-trim  "));
        }
    }
}
