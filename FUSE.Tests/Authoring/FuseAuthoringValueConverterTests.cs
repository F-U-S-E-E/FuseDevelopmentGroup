using System;
using FUSE.Authoring.Entities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Authoring
{
    public class FuseAuthoringValueConverterTests
    {
        private enum Color
        {
            Red = 0,
            Green = 1,
            Blue = 2
        }

        [Fact]
        public void NullTargetType_PassesValueThrough()
        {
            var value = new object();

            var result = FuseAuthoringValueConverter.ConvertValue(value, null);

            Assert.Same(value, result);
        }

        [Fact]
        public void NullValue_ValueTypeTarget_ReturnsDefault()
        {
            var result = FuseAuthoringValueConverter.ConvertValue(null, typeof(int));

            Assert.Equal(0, result);
        }

        [Fact]
        public void NullValue_NullableTarget_ReturnsNull()
        {
            var result = FuseAuthoringValueConverter.ConvertValue(null, typeof(int?));

            Assert.Null(result);
        }

        [Fact]
        public void NullValue_ReferenceTarget_ReturnsNull()
        {
            var result = FuseAuthoringValueConverter.ConvertValue(null, typeof(string));

            Assert.Null(result);
        }

        [Fact]
        public void ValueAlreadyAssignable_IsReturnedAsIs()
        {
            var value = "hello";

            var result = FuseAuthoringValueConverter.ConvertValue(value, typeof(string));

            Assert.Same(value, result);
        }

        [Fact]
        public void Enum_FromStringName_Parses()
        {
            var result = FuseAuthoringValueConverter.ConvertValue("Green", typeof(Color));

            Assert.Equal(Color.Green, result);
        }

        [Fact]
        public void Enum_FromString_IsCaseInsensitive()
        {
            var result = FuseAuthoringValueConverter.ConvertValue("BLUE", typeof(Color));

            Assert.Equal(Color.Blue, result);
        }

        [Fact]
        public void Enum_FromInt_UsesToObject()
        {
            var result = FuseAuthoringValueConverter.ConvertValue(2, typeof(Color));

            Assert.Equal(Color.Blue, result);
        }

        [Fact]
        public void Guid_FromString_IsParsed()
        {
            var guidText = "12345678-1234-1234-1234-1234567890ab";

            var result = FuseAuthoringValueConverter.ConvertValue(guidText, typeof(Guid));

            Assert.Equal(new Guid(guidText), result);
        }

        [Fact]
        public void NullableTarget_PassesUnderlyingType_ToConverter()
        {
            // A long fits into int? — converter unwraps the nullable then ChangeType bridges
            // the numeric conversion.
            var result = FuseAuthoringValueConverter.ConvertValue(42L, typeof(int?));

            Assert.Equal(42, result);
        }

        [Fact]
        public void ChangeType_BridgesPrimitiveConversions()
        {
            var result = FuseAuthoringValueConverter.ConvertValue("3.14", typeof(double));

            Assert.Equal(3.14, (double)result, precision: 5);
        }

        [Fact]
        public void JToken_String_DeserializedToString()
        {
            var token = JValue.CreateString("from-jtoken");

            var result = FuseAuthoringValueConverter.ConvertValue(token, typeof(string));

            Assert.Equal("from-jtoken", result);
        }

        [Fact]
        public void JToken_Integer_DeserializedToInt()
        {
            JToken token = new JValue(99);

            var result = FuseAuthoringValueConverter.ConvertValue(token, typeof(int));

            Assert.Equal(99, result);
        }
    }
}
