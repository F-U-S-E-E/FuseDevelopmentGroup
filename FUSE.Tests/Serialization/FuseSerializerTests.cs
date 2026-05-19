using System;
using FUSE.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace FUSE.Tests.Serialization
{
    public class FuseSerializerTests
    {
        [Fact]
        public void Load_NullPath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => FuseSerializer.Load(null));
        }

        [Fact]
        public void Load_WhitespacePath_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => FuseSerializer.Load("   "));
        }

        [Fact]
        public void Load_UnknownExtension_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => FuseSerializer.Load("definition.txt"));

            Assert.Contains("Unknown FUSE definition format", ex.Message);
        }

        [Fact]
        public void FromJson_DuplicateProperties_Throws()
        {
            // ValidateNoDuplicateProperties is the first thing FromJson does;
            // duplicate keys must surface as a parse error before downstream
            // deserialization silently picks one.
            var json = "{\"name\":\"a\",\"name\":\"b\"}";

            Assert.Throws<JsonReaderException>(() => FuseSerializer.FromJson(json));
        }
    }
}
