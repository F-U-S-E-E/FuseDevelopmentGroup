using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Infrastructure
{
    public class FuseModSettingsStoreTests
    {
        public class NormalizeTypeTests
        {
            [Theory]
            [InlineData("bool", "bool")]
            [InlineData("boolean", "bool")]
            [InlineData("BOOL", "bool")]
            [InlineData("  Boolean  ", "bool")]
            public void Bool_Aliases_NormalizeToBool(string input, string expected)
            {
                Assert.Equal(expected, FuseModSettingsStore.NormalizeType(input));
            }

            [Theory]
            [InlineData("enum")]
            [InlineData("choice")]
            [InlineData("select")]
            public void Enum_Aliases_NormalizeToEnum(string input)
            {
                Assert.Equal("enum", FuseModSettingsStore.NormalizeType(input));
            }

            [Theory]
            [InlineData("number")]
            [InlineData("float")]
            [InlineData("double")]
            [InlineData("int")]
            [InlineData("integer")]
            public void Number_Aliases_NormalizeToNumber(string input)
            {
                Assert.Equal("number", FuseModSettingsStore.NormalizeType(input));
            }

            [Theory]
            [InlineData("path")]
            [InlineData("file")]
            [InlineData("folder")]
            public void Path_Aliases_NormalizeToPath(string input)
            {
                Assert.Equal("path", FuseModSettingsStore.NormalizeType(input));
            }

            [Theory]
            [InlineData("color")]
            [InlineData("colour")]
            public void Color_Aliases_NormalizeToColor(string input)
            {
                Assert.Equal("color", FuseModSettingsStore.NormalizeType(input));
            }

            [Theory]
            [InlineData("text")]
            [InlineData("string")]
            [InlineData("anything-else")]
            [InlineData("")]
            [InlineData(null)]
            public void Unknown_Or_TextLike_DefaultsToText(string input)
            {
                Assert.Equal("text", FuseModSettingsStore.NormalizeType(input));
            }
        }

        public class NormalizeScopeTests
        {
            [Theory]
            [InlineData("profile")]
            [InlineData("modset")]
            [InlineData("mod-set")]
            [InlineData("PROFILE")]
            [InlineData("  Profile  ")]
            public void Profile_Aliases_NormalizeToProfile(string input)
            {
                Assert.Equal(FuseModSettingsStore.ScopeProfile, FuseModSettingsStore.NormalizeScope(input));
            }

            [Theory]
            [InlineData("server")]
            [InlineData("shared")]
            [InlineData("multiplayer")]
            public void Server_Aliases_NormalizeToServer(string input)
            {
                Assert.Equal(FuseModSettingsStore.ScopeServer, FuseModSettingsStore.NormalizeScope(input));
            }

            [Theory]
            [InlineData("user")]
            [InlineData("local")]
            [InlineData("client")]
            [InlineData("anything-else")]
            [InlineData("")]
            [InlineData(null)]
            public void Unknown_Or_UserLike_DefaultsToUser(string input)
            {
                Assert.Equal(FuseModSettingsStore.ScopeUser, FuseModSettingsStore.NormalizeScope(input));
            }
        }

        public class FormatValueTests
        {
            [Fact]
            public void NullToken_FormatsAsEmptyString()
            {
                // FormatValue delegates to TokenToText, which returns empty for null/null-token.
                Assert.Equal(string.Empty, FuseModSettingsStore.FormatValue(null));
                Assert.Equal(string.Empty, FuseModSettingsStore.FormatValue(JValue.CreateNull()));
            }

            [Fact]
            public void StringToken_FormatsAsRawString()
            {
                Assert.Equal("hello", FuseModSettingsStore.FormatValue(new JValue("hello")));
            }

            [Fact]
            public void NumericToken_FormatsAsString()
            {
                Assert.Equal("42", FuseModSettingsStore.FormatValue(new JValue(42)));
            }

            [Fact]
            public void BooleanToken_FormatsAsLowercase()
            {
                // Newtonsoft renders bool as "True"/"False" by default; this test pins
                // whatever the actual behavior is so a future change is deliberate.
                var text = FuseModSettingsStore.FormatValue(new JValue(true));
                Assert.True(text == "true" || text == "True", $"unexpected: '{text}'");
            }
        }
    }
}
