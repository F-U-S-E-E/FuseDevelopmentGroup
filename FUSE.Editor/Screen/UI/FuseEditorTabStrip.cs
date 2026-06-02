using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Screen.UI
{
    /// <summary>
    /// Reusable tab-strip control used by both side panels. A strip
    /// owns a list of <see cref="Tab"/>s and a current selection;
    /// draw renders a tab bar on top with the active tab's content
    /// callback below.
    /// </summary>
    /// <remarks>
    /// The strip is a per-instance struct stored on the editor
    /// screen rather than a static registry — there are exactly two
    /// strips at any given time (left and right panel) and they
    /// don't need cross-strip coordination, so per-instance state
    /// keeps the API simple and avoids inventing identifiers for
    /// each strip.
    /// </remarks>
    internal sealed class FuseEditorTabStrip
    {
        public sealed class Tab
        {
            public Tab(string id, string labelKey, FuseEditorIconKind? iconKind,
                       Action<Rect> drawContent,
                       Func<bool> isAvailable = null,
                       string unavailableReasonKey = null)
            {
                Id = id;
                LabelKey = labelKey;
                IconKind = iconKind;
                DrawContent = drawContent;
                IsAvailable = isAvailable ?? AlwaysAvailable;
                UnavailableReasonKey = unavailableReasonKey;
            }

            public string Id { get; }
            public string LabelKey { get; }
            public FuseEditorIconKind? IconKind { get; }
            public Action<Rect> DrawContent { get; }
            public Func<bool> IsAvailable { get; }
            public string UnavailableReasonKey { get; }

            private static readonly Func<bool> AlwaysAvailable = () => true;
        }

        private readonly List<Tab> _tabs = new List<Tab>();
        private string _activeTabId;

        public FuseEditorTabStrip(params Tab[] tabs)
        {
            if (tabs == null) return;
            foreach (var tab in tabs)
            {
                if (tab != null) _tabs.Add(tab);
            }
            // Default to the first available tab so a strip never
            // renders with nothing selected.
            foreach (var tab in _tabs)
            {
                if (tab.IsAvailable())
                {
                    _activeTabId = tab.Id;
                    break;
                }
            }
        }

        public string ActiveTabId => _activeTabId;
        public IReadOnlyList<Tab> Tabs => _tabs;

        /// <summary>
        /// Switches the active tab to <paramref name="tabId"/> if
        /// present and available; no-ops otherwise. External callers
        /// (e.g. selecting an entity → auto-switch to Properties tab)
        /// drive selection through this.
        /// </summary>
        public void SetActive(string tabId)
        {
            foreach (var tab in _tabs)
            {
                if (tab.Id == tabId && tab.IsAvailable())
                {
                    _activeTabId = tabId;
                    return;
                }
            }
        }

        /// <summary>
        /// Paints the strip into <paramref name="rect"/>. The first
        /// <see cref="FuseEditorTheme.Metrics.TabStripHeight"/> pixels
        /// host the tab bar; the remainder hosts the active tab's
        /// content.
        /// </summary>
        public void Draw(Rect rect)
        {
            var barHeight = FuseEditorTheme.Metrics.TabStripHeight;
            var barRect = new Rect(rect.x, rect.y, rect.width, barHeight);
            var contentRect = new Rect(rect.x, rect.y + barHeight,
                                       rect.width, Mathf.Max(0f, rect.height - barHeight));

            DrawTabBar(barRect);
            FuseEditorTheme.DrawHorizontalDivider(new Rect(rect.x, rect.y + barHeight - 1, rect.width, 1));

            var activeTab = FindActiveTab();
            if (activeTab != null && contentRect.height > 0)
            {
                activeTab.DrawContent?.Invoke(contentRect);
            }
        }

        private Tab FindActiveTab()
        {
            foreach (var tab in _tabs)
            {
                if (tab.Id == _activeTabId) return tab;
            }
            return null;
        }

        private void DrawTabBar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, FuseEditorTheme.TabBar);

            // Equal-width tabs across the bar. Future enhancement
            // could measure label width and pack proportionally, but
            // EDEN's tab strips use uniform widths and our two-tab
            // case looks balanced this way.
            if (_tabs.Count == 0) return;

            var tabWidth = rect.width / _tabs.Count;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                var tabRect = new Rect(rect.x + tabWidth * i, rect.y, tabWidth, rect.height);
                DrawTab(tabRect, tab);
            }
        }

        private void DrawTab(Rect rect, Tab tab)
        {
            var label = FuseEditorUiHelper.TranslateLabel(tab.LabelKey);
            var content = tab.IconKind.HasValue
                ? new GUIContent($"{FuseEditorIcons.Get(tab.IconKind.Value).GlyphFallback}  {label.Title}", label.Description)
                : new GUIContent(label.Title, label.Description);

            var available = tab.IsAvailable();
            var isActive = tab.Id == _activeTabId && available;
            var style = isActive ? FuseEditorTheme.TabActive : FuseEditorTheme.Tab;

            if (!available)
            {
                var prev = GUI.enabled;
                GUI.enabled = false;
                var reason = string.IsNullOrEmpty(tab.UnavailableReasonKey)
                    ? null
                    : FuseEditorUiHelper.TranslateLabel(tab.UnavailableReasonKey).Title;
                GUI.Button(rect, new GUIContent(content.text, reason ?? content.tooltip), style);
                GUI.enabled = prev;
                return;
            }

            if (GUI.Button(rect, content, style))
            {
                _activeTabId = tab.Id;
            }
        }
    }
}
