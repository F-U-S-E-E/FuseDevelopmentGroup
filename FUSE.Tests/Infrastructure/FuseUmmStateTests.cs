using FUSE.Infrastructure;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public class FuseUmmStateTests
    {
        [Theory]
        [InlineData(true, true, true, false, true)]
        [InlineData(false, true, true, false, false)]
        [InlineData(true, false, true, false, false)]
        [InlineData(true, true, false, false, false)]
        [InlineData(true, true, true, true, false)]
        public void Runtime_ownership_requires_a_successfully_started_enabled_entry(
            bool enabled,
            bool started,
            bool loaded,
            bool errorOnLoading,
            bool expected)
        {
            Assert.Equal(
                expected,
                FuseUmmState.IsActiveRuntimeState(enabled, started, loaded, errorOnLoading));
        }
    }
}
