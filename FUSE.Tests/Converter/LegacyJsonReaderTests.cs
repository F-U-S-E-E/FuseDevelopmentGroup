using FUSE.Converter.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Converter
{
    public sealed class LegacyJsonReaderTests
    {
        // ------------------------------------------------------------------
        // StripComments
        // ------------------------------------------------------------------

        [Fact]
        public void StripComments_removes_line_comments()
        {
            var input = "{\n  \"a\": 1, // a comment\n  \"b\": 2\n}";
            var stripped = LegacyJsonReader.StripComments(input);
            var parsed = JObject.Parse(stripped);
            Assert.Equal(1, parsed.Value<int>("a"));
            Assert.Equal(2, parsed.Value<int>("b"));
        }

        [Fact]
        public void StripComments_removes_block_comments()
        {
            var input = "{ \"a\": 1 /* keep me out */, \"b\": 2 }";
            var stripped = LegacyJsonReader.StripComments(input);
            var parsed = JObject.Parse(stripped);
            Assert.Equal(2, parsed.Value<int>("b"));
        }

        [Fact]
        public void StripComments_preserves_double_slash_inside_strings()
        {
            var input = "{ \"path\": \"//foo//bar\" }";
            var stripped = LegacyJsonReader.StripComments(input);
            Assert.Contains("//foo//bar", stripped);
        }

        [Fact]
        public void StripComments_preserves_escapes_inside_strings()
        {
            var input = "{ \"s\": \"a\\\"b // not a comment\" }";
            var stripped = LegacyJsonReader.StripComments(input);
            // The // inside the escaped string must survive.
            Assert.Contains("// not a comment", stripped);
        }

        // ------------------------------------------------------------------
        // RemoveTrailingCommas
        // ------------------------------------------------------------------

        [Fact]
        public void RemoveTrailingCommas_drops_pre_close_commas()
        {
            var input = "{ \"a\": [1, 2, 3,], \"b\": { \"x\": 1, } }";
            var fixedText = LegacyJsonReader.RemoveTrailingCommas(input);
            // Must now parse cleanly.
            var parsed = JObject.Parse(fixedText);
            Assert.Equal(3, ((JArray)parsed["a"]).Count);
        }

        [Fact]
        public void RemoveTrailingCommas_iterates_to_handle_nested()
        {
            var input = "{ \"a\": [1,2,], }";
            var fixedText = LegacyJsonReader.RemoveTrailingCommas(input);
            var parsed = JObject.Parse(fixedText);
            Assert.Equal(2, ((JArray)parsed["a"]).Count);
        }

        // ------------------------------------------------------------------
        // CloseUnbalancedJson
        // ------------------------------------------------------------------

        [Fact]
        public void CloseUnbalancedJson_appends_missing_closers_in_reverse_order()
        {
            var input = "{ \"a\": { \"b\": [1, 2";  // missing ], }, }
            var closed = LegacyJsonReader.CloseUnbalancedJson(input);
            // Should parse now.
            var parsed = JObject.Parse(closed);
            var b = (JArray)((JObject)parsed["a"])["b"];
            Assert.Equal(2, b.Count);
        }

        [Fact]
        public void CloseUnbalancedJson_leaves_balanced_input_untouched()
        {
            var input = "{ \"a\": 1 }";
            Assert.Equal(input, LegacyJsonReader.CloseUnbalancedJson(input));
        }

        [Fact]
        public void CloseUnbalancedJson_ignores_brackets_inside_strings()
        {
            var input = "{ \"s\": \"[unclosed string of brackets\" }";
            // Already balanced; the brackets inside the string don't
            // pollute the stack.
            Assert.Equal(input, LegacyJsonReader.CloseUnbalancedJson(input));
        }

        // ------------------------------------------------------------------
        // End-to-end Loads
        // ------------------------------------------------------------------

        [Fact]
        public void Loads_recovers_truncated_jsonc()
        {
            // JSONC with comments + trailing commas + missing closers.
            var raw = "// header\n{ \"a\": [1, 2,], \"b\": { \"c\": 3,";
            var parsed = LegacyJsonReader.Loads(raw) as JObject;

            Assert.NotNull(parsed);
            Assert.Equal(2, ((JArray)parsed["a"]).Count);
            Assert.Equal(3, ((JObject)parsed["b"]).Value<int>("c"));
        }

        [Fact]
        public void Loads_with_repair_false_throws_on_truncation()
        {
            var raw = "{ \"a\": 1";
            Assert.ThrowsAny<System.Exception>(() => LegacyJsonReader.Loads(raw, repair: false));
        }
    }
}
