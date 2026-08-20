using System.Runtime.Serialization;
using FUSE.Patches;
using Model.Ops;
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
        public void Prefix_UsesLegacyCustomIndustryTitleContract()
        {
            var component = (FakeTitledComponent)FormatterServices
                .GetUninitializedObject(typeof(FakeTitledComponent));
            var result = string.Empty;

            var runOriginal = FuseCustomIndustryTitlePatch.Prefix(component, ref result);

            Assert.False(runOriginal);
            Assert.Equal("Acquire coal at test depot", result);
        }

        private sealed class FakeTitledComponent : IndustryComponent, ICustomIndustryTitle
        {
            public string Title => "Acquire coal at test depot";

            public override void OrderCars(IIndustryContext ctx)
            {
            }

            public override void Service(IIndustryContext ctx)
            {
            }
        }
    }
}
