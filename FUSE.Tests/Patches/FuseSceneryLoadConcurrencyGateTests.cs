using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseSceneryLoadConcurrencyGateTests
    {
        [Fact]
        public void TryAcquire_RejectsLoadsBeyondTheLimit()
        {
            var gate = new FuseSceneryLoadConcurrencyGate(2);

            Assert.True(gate.TryAcquire(out var first));
            Assert.True(gate.TryAcquire(out var second));
            Assert.False(gate.TryAcquire(out var rejected));
            Assert.Null(rejected);
            Assert.Equal(2, gate.Active);
            Assert.Equal(2, gate.Peak);

            first.Dispose();
            second.Dispose();
        }

        [Fact]
        public void CompletedLease_OpensExactlyOneSlot()
        {
            var gate = new FuseSceneryLoadConcurrencyGate(1);
            Assert.True(gate.TryAcquire(out var first));

            first.Dispose();
            first.Dispose();

            Assert.Equal(0, gate.Active);
            Assert.True(gate.TryAcquire(out var replacement));
            replacement.Dispose();
        }

        [Fact]
        public void Reset_MakesLateTaskCompletionHarmless()
        {
            var gate = new FuseSceneryLoadConcurrencyGate(1);
            Assert.True(gate.TryAcquire(out var oldGeneration));

            gate.Reset();
            Assert.True(gate.TryAcquire(out var currentGeneration));
            oldGeneration.Dispose();

            Assert.Equal(1, gate.Active);
            currentGeneration.Dispose();
            Assert.Equal(0, gate.Active);
        }

        [Fact]
        public void ParallelAcquire_NeverExceedsTheConfiguredCeiling()
        {
            const int limit = 8;
            var gate = new FuseSceneryLoadConcurrencyGate(limit);
            var acquired = new ConcurrentBag<FuseSceneryLoadConcurrencyGate.Lease>();

            Parallel.For(
                0,
                128,
                _ =>
                {
                    if (gate.TryAcquire(out var lease))
                    {
                        acquired.Add(lease);
                    }
                });

            Assert.Equal(limit, acquired.Count);
            Assert.Equal(limit, gate.Active);
            Assert.Equal(limit, gate.Peak);

            foreach (var lease in acquired.ToArray())
            {
                lease.Dispose();
            }

            Assert.Equal(0, gate.Active);
        }
    }
}
