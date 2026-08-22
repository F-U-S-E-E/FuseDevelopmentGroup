using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Fuse.Core.Bridge;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    /// <summary>
    /// Locks the rule that the bridge mods' runtime protocol files
    /// (heartbeats, RPC requests/results) are never treated as fallback
    /// package definitions. FUSE.LiveBridge writes
    /// <c>Mods/FUSE.LiveBridge/bridge_state.json</c> while its Info.json
    /// declares <c>Requirements: ["FUSE"]</c>, so before the exclusion,
    /// discovery classified the bridge mod folder as a data package and
    /// every session recorded a permanent "Definition file ... is missing
    /// an id" fault. FUSE.dll cannot reference FUSE.Core (it ships only
    /// with the bridge mods), so the exclusion list in
    /// <see cref="FuseDefinitionFileDiscovery"/> repeats the names as
    /// literals; the InlineData below references the canonical
    /// <see cref="BridgeProtocol"/> constants so a rename in FUSE.Core
    /// fails here instead of silently drifting apart.
    /// </summary>
    public class FuseDefinitionFileDiscoveryTests : IDisposable
    {
        private readonly string _root;
        private bool _disposed;

        public FuseDefinitionFileDiscoveryTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "FuseDefinitionDiscoveryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
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
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort cleanup. Stragglers in TEMP are harmless.
                System.Console.WriteLine($"Cleanup of '{_root}' failed: {ex.Message}");
            }
            GC.SuppressFinalize(this);
        }

        private string CreateModFolder(string name)
        {
            var folder = Path.Combine(_root, name);
            Directory.CreateDirectory(folder);
            return folder;
        }

        [Theory]
        [InlineData(BridgeProtocol.StateFileName)]
        [InlineData(BridgeProtocol.CommandFileName)]
        [InlineData(BridgeProtocol.TestStateFileName)]
        [InlineData(BridgeProtocol.TestRequestPrefix + "abc123.json")]
        [InlineData(BridgeProtocol.TestResultPrefix + "abc123.json")]
        public void Bridge_protocol_files_are_not_fallback_definitions(string fileName)
        {
            var folder = CreateModFolder(BridgeProtocol.BridgeModFolderName);
            File.WriteAllText(Path.Combine(folder, fileName), "{}");

            Assert.Empty(FuseDefinitionFileDiscovery.ResolveFallbackDefinitionPaths(folder));
            Assert.False(FuseDefinitionFileDiscovery.HasFallbackDefinitionFile(folder));
        }

        [Fact]
        public void Real_definition_still_resolves_next_to_bridge_state()
        {
            var folder = CreateModFolder("SomePackage");
            File.WriteAllText(Path.Combine(folder, BridgeProtocol.StateFileName), "{}");
            var definitionPath = Path.Combine(folder, "trackwork.fuse.json");
            File.WriteAllText(definitionPath, "{}");

            var resolved = FuseDefinitionFileDiscovery.ResolveFallbackDefinitionPaths(folder);

            Assert.Equal(new[] { definitionPath }, resolved);
        }

        [Fact]
        public void LiveBridge_mod_folder_is_not_discovered_as_a_data_package()
        {
            // Real-world shape of Mods/FUSE.LiveBridge once the in-game bridge
            // has published its heartbeat: a UMM Info.json requiring FUSE plus
            // bridge_state.json. Without the exclusion, the heartbeat counts as
            // a root definition file and the FUSE requirement promotes the
            // folder to a data package.
            var folder = CreateModFolder(BridgeProtocol.BridgeModFolderName);
            File.WriteAllText(Path.Combine(folder, "Info.json"), @"{
  ""Id"": ""FUSE.LiveBridge"",
  ""DisplayName"": ""FUSE Live Bridge"",
  ""Version"": ""0.1.0"",
  ""AssemblyName"": ""FUSE.LiveBridge.dll"",
  ""EntryMethod"": ""FUSE.LiveBridge.Main.Load"",
  ""Requirements"": [""FUSE""],
  ""LoadAfter"": [""FUSE""]
}");
            File.WriteAllText(
                Path.Combine(folder, BridgeProtocol.StateFileName),
                @"{ ""Schema"": 1, ""Pid"": 1234, ""HeartbeatUtc"": ""2026-06-10T00:00:00Z"", ""Ok"": true }");

            Assert.Empty(FuseDataPackageDiscovery.DiscoverPackageFolders(_root));
        }

        [Fact]
        public void AlinaUtilities_registry_is_not_discovered_as_a_data_package()
        {
            // Alina Utilities is a dual-format executable mod. Its registry.json
            // is updater/catalog metadata, not a native FUSE definition. A FUSE
            // requirement in Info.json must not promote that support file into
            // the data-package loader.
            var folder = CreateModFolder("AlinasUtils");
            File.WriteAllText(Path.Combine(folder, "Info.json"), @"{
  ""Id"": ""AlinaNova21.AlinasUtils"",
  ""DisplayName"": ""Alina's Utilities"",
  ""Version"": ""1.0.0"",
  ""AssemblyName"": ""AlinasUtils.dll"",
  ""EntryMethod"": ""AlinasUtils.UMM.Mod.Load"",
  ""Requirements"": [""FUSE""],
  ""LoadAfter"": [""FUSE""]
}");
            File.WriteAllText(
                Path.Combine(folder, "registry.json"),
                @"{ ""mods"": [{ ""id"": ""AlinaNova21.AlinasUtils"" }] }");

            Assert.Empty(FuseDefinitionFileDiscovery.ResolveFallbackDefinitionPaths(folder));
            Assert.Empty(FuseDataPackageDiscovery.DiscoverPackageFolders(_root));
        }

        [Fact]
        public void NativePackageWithMalformedInfo_IsStillDiscoveredForFaultReporting()
        {
            var folder = CreateModFolder("BrokenNativePackage");
            File.WriteAllText(Path.Combine(folder, "Info.json"), "{ \"Id\": \"BrokenNativePackage\", ");
            File.WriteAllText(Path.Combine(folder, "track.fuse.json"), "{}");

            var discovered = FuseDataPackageDiscovery.DiscoverPackageFolders(_root);

            Assert.Equal(new[] { folder }, discovered);
        }

        [Fact]
        public void PluralDataFilesRemainValidWhenSingularFieldIsJsonNull()
        {
            var folder = CreateModFolder("PluralDataFiles");
            File.WriteAllText(Path.Combine(folder, "Info.json"), @"{
  ""Id"": ""Plural.Data.Files"",
  ""FuseDataFile"": null,
  ""FuseDataFiles"": [""first.fuse.json"", ""second.fuse.json""],
  ""FuseDisabled"": true
}");

            var manifest = Assert.Single(FuseDataPackageDiscovery.GetPackageManifestSnapshots(_root));

            Assert.Equal("Plural.Data.Files", manifest.Id);
            Assert.Empty(manifest.Faults);
        }

        [Fact]
        public void DisabledPackage_DoesNotValidateItsOwnDependencies()
        {
            var folder = CreateModFolder("DisabledPackage");
            File.WriteAllText(Path.Combine(folder, "Info.json"), @"{
  ""Id"": ""Disabled.Package"",
  ""FuseDataFile"": ""content.fuse.json"",
  ""FuseDisabled"": true,
  ""FuseRequires"": [""Missing.Package""]
}");

            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots(_root);
            var manifest = Assert.Single(manifests);

            Assert.True(manifest.Disabled);
            Assert.Empty(manifest.Faults);
        }
    }
}
