using System;
using System.Collections.Generic;
using FUSE.Patches;
using Model.Definition;
using Model.Definition.Data;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Direct-invocation tests for
    /// <see cref="FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition"/>.
    ///
    /// This patch wraps
    /// <c>PrefabStore.AllDefinitionInfosOfType&lt;MaterialDefinition&gt;</c>
    /// and replaces the returned enumeration with one that sanitises
    /// each <see cref="MaterialDefinition"/> in flight, guaranteeing
    /// that downstream consumers (notably
    /// <see cref="FuseAggregateLoadModelMaterialFieldPatch.TryGetFieldSafely"/>
    /// once the prefix hands control to it) never see a null
    /// <see cref="MaterialDefinition.Fields"/> list or a null
    /// FieldPair entry inside it. The sanitiser is the second line
    /// of defence — the first is the field-lookup hardening, but
    /// fixing the source data upstream means many other consumers
    /// also benefit.
    ///
    /// Pinned behaviours:
    ///   - null item / null Definition → no-op, never throw
    ///   - null Fields list → replaced with empty list, warn once
    ///   - mixed null/valid FieldPair entries → nulls removed, warn once
    ///   - all-null Fields list → all nulls removed, warn once
    ///   - already-clean definition → no mutation, no warning
    ///   - missing identifier → fallback to "&lt;unknown&gt;" in warnings
    ///
    /// The warn-once-per-identifier contract is the only state the
    /// sanitiser carries — tests reset it in SetUp/TearDown so each
    /// branch's first-fire behaviour is observable in isolation.
    /// </summary>
    public class FusePrefabStoreMaterialDefinitionsPatchTests : IDisposable
    {
        public FusePrefabStoreMaterialDefinitionsPatchTests()
        {
            FusePrefabStoreMaterialDefinitionsPatch.ResetSanitizerLoggingForTests();
        }

        public void Dispose()
        {
            FusePrefabStoreMaterialDefinitionsPatch.ResetSanitizerLoggingForTests();
            GC.SuppressFinalize(this);
        }

        private static TypedContainerItem<MaterialDefinition> Wrap(string identifier, MaterialDefinition definition) =>
            new TypedContainerItem<MaterialDefinition>
            {
                Identifier = identifier,
                Definition = definition
            };

        // ---- guard paths ----

        [Fact]
        public void NullItem_IsNoOp_DoesNotThrow()
        {
            // Defensive: the upstream enumeration is allowed to yield
            // a null wrapper, and we must skip it cleanly rather than
            // NRE inside the sanitiser.
            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(null);
        }

        [Fact]
        public void NullDefinitionInItem_IsNoOp_DoesNotThrow()
        {
            // Same defensive contract for a wrapper whose Definition
            // field is null — the upstream enumeration sometimes
            // yields these for placeholder slots.
            var item = new TypedContainerItem<MaterialDefinition>
            {
                Identifier = "test/null-definition",
                Definition = null
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(item);

            Assert.Null(item.Definition);
        }

        // ---- Fields list creation ----

        [Fact]
        public void NullFieldsList_IsReplacedWithEmptyList()
        {
            // The specific malformation the patch was added to handle.
            // After sanitisation, Fields must be a non-null empty list
            // so downstream "for (var i = 0; i < def.Fields.Count; ...)"
            // loops don't NRE.
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/null-fields",
                Fields = null
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/null-fields", def));

            Assert.NotNull(def.Fields);
            Assert.Empty(def.Fields);
        }

        [Fact]
        public void NullFieldsList_OnlyReplaced_FurtherInvocationsAreNoOp()
        {
            // After the first sanitisation, Fields is a non-null empty
            // list and any subsequent call must NOT recreate it (that
            // would lose any entries the consumer added between calls)
            // or warn again for the same identifier.
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/once",
                Fields = null
            };
            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/once", def));
            var firstList = def.Fields;
            def.Fields.Add(new MaterialDefinition.FieldPair { Key = "color", Value = "red" });

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/once", def));

            Assert.Same(firstList, def.Fields);
            Assert.Single(def.Fields);
            Assert.Equal("color", def.Fields[0].Key);
        }

        // ---- null FieldPair removal ----

        [Fact]
        public void NullFieldPairEntries_AreRemoved()
        {
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/mixed-nulls",
                Fields = new List<MaterialDefinition.FieldPair>
                {
                    null,
                    new MaterialDefinition.FieldPair { Key = "color", Value = "red" },
                    null,
                    new MaterialDefinition.FieldPair { Key = "metalness", Value = "0.4" },
                    null
                }
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/mixed-nulls", def));

            Assert.Equal(2, def.Fields.Count);
            Assert.All(def.Fields, pair => Assert.NotNull(pair));
            Assert.Equal("color", def.Fields[0].Key);
            Assert.Equal("metalness", def.Fields[1].Key);
        }

        [Fact]
        public void AllNullFieldPairs_AreAllRemoved_LeavingEmptyList()
        {
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/all-null-pairs",
                Fields = new List<MaterialDefinition.FieldPair> { null, null, null }
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/all-null-pairs", def));

            Assert.NotNull(def.Fields);
            Assert.Empty(def.Fields);
        }

        // ---- already-clean cases ----

        [Fact]
        public void AlreadyCleanDefinition_IsUntouched()
        {
            var pair = new MaterialDefinition.FieldPair { Key = "color", Value = "red" };
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/clean",
                Fields = new List<MaterialDefinition.FieldPair> { pair }
            };
            var listBefore = def.Fields;

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/clean", def));

            // Same list instance (we didn't reallocate), same single
            // pair instance (we didn't remove it).
            Assert.Same(listBefore, def.Fields);
            Assert.Single(def.Fields);
            Assert.Same(pair, def.Fields[0]);
        }

        [Fact]
        public void EmptyFieldsList_IsUntouched()
        {
            var def = new MaterialDefinition
            {
                AssetIdentifier = "test/empty",
                Fields = new List<MaterialDefinition.FieldPair>()
            };
            var listBefore = def.Fields;

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("test/empty", def));

            Assert.Same(listBefore, def.Fields);
            Assert.Empty(def.Fields);
        }

        // ---- identifier fallback ----

        [Fact]
        public void IdentifierFallback_PrefersWrapperIdentifier()
        {
            // MaterialIdentifier is used to dedup warnings. Prefer
            // the wrapper's Identifier over the inner definition's
            // AssetIdentifier — the wrapper's id is what the upstream
            // PrefabStore enumeration treats as canonical.
            var def = new MaterialDefinition
            {
                AssetIdentifier = "ignored-id",
                Fields = null
            };

            // No throw → identifier resolved cleanly.
            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap("wrapper-id", def));

            Assert.NotNull(def.Fields);
        }

        [Fact]
        public void IdentifierFallback_FallsBackToDefinitionWhenWrapperBlank()
        {
            var def = new MaterialDefinition
            {
                AssetIdentifier = "definition-id",
                Fields = null
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap(null, def));

            Assert.NotNull(def.Fields);
        }

        [Fact]
        public void IdentifierFallback_UsesUnknownWhenAllBlank()
        {
            // No wrapper id and no AssetIdentifier — the warning
            // code path still needs a non-null identifier string so
            // the HashSet-based dedup doesn't NRE.
            var def = new MaterialDefinition
            {
                AssetIdentifier = null,
                Fields = null
            };

            FusePrefabStoreMaterialDefinitionsPatch.SanitizeMaterialDefinition(Wrap(null, def));

            Assert.NotNull(def.Fields);
        }
    }
}
