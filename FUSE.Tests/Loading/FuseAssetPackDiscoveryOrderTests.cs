using System;
using System.IO;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Locks the order in which FUSE's asset-pack discovery yields pack
    /// folders for a single mod folder. The order is load-bearing: the
    /// game registers stores into <c>PrefabStore._stores</c> in this
    /// order, and the FIRST store to claim an asset identifier wins for
    /// that asset's bundle loads. Mods that ship duplicate pack folders
    /// at the root and under <c>SCAssetPacks/</c> rely on the root copy
    /// being registered first — those two bundles share a Unity CAB
    /// name but contain different versions of the same content, so
    /// registering them in the wrong order makes Unity load the legacy
    /// bundle for a modern car (or vice versa) and produces broken
    /// rendering. A silent inversion here is the exact regression that
    /// caused that bug; this test fixture exists to prevent it.
    /// </summary>
    public class FuseAssetPackDiscoveryOrderTests : IDisposable
    {
        private readonly string _modsRoot;
        private bool _disposed;

        public FuseAssetPackDiscoveryOrderTests()
        {
            _modsRoot = Path.Combine(
                Path.GetTempPath(),
                "FuseDiscoveryOrderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_modsRoot);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                if (Directory.Exists(_modsRoot))
                {
                    Directory.Delete(_modsRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup. Stragglers in TEMP are harmless.
            }
            GC.SuppressFinalize(this);
        }

        private string CreatePackFolder(string relative)
        {
            var full = Path.Combine(_modsRoot, relative);
            Directory.CreateDirectory(full);
            // Definition-bearing packs include all three files.
            File.WriteAllText(Path.Combine(full, "Bundle"), "fake-bundle");
            File.WriteAllText(Path.Combine(full, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(full, "Definitions.json"), "{}");
            return full;
        }

        private string CreateModelOnlyPackFolder(string relative)
        {
            var full = Path.Combine(_modsRoot, relative);
            Directory.CreateDirectory(full);
            File.WriteAllText(Path.Combine(full, "Bundle"), "fake-bundle");
            File.WriteAllText(Path.Combine(full, "Catalog.json"), "{}");
            return full;
        }

        private string CreateCatalogOnlyPackFolder(string relative)
        {
            var full = Path.Combine(_modsRoot, relative);
            Directory.CreateDirectory(full);
            File.WriteAllText(Path.Combine(full, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(full, "Definitions.json"), "{}");
            return full;
        }

        [Fact]
        public void Discovery_yields_root_packs_before_SCAssetPacks_packs()
        {
            // TOFC Cars-shaped layout: a mod folder with sibling pack
            // subfolders at the root AND a SCAssetPacks/ folder that
            // mirrors the same pack names (a duplicate-version pattern).
            var modFolder = Path.Combine(_modsRoot, "TOFC Cars");
            Directory.CreateDirectory(modFolder);

            var rootSpinecar1 = CreatePackFolder(@"TOFC Cars\spinecar1");
            var rootSpinecar2 = CreatePackFolder(@"TOFC Cars\spinecar2");
            var scSpinecar1 = CreatePackFolder(@"TOFC Cars\SCAssetPacks\spinecar1");
            var scSpinecar2 = CreatePackFolder(@"TOFC Cars\SCAssetPacks\spinecar2");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            // Both root packs must be ahead of both SCAssetPacks packs.
            var rootIndices = new[]
            {
                Array.IndexOf(folders, Path.GetFullPath(rootSpinecar1)),
                Array.IndexOf(folders, Path.GetFullPath(rootSpinecar2)),
            };
            var scIndices = new[]
            {
                Array.IndexOf(folders, Path.GetFullPath(scSpinecar1)),
                Array.IndexOf(folders, Path.GetFullPath(scSpinecar2)),
            };

            Assert.All(rootIndices, idx => Assert.True(idx >= 0, "root pack missing from discovery"));
            Assert.All(scIndices, idx => Assert.True(idx >= 0, "SCAssetPacks pack missing from discovery"));

            var maxRoot = rootIndices.Max();
            var minSc = scIndices.Min();
            Assert.True(
                maxRoot < minSc,
                $"Root packs must come before SCAssetPacks packs. " +
                $"Root indices: [{string.Join(",", rootIndices)}], " +
                $"SCAssetPacks indices: [{string.Join(",", scIndices)}]. " +
                $"Order observed: [{string.Join(", ", folders.Select(Path.GetFileName))}]");
        }

        [Fact]
        public void Discovery_yields_root_packs_when_no_SCAssetPacks_exists()
        {
            var modFolder = Path.Combine(_modsRoot, "SimpleMod");
            Directory.CreateDirectory(modFolder);

            var pack1 = CreatePackFolder(@"SimpleMod\pack1");
            var pack2 = CreatePackFolder(@"SimpleMod\pack2");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            Assert.Contains(Path.GetFullPath(pack1), folders);
            Assert.Contains(Path.GetFullPath(pack2), folders);
        }

        [Fact]
        public void Discovery_yields_package_root_catalog_store_before_child_stores()
        {
            var modFolder = Path.Combine(_modsRoot, "RootCatalogMod");
            Directory.CreateDirectory(modFolder);
            File.WriteAllText(Path.Combine(modFolder, "Catalog.json"), "{}");
            File.WriteAllText(Path.Combine(modFolder, "Definitions.json"), "{}");
            var child = CreatePackFolder(@"RootCatalogMod\child-store");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            Assert.Equal(Path.GetFullPath(modFolder), folders[0]);
            Assert.Contains(Path.GetFullPath(child), folders);
        }

        [Fact]
        public void Discovery_yields_model_only_packs_without_definitions()
        {
            var modFolder = Path.Combine(_modsRoot, "WhistleModels");
            Directory.CreateDirectory(modFolder);

            var modelPack = CreateModelOnlyPackFolder(@"WhistleModels\TPPWhistleModels");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            Assert.Contains(Path.GetFullPath(modelPack), folders);
        }

        [Fact]
        public void Discovery_yields_catalog_stores_without_a_local_bundle()
        {
            var modFolder = Path.Combine(_modsRoot, "DefinitionsOnlyCatalogMod");
            Directory.CreateDirectory(modFolder);

            var catalogStore = CreateCatalogOnlyPackFolder(
                @"DefinitionsOnlyCatalogMod\player-coal-load");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            Assert.Contains(Path.GetFullPath(catalogStore), folders);
        }

        [Fact]
        public void Discovery_yields_SCAssetPacks_packs_when_no_root_packs_exist()
        {
            var modFolder = Path.Combine(_modsRoot, "ScOnlyMod");
            Directory.CreateDirectory(modFolder);

            var scPack = CreatePackFolder(@"ScOnlyMod\SCAssetPacks\widget");

            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            Assert.Contains(Path.GetFullPath(scPack), folders);
        }

        [Fact]
        public void Discovery_does_not_yield_SCAssetPacks_directory_itself_as_a_pack()
        {
            // The SCAssetPacks/ folder is a CONTAINER for packs, not a
            // pack itself — even if a careless mod author drops the
            // sentinel Bundle/Catalog/Definitions files at the
            // SCAssetPacks/ root, walking deeper still finds the real
            // packs inside it. Returning the container would register
            // a store whose BasePath was the SCAssetPacks dir, which
            // has no real bundle.
            var modFolder = Path.Combine(_modsRoot, "WeirdMod");
            Directory.CreateDirectory(modFolder);
            CreatePackFolder(@"WeirdMod\SCAssetPacks\inner");

            // Intentionally do not put Catalog.json at the SCAssetPacks/
            // root — only inside its child folder.
            var folders = FuseAssetPackRegistry
                .EnumerateFallbackAssetPackFolders(modFolder)
                .Select(Path.GetFullPath)
                .ToArray();

            var scContainer = Path.GetFullPath(Path.Combine(modFolder, "SCAssetPacks"));
            Assert.DoesNotContain(scContainer, folders);
            Assert.Contains(Path.GetFullPath(Path.Combine(modFolder, "SCAssetPacks", "inner")), folders);
        }
    }
}
