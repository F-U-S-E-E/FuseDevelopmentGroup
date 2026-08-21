using FUSE.Patches;
using StrangeCustoms.Tracks.Industries;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FuseCustomIndustryTitlePatchTests
    {
        [Fact]
        public void TargetMethod_ResolvesCurrentGamePickerTitleMethod()
        {
            Assert.NotNull(FuseCustomIndustryTitlePatch.TargetMethod());
        }

        [Fact]
        public void TryGetCustomTitle_UsesLegacyCustomIndustryTitleContract()
        {
            var component = new FakeTitledComponent("Acquire coal at test depot");

            var found = FuseCustomIndustryTitlePatch.TryGetCustomTitle(component, out var title);

            Assert.True(found);
            Assert.Equal("Acquire coal at test depot", title);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryGetCustomTitle_RejectsMissingTitle(string value)
        {
            var component = new FakeTitledComponent(value);

            var found = FuseCustomIndustryTitlePatch.TryGetCustomTitle(component, out var title);

            Assert.False(found);
            Assert.Equal(value, title);
        }

        private sealed class FakeTitledComponent : ICustomIndustryTitle
        {
            internal FakeTitledComponent(string title)
            {
                Title = title;
            }

            public string Title { get; }
        }
    }
}
