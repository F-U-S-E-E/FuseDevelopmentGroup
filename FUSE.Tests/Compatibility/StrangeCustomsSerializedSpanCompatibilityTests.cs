using System.Reflection;
using System.Runtime.Serialization;
using StrangeCustoms.Tracks;
using Xunit;

namespace FUSE.Tests.Compatibility
{
#pragma warning disable CS0618 // Verifies the intentional legacy StrangeCustoms binary surface.
    public sealed class StrangeCustomsSerializedSpanCompatibilityTests
    {
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

        [Fact]
        public void PatchingException_PreservesLegacyPathDuringSerialization()
        {
            var original = new SCPatchingException("invalid value", "sections[0]");
            var info = new SerializationInfo(
                typeof(SCPatchingException),
                new FormatterConverter());
            var context = new StreamingContext(StreamingContextStates.All);

            original.GetObjectData(info, context);
            var constructor = typeof(SCPatchingException).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(SerializationInfo), typeof(StreamingContext) },
                modifiers: null);
            Assert.NotNull(constructor);

            var restored = (SCPatchingException)constructor.Invoke(new object[] { info, context });
            Assert.Equal("sections[0]", restored.JsonPath);
            Assert.Equal("sections[0]", restored.ParameterName);
        }
    }
#pragma warning restore CS0618
}
