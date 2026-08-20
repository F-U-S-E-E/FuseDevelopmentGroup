using System;
using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FuseLiveLogBufferTestCollection
    {
        public const string Name = "Fuse live log buffer tests";
    }

    [Collection(FuseLiveLogBufferTestCollection.Name)]
    public class FuseLiveLogBufferTests
    {
        public FuseLiveLogBufferTests()
        {
            FuseLiveLogBuffer.ResetForTests();
        }

        [Fact]
        public void Snapshot_FiltersBySeverityAndText()
        {
            var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Local);
            FuseLiveLogBuffer.Append(now, "INFO", "mounted track package");
            FuseLiveLogBuffer.Append(now.AddSeconds(1), "WARN", "missing optional asset alias");
            FuseLiveLogBuffer.Append(now.AddSeconds(2), "ERROR", "bad track json");

            var warnings = FuseLiveLogBuffer.Snapshot("Warnings + Errors", string.Empty, 20);
            Assert.Equal(2, warnings.Length);
            Assert.Equal("WARN", warnings[0].Level);
            Assert.Equal("ERROR", warnings[1].Level);

            var json = FuseLiveLogBuffer.Snapshot("All", "json", 20);
            Assert.Single(json);
            Assert.Equal("bad track json", json[0].Message);
        }

        [Fact]
        public void Buffer_IsBoundedAndSnapshotReturnsNewestEntriesInOrder()
        {
            var now = DateTime.Now;
            for (var index = 0; index < FuseLiveLogBuffer.Capacity + 25; index++)
            {
                FuseLiveLogBuffer.Append(now.AddMilliseconds(index), "INFO", "line " + index);
            }

            Assert.Equal(FuseLiveLogBuffer.Capacity, FuseLiveLogBuffer.Count);
            var latest = FuseLiveLogBuffer.Snapshot("All", string.Empty, 3);
            Assert.Equal(new[] { "line 1022", "line 1023", "line 1024" },
                new[] { latest[0].Message, latest[1].Message, latest[2].Message });
        }
    }
}
