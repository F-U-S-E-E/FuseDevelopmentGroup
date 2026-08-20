using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using FUSE.Compatibility;
using FUSE.Loading;
using HarmonyLib;
using Railloader;
using Xunit;

#pragma warning disable CS0618 // This test intentionally exercises the legacy shim.

namespace FUSE.Tests.Loading
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class FuseRmroc451CompatibilityCollection
    {
        public const string Name = "RMROC451 real legacy assembly compatibility";
    }

    /// <summary>
    /// Opt-in field harness for issue #198. This loads the real third-party
    /// assembly, resolves its Railloader.Interchange reference through FUSE,
    /// constructs the plugin with FUSE's independent context, and resolves the
    /// plugin's real Harmony targets against the installed Railroader API. The
    /// test host does not patch Unity ECall methods because they require Unity's
    /// native runtime; successful target resolution is the safe compatibility
    /// gate available under the net48 xUnit runner.
    /// </summary>
    [Collection(FuseRmroc451CompatibilityCollection.Name)]
    public sealed class FuseRmroc451CompatibilityTests
    {
        [Rmroc451RealAssemblyFact]
        public void Real_plugin_resolves_hosts_and_patches_with_fuse_shim()
        {
            var archivePath = Environment.GetEnvironmentVariable("FUSE_TEST_RMROC451_ZIP");
            var gameDirectory = Environment.GetEnvironmentVariable("FUSE_TEST_GAME_DIR");
            var managedDirectory = Path.Combine(gameDirectory, "Railroader_Data", "Managed");
            var extractionRoot = Path.Combine(
                Path.GetTempPath(),
                "fuse-rmroc451-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractionRoot);

            PluginBase plugin = null;
            ResolveEventHandler gameAssemblyResolver = (_, args) =>
            {
                var requested = new AssemblyName(args.Name).Name;
                var candidate = Path.Combine(managedDirectory, requested + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            FuseLegacySupportAssemblyShim.Initialize();
            AppDomain.CurrentDomain.AssemblyResolve += gameAssemblyResolver;
            try
            {
                ZipFile.ExtractToDirectory(archivePath, extractionRoot);
                var assemblyPath = Directory.GetFiles(
                        extractionRoot,
                        "RMROC451.TweaksAndThings.dll",
                        SearchOption.AllDirectories)
                    .Single();
                var packageFolder = Path.GetDirectoryName(assemblyPath);
                var modsRoot = Directory.GetParent(packageFolder)?.FullName
                               ?? extractionRoot;
                var manifest = new FuseLegacyAssemblyManifest
                {
                    Id = "RMROC451.TweaksAndThings",
                    Name = "RMROC451's Tweaks and Things",
                    Version = "2.1.7",
                    FolderPath = packageFolder,
                    Assemblies = new[] { "RMROC451.TweaksAndThings" }
                };
                var definition = new FuseLegacyModDefinition(manifest);
                var context = new FuseLegacyModdingContext(modsRoot);
                var assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));
                var pluginType = assembly.GetType(
                    "RMROC451.TweaksAndThings.TweaksAndThingsPlugin",
                    throwOnError: true);

                Assert.True(typeof(PluginBase).IsAssignableFrom(pluginType));
                Assert.True(typeof(IUpdateHandler).IsAssignableFrom(pluginType));
                Assert.True(typeof(IModTabHandler).IsAssignableFrom(pluginType));

                plugin = (PluginBase)Activator.CreateInstance(
                    pluginType,
                    context,
                    definition);
                AssertHarmonyTargetsResolve(
                    assembly,
                    "RMROC451TweaksAndThings");
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= gameAssemblyResolver;
                FuseLegacySupportAssemblyShim.Shutdown();
                try
                {
                    Directory.Delete(extractionRoot, recursive: true);
                }
                catch (IOException ex)
                {
                    // Assembly.Load(byte[]) normally leaves the extraction
                    // unlocked. If an old CLR probes a sidecar from this folder,
                    // the operating system will reclaim the temp folder later.
                    System.Diagnostics.Trace.TraceWarning(
                        $"RMROC451 compatibility harness could not remove temporary folder '{extractionRoot}': {ex.Message}");
                }
            }
        }

        private static void AssertHarmonyTargetsResolve(
            Assembly assembly,
            string category)
        {
            var harmony = new Harmony("FUSE.Tests.RMROC451.TargetAudit");
            var processorType = typeof(PatchClassProcessor);
            var getBulkMethods = processorType.GetMethod(
                "GetBulkMethods",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var patchMethodsField = processorType.GetField(
                "patchMethods",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var getOriginalMethod = typeof(Harmony).Assembly
                .GetType("HarmonyLib.PatchTools", throwOnError: false)?
                .GetMethod(
                    "GetOriginalMethod",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(HarmonyMethod) },
                    null);
            Assert.NotNull(getBulkMethods);
            Assert.NotNull(patchMethodsField);
            Assert.NotNull(getOriginalMethod);

            var patchClassCount = 0;
            var resolvedTargetCount = 0;
            foreach (var type in assembly.GetTypes())
            {
                var processor = new PatchClassProcessor(harmony, type);
                if (!string.Equals(
                        processor.Category,
                        category,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                patchClassCount++;
                var bulkMethods = (System.Collections.IEnumerable)
                    getBulkMethods.Invoke(processor, null);
                var bulkCount = 0;
                foreach (var target in bulkMethods)
                {
                    Assert.NotNull(target);
                    bulkCount++;
                    resolvedTargetCount++;
                }
                if (bulkCount > 0)
                {
                    continue;
                }

                var patchMethods = (System.Collections.IEnumerable)
                    patchMethodsField.GetValue(processor);
                foreach (var patch in patchMethods)
                {
                    var info = (HarmonyMethod)patch.GetType()
                        .GetField("info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .GetValue(patch);
                    Assert.NotNull(info.method);
                    Assert.NotNull(getOriginalMethod.Invoke(null, new object[] { info }));
                    resolvedTargetCount++;
                }
            }

            Assert.True(patchClassCount > 0, "No RMROC451 Harmony patch classes were discovered.");
            Assert.True(resolvedTargetCount > 0, "No RMROC451 Harmony targets resolved against the installed game.");
        }
    }

    internal sealed class Rmroc451RealAssemblyFactAttribute : FactAttribute
    {
        public Rmroc451RealAssemblyFactAttribute()
        {
            var archivePath = Environment.GetEnvironmentVariable("FUSE_TEST_RMROC451_ZIP");
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                Skip = "Opt-in real RMROC451 harness: set FUSE_TEST_RMROC451_ZIP to RMROC451.TweaksAndThings.zip.";
                return;
            }

            var gameDirectory = Environment.GetEnvironmentVariable("FUSE_TEST_GAME_DIR");
            if (string.IsNullOrWhiteSpace(gameDirectory) ||
                !File.Exists(Path.Combine(gameDirectory, "Railroader_Data", "Managed", "Serilog.dll")))
            {
                Skip = "Opt-in real RMROC451 harness: set FUSE_TEST_GAME_DIR to a Railroader installation.";
            }
        }
    }
}

#pragma warning restore CS0618
