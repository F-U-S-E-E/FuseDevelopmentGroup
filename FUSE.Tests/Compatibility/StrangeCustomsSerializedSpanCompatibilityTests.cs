using System.Reflection;
using StrangeCustoms.Tracks;
using Track;
using Xunit;

namespace FUSE.Tests.Compatibility
{
#pragma warning disable CS0618 // Verifies the intentional legacy StrangeCustoms binary surface.
    public sealed class StrangeCustomsSerializedSpanCompatibilityTests
    {
        [Fact]
        public void SerializedSpan_ExposesSignalsEverywhereRuntimeContract()
        {
            Assert.NotNull(typeof(SerializedSpan).GetConstructor(new[] { typeof(TrackSpan) }));
            Assert.NotNull(typeof(SerializedSpan).GetMethod(
                "ApplyTo",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string), typeof(PatchingContext), typeof(TrackSpan) },
                modifiers: null));
        }
    }
#pragma warning restore CS0618
}
