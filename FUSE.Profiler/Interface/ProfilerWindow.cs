using System;
using System.Collections.Generic;
using FUSE.Profiler.Engine;
using FUSE.Profiler.Entries;
using FUSE.Profiler.Instrumentation;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Profiler.Interface
{
    /// <summary>
    /// The profiler window: category tabs on the left, the sorted results
    /// table in the middle, a per-selection graph at the bottom, and the
    /// search/custom tools on the Custom tab.
    ///
    /// IMGUI discipline: everything the layout depends on (rows, entry
    /// states, mod lists) is snapshotted ONCE per frame on the Layout event
    /// and rendered from that snapshot — background patching tasks mutate
    /// entry state at arbitrary times, and a control-count change between
    /// the Layout and Repaint halves of one frame throws GUILayout's
    /// "Getting control N's position" ArgumentException.
    /// </summary>
    internal static class ProfilerWindow
    {
        private const int WindowId = 0x46555345; // 'FUSE'
        private const float TabColumnWidth = 210f;
        private const float GraphHeight = 150f;
        private const float RowHeight = 22f;

        private sealed class EntryView
        {
            internal ProfilerEntry Entry;
            internal string Display;
            internal bool Active;
            internal string FailedNote;
        }

        private sealed class ModView
        {
            internal UnityModManager.ModEntry Mod;
            internal string Display;
        }

        private static Rect _windowRect = new Rect(80f, 80f, 1040f, 680f);
        private static bool _resizing;
        private static ProfilerCategory _selectedCategory = ProfilerCategory.Culling;
        private static Vector2 _rowScroll;
        private static Vector2 _entryScroll;
        private static string _selectedRowKey;
        private static string _searchInput = "";
        private static string _searchNotice = "";
        private static readonly double[] GraphBuffer = new double[400];

        // Per-frame snapshot (rebuilt on Layout, rendered on every event).
        private static List<ProbeRow> _rows = new List<ProbeRow>();
        private static List<ProbeRow> _visibleRows = new List<ProbeRow>();
        private static List<EntryView> _entryViews = new List<EntryView>();
        private static List<ModView> _modViews = new List<ModView>();
        private static List<string> _modRollupLines = new List<string>();
        private static float _rowsRefreshedAt = -10f;

        internal static Rect CurrentWindowRect => _windowRect;

        internal static void Draw()
        {
            if (Event.current.type == EventType.Layout)
            {
                RefreshFrameState();
            }

            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, GUIContent.none);
        }

        private static void RefreshFrameState()
        {
            // Rows at most a few times a second; the cheap view models every
            // frame (they must track toggles immediately).
            if (Time.unscaledTime - _rowsRefreshedAt > 0.25f)
            {
                _rows = ProfilerSession.CopyRows();
                _rowsRefreshedAt = Time.unscaledTime;
            }

            _visibleRows = FilterRowsForCategory(_selectedCategory);

            _entryViews.Clear();
            var entryCategory = _selectedCategory;
            foreach (var entry in EntryCatalog.ForCategory(entryCategory))
            {
                int failed;
                lock (entry.FailedTargets)
                {
                    failed = entry.FailedTargets.Count;
                }

                var suffix = entry.PatchInFlight
                    ? " (patching…)"
                    : entry.Patched
                        ? $" ({entry.InstrumentedCount})"
                        : "";
                _entryViews.Add(new EntryView
                {
                    Entry = entry,
                    Display = entry.Label + suffix,
                    Active = entry.Active,
                    FailedNote = failed > 0 ? $"  {failed} target(s) failed — see log" : null,
                });
            }

            if (_selectedCategory == ProfilerCategory.Custom)
            {
                _modViews.Clear();
                var mods = UnityModManager.modEntries;
                if (mods != null)
                {
                    for (var i = 0; i < mods.Count; i++)
                    {
                        var mod = mods[i];
                        if (mod?.Assembly == null || mod.Info == null)
                        {
                            continue;
                        }

                        if (!MethodResolver.IsProfilableAssemblyName(mod.Assembly.GetName().Name))
                        {
                            continue;
                        }

                        _modViews.Add(new ModView { Mod = mod, Display = mod.Info.DisplayName ?? mod.Info.Id });
                    }
                }
            }

            if (_selectedCategory == ProfilerCategory.Mods)
            {
                _modRollupLines = BuildModRollupLines();
            }
        }

        private static void DrawWindow(int id)
        {
            var area = new Rect(0f, 0f, _windowRect.width, _windowRect.height);
            ImguiKit.FillRect(area, ImguiKit.SolidDark);

            DrawTitleRow();

            var contentTop = 30f;
            var graphTop = _windowRect.height - GraphHeight - 8f;
            var tabsRect = new Rect(6f, contentTop, TabColumnWidth, graphTop - contentTop - 4f);
            var rowsRect = new Rect(TabColumnWidth + 12f, contentTop, _windowRect.width - TabColumnWidth - 18f, graphTop - contentTop - 4f);
            var graphRect = new Rect(6f, graphTop, _windowRect.width - 12f, GraphHeight);

            DrawTabColumn(tabsRect);
            DrawRowsPanel(rowsRect);
            DrawGraphPanel(graphRect);
            HandleResize();

            // Title strip drags the window; keep the strip clear of buttons.
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 220f, 26f));
        }

        private static void DrawTitleRow()
        {
            GUI.Label(new Rect(10f, 4f, 300f, 22f), "FUSE Profiler", ImguiKit.Header);

            var x = _windowRect.width - 214f;
            var paused = ProfilerSession.Paused;
            if (GUI.Button(new Rect(x, 4f, 70f, 22f), paused ? "Resume" : "Pause"))
            {
                ProfilerSession.Paused = !paused;
            }

            if (GUI.Button(new Rect(x + 74f, 4f, 76f, 22f), "Sort: " + ProfilerSession.SortMode))
            {
                var next = (int)ProfilerSession.SortMode + 1;
                if (next > (int)ProbeSortMode.Name)
                {
                    next = 0;
                }

                ProfilerSession.SortMode = (ProbeSortMode)next;
            }

            if (GUI.Button(new Rect(x + 154f, 4f, 54f, 22f), "Close"))
            {
                ProfilerRuntime.CloseWindow();
            }
        }

        private static void DrawTabColumn(Rect rect)
        {
            ImguiKit.FillRect(rect, ImguiKit.SolidPanel);
            GUILayout.BeginArea(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f));

            foreach (ProfilerCategory category in Enum.GetValues(typeof(ProfilerCategory)))
            {
                var isSelected = category == _selectedCategory;
                if (GUILayout.Toggle(isSelected, CategoryLabel(category), GUI.skin.button) && !isSelected)
                {
                    _selectedCategory = category;
                    _selectedRowKey = null;
                }
            }

            GUILayout.Space(8f);
            _entryScroll = GUILayout.BeginScrollView(_entryScroll);
            switch (_selectedCategory)
            {
                case ProfilerCategory.Custom:
                    DrawCustomTools();
                    break;
                case ProfilerCategory.Mods:
                    DrawModsTools();
                    break;
                default:
                    DrawEntryToggles();
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawEntryToggles()
        {
            for (var i = 0; i < _entryViews.Count; i++)
            {
                var view = _entryViews[i];
                var next = GUILayout.Toggle(view.Active, view.Display);
                if (next != view.Active)
                {
                    view.Active = next;
                    EntryCatalog.SetActive(view.Entry, next);
                }

                if (view.FailedNote != null)
                {
                    GUILayout.Label(view.FailedNote, ImguiKit.Cell);
                }
            }
        }

        private static void DrawCustomTools()
        {
            GUILayout.Label("Method spec:", ImguiKit.Cell);
            _searchInput = GUILayout.TextField(_searchInput ?? "");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Method"))
            {
                RunSearch(coroutine: false, asType: false);
            }

            if (GUILayout.Button("Coroutine"))
            {
                RunSearch(coroutine: true, asType: false);
            }

            if (GUILayout.Button("Type"))
            {
                RunSearch(coroutine: false, asType: true);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("Namespace.Type:Method or Namespace.Type", ImguiKit.Cell);
            if (!string.IsNullOrEmpty(_searchNotice))
            {
                GUILayout.Label(_searchNotice, ImguiKit.Cell);
            }

            GUILayout.Space(6f);
            DrawEntryToggles();

            GUILayout.Space(6f);
            GUILayout.Label("Profile a mod's assembly:", ImguiKit.Cell);
            for (var i = 0; i < _modViews.Count; i++)
            {
                if (GUILayout.Button(_modViews[i].Display))
                {
                    RuntimeEntryFactory.CreateModAssemblyEntry(_modViews[i].Mod);
                    _searchNotice = "Instrumenting " + _modViews[i].Display + "…";
                }
            }
        }

        private static void DrawModsTools()
        {
            GUILayout.Label("Attribute Harmony patch cost to mods:", ImguiKit.Cell);
            if (GUILayout.Button("Instrument all mods' patches"))
            {
                RuntimeEntryFactory.CreateForeignPatchesEntry();
            }

            GUILayout.Space(4f);
            DrawEntryToggles();

            GUILayout.Space(8f);
            GUILayout.Label("Per-mod totals (avg ms per frame):", ImguiKit.Cell);
            for (var i = 0; i < _modRollupLines.Count; i++)
            {
                GUILayout.Label(_modRollupLines[i], ImguiKit.Cell);
            }
        }

        private static void RunSearch(bool coroutine, bool asType)
        {
            var input = (_searchInput ?? "").Trim();
            if (input.Length == 0)
            {
                _searchNotice = "Enter a spec first.";
                return;
            }

            if (asType)
            {
                RuntimeEntryFactory.CreateTypeEntry(input);
                _searchNotice = "Instrumenting type " + input + "…";
                return;
            }

            if (!input.Contains(":"))
            {
                _searchNotice = "Method specs need the form Namespace.Type:Method.";
                return;
            }

            RuntimeEntryFactory.CreateMethodEntry(input, coroutine);
            _searchNotice = "Instrumenting " + input + "…";
        }

        private static void DrawRowsPanel(Rect rect)
        {
            ImguiKit.FillRect(rect, ImguiKit.SolidPanel);

            var header = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 20f);
            var labelWidth = header.width - 90f - 90f - 70f - 70f - 30f;
            GUI.Label(new Rect(header.x + 30f, header.y, labelWidth, 20f), "Name", ImguiKit.Header);
            GUI.Label(new Rect(header.x + 30f + labelWidth, header.y, 90f, 20f), "Avg ms", ImguiKit.Header);
            GUI.Label(new Rect(header.x + 30f + labelWidth + 90f, header.y, 90f, 20f), "Max ms", ImguiKit.Header);
            GUI.Label(new Rect(header.x + 30f + labelWidth + 180f, header.y, 70f, 20f), "Calls", ImguiKit.Header);
            GUI.Label(new Rect(header.x + 30f + labelWidth + 250f, header.y, 70f, 20f), "%", ImguiKit.Header);

            var visible = _visibleRows;
            var viewRect = new Rect(0f, 0f, rect.width - 24f, visible.Count * RowHeight);
            var scrollArea = new Rect(rect.x + 2f, rect.y + 24f, rect.width - 4f, rect.height - 28f);
            _rowScroll = GUI.BeginScrollView(scrollArea, _rowScroll, viewRect);

            // Manual virtualization: only draw rows inside the view window.
            // Fixed-position GUI.* controls, so a mid-frame count change
            // cannot desync IMGUI layout state.
            var firstVisible = Mathf.Max(0, (int)(_rowScroll.y / RowHeight) - 1);
            var lastVisible = Mathf.Min(visible.Count - 1, (int)((_rowScroll.y + scrollArea.height) / RowHeight) + 1);
            for (var i = firstVisible; i <= lastVisible; i++)
            {
                DrawRow(visible[i], i, viewRect.width, labelWidth);
            }

            GUI.EndScrollView();
        }

        private static void DrawRow(ProbeRow row, int index, float width, float labelWidth)
        {
            var y = index * RowHeight;
            var rowRect = new Rect(0f, y, width, RowHeight);
            if (row.Key == _selectedRowKey)
            {
                ImguiKit.FillRect(rowRect, ImguiKit.SolidAccent);
            }
            else if (index % 2 == 0)
            {
                ImguiKit.FillRect(rowRect, ImguiKit.SolidDark);
            }

            var pinned = ProfilerSession.IsPinned(row.Key);
            if (GUI.Button(new Rect(2f, y, 24f, RowHeight), pinned ? "★" : "☆", ImguiKit.Cell))
            {
                ProfilerSession.TogglePinned(row.Key);
            }

            if (GUI.Button(new Rect(30f, y, labelWidth, RowHeight), row.Label, ImguiKit.RowButton))
            {
                _selectedRowKey = row.Key;
            }

            GUI.Label(new Rect(30f + labelWidth, y, 84f, RowHeight), row.AverageMs.ToString("0.000"), ImguiKit.CellRight);
            GUI.Label(new Rect(30f + labelWidth + 90f, y, 84f, RowHeight), row.MaxMs.ToString("0.000"), ImguiKit.CellRight);
            GUI.Label(new Rect(30f + labelWidth + 180f, y, 64f, RowHeight), row.Calls.ToString(), ImguiKit.CellRight);
            GUI.Label(
                new Rect(30f + labelWidth + 250f, y, 64f, RowHeight),
                row.PercentOfFrame >= 0d ? row.PercentOfFrame.ToString("0.0") : "—",
                ImguiKit.CellRight);
        }

        private static void DrawGraphPanel(Rect rect)
        {
            ImguiKit.FillRect(rect, ImguiKit.SolidPanel);
            var inner = new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, rect.height - 28f);

            if (_selectedRowKey == null || !ProbeRegistry.TryGet(_selectedRowKey, out var probe))
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 20f), "Select a row to graph its per-cycle time.", ImguiKit.Cell);
                return;
            }

            var count = probe.CopyRecentInto(GraphBuffer, GraphBuffer.Length);
            double max = 0d;
            for (var i = 0; i < count; i++)
            {
                if (GraphBuffer[i] > max)
                {
                    max = GraphBuffer[i];
                }
            }

            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 20f),
                $"{probe.Label} — last {count} cycles, peak {max:0.000} ms",
                ImguiKit.Cell);

            if (count >= 2 && max > 0d)
            {
                ImguiKit.DrawHorizontalRule(inner, 1f, ImguiKit.GraphMaxColor);
                ImguiKit.DrawPolyline(inner, GraphBuffer, count, max, ImguiKit.GraphColor);
            }
        }

        private static void HandleResize()
        {
            var handle = new Rect(_windowRect.width - 18f, _windowRect.height - 18f, 18f, 18f);
            GUI.Label(handle, "◢");
            var e = Event.current;

            // rawType sees the MouseUp even when it happens outside the
            // window/control, so a released drag can never stick.
            if (_resizing && e.rawType == EventType.MouseUp)
            {
                _resizing = false;
                GUIUtility.hotControl = 0;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition))
            {
                _resizing = true;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                e.Use();
            }
            else if (_resizing && e.type == EventType.MouseDrag)
            {
                _windowRect.width = Mathf.Max(720f, _windowRect.width + e.delta.x);
                _windowRect.height = Mathf.Max(420f, _windowRect.height + e.delta.y);
                e.Use();
            }
        }

        private static List<ProbeRow> FilterRowsForCategory(ProfilerCategory category)
        {
            var entries = EntryCatalog.ForCategory(category);
            var result = new List<ProbeRow>();
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Key == ProfilerSession.FrameProbeKey)
                {
                    result.Add(row);
                    continue;
                }

                if (category == ProfilerCategory.Physics && row.Key == SimTickDriver.StepProbeKey)
                {
                    result.Add(row);
                    continue;
                }

                for (var j = 0; j < entries.Count; j++)
                {
                    if (row.Key.StartsWith(entries[j].Id + "|", StringComparison.Ordinal))
                    {
                        result.Add(row);
                        break;
                    }
                }
            }

            return result;
        }

        private static List<string> BuildModRollupLines()
        {
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.GroupKey == null)
                {
                    continue;
                }

                totals.TryGetValue(row.GroupKey, out var sum);
                totals[row.GroupKey] = sum + row.AverageMs;
            }

            var lines = new List<string>();
            if (totals.Count == 0)
            {
                lines.Add("(no grouped probes yet)");
                return lines;
            }

            var ordered = new List<KeyValuePair<string, double>>(totals);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (var i = 0; i < ordered.Count; i++)
            {
                lines.Add($"{ordered[i].Key}: {ordered[i].Value:0.000} ms");
            }

            return lines;
        }

        private static string CategoryLabel(ProfilerCategory category)
        {
            switch (category)
            {
                case ProfilerCategory.UiEvents: return "UI / Events";
                case ProfilerCategory.KeyValue: return "Key-Value";
                default: return category.ToString();
            }
        }
    }
}
