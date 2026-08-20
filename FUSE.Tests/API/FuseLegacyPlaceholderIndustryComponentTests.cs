using System;
using System.Linq;
using System.Runtime.Serialization;
using FUSE.Runtime.API;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using Xunit;

namespace FUSE.Tests.API
{
    public sealed class FuseLegacyPlaceholderIndustryComponentTests
    {
        [Fact]
        public void ConfusingSupplementsEmpty_RemainsVisibleAndAcceptsEveryAutoDestinationKind()
        {
            var component = (FuseLegacyPlaceholderIndustryComponent)
                FormatterServices.GetUninitializedObject(typeof(FuseLegacyPlaceholderIndustryComponent));
            component.trackSpans = new TrackSpan[1];

            Assert.True(component.IsVisible);
            Assert.All(
                Enum.GetValues(typeof(AutoDestinationType)).Cast<AutoDestinationType>(),
                destinationType => Assert.True(component.WantsAutoDestination(destinationType)));
        }
    }
}
