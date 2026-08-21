using System;
using System.IO;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
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
        public void LoadDefinition_UsesManifestPackageIdForValidationFaults()
        {
            var definition = new FuseModDefinition
            {
                Id = "definition-id",
                Name = string.Empty,
                SchemaVersion = "1.0"
            };

            Assert.Throws<InvalidOperationException>(() =>
                FuseModLoader.LoadDefinition(definition, null, null, "manifest-id"));

            Assert.True(FusePackageFaultRegistry.IsFaulted("manifest-id"));
            Assert.False(FusePackageFaultRegistry.IsFaulted("definition-id"));
            Assert.All(
                FusePackageFaultRegistry.GetFaults(),
                fault => Assert.Equal("manifest-id", fault.PackageId));
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
        public void RecordFault_ExtractsJsonLocationAndSourceFile()
        {
            Exception exception = null;
            try
            {
                JObject.Parse("{\"track\": [ }");
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            var folderPath = Path.Combine("C:\\", "Railroader", "Mods", "BrokenTrack");
            var source = Path.Combine(folderPath, "track.fuse.json");
            FusePackageFaultRegistry.RecordFault(
                "BrokenTrack",
                "JSON deserialization",
                "Invalid JSON",
                exception,
                folderPath,
                source);

            var fault = Assert.Single(FusePackageFaultRegistry.GetFaults());
            Assert.Equal(source, fault.SourceFile);
            Assert.Equal("BrokenTrack", fault.PackageName);
            Assert.Equal("track.fuse.json", fault.RelativeSourceFile);
            Assert.Equal("track", fault.JsonPath);
            Assert.True(fault.LineNumber > 0);
            Assert.Contains("Valid JSON", fault.ExpectedShape);
            Assert.Contains("Unexpected character", fault.ReceivedValue);
            Assert.Contains("Correct the JSON", fault.SuggestedAction);
        }

        [Fact]
        public void RecordFault_PreservesSchemaExpectationCodeAndReceivedValue()
        {
            var folderPath = Path.Combine("C:\\", "Railroader", "Mods", "Pretty Folder");
            FusePackageFaultRegistry.RecordFault(
                "pkg",
                "schema validation",
                "Number expected.",
                folderPath: folderPath,
                sourceFile: Path.Combine(folderPath, "map.fuse.json"),
                jsonPath: "operations.loaders.loader.rate",
                packageName: "Pretty Package",
                validationCode: "fuse.number",
                expectedShape: "A finite number greater than zero.",
                receivedValue: "fast");

            var fault = Assert.Single(FusePackageFaultRegistry.GetFaults());
            Assert.Equal("Pretty Package", fault.PackageName);
            Assert.Equal("map.fuse.json", fault.RelativeSourceFile);
            Assert.Equal("fuse.number", fault.ValidationCode);
            Assert.Equal("A finite number greater than zero.", fault.ExpectedShape);
            Assert.Equal("fast", fault.ReceivedValue);
        }

        [Fact]
        public void MarkDisabled_BlankReason_DefaultsTo_ManifestReason()
        {
            FusePackageFaultRegistry.MarkDisabled("pkg", null);

            var disabled = FusePackageFaultRegistry.GetDisabledPackages();
            Assert.Equal("disabled by manifest", disabled["pkg"]);
        }

        [Fact]
        public void MarkDisabled_DoesNotMakePackageAnActionableSkip()
        {
            FusePackageFaultRegistry.MarkDisabled("pkg", "disabled by active FUSE mod set");

            Assert.Empty(FusePackageFaultRegistry.GetSkippedPackages());
            Assert.Equal(
                "disabled by active FUSE mod set",
                FusePackageFaultRegistry.GetDisabledPackages()["pkg"]);
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
        [InlineData("package='Author.Optional' mixinto dependency missing id='Companion.Mod' target='game-graph'", true)]
        [InlineData("package='Author.Optional' mixinto conflict matched id='Incompatible.Mod' target='game-graph'", true)]
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
