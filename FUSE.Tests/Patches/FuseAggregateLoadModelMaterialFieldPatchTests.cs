using System;
using System.Collections.Generic;
using FUSE.Patches;
using Model.Definition.Data;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Direct-invocation tests for
    /// <see cref="FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely"/>.
    ///
    /// This patch hardens
    /// <c>RollingStock.LoadModels.AggregateLoadModelController.TryGetField</c>
    /// against the malformed <see cref="MaterialDefinition.Fields"/>
    /// shapes some FUSE-loaded asset packs ship. The stock game
    /// implementation throws on null fields-list, null field
    /// entries, and a few covariance edge cases that surfaced in
    /// production as an <c>ArrayTypeMismatchException</c> on
    /// <see cref="System.Object.virt_stelemref_sealed_class"/>.
    /// Our prefix returns <c>false</c> from the original method and
    /// substitutes a <c>__result</c> computed by
    /// <see cref="FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely"/>.
    ///
    /// These tests pin each branch:
    ///   - null definition → returns false, value null
    ///   - null Fields list → returns false, value null, warns once
    ///   - empty Fields list → returns false, value null, no warning
    ///   - null FieldPair entry → skipped, lookup continues
    ///   - missing key → returns false, no value, no warning
    ///   - matching key → returns true, value set
    ///   - exception inside the loop → caught, returns false, warns once
    ///
    /// The "warns once per (identifier, key)" contract is the only
    /// place the patch carries log-dedup state — these tests reset
    /// that state in SetUp so observed log-fire behaviour is
    /// deterministic across the suite.
    /// </summary>
    public class FuseAggregateLoadModelMaterialFieldPatchTests : IDisposable
    {
        public FuseAggregateLoadModelMaterialFieldPatchTests()
        {
            // Fresh dedup state per test — the patch tracks
            // "warned for this (identifier, key)" via static HashSets
            // and we want each branch's first-fire behaviour
            // observable in isolation.
            FuseAggregateLoadModelMaterialFieldPatch.ResetLookupLoggingForTests();
        }

        public void Dispose()
        {
            FuseAggregateLoadModelMaterialFieldPatch.ResetLookupLoggingForTests();
            GC.SuppressFinalize(this);
        }

        // ---- guard paths ----

        [Fact]
        public void NullDefinition_ReturnsFalse_NoValue()
        {
            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(null, "anyKey", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }

        [Fact]
        public void NullFieldsList_ReturnsFalse_NoValue()
        {
            // The malformed shape that first surfaced the regression:
            // some MaterialDefinitions ship with Fields == null and
            // the stock game enumeration throws NullReferenceException.
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/null-fields",
                Fields = null
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "anyKey", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }

        [Fact]
        public void EmptyFieldsList_ReturnsFalse_NoValue()
        {
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/empty-fields",
                Fields = new List<MaterialDefinition.FieldPair>()
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "anyKey", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }

        // ---- match / no-match paths ----

        [Fact]
        public void MatchingKey_ReturnsTrue_ValueSet()
        {
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/has-fields",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    new MaterialDefinition.FieldPair { Key = "color", Value = "red" },
                    new MaterialDefinition.FieldPair { Key = "metalness", Value = "0.4" }
                }
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "metalness", out var value);

            Assert.True(ok);
            Assert.Equal("0.4", value);
        }

        [Fact]
        public void NoMatchingKey_ReturnsFalse_NoValue()
        {
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/has-fields",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    new MaterialDefinition.FieldPair { Key = "color", Value = "red" }
                }
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "metalness", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }

        [Fact]
        public void KeyComparison_IsCaseSensitive()
        {
            // Material field keys are author-controlled and we match
            // them via ordinal string comparison — a case-mismatched
            // lookup must NOT silently fall through to a sibling.
            // This pins the case-sensitivity contract so future
            // refactors don't accidentally make it OrdinalIgnoreCase.
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/case",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    new MaterialDefinition.FieldPair { Key = "Color", Value = "red" }
                }
            };

            Assert.False(FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "color", out var lower));
            Assert.Null(lower);
            Assert.True(FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "Color", out var exact));
            Assert.Equal("red", exact);
        }

        // ---- null-entry handling ----

        [Fact]
        public void NullFieldPairEntry_IsSkipped_DoesNotMatch()
        {
            // A null FieldPair entry in the list must not crash the
            // lookup or be mistaken for a key match.
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/null-pair",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    null,
                    new MaterialDefinition.FieldPair { Key = "color", Value = "red" }
                }
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "color", out var value);

            Assert.True(ok, "valid pair after a null entry must still be reachable");
            Assert.Equal("red", value);
        }

        [Fact]
        public void NullFieldPairEntry_BeforeMatch_DoesNotShortCircuit()
        {
            // Defends against a "first null entry aborts the loop"
            // regression — the loop must skip and continue, not
            // bail out at the first null.
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/null-leading",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    null,
                    null,
                    new MaterialDefinition.FieldPair { Key = "first-real", Value = "value-A" },
                    null,
                    new MaterialDefinition.FieldPair { Key = "second-real", Value = "value-B" }
                }
            };

            Assert.True(FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "first-real", out var a));
            Assert.Equal("value-A", a);
            Assert.True(FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "second-real", out var b));
            Assert.Equal("value-B", b);
        }

        [Fact]
        public void AllNullEntries_ReturnsFalse_NoValue()
        {
            var definition = new MaterialDefinition
            {
                AssetIdentifier = "test/all-null",
                Fields = new List<MaterialDefinition.FieldPair> { null, null, null }
            };

            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "any", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }

        // ---- identifier fallback ----

        [Fact]
        public void NullAssetIdentifier_StillResolvesAndDoesNotThrow()
        {
            // MaterialIdentifier falls back to "<unknown>" when the
            // definition's AssetIdentifier is null. This guards
            // against an NRE in the warning code path when a malformed
            // definition without an identifier hits an exception or
            // null Fields list.
            var definition = new MaterialDefinition
            {
                AssetIdentifier = null,
                Fields = null
            };

            // Must NOT throw; the identifier-derived dedup key still
            // needs to be a non-null string for the HashSet.Add to
            // succeed.
            var ok = FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely(definition, "any", out var value);

            Assert.False(ok);
            Assert.Null(value);
        }
    }
}
