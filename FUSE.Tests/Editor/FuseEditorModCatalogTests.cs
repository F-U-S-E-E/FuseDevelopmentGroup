using System;
using System.IO;
using FUSE.Editor.Mods;
using Xunit;

namespace FUSE.Tests.Editor
{
    /// <summary>
    /// Coverage for the mod-browser's classification logic. Each test
    /// builds a fake mod folder with the appropriate marker files and
    /// asserts the catalog reads it the way the browser will at
    /// runtime. Pure filesystem — no Unity dependencies, no game state.
    /// </summary>
    public sealed class FuseEditorModCatalogTests : IDisposable
    {
        // Private parent dir, unique to this fixture instance. _modsRoot
        // is nested one level inside it so the parent's only child is
        // "mods" — this keeps the sibling-count assertions in
        // CreateNewMod_rejects_traversal_id_without_writing_outside_root
        // deterministic. (If _modsRoot lived directly under the shared
        // system temp dir, parallel test collections creating their own
        // temp dirs there would race the count.)
        private readonly string _root;
        private readonly string _modsRoot;

        public FuseEditorModCatalogTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "FuseEditorModCatalogTests-" + Guid.NewGuid().ToString("N"));
            _modsRoot = Path.Combine(_root, "mods");
            Directory.CreateDirectory(_modsRoot);
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
            catch
            {
                // Best-effort temp cleanup.
            }

