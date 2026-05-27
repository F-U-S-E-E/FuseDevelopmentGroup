using System;
using System.IO;
using FUSE.Editor.Bookmarks;
using UnityEngine;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Locks the bookmark registry's add / rename / remove semantics and
    /// the per-mod JSON round-trip. The Unity-camera-coupled bits
    /// (capturing Camera.main, dispatching to CameraSelector) live in
    /// the in-game harness; this suite focuses on the pure-data layer
    /// that's safe to test from xUnit.
    ///
    /// Registry state is static (mirrors Axiom's ViewManager); we
    /// serialise through <see cref="FuseEditorRegistryTestCollection"/>
    /// and reset between cases.
    /// </summary>
    [Collection(FuseEditorRegistryTestCollection.Name)]
    public sealed class FuseEditorBookmarkRegistryTests : IDisposable
    {
        private readonly string _tempFolder;

        public FuseEditorBookmarkRegistryTests()
        {
            FuseEditorBookmarkRegistry.Reset();
            _tempFolder = Path.Combine(Path.GetTempPath(), "FuseEditorBookmarkRegistryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempFolder);
        }

        public void Dispose()
        {
            FuseEditorBookmarkRegistry.Reset();

            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup — Windows may hold the file
                // briefly after the final flush; the OS reclaims %TEMP%.
            }

            GC.SuppressFinalize(this);
        }

        [Fact]
        public void Default_state_is_empty_with_no_active_bookmark()
        {
            Assert.Empty(FuseEditorBookmarkRegistry.All);
            Assert.Null(FuseEditorBookmarkRegistry.Active);
            Assert.Equal(-1, FuseEditorBookmarkRegistry.ActiveIndex);
            Assert.False(FuseEditorBookmarkRegistry.IsDirty);
        }

        [Fact]
        public void Add_appends_and_marks_dirty()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            var index = FuseEditorBookmarkRegistry.Add(MakeBookmark("Roundhouse", new Vector3(100, 0, 200)));

            Assert.Equal(0, index);
            Assert.True(FuseEditorBookmarkRegistry.IsDirty);
            Assert.Single(FuseEditorBookmarkRegistry.All);
            Assert.Equal("Roundhouse", FuseEditorBookmarkRegistry.All[0].Name);
        }

        [Fact]
        public void Add_with_empty_name_assigns_View_N_default()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            var index = FuseEditorBookmarkRegistry.Add(MakeBookmark(null, Vector3.zero));

            Assert.Equal(0, index);
            Assert.Equal("View 1", FuseEditorBookmarkRegistry.All[0].Name);
        }

        [Fact]
        public void Add_null_returns_minus_one_and_does_not_mark_dirty()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            var index = FuseEditorBookmarkRegistry.Add(null);

            Assert.Equal(-1, index);
            Assert.False(FuseEditorBookmarkRegistry.IsDirty);
            Assert.Empty(FuseEditorBookmarkRegistry.All);
        }

        [Fact]
        public void Rename_changes_name_and_marks_dirty()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("Original", Vector3.zero));
            FuseEditorBookmarkRegistry.SaveIfDirty();

            var renamed = FuseEditorBookmarkRegistry.Rename(0, "Renamed");

            Assert.True(renamed);
            Assert.Equal("Renamed", FuseEditorBookmarkRegistry.All[0].Name);
            Assert.True(FuseEditorBookmarkRegistry.IsDirty);
        }

        [Fact]
        public void Rename_rejects_empty_name_and_out_of_range()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("a", Vector3.zero));
            FuseEditorBookmarkRegistry.SaveIfDirty();

            Assert.False(FuseEditorBookmarkRegistry.Rename(0, ""));
            Assert.False(FuseEditorBookmarkRegistry.Rename(0, "   "));
            Assert.False(FuseEditorBookmarkRegistry.Rename(-1, "x"));
            Assert.False(FuseEditorBookmarkRegistry.Rename(99, "x"));
            Assert.False(FuseEditorBookmarkRegistry.IsDirty);
            Assert.Equal("a", FuseEditorBookmarkRegistry.All[0].Name);
        }

        [Fact]
        public void RemoveAt_shifts_active_index_down_when_removing_earlier_entry()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("a", Vector3.zero));
            FuseEditorBookmarkRegistry.Add(MakeBookmark("b", Vector3.zero));
            FuseEditorBookmarkRegistry.Add(MakeBookmark("c", Vector3.zero));
            FuseEditorBookmarkRegistry.SetActive(2);

            FuseEditorBookmarkRegistry.RemoveAt(0);

            Assert.Equal(2, FuseEditorBookmarkRegistry.All.Count);
            Assert.Equal(1, FuseEditorBookmarkRegistry.ActiveIndex);
            Assert.Equal("c", FuseEditorBookmarkRegistry.Active.Name);
        }

        [Fact]
        public void RemoveAt_clears_active_when_removing_the_active_entry()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("a", Vector3.zero));
            FuseEditorBookmarkRegistry.Add(MakeBookmark("b", Vector3.zero));
            FuseEditorBookmarkRegistry.SetActive(1);

            FuseEditorBookmarkRegistry.RemoveAt(1);

            Assert.Equal(-1, FuseEditorBookmarkRegistry.ActiveIndex);
            Assert.Null(FuseEditorBookmarkRegistry.Active);
        }

        [Fact]
        public void SetActive_clamps_out_of_range_silently()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("a", Vector3.zero));

            FuseEditorBookmarkRegistry.SetActive(99);

            Assert.Equal(-1, FuseEditorBookmarkRegistry.ActiveIndex);
        }

        [Fact]
        public void SaveIfDirty_writes_json_to_mod_folder()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("Yard ladder", new Vector3(1234.5f, 0f, -789.2f)));
            FuseEditorBookmarkRegistry.SetActive(0);

            var wrote = FuseEditorBookmarkRegistry.SaveIfDirty();

            Assert.True(wrote);
            Assert.False(FuseEditorBookmarkRegistry.IsDirty);
            var expectedPath = Path.Combine(_tempFolder, ".fuse-editor", "views.json");
            Assert.True(File.Exists(expectedPath));

            var text = File.ReadAllText(expectedPath);
            Assert.Contains("Yard ladder", text);
            Assert.Contains("\"x\": 1234", text);
        }

        [Fact]
        public void LoadForMod_round_trips_through_disk()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);
            FuseEditorBookmarkRegistry.Add(MakeBookmark("First", new Vector3(1, 2, 3)));
            FuseEditorBookmarkRegistry.Add(MakeBookmark("Second", new Vector3(4, 5, 6)));
            FuseEditorBookmarkRegistry.SetActive(1);
            FuseEditorBookmarkRegistry.SaveIfDirty();

            FuseEditorBookmarkRegistry.Reset();
            Assert.Empty(FuseEditorBookmarkRegistry.All);

            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            Assert.Equal(2, FuseEditorBookmarkRegistry.All.Count);
            Assert.Equal("First", FuseEditorBookmarkRegistry.All[0].Name);
            Assert.Equal("Second", FuseEditorBookmarkRegistry.All[1].Name);
            Assert.Equal(1, FuseEditorBookmarkRegistry.ActiveIndex);
            Assert.Equal(new Vector3(4, 5, 6), FuseEditorBookmarkRegistry.Active.PositionVector);
        }

        [Fact]
        public void LoadForMod_with_missing_file_starts_empty()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            Assert.Empty(FuseEditorBookmarkRegistry.All);
            Assert.Equal(-1, FuseEditorBookmarkRegistry.ActiveIndex);
        }

        [Fact]
        public void LoadForMod_does_not_throw_on_corrupt_file()
        {
            var dir = Path.Combine(_tempFolder, ".fuse-editor");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "views.json"), "{not json");

            // No throw — the registry catches and falls back to empty.
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            Assert.Empty(FuseEditorBookmarkRegistry.All);
        }

        [Fact]
        public void SaveIfDirty_with_no_dirty_state_is_noop()
        {
            FuseEditorBookmarkRegistry.LoadForMod(_tempFolder);

            Assert.False(FuseEditorBookmarkRegistry.SaveIfDirty());
        }

        private static FuseEditorBookmark MakeBookmark(string name, Vector3 position)
        {
            return new FuseEditorBookmark
            {
                Name = name,
                Position = position,
                Rotation = Quaternion.identity,
            };
        }
    }
}
