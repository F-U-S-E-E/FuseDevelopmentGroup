using System;
using System.IO;
using FUSE.Editor.Screen.UI;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Coverage for the JSON-persisted user settings store —
    /// defaults, set/get round-trip, clamping, Reset semantics.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorSettingsTests : IDisposable
    {
        private readonly string _tempPath;

        public FuseEditorSettingsTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(),
                "fuse-editor-settings-test-" + Guid.NewGuid().ToString("N") + ".json");
            FuseEditorSettings.SetPathOverride(_tempPath);
        }

        public void Dispose()
        {
            FuseEditorSettings.SetPathOverride(null);
            try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
            FuseEditorSettings.Reset();
        }

        [Fact]
        public void UiScale_default_is_1()
        {
            Assert.Equal(FuseEditorSettings.DefaultUiScale, FuseEditorSettings.UiScale);
        }

        [Fact]
        public void UiScale_round_trips_through_disk()
        {
            FuseEditorSettings.UiScale = 1.5f;
            FuseEditorSettings.Reset();
            // Reset drops the in-memory cache; next read pulls from disk.
            Assert.Equal(1.5f, FuseEditorSettings.UiScale, 4);
        }

        [Fact]
        public void UiScale_clamps_to_lower_bound()
        {
            FuseEditorSettings.UiScale = 0.1f;
            Assert.Equal(FuseEditorSettings.MinUiScale, FuseEditorSettings.UiScale);
        }

        [Fact]
        public void UiScale_clamps_to_upper_bound()
        {
            FuseEditorSettings.UiScale = 99f;
            Assert.Equal(FuseEditorSettings.MaxUiScale, FuseEditorSettings.UiScale);
        }

        [Fact]
        public void Setting_same_value_does_not_rewrite_disk()
        {
            FuseEditorSettings.UiScale = 1.2f;
            Assert.True(File.Exists(_tempPath));
            var firstWriteTime = File.GetLastWriteTimeUtc(_tempPath);

            // Identical value should be a no-op (no Save invocation).
            System.Threading.Thread.Sleep(10); // ensure mtime resolution
            FuseEditorSettings.UiScale = 1.2f;

            Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(_tempPath));
        }

        [Fact]
        public void Reset_drops_in_memory_cache()
        {
            FuseEditorSettings.UiScale = 1.3f;
            Assert.Equal(1.3f, FuseEditorSettings.UiScale, 4);

            FuseEditorSettings.Reset();
            // After Reset, value should still load from disk and match.
            Assert.Equal(1.3f, FuseEditorSettings.UiScale, 4);
        }

        [Fact]
        public void Missing_file_yields_default_value()
        {
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
            FuseEditorSettings.Reset();
            Assert.Equal(FuseEditorSettings.DefaultUiScale, FuseEditorSettings.UiScale);
        }

        [Fact]
        public void Malformed_json_falls_back_to_default()
        {
            File.WriteAllText(_tempPath, "{ this is not valid JSON");
            FuseEditorSettings.Reset();
            Assert.Equal(FuseEditorSettings.DefaultUiScale, FuseEditorSettings.UiScale);
        }
    }
}
