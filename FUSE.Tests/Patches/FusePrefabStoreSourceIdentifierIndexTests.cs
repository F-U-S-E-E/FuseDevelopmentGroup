using System;
using System.IO;
using System.Linq;
using FUSE.Patches;
using Xunit;

namespace FUSE.Tests.Patches
{
    public sealed class FusePrefabStoreSourceIdentifierIndexTests
    {
        [Fact]
        public void ReadTopLevelObjectIdentifiers_OnlyReturnsContainerObjectIdentifiers()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"objects\":[" +
                    "{\"identifier\":\"first\",\"definition\":{\"identifier\":\"nested\"}}," +
                    "{\"identifier\":\"second\",\"metadata\":{\"identifier\":\"also-nested\"}}" +
                    "]}");

                var identifiers =
                    FusePrefabStoreAssetPackContainingIdentifierTracePatch
                        .ReadTopLevelObjectIdentifiers(path)
                        .ToArray();

                Assert.Equal(new[] { "first", "second" }, identifiers);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void TryReadCompleteTopLevelObjectIdentifiers_DiscardsIdentifiersFromMalformedFile()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-malformed-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"objects\":[" +
                    "{\"identifier\":\"must-not-survive\"}," +
                    "{\"identifier\":\"broken\",\"definition\":{\"components\":[" +
                    "{\"name\":\"first\"} {\"name\":\"missing-comma\"}]}}]}");

                var success =
                    FusePrefabStoreAssetPackContainingIdentifierTracePatch
                        .TryReadCompleteTopLevelObjectIdentifiers(
                            path,
                            out var identifiers,
                            out var jsonException);

                Assert.False(success);
                Assert.Empty(identifiers);
                Assert.NotNull(jsonException);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void TryReadCompleteTopLevelObjectIdentifiers_RejectsTruncatedRootObject()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-truncated-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"objects\":[{\"identifier\":\"must-not-survive\"}]");

                var success =
                    FusePrefabStoreAssetPackContainingIdentifierTracePatch
                        .TryReadCompleteTopLevelObjectIdentifiers(
                            path,
                            out var identifiers,
                            out var jsonException);

                Assert.False(success);
                Assert.Empty(identifiers);
                Assert.NotNull(jsonException);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void TryReadCompleteTopLevelObjectIdentifiers_AcceptsPropertiesAfterObjectsArray()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-trailing-properties-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(
                    path,
                    "{\"objects\":[{\"identifier\":\"kept\"}]," +
                    "\"metadata\":{\"identifier\":\"ignored\",\"nested\":{\"version\":1}}," +
                    "\"version\":\"1.5\"}");

                var success =
                    FusePrefabStoreAssetPackContainingIdentifierTracePatch
                        .TryReadCompleteTopLevelObjectIdentifiers(
                            path,
                            out var identifiers,
                            out var jsonException);

                Assert.True(success);
                Assert.Equal(new[] { "kept" }, identifiers);
                Assert.Null(jsonException);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void TryReadCompleteTopLevelObjectIdentifiers_PropagatesNonJsonFailures()
        {
            var missingPath = Path.Combine(
                Path.GetTempPath(),
                "fuse-prefab-index-missing-" + Guid.NewGuid().ToString("N") + ".json");

            Assert.Throws<FileNotFoundException>(() =>
                FusePrefabStoreAssetPackContainingIdentifierTracePatch
                    .TryReadCompleteTopLevelObjectIdentifiers(
                        missingPath,
                        out _,
                        out _));
        }

        [Fact]
        public void SourceStoreScanState_MalformedStoreDoesNotBlockLaterCandidate()
        {
            var state =
                new FusePrefabStoreAssetPackContainingIdentifierTracePatch
                    .SourceStoreScanState<string>();

            Assert.True(state.MarkMalformed("malformed"));
            Assert.False(state.MarkMalformed("malformed"));
            Assert.False(state.ShouldProbe("malformed"));
            Assert.True(state.ShouldProbe("later-valid"));
            Assert.True(state.CanUseCandidate(2));
            Assert.Equal(-1, state.FirstOpaqueStoreIndex);
            Assert.Equal(1, state.MalformedStoreCount);
        }

        [Fact]
        public void SourceStoreScanState_OpaqueStoreBlocksOnlyLaterCandidates()
        {
            var state =
                new FusePrefabStoreAssetPackContainingIdentifierTracePatch
                    .SourceStoreScanState<string>();

            state.MarkOpaque(1);
            state.MarkOpaque(3);

            Assert.True(state.CanUseCandidate(0));
            Assert.True(state.CanUseCandidate(1));
            Assert.False(state.CanUseCandidate(2));
            Assert.Equal(1, state.FirstOpaqueStoreIndex);
        }

        [Fact]
        public void DefinitionStoreQuarantine_PersistsAcrossOwnerScanStates()
        {
            var quarantine =
                new FusePrefabStoreAssetPackContainingIdentifierTracePatch
                    .DefinitionStoreQuarantine<string>();
            Assert.True(quarantine.Add("owner-a-malformed"));
            Assert.True(quarantine.Add("owner-b-malformed"));
            Assert.False(quarantine.Add("owner-a-malformed"));

            var ownerAState = quarantine.CreateScanState(
                new[] { "owner-a-malformed", "owner-a-valid" });
            var ownerBState = quarantine.CreateScanState(
                new[] { "owner-b-malformed", "owner-b-valid" });
            var ownerARevisitedState = quarantine.CreateScanState(
                new[] { "owner-a-malformed", "owner-a-valid" });

            Assert.False(ownerAState.ShouldProbe("owner-a-malformed"));
            Assert.True(ownerAState.ShouldProbe("owner-a-valid"));
            Assert.False(ownerBState.ShouldProbe("owner-b-malformed"));
            Assert.True(ownerBState.ShouldProbe("owner-b-valid"));
            Assert.False(ownerARevisitedState.ShouldProbe("owner-a-malformed"));

            var staleOtherOwnerState =
                new FusePrefabStoreAssetPackContainingIdentifierTracePatch
                    .SourceStoreScanState<string>();
            Assert.False(quarantine.CanUseCandidate(
                staleOtherOwnerState,
                "owner-a-malformed",
                0));
            Assert.True(quarantine.CanUseCandidate(
                staleOtherOwnerState,
                "owner-b-valid",
                0));
        }
    }
}
