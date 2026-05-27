using System;
using System.Collections.Generic;
using System.IO;
using FUSE.Infrastructure;
using Newtonsoft.Json;

namespace FUSE.Editor.Bookmarks
{
    /// <summary>
    /// Per-mod store of named camera bookmarks. Modelled after Axiom's
    /// <c>ViewManager</c>: a flat list of <see cref="FuseEditorBookmark"/>
    /// instances persisted next to the mod's authoring data, with a
    /// debounced save so rapid mutations (add + rename + delete in
    /// one second) only hit disk once.
    /// </summary>
    /// <remarks>
    /// File path: <c>&lt;ActiveMod.FolderPath&gt;/.fuse-editor/views.json</c>.
    /// We use a hidden <c>.fuse-editor</c> subfolder so editor-only
    /// state never accidentally ships in the mod zip when an author
    /// shares their pack — they'd see the file in Explorer and
    /// recognise it as local-only.
    ///
    /// Camera control (capturing the live position, teleporting on
    /// activation) is intentionally out of this class — it stays a
    /// pure-data layer testable from xUnit. The UI layer
    /// (<see cref="Screen.FuseEditorScreen"/>) does the
    /// <c>Camera.main</c> capture and the <c>ZoomToPoint</c> dispatch
    /// after consulting this registry.
    /// </remarks>
    internal static class FuseEditorBookmarkRegistry
    {
        private const string BookmarkSubfolder = ".fuse-editor";
        private const string BookmarkFileName = "views.json";
        private const int MaxBookmarks = 32;

        private static readonly List<FuseEditorBookmark> Bookmarks = new List<FuseEditorBookmark>();
        private static string _activePath;
        private static int _activeIndex = -1;
        private static bool _dirty;

        public static IReadOnlyList<FuseEditorBookmark> All => Bookmarks;

        public static int ActiveIndex => _activeIndex;

        public static FuseEditorBookmark Active =>
            _activeIndex >= 0 && _activeIndex < Bookmarks.Count ? Bookmarks[_activeIndex] : null;

        public static bool IsLoadedFor(string modFolderPath)
        {
            return !string.IsNullOrEmpty(_activePath)
                   && string.Equals(_activePath, ResolveBookmarkPath(modFolderPath), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Loads bookmarks from <paramref name="modFolderPath"/>'s hidden
        /// editor folder, replacing whatever was in memory. Safe to call
        /// when the file doesn't exist yet — that just starts with an
        /// empty list.
        /// </summary>
        public static void LoadForMod(string modFolderPath)
        {
            Bookmarks.Clear();
            _activeIndex = -1;
            _dirty = false;

            if (string.IsNullOrEmpty(modFolderPath))
            {
                _activePath = null;
                return;
            }

            _activePath = ResolveBookmarkPath(modFolderPath);

            if (!File.Exists(_activePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_activePath);
                var payload = JsonConvert.DeserializeObject<BookmarkFile>(json);
                if (payload?.Bookmarks != null)
                {
                    foreach (var b in payload.Bookmarks)
                    {
                        if (b != null)
                        {
                            Bookmarks.Add(b);
                        }
                    }
                    if (payload.ActiveIndex >= 0 && payload.ActiveIndex < Bookmarks.Count)
                    {
                        _activeIndex = payload.ActiveIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor bookmarks: failed to load {_activePath}.", ex);
                Bookmarks.Clear();
                _activeIndex = -1;
            }
        }

        /// <summary>
        /// Appends a bookmark if the cap hasn't been hit. Returns the
        /// index of the new entry, or <c>-1</c> if rejected (null
        /// argument or cap reached). Marks the registry dirty.
        /// </summary>
        public static int Add(FuseEditorBookmark bookmark)
        {
            if (bookmark == null || Bookmarks.Count >= MaxBookmarks)
            {
                return -1;
            }

            if (string.IsNullOrWhiteSpace(bookmark.Name))
            {
                bookmark.Name = $"View {Bookmarks.Count + 1}";
            }

            Bookmarks.Add(bookmark);
            _dirty = true;
            return Bookmarks.Count - 1;
        }

        public static bool Rename(int index, string name)
        {
            if (index < 0 || index >= Bookmarks.Count || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            Bookmarks[index].Name = name;
            _dirty = true;
            return true;
        }

        public static bool RemoveAt(int index)
        {
            if (index < 0 || index >= Bookmarks.Count)
            {
                return false;
            }

            Bookmarks.RemoveAt(index);
            if (_activeIndex == index)
            {
                _activeIndex = -1;
            }
            else if (_activeIndex > index)
            {
                _activeIndex--;
            }
            _dirty = true;
            return true;
        }

        public static void SetActive(int index)
        {
            if (index < -1 || index >= Bookmarks.Count)
            {
                return;
            }

            if (_activeIndex == index)
            {
                return;
            }

            _activeIndex = index;
            _dirty = true;
        }

        /// <summary>
        /// Returns whether any pending changes haven't been written to
        /// disk yet. The UI layer can use this to drive a "saving…"
        /// indicator if it wants.
        /// </summary>
        public static bool IsDirty => _dirty;

        /// <summary>
        /// Writes the current state to disk if dirty. Returns whether a
        /// write happened. Caller is responsible for picking the cadence
        /// (per-frame "if dirty save" is fine for normal-size lists).
        /// </summary>
        public static bool SaveIfDirty()
        {
            if (!_dirty || string.IsNullOrEmpty(_activePath))
            {
                return false;
            }

            try
            {
                var dir = Path.GetDirectoryName(_activePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var payload = new BookmarkFile
                {
                    Bookmarks = Bookmarks.ToArray(),
                    ActiveIndex = _activeIndex,
                };
                var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(_activePath, json);
                _dirty = false;
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE editor bookmarks: failed to save {_activePath}.", ex);
                return false;
            }
        }

        public static void Reset()
        {
            Bookmarks.Clear();
            _activeIndex = -1;
            _activePath = null;
            _dirty = false;
        }

        /// <summary>
        /// Test-only override: aim the registry at <paramref name="path"/>
        /// without going through a real mod folder. Production code
        /// should use <see cref="LoadForMod"/>.
        /// </summary>
        internal static void OverridePathForTest(string path)
        {
            _activePath = path;
            _dirty = false;
        }

        private static string ResolveBookmarkPath(string modFolderPath)
        {
            if (string.IsNullOrEmpty(modFolderPath))
            {
                return null;
            }

            return Path.Combine(modFolderPath, BookmarkSubfolder, BookmarkFileName);
        }

        /// <summary>JSON envelope written to disk.</summary>
        private sealed class BookmarkFile
        {
            [JsonProperty("bookmarks")]
            public FuseEditorBookmark[] Bookmarks { get; set; }

            [JsonProperty("activeIndex")]
            public int ActiveIndex { get; set; } = -1;
        }
    }
}
