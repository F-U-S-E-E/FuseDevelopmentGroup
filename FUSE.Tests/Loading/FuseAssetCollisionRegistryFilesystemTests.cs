using System;
using System.IO;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Integration tests that exercise the collision registry against
    /// real on-disk pack-folder structures. We synthesize a tiny mods
    /// folder (Catalog.json + Definitions.json + a sentinel Bundle file
    /// per pack), run the scan, and check the recorded collisions.
    ///
    /// <para>These tests catch path-normalization regressions the
    /// in-memory-only tests miss — anything that depends on
    /// <see cref="Path.GetFullPath"/>, real
    /// <see cref="Path.DirectorySeparatorChar"/>, or case-insensitive
    /// matching against actual filesystem entries is best validated
    /// here.</para>
    /// </summary>
    [Collection(FuseAssetCollisionRegistryTestCollection.Name)]
    public class FuseAssetCollisionRegistryFilesystemTests : IDisposable
    {
        private readonly string _modsRoot;
        private bool _disposed;

        public FuseAssetCollisionRegistryFilesystemTests()
        {
            FuseAssetCollisionRegistry.Reset();
            _modsRoot = Path.Combine(
                Path.GetTempPath(),
                "FuseTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_modsRoot);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            FuseAssetCollisionRegistry.Reset();
            try
            {
                if (Directory.Exists(_modsRoot))
                {
                    Directory.Delete(_modsRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup; another test process touching
                // the same path should never happen because of the GUID
                // suffix, but if it does we leave the residue rather than
                // fail the test.
            }
        }

        [Fact]
        public void Scan_detects_within_mod_root_vs_SCAssetPacks_duplicate()
        {
            var modFolder = Path.Combine(_modsRoot, "MyMod");
            CreatePack(Path.Combine(modFolder, "widget"));
            CreatePack(Path.Combine(modFolder, "SCAssetPacks", "widget"));

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            var collision = Assert.Single(collisions);
            Assert.Equal("widget", collision.SharedIdentifier);
            Assert.Single(collision.LoserFolders);
            // Root pack is the winner; SCAssetPacks copy is the
            // loser (legacy fallback convention).
            Assert.DoesNotContain("SCAssetPacks", collision.WinnerFolder);
            Assert.Contains("SCAssetPacks", collision.LoserFolders[0]);
        }

        [Fact]
        public void Scan_does_not_flag_packs_with_distinct_leaf_names_within_same_mod()
        {
            var modFolder = Path.Combine(_modsRoot, "LLW-style");
            CreatePack(Path.Combine(modFolder, "ls-282-k35a"));
            CreatePack(Path.Combine(modFolder, "ls-282-k35b"));

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Scan_does_not_flag_cross_mod_overlap_even_when_leaf_names_match()
        {
            CreatePack(Path.Combine(_modsRoot, "ModA", "widget"));
            CreatePack(Path.Combine(_modsRoot, "ModB", "widget"));

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Empty(collisions);
        }

        [Fact]
        public void Scan_detects_multiple_independent_collisions_in_same_session()
        {
            // Three mods, each with their own root-vs-SCAssetPacks
            // duplicate. Each must surface as its own collision.
            var modA = Path.Combine(_modsRoot, "ModA");
            CreatePack(Path.Combine(modA, "foo"));
            CreatePack(Path.Combine(modA, "SCAssetPacks", "foo"));

            var modB = Path.Combine(_modsRoot, "ModB");
            CreatePack(Path.Combine(modB, "bar"));
            CreatePack(Path.Combine(modB, "SCAssetPacks", "bar"));

            var modC = Path.Combine(_modsRoot, "ModC");
            CreatePack(Path.Combine(modC, "baz"));
            CreatePack(Path.Combine(modC, "SCAssetPacks", "baz"));

            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Equal(3, collisions.Count);
            var labels = collisions.Select(c => c.SharedIdentifier).OrderBy(s => s).ToArray();
            Assert.Equal(new[] { "bar", "baz", "foo" }, labels);
        }

        [Fact]
        public void Scan_redirect_path_actually_points_at_winner_bundle_file()
        {
            var modFolder = Path.Combine(_modsRoot, "MyMod");
            var rootPack = Path.Combine(modFolder, "widget");
            var conventionPack = Path.Combine(modFolder, "SCAssetPacks", "widget");
            CreatePack(rootPack);
            CreatePack(conventionPack);

            FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            // SCAssetPacks copy is the loser; redirect points at the
            // root pack's Bundle file.
            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(conventionPack, out var redirect));
            Assert.Equal(Path.Combine(rootPack, "Bundle"), redirect);
            // And the redirect target must actually exist on disk so a
            // real Unity LoadFromFile would find it.
            Assert.True(File.Exists(redirect));
        }

        [Fact]
        public void Scan_handles_case_insensitive_path_components()
        {
            // The detection groups by host mod + leaf name with
            // case-insensitive equality. Verify a real filesystem path
            // that varies only by case is still recognized as the same
            // pack — Windows is case-preserving but case-insensitive
            // and pack folders sometimes get reported with a different
            // casing depending on which API enumerated them.
            var modFolder = Path.Combine(_modsRoot, "CaseMod");
            CreatePack(Path.Combine(modFolder, "WIDGET"));
            CreatePack(Path.Combine(modFolder, "SCAssetPacks", "widget"));

            var folders = EnumeratePackFolders(_modsRoot);
            var collisions = FuseAssetCollisionRegistry.ScanForCollisions(
                folders,
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.Single(collisions);
        }

        [Fact]
        public void Reset_after_real_scan_clears_disk_derived_redirects()
        {
            var modFolder = Path.Combine(_modsRoot, "MyMod");
            var rootPack = Path.Combine(modFolder, "widget");
            var scPack = Path.Combine(modFolder, "SCAssetPacks", "widget");
            CreatePack(rootPack);
            CreatePack(scPack);

            FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.True(FuseAssetCollisionRegistry.TryGetBundleRedirect(scPack, out _));

            FuseAssetCollisionRegistry.Reset();

            Assert.False(FuseAssetCollisionRegistry.TryGetBundleRedirect(scPack, out _));
        }

        [Fact]
        public void Reset_disposes_FuseRegistry_claims_from_disk_scan()
        {
            var modFolder = Path.Combine(_modsRoot, "ReleaseMod");
            CreatePack(Path.Combine(modFolder, "widget"));
            CreatePack(Path.Combine(modFolder, "SCAssetPacks", "widget"));

            FuseAssetCollisionRegistry.ScanForCollisions(
                EnumeratePackFolders(_modsRoot),
                pack => HostModName(pack),
                pack => HostMod(pack));

            Assert.NotEmpty(FUSE.Runtime.Registry.FuseRegistry.GetSharedOwners(
                FUSE.Runtime.Registry.FuseClaimKind.AssetCollision, "widget"));

            FuseAssetCollisionRegistry.Reset();

            Assert.Empty(FUSE.Runtime.Registry.FuseRegistry.GetSharedOwners(
                FUSE.Runtime.Registry.FuseClaimKind.AssetCollision, "widget"));
        }

        // ----- helpers -----

        private static void CreatePack(string folder)
        {
            Directory.CreateDirectory(folder);
            // Synthesize minimal pack contents. Discovery does not parse
            // these files for the leaf-name detection path; they just need
            // to exist so IsAssetPackFolder() (and our enumerator) would
            // accept the folder as a valid pack.
            File.WriteAllText(
                Path.Combine(folder, "Catalog.json"),
                "{\"identifier\":\"" + Path.GetFileName(folder) + "\",\"name\":\"" + Path.GetFileName(folder) + "\",\"assets\":{}}");
            File.WriteAllText(
                Path.Combine(folder, "Definitions.json"),
                "{\"objects\":[]}");
            File.WriteAllText(Path.Combine(folder, "Bundle"), "synthetic-bundle-bytes");
        }

        private static string[] EnumeratePackFolders(string modsRoot)
        {
            // Mirror FuseAssetPackRegistry.IsAssetPackFolder semantics:
            // any directory that contains Catalog.json + Definitions.json
            // + Bundle counts as a pack.
            return Directory
                .EnumerateDirectories(modsRoot, "*", SearchOption.AllDirectories)
                .Where(IsPackFolder)
                .ToArray();
        }

        private static bool IsPackFolder(string folder)
        {
            return File.Exists(Path.Combine(folder, "Catalog.json")) &&
                   File.Exists(Path.Combine(folder, "Definitions.json")) &&
                   File.Exists(Path.Combine(folder, "Bundle"));
        }

        private string HostMod(string packFolder)
        {
            var cursor = Path.GetFullPath(packFolder);
            var modsRootFull = Path.GetFullPath(_modsRoot);
            while (!string.IsNullOrEmpty(cursor))
            {
                var parent = Path.GetDirectoryName(cursor);
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }
                if (string.Equals(parent, modsRootFull, StringComparison.OrdinalIgnoreCase))
                {
                    return cursor;
                }
                cursor = parent;
            }
            return null;
        }

        private string HostModName(string packFolder)
        {
            var host = HostMod(packFolder);
            return host == null ? null : Path.GetFileName(host);
        }
    }
}
