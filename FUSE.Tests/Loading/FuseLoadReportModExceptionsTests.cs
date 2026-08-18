using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Runtime.Registry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Pins the report surfaces for the third-party exception registry: the
    /// summary's <c>modErrors</c> segment sits between <c>orphans</c> and the
    /// <c>/fuse.report</c> suffix, the details section names each mod, the
    /// JSON block rides beside <c>runtimeGuards</c>, and HasProblems only
    /// flips for repeated faults (episodes >= 3 or count >= 10 per mod).
    /// Uses the internal Build*ForTests seams against a hand-built snapshot
    /// so no live capture registries are touched. The exception registry is
    /// static session-cumulative state shared with
    /// <see cref="FUSE.Tests.Infrastructure.FuseModExceptionRegistryTests"/>;
    /// both classes therefore sit in the same xUnit collection (xUnit runs
    /// different collections in parallel) and every test resets it first.
    /// </summary>
    [Collection(FUSE.Tests.Infrastructure.FuseModExceptionRegistryTestCollection.Name)]
    public class FuseLoadReportModExceptionsTests
    {
        public FuseLoadReportModExceptionsTests()
        {
            FuseModExceptionRegistry.ResetForTests();
        }

        /// <summary>
        /// Snapshot with every load-time registry surface empty and the
        /// mod-exception fields filled from the live registry, the same way
        /// CaptureSnapshot fills them — so whatever has been Record()ed is
        /// the only thing that can make the report react.
        /// </summary>
        private static FuseLoadReport.ReportSnapshot CreateEmptySnapshot()
        {
            return new FuseLoadReport.ReportSnapshot
            {
                Reason = "test",
                LoadedPackageIds = Array.Empty<string>(),
                AppliedPackageIds = Array.Empty<string>(),
                SkippedPackages = new Dictionary<string, string>(),
                DisabledPackages = new Dictionary<string, string>(),
                LegacyConvertedPackageIds = Array.Empty<string>(),
                Faults = Array.Empty<FusePackageFault>(),
                Conflicts = Array.Empty<FuseRegistryConflict>(),
                SceneSuppressions = Array.Empty<string>(),
                TrackGroupSuppressions = Array.Empty<string>(),
                AreaSuppressions = Array.Empty<string>(),
                UnknownSceneryAssets = Array.Empty<FuseLoadReport.UnknownSceneryAsset>(),
                GraphPostBindIssues = Array.Empty<string>(),
                ProgressionTransferSkips = Array.Empty<string>(),
                Notices = Array.Empty<string>(),
                BlockingNotices = Array.Empty<string>(),
                SceneryLoadFailures = Array.Empty<FuseLoadReport.SceneryLoadFailure>(),
                OrphanedCars = Array.Empty<FuseSaveCarFault>(),
                ModExceptions = FuseModExceptionRegistry.SnapshotForReport(),
                ModExceptionTotal = FuseModExceptionRegistry.GrandTotal,
                ModExceptionUnattributed = FuseModExceptionRegistry.TotalUnattributed
            };
        }

        private static void RecordMapEnhancerStyleException()
        {
            FuseModExceptionRegistry.Record(
                "LogHook",
                "mapEnhancer",
                "Map Enhancer",
                "NullReferenceException",
                "MapEnhancer.MapEnhancer.UpdateCullingSpheres",
                "Object reference not set to an instance of an object");
        }

        [Fact]
        public void Summary_CarriesModErrorsSegment_BetweenOrphansAndReportSuffix_EvenWhenIdle()
        {
            var summary = FuseLoadReport.BuildSummaryForTests(CreateEmptySnapshot());

            Assert.Contains("orphans 0 | modErrors 0 | /fuse.report", summary);
        }

        [Fact]
        public void Details_OmitModExceptionSection_WhenRegistryIsIdle()
        {
            var details = FuseLoadReport.BuildDetailsForTests(CreateEmptySnapshot());

            Assert.DoesNotContain("Third-party mod exceptions", details);
        }

        [Fact]
        public void PopulatedRegistry_SurfacesInSummary_Details_AndJson()
        {
            for (var i = 0; i < 10; i++)
            {
                RecordMapEnhancerStyleException();
            }

            var snapshot = CreateEmptySnapshot();

            var summary = FuseLoadReport.BuildSummaryForTests(snapshot);
            Assert.Contains($"modErrors {FuseModExceptionRegistry.GrandTotal} | /fuse.report", summary);

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains("Third-party mod exceptions observed this session:", details);
            Assert.Contains("Map Enhancer:", details);
            Assert.Contains("— top: NullReferenceException @ MapEnhancer.MapEnhancer.UpdateCullingSpheres", details);

            var json = JObject.Parse(FuseLoadReport.BuildJsonForTests(snapshot));
            var modExceptions = json["modExceptions"] as JObject;
            Assert.NotNull(modExceptions);
            Assert.Equal(FuseModExceptionRegistry.GrandTotal, (long)modExceptions["total"]);

            var mods = modExceptions["mods"] as JArray;
            Assert.NotNull(mods);
            var entry = mods.OfType<JObject>().FirstOrDefault(item => (string)item["modId"] == "mapEnhancer");
            Assert.NotNull(entry);
            Assert.Equal("Map Enhancer", (string)entry["displayName"]);

            var signatures = entry["signatures"] as JArray;
            Assert.NotNull(signatures);
            Assert.True(signatures.Count >= 1, "expected at least one signature in the JSON block");
            var topSignature = signatures.OfType<JObject>().First();
            Assert.Equal("NullReferenceException", (string)topSignature["type"]);
            Assert.Equal("MapEnhancer.MapEnhancer.UpdateCullingSpheres", (string)topSignature["frame"]);
        }

        [Fact]
        public void UnattributedBucket_RendersAsItsOwnRow_NotADuplicateFooter()
        {
            // The registry snapshots the "(unattributed)" bucket as a record
            // of its own, so the details section must not append a second
            // unattributed footer line — one mention per bucket.
            FuseModExceptionRegistry.Record(
                "LogHook", null, null, "InvalidOperationException", null, "boom");

            var details = FuseLoadReport.BuildDetailsForTests(CreateEmptySnapshot());

            Assert.Contains("Third-party mod exceptions observed this session:", details);
            Assert.Contains("(unattributed):", details);
            Assert.DoesNotContain("no recognizable mod frame", details);
        }

        [Fact]
        public void HasProblems_IgnoresOneOffException_ButTripsOnRepeats()
        {
            RecordMapEnhancerStyleException();
            Assert.False(CreateEmptySnapshot().HasProblems);

            // Nine more of the same signature crosses the count >= 10
            // threshold regardless of how the burst coalesces into episodes.
            for (var i = 0; i < 9; i++)
            {
                RecordMapEnhancerStyleException();
            }

            Assert.True(CreateEmptySnapshot().HasProblems);
        }

        [Fact]
        public void HasProblems_IgnoresInformationalNotices_ButPreservesTheAdvisory()
        {
            var snapshot = CreateEmptySnapshot();
            snapshot.Notices = new[] { "Optional alias catalog could not be read." };

            Assert.False(snapshot.HasProblems);
            Assert.True(snapshot.HasAdvisories);

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains("Notices:", details);
            Assert.Contains("Optional alias catalog could not be read.", details);

            var json = JObject.Parse(FuseLoadReport.BuildJsonForTests(snapshot));
            Assert.False((bool)json["hasProblems"]);
            Assert.True((bool)json["hasAdvisories"]);
            Assert.Equal(1, (int)json["counts"]["notices"]);
            Assert.Equal(0, (int)json["counts"]["blockingNotices"]);
        }

        [Fact]
        public void HasProblems_RetainsBlockingNoticeBehavior()
        {
            var snapshot = CreateEmptySnapshot();
            const string message = "Map-load package pipeline failed.";
            snapshot.Notices = new[] { message };
            snapshot.BlockingNotices = new[] { message };

            Assert.True(snapshot.HasProblems);
            Assert.False(snapshot.HasAdvisories);

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains("Readiness notices:", details);
            Assert.Contains(message, details);

            var json = JObject.Parse(FuseLoadReport.BuildJsonForTests(snapshot));
            Assert.True((bool)json["hasProblems"]);
            Assert.Equal(1, (int)json["counts"]["blockingNotices"]);
        }
    }
}
