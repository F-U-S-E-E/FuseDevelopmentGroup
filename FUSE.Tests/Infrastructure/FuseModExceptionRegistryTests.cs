using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    [CollectionDefinition(Name)]
    public sealed class FuseModExceptionRegistryTestCollection
    {
        public const string Name = "FuseModExceptionRegistry";
    }

    /// <summary>
    /// Tests for the legacy-mod health monitor's exception registry (visible
    /// via InternalsVisibleTo). The registry is the single sink all three
    /// capture sources write and the load report / Status page read, so its
    /// dedupe, episode coalescing, caps, and summary line are the contract a
    /// pasted /fuse.report depends on. State is static and
    /// session-cumulative; every test resets first and the collection keeps
    /// classes that share the statics from interleaving.
    /// </summary>
    [Collection(FuseModExceptionRegistryTestCollection.Name)]
    public class FuseModExceptionRegistryTests
    {
        public FuseModExceptionRegistryTests()
        {
            FuseModExceptionRegistry.ResetForTests();
            FuseModAttributionMap.ResetForTests();
        }

        private static void RecordDefault(
            string modId = "mapenhancer",
            string frame = "MapEnhancer.MapEnhancer.UpdateCullingSpheres",
            string source = "LogHook",
            string exceptionType = "NullReferenceException",
            string message = "Object reference not set to an instance of an object")
        {
            FuseModExceptionRegistry.Record(source, modId, "Map Enhancer", exceptionType, frame, message);
        }

        [Fact]
        public void FreshRegistry_IsIdle_AndSummarizesAllZero()
        {
            Assert.True(FuseModExceptionRegistry.AllIdle);
            Assert.Equal(0, FuseModExceptionRegistry.GrandTotal);
            Assert.Equal(0, FuseModExceptionRegistry.TotalUnattributed);
            Assert.Equal("modErrors=0 unattributed=0 mods=0", FuseModExceptionRegistry.FormatSummary());
            Assert.Empty(FuseModExceptionRegistry.SnapshotForReport());
        }

        [Fact]
        public void Record_CreatesModRecordAndSignature()
        {
            RecordDefault();

            Assert.False(FuseModExceptionRegistry.AllIdle);
            Assert.Equal(1, FuseModExceptionRegistry.GrandTotal);
            Assert.Equal("modErrors=1 unattributed=0 mods=1", FuseModExceptionRegistry.FormatSummary());

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal("mapenhancer", snapshot.ModId);
            Assert.Equal("Map Enhancer", snapshot.DisplayName);
            Assert.Equal(1, snapshot.Count);
            Assert.Equal(1, snapshot.Episodes);

            var signature = Assert.Single(snapshot.Signatures);
            Assert.Equal("NullReferenceException", signature.ExceptionType);
            Assert.Equal("MapEnhancer.MapEnhancer.UpdateCullingSpheres", signature.TopOwnedFrame);
            Assert.Equal("LogHook", signature.Source);
            Assert.Equal(1, signature.Count);
            Assert.Equal(1, signature.Episodes);
            Assert.Equal("Object reference not set to an instance of an object", signature.SampleMessage);
        }

        [Fact]
        public void SameSignature_Dedupes_DistinctSignature_GetsOwnRow()
        {
            RecordDefault();
            RecordDefault();
            RecordDefault(frame: "MapEnhancer.MapEnhancer.CreateSwitches");

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(3, snapshot.Count);
            Assert.Equal(2, snapshot.Signatures.Length);

            // Worst signature first.
            Assert.Equal("MapEnhancer.MapEnhancer.UpdateCullingSpheres", snapshot.Signatures[0].TopOwnedFrame);
            Assert.Equal(2, snapshot.Signatures[0].Count);
            Assert.Equal(1, snapshot.Signatures[1].Count);
        }

        [Fact]
        public void EpisodeCoalescing_MergesOccurrencesWithinOneSecond()
        {
            long tick = 0;
            FuseModExceptionRegistry.TickSource = () => tick;

            RecordDefault();          // tick 0    -> episode 1
            tick = 400;
            RecordDefault();          // +400ms    -> same episode
            tick = 900;
            RecordDefault();          // +500ms    -> same episode (sliding window)
            tick = 2000;
            RecordDefault();          // +1100ms   -> episode 2
            tick = 2100;
            RecordDefault();          // +100ms    -> same episode
            tick = 3500;
            RecordDefault();          // +1400ms   -> episode 3

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(6, snapshot.Count);
            Assert.Equal(3, snapshot.Episodes);

            var signature = Assert.Single(snapshot.Signatures);
            Assert.Equal(6, signature.Count);
            Assert.Equal(3, signature.Episodes);
        }

        [Fact]
        public void Episodes_AreTrackedPerSignature()
        {
            long tick = 0;
            FuseModExceptionRegistry.TickSource = () => tick;

            RecordDefault(frame: "ModA.First");
            tick = 100;
            RecordDefault(frame: "ModA.Second");
            tick = 200;
            RecordDefault(frame: "ModA.First");
            RecordDefault(frame: "ModA.Second");

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            // Each signature stays one episode; the mod aggregates both.
            Assert.Equal(2, snapshot.Episodes);
            Assert.All(snapshot.Signatures, signature => Assert.Equal(1, signature.Episodes));
        }

        [Fact]
        public void SignatureCap_KeepsEight_AndCountsOverflowIntoModTotals()
        {
            long tick = 0;
            FuseModExceptionRegistry.TickSource = () => tick;

            for (var i = 0; i < 10; i++)
            {
                RecordDefault(frame: $"MapEnhancer.MapEnhancer.Method{i}");
            }

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(8, snapshot.Signatures.Length);
            Assert.Equal(10, snapshot.Count);
            // 8 tracked signatures (1 episode each) + the two overflow
            // occurrences at the same tick coalescing into one episode.
            Assert.Equal(9, snapshot.Episodes);
            Assert.Equal(2, FuseModExceptionRegistry.SignatureOverflowDropped);
            Assert.Equal(10, FuseModExceptionRegistry.GrandTotal);
        }

        [Fact]
        public void ModCap_FoldsLaterModsIntoTheOverflowBucket()
        {
            for (var i = 0; i < 40; i++)
            {
                FuseModExceptionRegistry.Record(
                    "LogHook", $"mod{i}", $"Mod {i}", "NullReferenceException", $"Mod{i}.Type.Method", "boom");
            }

            var snapshots = FuseModExceptionRegistry.SnapshotForReport();
            Assert.Equal(33, snapshots.Length); // 32 named + "<other>"

            var overflow = snapshots.Single(s => s.ModId == FuseModExceptionRegistry.OverflowModId);
            Assert.Equal(8, overflow.Count);
            Assert.Equal("(other mods)", overflow.DisplayName);

            // Sentinel buckets stay out of the mods= count; overflow means it is a floor.
            Assert.Equal("modErrors=40 unattributed=0 mods=32", FuseModExceptionRegistry.FormatSummary());
        }

        [Fact]
        public void NullOrSentinelModId_LandsInTheUnattributedBucket()
        {
            FuseModExceptionRegistry.Record("LogHook", null, null, "NullReferenceException", "Game.Foo.Bar", "boom");
            FuseModExceptionRegistry.Record(
                "LogHook", FuseModExceptionRegistry.UnattributedModId, null, "NullReferenceException", "Game.Foo.Bar", "boom");

            Assert.Equal(2, FuseModExceptionRegistry.TotalUnattributed);
            Assert.Equal("modErrors=2 unattributed=2 mods=0", FuseModExceptionRegistry.FormatSummary());

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(FuseModExceptionRegistry.UnattributedModId, snapshot.ModId);
            Assert.Equal("(unattributed)", snapshot.DisplayName);
            Assert.Equal(2, snapshot.Count);
        }

        [Fact]
        public void RecordContained_ByModId_UsesTheLegacyHostSource()
        {
            FuseModExceptionRegistry.RecordContained(
                new InvalidOperationException("plugin blew up"), "legacy.pack", "legacy host OnEnable");

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal("legacy.pack", snapshot.ModId);

            var signature = Assert.Single(snapshot.Signatures);
            Assert.Equal("LegacyHost", signature.Source);
            Assert.Equal("InvalidOperationException", signature.ExceptionType);
            Assert.Equal("legacy host OnEnable", signature.TopOwnedFrame);
            Assert.Equal("plugin blew up", signature.SampleMessage);
        }

        [Fact]
        public void RecordContained_ByType_AttributesThroughTheAttributionMap()
        {
            FuseModAttributionMap.SetMapsForTests(
                tokenMap: null,
                assemblyMap: new Dictionary<Assembly, (string modId, string displayName)>
                {
                    [typeof(FuseModExceptionRegistryTests).Assembly] = ("test.mod", "Test Mod")
                });

            FuseModExceptionRegistry.RecordContained(
                new NullReferenceException("boom"), typeof(FuseModExceptionRegistryTests), "map lifecycle listener");

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal("test.mod", snapshot.ModId);
            Assert.Equal("Test Mod", snapshot.DisplayName);

            var signature = Assert.Single(snapshot.Signatures);
            Assert.Equal("Messenger", signature.Source);
            Assert.Contains("FuseModExceptionRegistryTests", signature.TopOwnedFrame);
            Assert.Contains("[map lifecycle listener]", signature.TopOwnedFrame);
        }

        [Fact]
        public void RecordContained_ByType_FallsBackToUnattributed_WhenTheMapDoesNotKnowTheAssembly()
        {
            FuseModAttributionMap.SetMapsForTests(tokenMap: null, assemblyMap: null);

            FuseModExceptionRegistry.RecordContained(
                new NullReferenceException("boom"), typeof(FuseModExceptionRegistryTests), "map lifecycle listener");

            Assert.Equal(1, FuseModExceptionRegistry.TotalUnattributed);
            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(FuseModExceptionRegistry.UnattributedModId, snapshot.ModId);
            // The recipient identity is preserved even without attribution.
            Assert.Contains("FuseModExceptionRegistryTests", snapshot.Signatures[0].TopOwnedFrame);
        }

        [Fact]
        public void RecordContained_NullException_IsANoOp()
        {
            FuseModExceptionRegistry.RecordContained(null, "some.mod", "context");
            FuseModExceptionRegistry.RecordContained(null, typeof(FuseModExceptionRegistryTests), "context");

            Assert.True(FuseModExceptionRegistry.AllIdle);
            Assert.Empty(FuseModExceptionRegistry.SnapshotForReport());
        }

        [Fact]
        public void SampleMessage_IsTruncatedTo200Characters()
        {
            RecordDefault(message: new string('x', 300));

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(200, snapshot.Signatures[0].SampleMessage.Length);
        }

        [Fact]
        public void FirstAndLastSeen_TrackTheInjectedClock()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            FuseModExceptionRegistry.UtcNowSource = () => now;
            long tick = 0;
            FuseModExceptionRegistry.TickSource = () => tick;

            RecordDefault();
            now = now.AddMinutes(5);
            tick = 5 * 60 * 1000;
            RecordDefault();

            var snapshot = Assert.Single(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), snapshot.FirstSeenUtc);
            Assert.Equal(new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc), snapshot.LastSeenUtc);
        }

        [Fact]
        public void ResetForTests_ClearsAllStateAndRestoresDefaultClocks()
        {
            long tick = 0;
            FuseModExceptionRegistry.TickSource = () => tick;
            RecordDefault();
            FuseModExceptionRegistry.Record("LogHook", null, null, "Exception", "X.Y", "m");
            Assert.NotEqual(0, FuseModExceptionRegistry.GrandTotal);

            FuseModExceptionRegistry.ResetForTests();

            Assert.True(FuseModExceptionRegistry.AllIdle);
            Assert.Equal(0, FuseModExceptionRegistry.TotalUnattributed);
            Assert.Equal(0, FuseModExceptionRegistry.SignatureOverflowDropped);
            Assert.Empty(FuseModExceptionRegistry.SnapshotForReport());
            Assert.Equal("modErrors=0 unattributed=0 mods=0", FuseModExceptionRegistry.FormatSummary());

            // Recording still works against the restored default clocks.
            RecordDefault();
            Assert.Equal(1, FuseModExceptionRegistry.GrandTotal);
        }

        [Fact]
        public void Snapshot_OrdersModsWorstFirst()
        {
            RecordDefault(modId: "quiet.mod", frame: "Quiet.Type.Method");
            for (var i = 0; i < 3; i++)
            {
                RecordDefault(modId: "noisy.mod", frame: "Noisy.Type.Method");
            }

            var snapshots = FuseModExceptionRegistry.SnapshotForReport();
            Assert.Equal(2, snapshots.Length);
            Assert.Equal("noisy.mod", snapshots[0].ModId);
            Assert.Equal(3, snapshots[0].Count);
            Assert.Equal("quiet.mod", snapshots[1].ModId);
        }
    }
}
