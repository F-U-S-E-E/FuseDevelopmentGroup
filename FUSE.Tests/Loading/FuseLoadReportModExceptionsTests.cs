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
    /// exception counts stay out of the readiness summary while the details
    /// and structured JSON retain the complete diagnostic evidence. Runtime
    /// exception observations never change package readiness.
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
        public void Summary_OmitsRuntimeExceptionNoise()
        {
            var summary = FuseLoadReport.BuildSummaryForTests(CreateEmptySnapshot());

            Assert.Contains("orphans 0 | /fuse.report", summary);
            Assert.DoesNotContain("modErrors", summary);
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
            Assert.DoesNotContain("modErrors", summary);

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
        public void HasProblems_IgnoresOneOffAndRepeatingRuntimeExceptions()
        {
            RecordMapEnhancerStyleException();
            Assert.False(CreateEmptySnapshot().HasProblems);

            // Nine more of the same signature crosses the count >= 10
            // threshold regardless of how the burst coalesces into episodes.
            for (var i = 0; i < 9; i++)
            {
                RecordMapEnhancerStyleException();
            }

            Assert.False(CreateEmptySnapshot().HasProblems);
            Assert.True(CreateEmptySnapshot().HasModExceptionProblem);
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

        [Fact]
        public void PackageFaultLocation_RendersInHumanAndStructuredReports()
        {
            var snapshot = CreateEmptySnapshot();
            snapshot.Faults = new[]
            {
                new FusePackageFault(
                    "Broken.Track",
                    "schema validation",
                    "Segment is missing an endpoint.",
                    "validator details",
                    @"C:\Railroader\Mods\Broken.Track",
                    @"C:\Railroader\Mods\Broken.Track\track.fuse.json",
                    "track.segments.bad.a",
                    42,
                    17,
                    "Add endpoint node a.")
            };

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains(@"file: C:\Railroader\Mods\Broken.Track\track.fuse.json", details);
            Assert.Contains("relative file: track.fuse.json", details);
            Assert.Contains("JSON location: track.segments.bad.a line 42, position 17", details);
            Assert.Contains("action: Add endpoint node a.", details);

            var json = JObject.Parse(FuseLoadReport.BuildJsonForTests(snapshot));
            var fault = (JObject)json["packages"]["faults"][0];
            Assert.Equal(@"C:\Railroader\Mods\Broken.Track\track.fuse.json", (string)fault["sourceFile"]);
            Assert.Equal("track.fuse.json", (string)fault["relativeSourceFile"]);
            Assert.Equal("Broken.Track", (string)fault["packageName"]);
            Assert.Equal("track.segments.bad.a", (string)fault["jsonPath"]);
            Assert.Equal(42, (int)fault["lineNumber"]);
            Assert.Equal("validator details", (string)fault["details"]);
        }

        [Fact]
        public void Shared_extension_merge_does_not_inflate_conflicts_or_health()
        {
            var snapshot = CreateEmptySnapshot();
            snapshot.Conflicts = new[]
            {
                new FuseRegistryConflict
                {
                    Kind = FuseClaimKind.Industry,
                    Id = "destination:type=teleportLoading,name=Kirkland Valley Coal",
                    OwnerPackageId = "CF.AndrewsCoalPower.FUSE.patch",
                    AttemptedPackageId = "Katers.TuckasegeeSteelWorks.FUSE.kirklandcoal",
                    Resolution = "shared industry destination overlap; definitions merged into the same runtime location"
                }
            };

            Assert.Equal(0, snapshot.ActionableConflictCount);
            Assert.Equal(1, snapshot.CooperativeConflictCount);
            Assert.False(snapshot.HasProblems);

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains("Ownership conflicts requiring attention: 0", details);
            Assert.Contains("shared extension targets merged successfully: 1", details);

            var json = JObject.Parse(FuseLoadReport.BuildJsonForTests(snapshot));
            Assert.Equal(0, (int)json["counts"]["conflicts"]);
            Assert.Equal(1, (int)json["counts"]["sharedExtensionOverlaps"]);
            Assert.Equal("shared-extension", (string)json["conflicts"][0]["classification"]);
        }

        [Fact]
        public void Optional_mixinto_skips_are_reported_as_inactive_fragments()
        {
            var snapshot = CreateEmptySnapshot();
            snapshot.AppliedPackageIds = new[] { "Author.Package.base" };
            snapshot.SkippedPackages = new Dictionary<string, string>
            {
                ["Author.Package.optional"] =
                    "package='Author.Package.optional' mixinto dependency missing id='Optional.Companion' " +
                    "target='game-graph' folder='C:\\Mods\\AuthorPackage' sourceFile='legacy://optional.json'"
            };

            Assert.False(snapshot.HasProblems);
            Assert.Empty(snapshot.ActionableSkippedPackages);
            Assert.Single(snapshot.OptionalSkippedPackages);

            var details = FuseLoadReport.BuildDetailsForTests(snapshot);
            Assert.Contains("Runtime definitions: resident=0; applied=1; actionable skips=0; optional fragments inactive=1.", details);
            Assert.Contains("Optional conditional fragments inactive:", details);
            Assert.DoesNotContain("Skipped packages:", details);
        }
    }
}
