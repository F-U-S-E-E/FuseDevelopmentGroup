using System;
using System.Linq;
using FUSE.Loading;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseLegacyJsonPatchTests
    {
        private static JObject Obj(string json) => JObject.Parse(json);

        public class ApplyBasics
        {
            [Fact]
            public void NullTarget_IsNoOp()
            {
                FuseLegacyJsonPatch.Apply(null, Obj("{\"a\":1}"), "source"); // must not throw
            }

            [Fact]
            public void NullPatch_IsNoOp()
            {
                var target = Obj("{\"a\":1}");

                FuseLegacyJsonPatch.Apply(target, null, "source");

                Assert.Equal(1, (int)target["a"]);
            }

            [Fact]
            public void Adds_NewProperty()
            {
                var target = Obj("{\"a\":1}");
                var patch = Obj("{\"b\":2}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(1, (int)target["a"]);
                Assert.Equal(2, (int)target["b"]);
            }

            [Fact]
            public void Overwrites_ScalarProperty()
            {
                var target = Obj("{\"a\":1}");
                var patch = Obj("{\"a\":42}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(42, (int)target["a"]);
            }

            [Fact]
            public void NullPatchValue_RemovesProperty()
            {
                var target = Obj("{\"a\":1,\"b\":2}");
                var patch = Obj("{\"a\":null}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.False(target.ContainsKey("a"));
                Assert.Equal(2, (int)target["b"]);
            }

            [Fact]
            public void NestedObject_MergesDeeply()
            {
                var target = Obj("{\"nested\":{\"a\":1,\"b\":2}}");
                var patch = Obj("{\"nested\":{\"b\":99,\"c\":3}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(1, (int)target["nested"]["a"]);
                Assert.Equal(99, (int)target["nested"]["b"]);
                Assert.Equal(3, (int)target["nested"]["c"]);
            }
        }

        public class ReplaceDirective
        {
            [Fact]
            public void Replace_AtPropertyLevel_OverridesWithNewValue()
            {
                var target = Obj("{\"nested\":{\"a\":1,\"b\":2}}");
                var patch = Obj("{\"nested\":{\"$replace\":{\"x\":99}}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.False(((JObject)target["nested"]).ContainsKey("a"));
                Assert.Equal(99, (int)target["nested"]["x"]);
            }
        }

        public class RemoveDirective
        {
            [Fact]
            public void Remove_True_RemovesProperty()
            {
                var target = Obj("{\"a\":1,\"b\":2}");
                var patch = Obj("{\"a\":{\"$remove\":true}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.False(target.ContainsKey("a"));
                Assert.True(target.ContainsKey("b"));
            }

            [Fact]
            public void Delete_True_RemovesProperty()
            {
                var target = Obj("{\"a\":1,\"b\":2}");
                var patch = Obj("{\"a\":{\"$delete\":true}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.False(target.ContainsKey("a"));
            }

            [Fact]
            public void Remove_False_DoesNotRemove()
            {
                // IsRemoveDirective only fires on truthy values. False is ignored.
                var target = Obj("{\"a\":{\"keep\":true}}");
                var patch = Obj("{\"a\":{\"$remove\":false}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.True(target.ContainsKey("a"));
            }
        }

        public class MoveToDirective
        {
            [Fact]
            public void MoveTo_RelocatesProperty()
            {
                var target = Obj("{\"src\":{\"x\":1}}");
                var patch = Obj("{\"src\":{\"$moveTo\":\"dst\"}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.False(target.ContainsKey("src"));
                Assert.Equal(1, (int)target["dst"]["x"]);
            }

            [Fact]
            public void MoveTo_EmptyPath_Throws()
            {
                var target = Obj("{\"src\":{}}");
                var patch = Obj("{\"src\":{\"$moveTo\":\"\"}}");

                Assert.Throws<InvalidOperationException>(
                    () => FuseLegacyJsonPatch.Apply(target, patch, "source"));
            }

            [Fact]
            public void MoveTo_NestedPath_CreatesIntermediateObjects()
            {
                var target = Obj("{\"src\":{\"x\":1}}");
                var patch = Obj("{\"src\":{\"$moveTo\":\"outer.inner\"}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(1, (int)target["outer"]["inner"]["x"]);
            }
        }

        public class PropertyLevelArrayDirectives
        {
            [Fact]
            public void Add_ExtendsArray()
            {
                var target = Obj("{\"items\":[1,2]}");
                var patch = Obj("{\"items\":{\"$add\":[3,4]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(new[] { 1, 2, 3, 4 }, target["items"].ToObject<int[]>());
            }

            [Fact]
            public void Append_AppendsToArray()
            {
                var target = Obj("{\"items\":[1]}");
                var patch = Obj("{\"items\":{\"$append\":[2,3]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(new[] { 1, 2, 3 }, target["items"].ToObject<int[]>());
            }

            [Fact]
            public void Prepend_PrependsPreservingOrder()
            {
                var target = Obj("{\"items\":[3,4]}");
                var patch = Obj("{\"items\":{\"$prepend\":[1,2]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(new[] { 1, 2, 3, 4 }, target["items"].ToObject<int[]>());
            }

            [Fact]
            public void Remove_FiltersMatchingValues()
            {
                var target = Obj("{\"items\":[1,2,3,2]}");
                var patch = Obj("{\"items\":{\"$remove\":[2]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(new[] { 1, 3 }, target["items"].ToObject<int[]>());
            }

            [Fact]
            public void ArrayDirective_OnMissingProperty_CreatesArray()
            {
                var target = Obj("{}");
                var patch = Obj("{\"items\":{\"$add\":[1,2]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(new[] { 1, 2 }, target["items"].ToObject<int[]>());
            }
        }

        public class ObjectIntoArrayConflict
        {
            [Fact]
            public void MergeObject_IntoArray_Throws()
            {
                var target = Obj("{\"x\":[1,2]}");
                var patch = Obj("{\"x\":{\"key\":\"value\"}}");

                Assert.Throws<InvalidOperationException>(
                    () => FuseLegacyJsonPatch.Apply(target, patch, "source"));
            }
        }

        public class PlainArrayReplacement
        {
            // The field shape behind the five 'legacy game-graph
            // compatibility' faults: a spliney's directive-free "points"
            // array re-applied while the spliney already exists in the
            // runtime state. The literal array is the author's complete
            // value and must replace, not fault.
            [Fact]
            public void PlainObjectArray_OnExistingArray_ReplacesWholesale()
            {
                var target = Obj(
                    "{\"points\":[{\"x\":1,\"width\":7.0},{\"x\":2,\"width\":7.0},{\"x\":3,\"width\":7.0}]}");
                var patch = Obj("{\"points\":[{\"x\":10,\"width\":5.0},{\"x\":20,\"width\":5.0}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(2, target["points"].Count());
                Assert.Equal(10, (int)target["points"][0]["x"]);
                Assert.Equal(5.0, (double)target["points"][1]["width"]);
            }

            [Fact]
            public void PlainObjectArray_OnMissingKey_SetsTheArray()
            {
                var target = Obj("{}");
                var patch = Obj("{\"points\":[{\"x\":1},{\"x\":2}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(2, target["points"].Count());
            }

            [Fact]
            public void PrimitiveOnlyArray_KeepsAppendSemantics()
            {
                var target = Obj("{\"tags\":[\"a\",\"b\"]}");
                var patch = Obj("{\"tags\":[\"c\"]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(3, target["tags"].Count());
                Assert.Equal("c", (string)target["tags"][2]);
            }

            [Fact]
            public void MixedArray_WithADirective_KeepsStrictMergeAndStillRejectsPlainObjects()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\"}]}");
                var patch = Obj("{\"items\":[{\"$append\":[{\"id\":\"b\"}]},{\"id\":\"plain\"}]}");

                Assert.Throws<InvalidOperationException>(
                    () => FuseLegacyJsonPatch.Apply(target, patch, "source"));
            }
        }

        public class ArrayElementDirectives
        {
            [Fact]
            public void Find_With_EqualsCondition_UpdatesMatchingElement()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\",\"v\":1},{\"id\":\"b\",\"v\":2}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"a\"},\"v\":99}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(99, (int)target["items"][0]["v"]);
                Assert.Equal(2, (int)target["items"][1]["v"]);
            }

            [Fact]
            public void Find_With_Replace_SwapsMatchingElement()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\"},{\"id\":\"b\"}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"b\"},\"$replace\":{\"id\":\"NEW\"}}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal("a", (string)target["items"][0]["id"]);
                Assert.Equal("NEW", (string)target["items"][1]["id"]);
            }

            [Fact]
            public void Find_With_Remove_DropsMatchingElement()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\"},{\"id\":\"b\"}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"a\"},\"$remove\":true}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Single(target["items"]);
                Assert.Equal("b", (string)target["items"][0]["id"]);
            }

            [Fact]
            public void Find_NoMatch_NonOptional_Throws()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\"}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"missing\"},\"v\":1}]}");

                Assert.Throws<InvalidOperationException>(
                    () => FuseLegacyJsonPatch.Apply(target, patch, "source"));
            }

            [Fact]
            public void Find_NoMatch_Optional_SilentlySkipped()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\"}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"missing\"},\"$optional\":true,\"v\":1}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Single(target["items"]);
                Assert.Equal("a", (string)target["items"][0]["id"]);
            }

            [Fact]
            public void Find_With_Clone_AppendsClonedAndModifiedElement()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\",\"v\":1}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"id\",\"value\":\"a\"},\"$clone\":true,\"id\":\"a-copy\"}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(2, target["items"].Count());
                Assert.Equal("a", (string)target["items"][0]["id"]);
                Assert.Equal("a-copy", (string)target["items"][1]["id"]);
                Assert.Equal(1, (int)target["items"][1]["v"]);
            }
        }

        public class ComparisonOperators
        {
            [Theory]
            [InlineData("StartsWith", "id", "foo", "foo-bar", true)]
            [InlineData("EndsWith", "id", "-bar", "foo-bar", true)]
            [InlineData("Contains", "id", "ob", "foobar", true)]
            [InlineData("NotEquals", "id", "x", "y", true)]
            [InlineData("StartsWith", "id", "X", "y-foo", false)]
            public void Comparison_Operators_AreMatched(string comp, string path, string value, string actualId, bool shouldMatch)
            {
                var target = new JObject
                {
                    ["items"] = new JArray(new JObject { ["id"] = actualId, ["v"] = 1 })
                };
                var patch = Obj(
                    $"{{\"items\":[{{\"$find\":{{\"path\":\"{path}\",\"value\":\"{value}\",\"comp\":\"{comp}\"}},\"$optional\":true,\"v\":99}}]}}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(shouldMatch ? 99 : 1, (int)target["items"][0]["v"]);
            }

            [Fact]
            public void Exists_Operator_MatchesPresentProperty()
            {
                var target = Obj("{\"items\":[{\"id\":\"a\",\"flag\":true}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"flag\",\"comp\":\"exists\"},\"v\":99}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.Equal(99, (int)target["items"][0]["v"]);
            }
        }

        public class PathLookup
        {
            [Fact]
            public void NestedPath_DotSeparator_Works()
            {
                var target = Obj("{\"items\":[{\"meta\":{\"id\":\"a\"}}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"meta.id\",\"value\":\"a\"},\"hit\":true}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.True((bool)target["items"][0]["hit"]);
            }

            [Fact]
            public void NestedPath_SlashSeparator_Works()
            {
                var target = Obj("{\"items\":[{\"meta\":{\"id\":\"a\"}}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"meta/id\",\"value\":\"a\"},\"hit\":true}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.True((bool)target["items"][0]["hit"]);
            }

            [Fact]
            public void PathLookup_IsCaseInsensitive()
            {
                var target = Obj("{\"items\":[{\"Meta\":{\"ID\":\"a\"}}]}");
                var patch = Obj(
                    "{\"items\":[{\"$find\":{\"path\":\"meta.id\",\"value\":\"a\"},\"hit\":true}]}");

                FuseLegacyJsonPatch.Apply(target, patch, "source");

                Assert.True((bool)target["items"][0]["hit"]);
            }
        }

        public class DirectiveHelpers
        {
            [Theory]
            [InlineData("$add", true)]
            [InlineData("$append", true)]
            [InlineData("$replace", true)]
            [InlineData("$moveTo", true)]
            [InlineData("$find", true)]
            [InlineData("$optional", true)]
            [InlineData("$REPLACE", true)] // case-insensitive
            [InlineData("plain", false)]
            [InlineData("", false)]
            [InlineData(null, false)]
            public void IsDirective_RecognizesPrefixedKeys(string name, bool expected)
            {
                Assert.Equal(expected, FuseLegacyJsonPatch.IsDirective(name));
            }

            [Fact]
            public void IsRemovePatch_True_WhenRemoveIsTruthy()
            {
                Assert.True(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$remove\":true}")));
                Assert.True(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$delete\":\"true\"}")));
                Assert.True(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$remove\":1}")));
            }

            [Fact]
            public void IsRemovePatch_False_WhenRemoveIsFalsyOrAbsent()
            {
                Assert.False(FuseLegacyJsonPatch.IsRemovePatch(Obj("{}")));
                Assert.False(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$remove\":false}")));
                Assert.False(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$remove\":0}")));
                Assert.False(FuseLegacyJsonPatch.IsRemovePatch(Obj("{\"$remove\":null}")));
            }
        }
    }
}