            GC.SuppressFinalize(this);
        }

        [Fact]
        public void EnumerateAll_returns_empty_when_root_missing()
        {
            var result = FuseEditorModCatalog.EnumerateAll(Path.Combine(_modsRoot, "missing"));
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void EnumerateAll_returns_empty_when_root_null_or_empty()
        {
            Assert.Empty(FuseEditorModCatalog.EnumerateAll(null));
            Assert.Empty(FuseEditorModCatalog.EnumerateAll(string.Empty));
        }

        [Fact]
        public void Classify_FuseMod_with_fuse_json_marker()
        {
            var folder = CreateModFolder("my_mod");
            File.WriteAllText(Path.Combine(folder, "my_mod.fuse.json"),
                "{ \"Id\": \"my_mod\", \"Name\": \"My Mod\", \"Author\": \"alex\", \"ModVersion\": \"1.0.0\" }");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.FuseMod, entry.Kind);
            Assert.Equal("my_mod", entry.Id);
            Assert.Equal("My Mod", entry.DisplayName);
            Assert.Equal("alex", entry.Author);
            Assert.Equal("1.0.0", entry.Version);
            Assert.Null(entry.IneligibilityReason);
        }

        [Fact]
        public void Classify_LegacyRailLoader_when_definition_requires_railloader()
        {
            var folder = CreateModFolder("legacy_mod");
            File.WriteAllText(Path.Combine(folder, "Definition.json"),
                "{ \"manifestVersion\": 1, \"id\": \"acme.legacy\", \"name\": \"Legacy Mod\", " +
                "\"version\": \"1.0\", \"requires\": [{ \"id\": \"railloader\", \"notBefore\": \"1.0\" }] }");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.LegacyRailLoader, entry.Kind);
            Assert.Equal("acme.legacy", entry.Id);
            Assert.Equal("Legacy Mod", entry.DisplayName);
            Assert.Equal("1.0", entry.Version);
            Assert.NotNull(entry.IneligibilityReason);
        }

        [Fact]
        public void Classify_Definition_without_railloader_require_falls_through()
        {
            // A Definition.json without a railloader require is some
            // other kind of mod (or a malformed legacy); we don't claim
            // it as LegacyRailLoader.
            var folder = CreateModFolder("not_legacy");
            File.WriteAllText(Path.Combine(folder, "Definition.json"),
                "{ \"id\": \"x\", \"requires\": [{ \"id\": \"something_else\" }] }");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.Unknown, entry.Kind);
        }

        [Fact]
        public void Classify_LegacyMapMod_with_mapmod_yaml()
        {
            var folder = CreateModFolder("amm_patch");
            File.WriteAllText(Path.Combine(folder, "mapmod.yaml"), "name: My Patch\n");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.LegacyMapMod, entry.Kind);
            Assert.NotNull(entry.IneligibilityReason);
        }

        [Fact]
        public void Classify_CodeOnlyMod_with_info_json_but_no_data_markers()
        {
            var folder = CreateModFolder("CodeMod");
            File.WriteAllText(Path.Combine(folder, "Info.json"),
                "{ \"Id\": \"CodeMod\", \"DisplayName\": \"Code Mod\", \"Author\": \"alex\", " +
                "\"AssemblyName\": \"CodeMod.dll\", \"EntryMethod\": \"CodeMod.Plugin.Load\" }");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.CodeOnlyMod, entry.Kind);
            Assert.Equal("Code Mod", entry.DisplayName);
            Assert.Equal("alex", entry.Author);
        }

        [Fact]
        public void Classify_Unknown_when_no_recognised_markers()
        {
            var folder = CreateModFolder("rubbish");
            File.WriteAllText(Path.Combine(folder, "readme.txt"), "hello");

            var entry = FuseEditorModCatalog.Classify(folder);

            Assert.Equal(FuseEditorModKind.Unknown, entry.Kind);
        }

        [Fact]
        public void EnumerateAll_sorts_by_display_name_case_insensitive()
        {
            CreateFuseMod("zebra", "Zebra Mod");
            CreateFuseMod("alpha", "Alpha Mod");
            CreateFuseMod("middle", "Middle Mod");

            var entries = FuseEditorModCatalog.EnumerateAll(_modsRoot);

            Assert.Equal(3, entries.Count);
            Assert.Equal("Alpha Mod", entries[0].DisplayName);
            Assert.Equal("Middle Mod", entries[1].DisplayName);
            Assert.Equal("Zebra Mod", entries[2].DisplayName);
        }

        [Fact]
        public void CreateNewMod_writes_scaffold_files_and_returns_folder_path()
        {
            var path = FuseEditorModCatalog.CreateNewMod(_modsRoot, "test.mod", "Test Mod", "alex");

            Assert.NotNull(path);
            Assert.True(Directory.Exists(path));
            Assert.True(File.Exists(Path.Combine(path, "Info.json")));
            Assert.True(File.Exists(Path.Combine(path, "test.mod.fuse.json")));

            // Re-classifying the new folder should report it as FuseMod.
            var entry = FuseEditorModCatalog.Classify(path);
            Assert.Equal(FuseEditorModKind.FuseMod, entry.Kind);
            Assert.Equal("Test Mod", entry.DisplayName);
            Assert.Equal("alex", entry.Author);
        }

        [Fact]
        public void CreateNewMod_rejects_existing_folder()
        {
            FuseEditorModCatalog.CreateNewMod(_modsRoot, "dupe", "Dupe", "alex");
            var second = FuseEditorModCatalog.CreateNewMod(_modsRoot, "dupe", "Dupe", "alex");

            Assert.Null(second);
        }

        [Fact]
        public void CreateNewMod_rejects_empty_inputs()
        {
            Assert.Null(FuseEditorModCatalog.CreateNewMod(_modsRoot, null, "Name", "a"));
            Assert.Null(FuseEditorModCatalog.CreateNewMod(_modsRoot, "", "Name", "a"));
            Assert.Null(FuseEditorModCatalog.CreateNewMod(_modsRoot, "   ", "Name", "a"));
            Assert.Null(FuseEditorModCatalog.CreateNewMod(null, "id", "Name", "a"));
        }

        [Theory]
        [InlineData("simple", "simple")]
        [InlineData("with spaces", "with-spaces")]
        [InlineData("ALL CAPS", "ALL-CAPS")]
        [InlineData("path/like", "path-like")]
        [InlineData("multi   spaces", "multi-spaces")]
        [InlineData("--leading-trailing--", "leading-trailing")]
        [InlineData("dots.are.fine", "dots.are.fine")]
        [InlineData("under_scores_too", "under_scores_too")]
        [InlineData("@#$%^&*", "")]
        // Path-traversal neutralisation: a bare ".." (or any all-dot
        // run) collapses to empty so callers reject it; separators fold
        // to dashes and edge dots are trimmed, so no surviving segment
        // can escape the mods root via Path.Combine.
        [InlineData("..", "")]
        [InlineData(".", "")]
        [InlineData("...", "")]
        [InlineData("../etc", "etc")]
        [InlineData("..\\..\\evil", "evil")]
        [InlineData(".hidden", "hidden")]
        public void SanitizeId_normalises_to_filesystem_safe(string input, string expected)
        {
            Assert.Equal(expected, FuseEditorModCatalog.SanitizeId(input));
        }

        [Fact]
        public void CreateNewMod_rejects_traversal_id_without_writing_outside_root()
        {
            // SanitizeId folds a bare ".." to empty (rejected at the
            // empty-id gate), and the containment guard is the backstop.
            // Nothing must be created OUTSIDE the mods root, and the
            // sibling count of the root's parent must be unchanged.
            // The parent here is the fixture's private _root (whose only
            // child is "mods"), so the count is unperturbed by parallel
            // test collections and the assertions are deterministic.
            var parent = Directory.GetParent(_modsRoot);
            var before = parent.GetDirectories().Length;

            Assert.Null(FuseEditorModCatalog.CreateNewMod(_modsRoot, "..", "Escape", "alex"));
            Assert.Null(FuseEditorModCatalog.CreateNewMod(_modsRoot, "   ...   ", "Escape", "alex"));

            Assert.Equal(before, parent.GetDirectories().Length);

            // A traversal-flavoured id whose sanitised form is a safe
            // single segment ("../etc" -> "etc") is allowed, but it
            // lands INSIDE the root, never beside it.
            var created = FuseEditorModCatalog.CreateNewMod(_modsRoot, "../etc", "Etc", "alex");
            Assert.NotNull(created);
            Assert.Equal(before, parent.GetDirectories().Length); // still no new sibling of root
            Assert.True(Directory.Exists(Path.Combine(_modsRoot, "etc")));
        }

        // --- helpers ---

        private string CreateModFolder(string name)
        {
            var path = Path.Combine(_modsRoot, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private void CreateFuseMod(string id, string displayName)
        {
            var folder = CreateModFolder(id);
            File.WriteAllText(Path.Combine(folder, $"{id}.fuse.json"),
                "{ \"Id\": \"" + id + "\", \"Name\": \"" + displayName + "\" }");
        }

        // -------------------------------------------------------------
        // EnsureScratchMod
        //
        // Note: FuseModLoader.GetLoadedModsInOrder is empty in xUnit
        // (no Railroader runtime), so EnsureScratchMod's "is it
        // already loaded?" fast path is skipped and we exercise the
        // scaffold-on-disk slow path. Asserting on folder existence
        // (not the return value) keeps these tests deterministic.
        // -------------------------------------------------------------

        [Fact]
        public void EnsureScratchMod_scaffolds_folder_when_absent()
        {
            FuseEditorModCatalog.EnsureScratchMod(_modsRoot);
            var folder = Path.Combine(_modsRoot, FuseEditorModCatalog.ScratchModId);
            Assert.True(Directory.Exists(folder),
                "EnsureScratchMod should have scaffolded the scratch mod folder.");
            Assert.True(File.Exists(Path.Combine(folder, "Info.json")));
            Assert.True(File.Exists(Path.Combine(folder, FuseEditorModCatalog.ScratchModId + ".fuse.json")));
        }

        [Fact]
        public void EnsureScratchMod_is_idempotent()
        {
            FuseEditorModCatalog.EnsureScratchMod(_modsRoot);
            var folder = Path.Combine(_modsRoot, FuseEditorModCatalog.ScratchModId);
            var infoFirstWrite = File.GetLastWriteTimeUtc(Path.Combine(folder, "Info.json"));

            // Sleep so a mtime change would be observable, then call
            // again — should NOT rewrite (folder already exists short-
            // circuits the CreateNewMod path).
            System.Threading.Thread.Sleep(20);
            FuseEditorModCatalog.EnsureScratchMod(_modsRoot);
            var infoSecondWrite = File.GetLastWriteTimeUtc(Path.Combine(folder, "Info.json"));

            Assert.Equal(infoFirstWrite, infoSecondWrite);
        }

        [Fact]
        public void EnsureScratchMod_with_empty_root_returns_null()
        {
            Assert.Null(FuseEditorModCatalog.EnsureScratchMod(null));
            Assert.Null(FuseEditorModCatalog.EnsureScratchMod(string.Empty));
        }
    }
}
