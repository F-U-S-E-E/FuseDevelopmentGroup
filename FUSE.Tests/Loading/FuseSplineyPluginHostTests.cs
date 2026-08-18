using FUSE.Loading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Loading
{
#pragma warning disable CS0618 // Tests the intentional legacy StrangeCustoms compatibility surface.
    public sealed class FuseSplineyPluginHostTests
    {
        [Fact]
        public void BuilderTypeDetection_AcceptsConcreteBuilderOnly()
        {
            Assert.True(FuseSplineyPluginHost.IsConcreteSplineyBuilderType(typeof(TestBuilder)));
            Assert.False(FuseSplineyPluginHost.IsConcreteSplineyBuilderType(typeof(AbstractBuilder)));
            Assert.False(FuseSplineyPluginHost.IsConcreteSplineyBuilderType(typeof(string)));
            Assert.False(FuseSplineyPluginHost.IsConcreteSplineyBuilderType(null));
        }

        private sealed class TestBuilder : StrangeCustoms.ISplineyBuilder
        {
            public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
            {
                return null;
            }
        }

        private abstract class AbstractBuilder : StrangeCustoms.ISplineyBuilder
        {
            public abstract GameObject BuildSpliney(
                string id,
                Transform parentTransform,
                JObject data);
        }
    }
#pragma warning restore CS0618
}
