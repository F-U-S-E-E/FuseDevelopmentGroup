using System;
using System.IO;
using System.Linq;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseAssetLoaderDefinitionOverrideTests : IDisposable
    {
        private readonly string _root;

        public FuseAssetLoaderDefinitionOverrideTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "FuseAssetLoaderDefinitionOverrideTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Best-effort temporary fixture cleanup.
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        [Fact]
        public void Implicit_discovery_matches_AssetLoader_immediate_child_convention()
        {
            var package = CreatePackage("RollingStock", "RollingStock.Mod", null);
            var definitionsOnly = CreateDefinitions(package, @"fm-flatcar03\Definitions.json");
            var catalogChild = CreateDefinitions(package, @"catalog-store\Definitions.json");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(catalogChild), "Catalog.json"), "{}");
            CreateDefinitions(package, @"nested\ignored-store\Definitions.json");

            var candidates = FuseAssetPackRegistry.DiscoverDefinitionOverrideCandidatesForPackage(
                package,
                out var issues);

            var candidate = Assert.Single(candidates);
            Assert.Equal("fm-flatcar03", candidate.StoreIdentifier);
            Assert.Equal(Path.GetFullPath(definitionsOnly), candidate.DefinitionsPath);
            Assert.False(candidate.Explicit);
            Assert.Empty(issues);
        }

        [Fact]
        public void Explicit_manifest_entry_supports_nested_definition_file_and_target()
        {
            const string info = @"{
  ""Id"": ""Native.Override.Mod"",
  ""FuseDefinitionOverrides"": [
    {
      ""StoreIdentifier"": ""ne-caboose03"",
      ""Path"": ""overrides/caboose/Definitions.json""
    }
  ]
}";
            var package = CreatePackage("NativeOverride", "Native.Override.Mod", info);
            var definitions = CreateDefinitions(package, @"overrides\caboose\Definitions.json");

            var candidates = FuseAssetPackRegistry.DiscoverDefinitionOverrideCandidatesForPackage(
                package,
                out var issues);

            var candidate = Assert.Single(candidates);
            Assert.Equal("ne-caboose03", candidate.StoreIdentifier);
            Assert.Equal(Path.GetFullPath(definitions), candidate.DefinitionsPath);
            Assert.True(candidate.Explicit);
            Assert.Empty(issues);
        }

        [Fact]
        public void Explicit_manifest_entry_cannot_escape_package_folder()
        {
            const string info = @"{
  ""Id"": ""Unsafe.Override.Mod"",
  ""FuseDefinitionOverrides"": {
    ""StoreIdentifier"": ""shared"",
    ""Path"": ""../outside/Definitions.json""
  }
}";
            var package = CreatePackage("UnsafeOverride", "Unsafe.Override.Mod", info);

            var candidates = FuseAssetPackRegistry.DiscoverDefinitionOverrideCandidatesForPackage(
                package,
                out var issues);

            Assert.Empty(candidates);
            Assert.Contains(issues, issue => issue.Contains("escapes the package folder"));
        }

        [Fact]
        public void Explicit_candidate_wins_duplicate_target_deterministically()
        {
            var legacy = new FuseLegacyDefinitionOverrideRegistration
            {
                StoreIdentifier = "fm-flatcar03",
                DefinitionsPath = @"C:\Mods\Legacy\fm-flatcar03\Definitions.json",
                PackageId = "Legacy",
                PackagePath = @"C:\Mods\Legacy",
                Explicit = false
            };
            var explicitCandidate = new FuseLegacyDefinitionOverrideRegistration
            {
                StoreIdentifier = "fm-flatcar03",
                DefinitionsPath = @"C:\Mods\Native\overrides\Definitions.json",
                PackageId = "Native",
                PackagePath = @"C:\Mods\Native",
                Explicit = true
            };

            var winners = FuseAssetPackRegistry.SelectLegacyDefinitionOverrides(
                new[] { legacy, explicitCandidate },
                out var issues);

            Assert.Same(explicitCandidate, winners["fm-flatcar03"]);
            Assert.Single(issues);
            Assert.Contains("selected", issues[0]);
            Assert.Contains("ignored", issues[0]);
        }

        [Fact]
        public void Target_store_identifiers_remain_case_sensitive_like_PrefabStore()
        {
            var upper = Candidate("Store", @"C:\Mods\A\Definitions.json", "A");
            var lower = Candidate("store", @"C:\Mods\B\Definitions.json", "B");

            var winners = FuseAssetPackRegistry.SelectLegacyDefinitionOverrides(
                new[] { upper, lower },
                out var issues);

            Assert.Equal(2, winners.Count);
            Assert.Empty(issues);
        }

        private string CreatePackage(string folderName, string id, string infoText)
        {
            var package = Path.Combine(_root, folderName);
            Directory.CreateDirectory(package);
            File.WriteAllText(
                Path.Combine(package, "Info.json"),
                infoText ?? "{\"Id\":\"" + id + "\"}");
            return package;
        }

        private static string CreateDefinitions(string package, string relativePath)
        {
            var path = Path.Combine(package, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"objects\":[]}");
            return path;
        }

        private static FuseLegacyDefinitionOverrideRegistration Candidate(
            string storeIdentifier,
            string definitionsPath,
            string packageId)
        {
            return new FuseLegacyDefinitionOverrideRegistration
            {
                StoreIdentifier = storeIdentifier,
                DefinitionsPath = definitionsPath,
                PackageId = packageId,
                PackagePath = Path.GetDirectoryName(definitionsPath),
                Explicit = false
            };
        }
    }
}
