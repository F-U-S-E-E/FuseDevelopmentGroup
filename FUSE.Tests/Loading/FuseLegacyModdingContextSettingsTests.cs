using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FUSE.Loading;
using Xunit;

namespace FUSE.Tests.Loading
{
    public sealed class FuseLegacyModdingContextSettingsTests : IDisposable
    {
        private readonly string _modsRoot;

        public FuseLegacyModdingContextSettingsTests()
        {
            _modsRoot = Path.Combine(
                Path.GetTempPath(),
                "FuseLegacySettingsTests_" + Guid.NewGuid().ToString("N"),
                "Mods");
            Directory.CreateDirectory(_modsRoot);
        }

        public void Dispose()
        {
            try
            {
                var testRoot = Directory.GetParent(_modsRoot)?.FullName;
                if (!string.IsNullOrEmpty(testRoot) && Directory.Exists(testRoot))
                {
                    Directory.Delete(testRoot, recursive: true);
                }
            }
            catch (Exception cleanupException)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"Could not clean up legacy-settings test directory '{_modsRoot}': {cleanupException}");
            }
        }

        [Fact]
        public void Settings_path_matches_Railloader_layout_and_sanitization()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);

            var actual = context.GetSettingsFilePath("..\\Outside/Mod:Name?*");

            var expected = Path.GetFullPath(Path.Combine(
                _modsRoot,
                "Railloader",
                "ModSettings",
                ".._Outside_Mod_Name__.json"));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Save_and_load_round_trip_compact_Railloader_json()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);
            var settings = new LegacySettings
            {
                Enabled = true,
                Distance = 123,
                Label = "switch"
            };

            context.SaveSettingsData("Author.Mod", settings);

            var path = context.GetSettingsFilePath("Author.Mod");
            Assert.Equal(
                "{\"Enabled\":true,\"Distance\":123,\"Label\":\"switch\"}",
                File.ReadAllText(path));
            AssertLegacySettings(settings, context.LoadSettingsData<LegacySettings>("Author.Mod"));
        }

        [Fact]
        public void Repeated_save_replaces_complete_file_without_leaving_temporary_files()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);
            context.SaveSettingsData("Author.Mod", new LegacySettings { Distance = 1 });
            context.SaveSettingsData("Author.Mod", new LegacySettings { Distance = 2 });

            var loaded = context.LoadSettingsData<LegacySettings>("Author.Mod");
            Assert.Equal(2, loaded.Distance);

            var settingsDirectory = Path.GetDirectoryName(context.GetSettingsFilePath("Author.Mod"));
            Assert.Empty(Directory.GetFiles(settingsDirectory, "*.tmp"));
        }

        [Fact]
        public async Task Concurrent_first_saves_complete_and_leave_one_valid_settings_file()
        {
            const int WriterCount = 16;
            var context = new FuseLegacyModdingContext(_modsRoot);
            var writers = new Task[WriterCount];
            var start = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            for (var index = 0; index < writers.Length; index++)
            {
                var distance = index;
                writers[index] = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    context.SaveSettingsData(
                        "Concurrent.Mod",
                        new LegacySettings { Distance = distance });
                });
            }

            start.SetResult(null);
            await Task.WhenAll(writers);

            var loaded = context.LoadSettingsData<LegacySettings>("Concurrent.Mod");
            Assert.NotNull(loaded);
            Assert.InRange(loaded.Distance, 0, WriterCount - 1);

            var settingsDirectory = Path.GetDirectoryName(context.GetSettingsFilePath("Concurrent.Mod"));
            Assert.Empty(Directory.GetFiles(settingsDirectory, "*.tmp"));
        }

        [Fact]
        public void Missing_or_malformed_settings_return_null()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);
            Assert.Null(context.LoadSettingsData<LegacySettings>("Missing.Mod"));

            var path = context.GetSettingsFilePath("Broken.Mod");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ this is not json }");

            Assert.Null(context.LoadSettingsData<LegacySettings>("Broken.Mod"));
        }

        [Fact]
        public void Serializer_does_not_emit_or_honor_type_metadata()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);
            context.SaveSettingsData("Safe.Mod", new PolymorphicSettings
            {
                Value = new LegacySettings { Distance = 42 }
            });

            var path = context.GetSettingsFilePath("Safe.Mod");
            Assert.DoesNotContain("\"$type\"", File.ReadAllText(path));

            const string JsonWithTypeMetadata =
                "{\"Value\":{\"$type\":\"System.Version, mscorlib\",\"Major\":9}}";
            File.WriteAllText(path, JsonWithTypeMetadata);
            var loaded = context.LoadSettingsData<PolymorphicSettings>("Safe.Mod");

            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Value);
            Assert.Equal("Newtonsoft.Json.Linq.JObject", loaded.Value.GetType().FullName);
        }

        [Fact]
        public void Real_Railloader_interface_bridge_uses_the_same_settings_store()
        {
            var context = new FuseLegacyModdingContext(_modsRoot);
            var invokeBridge = typeof(FuseLegacyAssemblyHost).GetMethod(
                "InvokeRealModdingContext",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(invokeBridge);

            var saveMethod = typeof(IRealModdingContextContract)
                .GetMethod(nameof(IRealModdingContextContract.SaveSettingsData))
                .MakeGenericMethod(typeof(LegacySettings));
            var expected = new LegacySettings { Enabled = true, Distance = 77, Label = "bridge" };
            invokeBridge.Invoke(
                null,
                new object[] { context, saveMethod, new object[] { "Bridge.Mod", expected } });

            var loadMethod = typeof(IRealModdingContextContract)
                .GetMethod(nameof(IRealModdingContextContract.LoadSettingsData))
                .MakeGenericMethod(typeof(LegacySettings));
            var actual = (LegacySettings)invokeBridge.Invoke(
                null,
                new object[] { context, loadMethod, new object[] { "Bridge.Mod" } });

            AssertLegacySettings(expected, actual);
        }

        private static void AssertLegacySettings(LegacySettings expected, LegacySettings actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.Distance, actual.Distance);
            Assert.Equal(expected.Label, actual.Label);
        }

        private interface IRealModdingContextContract
        {
            T LoadSettingsData<T>(string settingsIdentifier) where T : class;
            void SaveSettingsData<T>(string settingsIdentifier, T settings) where T : class;
        }

        public sealed class LegacySettings
        {
            public bool Enabled { get; set; }
            public int Distance { get; set; }
            public string Label { get; set; }
        }

        public sealed class PolymorphicSettings
        {
            public object Value { get; set; }
        }
    }
}
