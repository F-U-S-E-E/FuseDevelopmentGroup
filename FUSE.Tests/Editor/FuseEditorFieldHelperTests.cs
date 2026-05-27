using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Pure-logic coverage for the float-field parsing helper used by
    /// the editor's Properties panel. The contract: partial-typing
    /// states never commit; valid parseable values that differ from
    /// the baseline do.
    /// </summary>
    public sealed class FuseEditorFieldHelperTests
    {
        [Theory]
        [InlineData("123.45", 0f, 123.45f)]
        [InlineData("-50", 100f, -50f)]
        [InlineData("0", 7f, 0f)]
        [InlineData("0.5", 0f, 0.5f)]
        [InlineData("-0.25", 1f, -0.25f)]
        public void Complete_parseable_differing_buffer_commits(string buffer, float committed, float expected)
        {
            var ok = FuseEditorFieldHelper.TryCommitFloat(buffer, committed, out var parsed);
            Assert.True(ok);
            Assert.Equal(expected, parsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("-")]
        [InlineData(".")]
        [InlineData("-.")]
        [InlineData("+.")]
        [InlineData("1.")]
        [InlineData("-1.")]
        public void Partial_typing_state_never_commits(string buffer)
        {
            var ok = FuseEditorFieldHelper.TryCommitFloat(buffer, 42f, out var parsed);
            Assert.False(ok);
            Assert.Equal(42f, parsed); // committed value unchanged
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("1.2.3")]
        [InlineData("--1")]
        [InlineData("0x10")]
        [InlineData("1,000")] // comma-thousands rejected — invariant culture
        public void Garbage_buffer_does_not_commit(string buffer)
        {
            var ok = FuseEditorFieldHelper.TryCommitFloat(buffer, 5f, out var parsed);
            Assert.False(ok);
            Assert.Equal(5f, parsed);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        public void Nan_and_infinity_are_rejected(string buffer)
        {
            var ok = FuseEditorFieldHelper.TryCommitFloat(buffer, 0f, out var parsed);
            Assert.False(ok);
            Assert.Equal(0f, parsed);
        }

        [Fact]
        public void Same_value_does_not_commit()
        {
            // No-op edits (re-typing the same value) shouldn't trigger
            // a save dispatch — the panel uses the commit signal as the
            // gate.
            var ok = FuseEditorFieldHelper.TryCommitFloat("3.14", 3.14f, out var parsed);
            Assert.False(ok);
            Assert.Equal(3.14f, parsed);
        }

        [Theory]
        [InlineData(0f, "0")]
        [InlineData(-1f, "-1")]
        [InlineData(0.5f, "0.5")]
        public void FormatFloat_roundtrips_through_TryCommit(float value, string expected)
        {
            var formatted = FuseEditorFieldHelper.FormatFloat(value);
            Assert.Equal(expected, formatted);

            // Format and parse with a different committed baseline so
            // the value is genuinely "new", confirming the format
            // round-trips through the parser.
            var ok = FuseEditorFieldHelper.TryCommitFloat(formatted, value + 1f, out var parsed);
            Assert.True(ok);
            Assert.Equal(value, parsed);
        }

        [Theory]
        [InlineData("", new string[] { })]
        [InlineData("   ", new string[] { })]
        [InlineData("main", new[] { "main" })]
        [InlineData("main, secondary", new[] { "main", "secondary" })]
        [InlineData("a,b,c", new[] { "a", "b", "c" })]
        [InlineData(" a , b , c ", new[] { "a", "b", "c" })]
        [InlineData("a,,b", new[] { "a", "b" })]
        [InlineData(",a,", new[] { "a" })]
        public void ParseTags_splits_trims_and_drops_empties(string raw, string[] expected)
        {
            var result = FuseEditorFieldHelper.ParseTags(raw);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseTags_null_returns_empty_array()
        {
            var result = FuseEditorFieldHelper.ParseTags(null);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Theory]
        [InlineData(new string[] { }, "")]
        [InlineData(null, "")]
        [InlineData(new[] { "main" }, "main")]
        [InlineData(new[] { "a", "b", "c" }, "a, b, c")]
        public void FormatTags_joins_with_comma_space(string[] tags, string expected)
        {
            var formatted = FuseEditorFieldHelper.FormatTags(tags);
            Assert.Equal(expected, formatted);
        }

        [Fact]
        public void Tags_roundtrip_format_then_parse_preserves_set()
        {
            var original = new[] { "main-line", "primary", "yard" };
            var formatted = FuseEditorFieldHelper.FormatTags(original);
            var parsed = FuseEditorFieldHelper.ParseTags(formatted);
            Assert.Equal(original, parsed);
        }
    }
}
