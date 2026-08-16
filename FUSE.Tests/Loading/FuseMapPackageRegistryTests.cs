using System;
using System.IO;
using FUSE.Authoring.Data;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public class FuseMapPackageRegistryTests : IDisposable
    {
        private readonly string _root;

        public FuseMapPackageRegistryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "fuse-map-registry-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort temp cleanup; a leftover temp folder is harmless.
                System.Console.WriteLine($"FuseMapPackageRegistryTests temp cleanup failed: {ex.Message}");
            }
        }

        private string CreatePackageFolder(string name, bool withMapFolder = true, bool withMapJson = true)
        {
            var packageFolder = Path.Combine(_root, name);
            Directory.CreateDirectory(packageFolder);
            if (withMapFolder)
            {
                var mapFolder = Path.Combine(packageFolder, "Map");
                Directory.CreateDirectory(mapFolder);
                if (withMapJson)
                {
                    File.WriteAllText(
                        Path.Combine(mapFolder, "Map.json"),
                        "{\"origin\":{\"latitude\":40.43,\"longitude\":-77.72},\"tileDimension\":500,\"tiles\":[]}");
                }
            }

            return packageFolder;
        }

        public class TryResolveMapFolder : FuseMapPackageRegistryTests
        {
            [Fact]
            public void ValidRelativeFolder_Resolves()
            {
                var packageFolder = CreatePackageFolder("pack");

                var ok = FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, "Map", out var resolved, out var error);

                Assert.True(ok, error);
                Assert.Equal(Path.Combine(Path.GetFullPath(packageFolder), "Map"), resolved);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public void BlankFolder_IsRejected(string mapFolder)
            {
                var packageFolder = CreatePackageFolder("pack");

                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, mapFolder, out _, out var error));
                Assert.Contains("blank", error);
            }

            [Fact]
            public void RootedFolder_IsRejected()
            {
                var packageFolder = CreatePackageFolder("pack");
                var rooted = Path.Combine(packageFolder, "Map");

                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, rooted, out _, out var error));
                Assert.Contains("rooted", error);
            }

            [Fact]
            public void TraversalOutsidePackage_IsRejected()
            {
                CreatePackageFolder("other");
                var packageFolder = CreatePackageFolder("pack");

                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, "..\\other\\Map", out _, out var error));
                Assert.Contains("outside", error);
            }

            [Fact]
            public void SiblingFolderWithSamePrefix_IsRejected()
            {
                var packageFolder = CreatePackageFolder("pack");
                CreatePackageFolder("pack2");

                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, "..\\pack2\\Map", out _, out var error));
                Assert.Contains("outside", error);
            }

            [Fact]
            public void MissingFolder_IsRejected()
            {
                var packageFolder = CreatePackageFolder("pack", withMapFolder: false);

                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(packageFolder, "Map", out _, out var error));
                Assert.Contains("does not exist", error);
            }

            [Fact]
            public void UnknownPackageFolder_IsRejected()
            {
                Assert.False(FuseMapPackageRegistry.TryResolveMapFolder(null, "Map", out _, out var error));
                Assert.Contains("package folder", error);
            }
        }

        public class BuildEntry : FuseMapPackageRegistryTests
        {
            [Fact]
            public void ValidDeclaration_ProducesValidEntry()
            {
                var packageFolder = CreatePackageFolder("pack");
                var declaration = new FuseMapDeclaration { DisplayName = "PRR Middle Division", MapFolder = "Map" };

                var entry = FuseMapPackageRegistry.BuildEntry("prr", "PRR Pack", packageFolder, declaration);

                Assert.True(entry.IsValid, entry.FaultReason);
                Assert.Equal("prr", entry.MapId);
                Assert.Equal("PRR Middle Division", entry.DisplayName);
                Assert.True(File.Exists(Path.Combine(entry.MapFolder, "Map.json")));
                Assert.True(entry.SuppressBaseWorld);
            }

            [Fact]
            public void DeclarationCanKeepBaseWorldForIntentionalOverlay()
            {
                var packageFolder = CreatePackageFolder("overlay");
                var declaration = new FuseMapDeclaration
                {
                    MapFolder = "Map",
                    SuppressBaseWorld = false
                };

                var entry = FuseMapPackageRegistry.BuildEntry(
                    "overlay",
                    "Overlay",
                    packageFolder,
                    declaration);

                Assert.False(entry.SuppressBaseWorld);
                Assert.False(FuseBaseWorldIsolation.ShouldSuppress(entry));
            }

            [Fact]
            public void DisplayNameFallsBackToPackageNameThenId()
            {
                var packageFolder = CreatePackageFolder("pack");

                var fromName = FuseMapPackageRegistry.BuildEntry("prr", "PRR Pack", packageFolder, new FuseMapDeclaration { MapFolder = "Map" });
                var fromId = FuseMapPackageRegistry.BuildEntry("prr", null, packageFolder, new FuseMapDeclaration { MapFolder = "Map" });

                Assert.Equal("PRR Pack", fromName.DisplayName);
                Assert.Equal("prr", fromId.DisplayName);
            }

            [Fact]
            public void MissingMapJson_ProducesFaultedEntry()
            {
                var packageFolder = CreatePackageFolder("pack", withMapFolder: true, withMapJson: false);

                var entry = FuseMapPackageRegistry.BuildEntry("prr", "PRR Pack", packageFolder, new FuseMapDeclaration { MapFolder = "Map" });

                Assert.False(entry.IsValid);
                Assert.Contains("Map.json", entry.FaultReason);
                Assert.Equal(string.Empty, entry.MapFolder);
            }

            [Fact]
            public void BlankMapFolder_ProducesFaultedEntry()
            {
                var packageFolder = CreatePackageFolder("pack");

                var entry = FuseMapPackageRegistry.BuildEntry("prr", "PRR Pack", packageFolder, new FuseMapDeclaration());

                Assert.False(entry.IsValid);
                Assert.Contains("blank", entry.FaultReason);
            }
        }

        public class Registration : FuseMapPackageRegistryTests
        {
            private FuseLoadedMod LoadedMapPackage(string id, string packageFolder)
            {
                return new FuseLoadedMod(packageFolder, Path.Combine(packageFolder, "fuse-mod.json"), new FuseModDefinition
                {
                    Id = id,
                    Name = id,
                    Map = new FuseMapDeclaration { DisplayName = id, MapFolder = "Map" }
                });
            }

            [Fact]
            public void RegisterTryGetUnregister_RoundTrips()
            {
                var id = "map-" + Guid.NewGuid().ToString("N");
                var packageFolder = CreatePackageFolder("pack");

                FuseMapPackageRegistry.RegisterFromDefinition(LoadedMapPackage(id, packageFolder));
                try
                {
                    Assert.True(FuseMapPackageRegistry.TryGetMap(id, out var map));
                    Assert.True(map.IsValid, map.FaultReason);
                    Assert.Contains(FuseMapPackageRegistry.GetRegisteredMaps(), m => m.MapId == id);
                }
                finally
                {
                    FuseMapPackageRegistry.Unregister(id);
                }

                Assert.False(FuseMapPackageRegistry.TryGetMap(id, out _));
            }

            [Fact]
            public void ReplacementWithoutMapDeclaration_RemovesRegistration()
            {
                var id = "map-" + Guid.NewGuid().ToString("N");
                var packageFolder = CreatePackageFolder("pack");

                FuseMapPackageRegistry.RegisterFromDefinition(LoadedMapPackage(id, packageFolder));
                try
                {
                    var withoutMap = new FuseLoadedMod(packageFolder, null, new FuseModDefinition { Id = id, Name = id });
                    FuseMapPackageRegistry.RegisterFromDefinition(withoutMap);

                    Assert.False(FuseMapPackageRegistry.TryGetMap(id, out _));
                }
                finally
                {
                    FuseMapPackageRegistry.Unregister(id);
                }
            }
        }
    }
}
