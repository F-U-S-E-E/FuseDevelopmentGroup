using System;
using System.Collections.Generic;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseOutboundIndustryRoutingPatchTests
    {
        [Theory]
        [InlineData(false, false, false, 0)]
        [InlineData(false, true, false, 1)]
        [InlineData(false, false, true, 2)]
        [InlineData(false, true, true, 2)]
        [InlineData(true, false, false, 2)]
        public void ResolveMode_preserves_opt_in_and_legacy_precedence(
            bool explicitOptIn,
            bool absolute,
            bool configurable,
            int expected)
        {
            Assert.Equal(expected, (int)FuseOutboundIndustryRoutingPatch.ResolveMode(
                explicitOptIn,
                absolute,
                configurable));
        }

        [Fact]
        public void Shuffle_is_deterministic_with_a_supplied_random_source()
        {
            var first = new List<int> { 1, 2, 3, 4, 5 };
            var second = new List<int> { 1, 2, 3, 4, 5 };

            FuseOutboundIndustryBlockingPatch.Shuffle(first, new Random(19));
            FuseOutboundIndustryBlockingPatch.Shuffle(second, new Random(19));

            Assert.Equal(first, second);
            Assert.NotEqual(new[] { 1, 2, 3, 4, 5 }, first);
        }

    }
}
