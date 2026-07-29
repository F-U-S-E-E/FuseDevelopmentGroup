using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseSceneryPendingLoadTokensTests
    {
        [Fact]
        public void Invalidate_PreventsQueuedLoadFromBeingConsumed()
        {
            var tokens = new FuseSceneryPendingLoadTokens();
            var queuedToken = tokens.Issue(42);

            tokens.Invalidate(42);

            Assert.False(tokens.TryConsume(42, queuedToken));
            Assert.Equal(0, tokens.Count);
        }

        [Fact]
        public void NewLoadForSameInstance_SupersedesCanceledQueueEntry()
        {
            var tokens = new FuseSceneryPendingLoadTokens();
            var oldToken = tokens.Issue(42);
            tokens.Invalidate(42);
            var newToken = tokens.Issue(42);

            Assert.False(tokens.TryConsume(42, oldToken));
            Assert.True(tokens.TryConsume(42, newToken));
            Assert.Equal(0, tokens.Count);
        }

        [Fact]
        public void TryConsume_RemovesOnlyTheCurrentEntry()
        {
            var tokens = new FuseSceneryPendingLoadTokens();
            var token = tokens.Issue(42);

            Assert.True(tokens.Contains(42));
            Assert.True(tokens.TryConsume(42, token));
            Assert.False(tokens.Contains(42));
            Assert.False(tokens.TryConsume(42, token));
        }

        [Fact]
        public void IsCurrent_DistinguishesSupersededTokensWithoutConsuming()
        {
            var tokens = new FuseSceneryPendingLoadTokens();
            var oldToken = tokens.Issue(42);
            var currentToken = tokens.Issue(42);

            Assert.False(tokens.IsCurrent(42, oldToken));
            Assert.True(tokens.IsCurrent(42, currentToken));
            Assert.True(tokens.Contains(42));
        }
    }
}
