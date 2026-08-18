using System.Reflection;
using System.Collections.Generic;
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

        [Fact]
        public void PatchingContext_ExposesWritableLiveGraphIndexes()
        {
            AssertWritableDictionary<TrackNode>("NodesById");
            AssertWritableDictionary<TrackSegment>("SegmentsById");
            AssertWritableDictionary<TrackSpan>("SpansById");
        }

        [Fact]
        public void PatchingException_ExposesLegacyConstructorSurface()
        {
            Assert.NotNull(typeof(SCPatchingException).GetConstructor(System.Type.EmptyTypes));
            Assert.NotNull(typeof(SCPatchingException).GetConstructor(new[] { typeof(string) }));
            Assert.NotNull(typeof(SCPatchingException).GetConstructor(
                new[] { typeof(string), typeof(string) }));
            Assert.NotNull(typeof(SCPatchingException).GetConstructor(
                new[] { typeof(string), typeof(System.Exception) }));
            Assert.NotNull(typeof(SCPatchingException).GetConstructor(
                new[] { typeof(SCPatchingException), typeof(string) }));
        }

        private static void AssertWritableDictionary<TValue>(string propertyName)
        {
            var property = typeof(PatchingContext).GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.True(property.CanRead);
            Assert.True(property.CanWrite);
            Assert.Equal(typeof(Dictionary<string, TValue>), property.PropertyType);
        }
    }
#pragma warning restore CS0618
}
