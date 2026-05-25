using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.API;
using FUSE.Cache;
using FUSE.Data;
using FUSE.Infrastructure;
using FUSE.Lifecycle;
using FUSE.Loading;
using FUSE.Migrations;
using FUSE.Registry;
using Model;
using Model.Ops;
using Newtonsoft.Json.Linq;
using Railloader;
using TMPro;
using Track;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FUSE.Interface
{
    internal sealed class FuseHealthUi : MonoBehaviour
    {
        private const string WindowIdentifier = "FUSE.Health";
        private static GameObject _host;
        private static Sprite _iconSprite;

        private Button _button;
        private Window _window;
        private UIPanel _panel;
        private Page _activePage = Page.Health;
        private Page _lastBuiltPage = Page.Health;
        private string _lastAction = "No runtime action has been run from this page.";
        private float _fpsElapsed;
        private int _fpsFrames;
        private float _fpsAverage;
        private float _frameMilliseconds;
        private long _managedMemoryBytes;
        private long _unityAllocatedBytes;
        private long _unityReservedBytes;
        private string _advancedSearchTerm = string.Empty;
        private string _inspectorSearchTerm = string.Empty;
        private string _inspectorSelectedSignature = string.Empty;
        private string _selectedPackageId = string.Empty;
        private string _selectedLegacyModSignature = string.Empty;
        // Tracks which legacy IModTabHandler plugin instances currently have an
        // "open" tab in our settings UI. Key is "{packageId}|{pluginTypeFullName}";
        // value is the plugin reference held until we call ModTabDidClose on it.
        // We use this to honour the Railloader contract where DidOpen runs every
        // rebuild while a tab is visible, and DidClose runs once when the user
        // navigates away — exactly so plugins like NotEnoughRosters can persist
        // their state on close.
        private readonly Dictionary<string, IModTabHandler> _openTabHandlers =
            new Dictionary<string, IModTabHandler>(StringComparer.OrdinalIgnoreCase);

        private Vector2Int DefaultSize => new Vector2Int(740, 660);
        private Vector2Int MaxSize => new Vector2Int(Screen.width, Screen.height);
        private Window.Sizing DefaultSizing => Window.Sizing.Resizable(DefaultSize, MaxSize);
        private Window.Position DefaultPosition => Window.Position.UpperLeft;

        public static void Ensure()
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Health UI");
            DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<FuseHealthUi>();
            FuseLog.Info("FUSE health UI initialized.");
        }

        public static void Shutdown()
        {
            if (_host != null)
            {
                Destroy(_host);
                _host = null;
            }

            _iconSprite = null;
        }

        private void Start()
        {
            TryInstallHudButton();
        }

        private void Update()
        {
            UpdatePerformanceCounters();

            if (_button == null)
            {
                TryInstallHudButton();
            }
        }

        private void UpdatePerformanceCounters()
        {
            var delta = Time.unscaledDeltaTime;
            if (delta <= 0f)
            {
                return;
            }

            _fpsElapsed += delta;
            _fpsFrames++;
            if (_fpsElapsed < 0.5f)
            {
                return;
            }

            _fpsAverage = _fpsFrames / _fpsElapsed;
            _frameMilliseconds = 1000f / Mathf.Max(_fpsAverage, 0.01f);
            _fpsElapsed = 0f;
            _fpsFrames = 0;
            _managedMemoryBytes = GC.GetTotalMemory(false);
            _unityAllocatedBytes = ReadProfilerMetric(Profiler.GetTotalAllocatedMemoryLong);
            _unityReservedBytes = ReadProfilerMetric(Profiler.GetTotalReservedMemoryLong);
        }

        private void OnDestroy()
        {
            // Notify any open legacy plugin tabs before tearing the panel down so
            // they get a chance to persist state (NotEnoughRosters writes to
            // trains.json from ModTabDidClose, for example).
            CloseAllOpenTabHandlers("FUSE health UI destroyed");

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            if (_window != null && _window.gameObject != null)
            {
                Destroy(_window.gameObject);
                _window = null;
            }

            if (_button != null && _button.gameObject != null)
            {
                Destroy(_button.gameObject);
                _button = null;
            }
        }

        private void TryInstallHudButton()
        {
            try
            {
                var topRightArea = FindObjectOfType<TopRightArea>();
                var strip = topRightArea == null ? null : topRightArea.transform.Find("Strip");
                if (strip == null)
                {
                    return;
                }

                var existing = strip.Find("FUSEHealthButton");
                var buttonObject = existing == null
                    ? new GameObject("FUSEHealthButton", typeof(RectTransform))
                    : existing.gameObject;
                buttonObject.transform.SetParent(strip, false);
                PositionHudButton(strip, buttonObject.transform);

                var image = buttonObject.GetComponent<Image>() ?? buttonObject.AddComponent<Image>();
                image.sprite = LoadIconSprite();
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = true;
                image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 34f);
                image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 34f);

                var layout = buttonObject.GetComponent<LayoutElement>() ?? buttonObject.AddComponent<LayoutElement>();
                layout.minWidth = 34f;
                layout.preferredWidth = 34f;
                layout.minHeight = 34f;
                layout.preferredHeight = 34f;
                layout.flexibleWidth = 0f;
                layout.flexibleHeight = 0f;

                _button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
                _button.targetGraphic = image;
                _button.interactable = true;
                _button.onClick.RemoveListener(ToggleWindow);
                _button.onClick.AddListener(ToggleWindow);

                FuseLog.Info($"FUSE health HUD button added to base-game TopRightArea strip at siblingIndex={buttonObject.transform.GetSiblingIndex()}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE health HUD button install failed: {ex.GetBaseException().Message}");
            }
        }

        private static void PositionHudButton(Transform strip, Transform buttonTransform)
        {
            var cashTransform = FindCashTransform(strip);
            if (cashTransform != null && cashTransform != buttonTransform)
            {
                buttonTransform.SetSiblingIndex(cashTransform.GetSiblingIndex());
                return;
            }

            buttonTransform.SetAsLastSibling();
        }

        private static Transform FindCashTransform(Transform strip)
        {
            foreach (Transform child in strip)
            {
                if (child == null || child.name == "FUSEHealthButton")
                {
                    continue;
                }

                var name = child.name ?? string.Empty;
                if (name.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("money", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("balance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("fund", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }

                if (ContainsCashText(child))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool ContainsCashText(Transform root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (LooksLikeCash(text == null ? null : text.text))
                {
                    return true;
                }
            }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (LooksLikeCash(text == null ? null : text.text))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeCash(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("$", StringComparison.Ordinal);
        }

        private void ToggleWindow()
        {
            if (!EnsureWindow())
            {
                return;
            }

            if (_window.IsShown)
            {
                CloseAllOpenTabHandlers("FUSE health window closed");
                _window.CloseWindow();
                return;
            }

            RebuildWindow();
            _window.ShowWindow();
        }

        private bool EnsureWindow()
        {
            if (_window != null && _window.gameObject != null && _window.contentRectTransform != null)
            {
                return true;
            }

            if (!WindowCreatorHelper.CanCreateWindow)
            {
                FuseLog.Warning("FUSE health window could not open because ProgrammaticWindowCreator is not available yet.");
                return false;
            }

            _window = WindowCreatorHelper.Shared.CreateWindow(WindowIdentifier, DefaultSize.x, DefaultSize.y, DefaultPosition);
            if (_window == null)
            {
                FuseLog.Warning("FUSE health window could not be created from the base-game window prefab.");
                return false;
            }

            _window.Title = GetWindowTitle();
            return true;
        }

        private void RebuildWindow()
        {
            if (!EnsureWindow())
            {
                return;
            }

            var restoreScroll = _lastBuiltPage == _activePage;
            var scrollPosition = restoreScroll ? CaptureScrollPosition() : 1f;

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            _window.Title = GetWindowTitle();
            _panel = WindowCreatorHelper.Shared.PopulateWindow(_window, BuildHealthPage);
            _lastBuiltPage = _activePage;

            WindowPersistence.SetInitialPositionSize(_window, WindowIdentifier, DefaultSize, DefaultPosition, DefaultSizing);

            if (restoreScroll)
            {
                RestoreScrollPosition(scrollPosition);
                StartCoroutine(RestoreScrollPositionNextFrame(scrollPosition));
            }
        }

        private void BuildHealthPage(UIPanelBuilder builder)
        {
            builder.HStack(row =>
            {
                row.AddButtonCompact(_activePage == Page.Health ? "[ Status ]" : "Status", () => SetPage(Page.Health));
                row.AddButtonCompact(_activePage == Page.Packages ? "[ Mods ]" : "Mods", () => SetPage(Page.Packages));
                row.AddButtonCompact(_activePage == Page.Assets ? "[ Assets ]" : "Assets", () => SetPage(Page.Assets));
                row.AddButtonCompact(_activePage == Page.Runtime ? "[ World ]" : "World", () => SetPage(Page.Runtime));
                row.AddButtonCompact(_activePage == Page.Logs ? "[ Issues ]" : "Issues", () => SetPage(Page.Logs));
                row.AddButtonCompact(_activePage == Page.ModSets ? "[ Profiles ]" : "Profiles", () => SetPage(Page.ModSets));
            }, 6f).Height(34f);

            builder.HStack(row =>
            {
                row.AddButtonCompact(_activePage == Page.Inspector ? "[ Inspector ]" : "Inspector", () => SetPage(Page.Inspector));
                row.AddButtonCompact(_activePage == Page.Audits ? "[ Audits ]" : "Audits", () => SetPage(Page.Audits));
                row.AddButtonCompact(_activePage == Page.Advanced ? "[ Advanced ]" : "Advanced", () => SetPage(Page.Advanced));
                row.AddButtonCompact(_activePage == Page.Settings ? "[ Settings ]" : "Settings", () => SetPage(Page.Settings));
                row.AddButtonCompact(_activePage == Page.LegacyMods ? "[ Legacy Mods ]" : "Legacy Mods", () => SetPage(Page.LegacyMods));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(34f);

            builder.VScrollView(
                BuildActivePageContent,
                new RectOffset(12, 14, 8, 12));
        }

        private void BuildActivePageContent(UIPanelBuilder builder)
        {
            switch (_activePage)
            {
                case Page.Packages:
                    BuildPackagesContent(builder);
                    return;
                case Page.Assets:
                    BuildAssetsContent(builder);
                    return;
                case Page.Runtime:
                    BuildRuntimeContent(builder);
                    return;
                case Page.Logs:
                    BuildLogsContent(builder);
                    return;
                case Page.Inspector:
                    BuildInspectorContent(builder);
                    return;
                case Page.Audits:
                    BuildAuditsContent(builder);
                    return;
                case Page.Advanced:
                    BuildAdvancedContent(builder);
                    return;
                case Page.Settings:
                    BuildSettingsContent(builder);
                    return;
                case Page.ModSets:
                    BuildModSetsContent(builder);
                    return;
                case Page.LegacyMods:
                    BuildLegacyModsContent(builder);
                    return;
                case Page.Health:
                default:
                    BuildHealthContent(builder);
                    return;
            }
        }

        private void SetPage(Page page)
        {
            // Leaving the Legacy Mods page closes any open IModTabHandler tabs
            // so their ModTabDidClose runs (e.g. NotEnoughRosters writes its
            // trains.json from that hook). When the user returns, the tabs
            // are re-opened on the next rebuild.
            if (_activePage == Page.LegacyMods && page != Page.LegacyMods)
            {
                CloseAllOpenTabHandlers("user left FUSE Legacy Mods page");
            }

            _activePage = page;
            RebuildWindow();
        }

        private float CaptureScrollPosition()
        {
            try
            {
                var scrollRect = FindHealthScrollRect();
                return scrollRect == null ? 1f : scrollRect.verticalNormalizedPosition;
            }
            catch
            {
                return 1f;
            }
        }

        private void RestoreScrollPosition(float position)
        {
            try
            {
                Canvas.ForceUpdateCanvases();
                var scrollRect = FindHealthScrollRect();
                if (scrollRect == null)
                {
                    return;
                }

                scrollRect.verticalNormalizedPosition = Mathf.Clamp01(position);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE health page could not restore scroll position: {ex.GetBaseException().Message}");
            }
        }

        private IEnumerator RestoreScrollPositionNextFrame(float position)
        {
            yield return null;
            RestoreScrollPosition(position);
            yield return null;
            RestoreScrollPosition(position);
        }

        private ScrollRect FindHealthScrollRect()
        {
            if (_window?.contentRectTransform == null)
            {
                return null;
            }

            var scrollRects = _window.contentRectTransform.GetComponentsInChildren<ScrollRect>(true);
            return scrollRects == null || scrollRects.Length == 0
                ? null
                : scrollRects[scrollRects.Length - 1];
        }

        private void BuildHealthContent(UIPanelBuilder builder)
        {
            var report = LoadReportJson();
            var counts = report["counts"] as JObject ?? new JObject();
            var hasProblems = ReadBool(report["hasProblems"], false);
            var loadedPackages = ReadInt(counts["loadedPackages"]);
            var appliedPackages = ReadInt(counts["appliedPackages"]);
            var faultCount = ReadInt(counts["faultedPackages"]);
            var conflictCount = ReadInt(counts["conflicts"]);
            var unknownAssetCount = ReadInt(counts["unknownSceneryAssets"]);
            var graphIssueCount = ReadInt(counts["graphIssues"]);
            var transferSkipCount = ReadInt(counts["progressionTransferSkips"]);
            var noticeCount = CountArray(report["notices"]);

            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            builder.AddSection("Stream Readiness");
            AddValueField(builder, "State", hasProblems ? "Needs Attention" : "Ready");
            AddWrappedField(
                builder,
                "Status",
                hasProblems
                    ? "FUSE found items that need review before a clean session."
                    : "Full stack loaded cleanly. No package faults, asset misses, graph issues, or transfer skips are reported.",
                50f);
            AddValueField(builder, "Version", "FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.Spacer(6f);

            builder.AddSection("Checklist");
            AddReadinessRow(builder, "Packages", faultCount == 0, $"{appliedPackages}/{loadedPackages} applied", faultCount + " fault(s)");
            AddReadinessRow(builder, "Assets", unknownAssetCount == 0, "0 unknown assets", unknownAssetCount + " unknown");
            AddReadinessRow(builder, "Track Graph", graphIssueCount == 0, "0 graph issues", graphIssueCount + " issue(s)");
            AddReadinessRow(builder, "Progression", transferSkipCount == 0, "0 transfer skips", transferSkipCount + " skip(s)");
            AddReadinessRow(builder, "Registry", conflictCount == 0, "0 conflicts", conflictCount + " conflict(s)");
            AddReadinessRow(builder, "Notices", noticeCount == 0, "0 notices", noticeCount + " notice(s)");
            builder.Spacer(6f);

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("Active Profile");
            AddValueField(builder, "Mode", multiplayer.Mode + " | " + multiplayer.Role);
            AddValueField(builder, "Mutation Policy", multiplayer.MutationPolicy);
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Packages", multiplayer.LocalPackageSummary, 38f);
            builder.Spacer(6f);

            builder.AddSection("Load Timing");
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            AddWrappedField(builder, "Slowest", FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPackage()), 42f);
            builder.Spacer(6f);

            builder.AddSection("Actions");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Readiness", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildStreamReadinessReport(report);
                    _lastAction = "Copied FUSE readiness report to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Open Issues", () => SetPage(Page.Logs));
                row.AddButtonCompact("Advanced", () => SetPage(Page.Advanced));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 34f);
            builder.Spacer(6f);

            builder.AddSection("Active Problems");
            var problemRows = 0;
            problemRows += AddProblemSummary(builder, report, "packages", "faults", "Package Faults", false);
            problemRows += AddProblemSummary(builder, report, null, "conflicts", "Conflicts", false);
            problemRows += AddProblemSummary(builder, report, null, "unknownSceneryAssets", "Unknown Assets", false);
            problemRows += AddProblemSummary(builder, report, null, "graphPostBindIssues", "Graph Issues", false);
            problemRows += AddProblemSummary(builder, report, null, "progressionTransferSkips", "Transfer Skips", false);
            problemRows += AddProblemSummary(builder, report, null, "notices", "Notices", false);
            problemRows += AddSaveCarFaultSummaryRow(builder);
            if (problemRows == 0)
            {
                AddValueField(builder, "Status", "None");
            }
            builder.Spacer(8f);

            BuildSaveCarFaultsSection(builder);
        }

        /// <summary>
        /// Adds a single-row entry to "Active Problems" for cars the
        /// save load could not restore. The count is read live from
        /// <see cref="FuseSaveCarFaultRegistry"/>; if zero, no row is
        /// drawn (matching the no-show-zero convention of the
        /// surrounding rows). Returns the number of rows produced so
        /// the caller can sum into the empty-state "Status: None"
        /// path.
        /// </summary>
        private static int AddSaveCarFaultSummaryRow(UIPanelBuilder builder)
        {
            var count = FuseSaveCarFaultRegistry.Count;
            if (count == 0)
            {
                return 0;
            }
            builder.AddField("Orphaned Cars", () => count + " - see below", 0).Height(24f);
            return 1;
        }

        // Per-prototype replacement-target selection persists across
        // the panel rebuild cycle so the user's dropdown choice
        // doesn't reset every time the UI redraws.
        private static readonly Dictionary<string, string> _saveCarFaultReplacementSelection =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lists every car the save load could not restore, grouped
        /// by the missing prototype identifier so cars that share a
        /// broken type cluster together (and a fix targeting the
        /// type can address them all at once). Empty when the
        /// registry has no entries — silent in that case so the
        /// Health page stays clean. Each group has a picker for a
        /// replacement car type and a button that applies the
        /// replacement to every car in the group, spawning new cars
        /// at the original locations with the original ids /
        /// waybills / properties preserved.
        /// </summary>
        private void BuildSaveCarFaultsSection(UIPanelBuilder builder)
        {
            var faults = FuseSaveCarFaultRegistry.GetAll();
            if (faults.Count == 0)
            {
                return;
            }

            builder.AddSection("Orphaned Cars (this save)");
            AddWrappedLabel(
                builder,
                "These cars were in the save but could not be restored — their car-type definitions " +
                "weren't usable (e.g., the only definition lived in a legacy SCAssetPacks pack whose " +
                "bundle conflicts with the modern pack's bundle, so FUSE filtered it out to prevent " +
                "Unity from refusing the bundle load). Pick a replacement car type per group below " +
                "and FUSE will spawn the car back at its original location with the same id, road " +
                "number, waybill, and load — only the prefab/type changes.",
                52f);
            builder.Spacer(4f);

            // Refresh the available-replacement list every panel
            // rebuild so the picker reflects packs that came in via a
            // late legacy-data converter (the list is cheap to
            // enumerate; this keeps the UI honest about what the game
            // can actually load right now).
            var availableReplacements = FuseSaveCarFaultReplacement.GetAvailablePrototypeIds();

            var byPrototype = faults
                .GroupBy(f => f.MissingPrototypeId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in byPrototype)
            {
                var groupKey = string.IsNullOrEmpty(group.Key) ? "<unknown>" : group.Key;
                var groupList = group.ToList();
                AddValueField(builder, "Type", $"{groupKey} ({groupList.Count})");
                foreach (var fault in groupList.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    AddWrappedField(
                        builder,
                        "  " + fault.DisplayName,
                        $"id={fault.CarId} at segment={fault.LocationSegmentId} dist={fault.LocationDistance:F1}",
                        34f);
                }

                BuildReplacementPickerRow(builder, groupKey, groupList, availableReplacements);
                builder.Spacer(4f);
            }
            builder.Spacer(8f);
        }

        /// <summary>
        /// Renders the replacement controls for one prototype group:
        /// a dropdown of currently-loadable car identifiers and an
        /// Apply button that spawns a replacement for every car in
        /// the group using the selected identifier. Choices persist
        /// across rebuilds via
        /// <see cref="_saveCarFaultReplacementSelection"/>.
        /// </summary>
        private void BuildReplacementPickerRow(
            UIPanelBuilder builder,
            string groupKey,
            List<FuseSaveCarFault> groupFaults,
            string[] availableReplacements)
        {
            if (availableReplacements == null || availableReplacements.Length == 0)
            {
                AddWrappedLabel(
                    builder,
                    "  No replacement car types are currently loadable. Make sure your TOFC Cars (or " +
                    "equivalent) pack is installed at the mod root with the modern definitions.",
                    32f);
                return;
            }

            if (!_saveCarFaultReplacementSelection.TryGetValue(groupKey, out var selected) ||
                Array.IndexOf(availableReplacements, selected) < 0)
            {
                selected = availableReplacements[0];
                _saveCarFaultReplacementSelection[groupKey] = selected;
            }

            // The picker uses a paged-button row instead of a
            // dropdown so the implementation stays simple and the UI
            // works on all UIPanelBuilder shipping in the host. Each
            // button shows one available identifier; clicking sets
            // the selection. Selected identifier is shown bold-ish
            // via a square-bracket marker (no rich-text dep).
            builder.AddField("  Replacement", () =>
            {
                if (_saveCarFaultReplacementSelection.TryGetValue(groupKey, out var current))
                {
                    return current;
                }
                return availableReplacements[0];
            }, 0).Height(24f);

            // Render up to ~6 candidates per row so the user can scan
            // without scrolling forever. For large catalogs we just
            // show the first N alphabetically; refining with a
            // search box can come in a later iteration.
            const int MaxCandidates = 24;
            var candidates = availableReplacements.Take(MaxCandidates).ToArray();
            builder.HStack(row =>
            {
                row.Spacing = 2f;
                foreach (var candidate in candidates)
                {
                    var captured = candidate;
                    var label = string.Equals(captured, selected, StringComparison.Ordinal)
                        ? "[" + captured + "]"
                        : captured;
                    row.AddButtonCompact(label, () =>
                    {
                        _saveCarFaultReplacementSelection[groupKey] = captured;
                        RebuildWindow();
                    });
                }
            }, 4f).Height(28f);

            builder.HStack(row =>
            {
                row.AddButtonCompact(
                    $"Replace {groupFaults.Count} car(s) with '{selected}'",
                    () => ApplyReplacementGroup(groupKey, groupFaults, selected));
            }, 6f).Height(32f);
        }

        private void ApplyReplacementGroup(
            string groupKey,
            List<FuseSaveCarFault> groupFaults,
            string replacementPrototypeId)
        {
            var applied = 0;
            var failed = 0;
            foreach (var fault in groupFaults)
            {
                if (FuseSaveCarFaultReplacement.TryApply(fault, replacementPrototypeId))
                {
                    applied++;
                }
                else
                {
                    failed++;
                }
            }

            _lastAction = failed == 0
                ? $"Replaced {applied} orphaned car(s) of type '{groupKey}' with '{replacementPrototypeId}'."
                : $"Replaced {applied} of {applied + failed} orphaned car(s) of type '{groupKey}' " +
                  $"with '{replacementPrototypeId}'; {failed} failed — see FUSE.log.";
            RebuildWindow();
        }

        private void BuildPackagesContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 150f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots();
            var selected = ResolveSelectedPackage(manifests);

            builder.AddSection("Mod Browser");
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Packages", multiplayer.LocalPackageSummary, 38f);
            AddWrappedField(builder, "Selected", selected == null ? "No package selected." : PackageDisplayName(selected), 34f);
            builder.Spacer(4f);

            if (manifests.Count == 0)
            {
                AddValueField(builder, "Packages", "No FUSE data packages discovered.");
                builder.Spacer(8f);
                return;
            }

            BuildPackageSelector(builder, manifests, selected);
            builder.Spacer(6f);
            BuildSelectedPackagePage(builder, selected);

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                builder.Spacer(4f);
                builder.AddSection("Dependency Graph");
                BuildDependencyGraph(builder, manifests);
            }

            builder.Spacer(8f);
        }

        private FusePackageManifestSnapshot ResolveSelectedPackage(IReadOnlyList<FusePackageManifestSnapshot> manifests)
        {
            if (manifests == null || manifests.Count == 0)
            {
                _selectedPackageId = string.Empty;
                return null;
            }

            var selected = manifests.FirstOrDefault(manifest =>
                string.Equals(manifest.Id, _selectedPackageId, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                return selected;
            }

            selected = manifests.FirstOrDefault(manifest => manifest.Faults.Length > 0)
                ?? manifests.FirstOrDefault(HasPackageSettings)
                ?? manifests.FirstOrDefault(manifest => !manifest.Disabled)
                ?? manifests[0];
            _selectedPackageId = selected.Id ?? string.Empty;
            return selected;
        }

        private void BuildPackageSelector(
            UIPanelBuilder builder,
            IReadOnlyList<FusePackageManifestSnapshot> manifests,
            FusePackageManifestSnapshot selected)
        {
            builder.AddSection("Packages");
            var rowsShown = 0;
            foreach (var manifest in manifests)
            {
                if (!FuseSettings.ShowAdvancedHealthDetails && rowsShown >= 18)
                {
                    continue;
                }

                rowsShown++;
                var captured = manifest;
                var isSelected = selected != null && string.Equals(selected.Id, manifest.Id, StringComparison.OrdinalIgnoreCase);
                builder.HStack(row =>
                {
                    row.AddButtonCompact(isSelected ? "[ " + TrimPackageLabel(PackageDisplayName(captured), 34) + " ]" : TrimPackageLabel(PackageDisplayName(captured), 38), () =>
                    {
                        _selectedPackageId = captured.Id ?? string.Empty;
                        RebuildWindow();
                    });
                    row.AddLabel(PackageStatusText(captured), text =>
                    {
                        text.enableWordWrapping = false;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                        text.alignment = TextAlignmentOptions.Left;
                    });
                }, 6f).Height(30f);
            }

            if (!FuseSettings.ShowAdvancedHealthDetails && manifests.Count > rowsShown)
            {
                AddWrappedField(builder, "More", (manifests.Count - rowsShown) + " hidden. Enable Advanced Details to show the full package list.", 38f);
            }
        }

        private void BuildSelectedPackagePage(UIPanelBuilder builder, FusePackageManifestSnapshot manifest)
        {
            builder.AddSection("Selected Mod");
            if (manifest == null)
            {
                AddValueField(builder, "Status", "No package selected.");
                return;
            }

            var definitions = GetLoadedDefinitionsForPackage(manifest);
            AddWrappedLabel(builder, PackageDisplayName(manifest), 34f);
            AddValueField(builder, "Status", PackageStatusText(manifest));
            AddValueField(builder, "Version", BlankAs(manifest.Version, "?"));
            AddWrappedField(builder, "Id", manifest.Id, 34f);
            AddWrappedField(builder, "Folder", manifest.FolderName, 34f);
            AddValueField(builder, "Definitions", definitions.Length.ToString());
            AddValueField(builder, "Settings", CountPackageSettings(definitions).ToString());

            if (manifest.Faults.Length > 0)
            {
                AddWrappedField(builder, "Faults", string.Join("; ", manifest.Faults), 54f);
            }

            if (manifest.Disabled && !string.IsNullOrWhiteSpace(manifest.DisabledReason))
            {
                AddWrappedField(builder, "Disabled", manifest.DisabledReason, 44f);
            }

            var deps = BuildDependencySummary(manifest);
            if (!string.IsNullOrWhiteSpace(deps))
            {
                AddWrappedField(builder, "Dependencies", deps, 42f);
            }

            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Mod Info", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildSelectedPackageReport(manifest, definitions);
                    _lastAction = "Copied selected mod information to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Issues", () => SetPage(Page.Logs));
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);

            builder.Spacer(6f);
            builder.AddSection("Mod Settings");
            if (definitions.Length == 0)
            {
                AddWrappedField(builder, "Settings", "This package is not loaded, so no runtime settings definition is available.", 44f);
                return;
            }

            var rendered = 0;
            foreach (var loaded in definitions)
            {
                if (loaded.Definition?.Settings == null || loaded.Definition.Settings.Count == 0)
                {
                    continue;
                }

                if (definitions.Length > 1)
                {
                    AddWrappedLabel(builder, loaded.Definition.Id, 30f);
                }

                foreach (var pair in loaded.Definition.Settings
                    .Where(pair => pair.Value != null)
                    .Where(pair => FuseSettings.ShowAdvancedHealthDetails || !pair.Value.Advanced)
                    .OrderBy(pair => GetSettingLabel(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase))
                {
                    BuildPackageSettingControl(builder, loaded.Definition, pair.Key, pair.Value);
                    rendered++;
                }
            }

            if (rendered == 0)
            {
                AddWrappedField(
                    builder,
                    "Settings",
                    CountPackageSettings(definitions) == 0
                        ? "This mod does not declare FUSE settings."
                        : "Only advanced settings are declared. Enable Advanced Details to show them.",
                    44f);
            }

            // Legacy-loader plugin settings (IModTabHandler) live on their own
            // tab — see Page.LegacyMods / BuildLegacyModsContent. We keep the
            // per-package settings panel focused on FUSE-native settings so
            // legacy plugin UIs do not double-render here.
        }

        /// <summary>
        /// Renders the "Legacy Mods" page: a mod picker at the top, then the
        /// selected mod's <see cref="IModTabHandler"/> tab rendered into its
        /// own panel below. Only mods whose hosted plugin implements
        /// <c>IModTabHandler</c> (i.e. expose at least one option) are listed.
        ///
        /// Lifecycle: selecting a different mod fires <c>ModTabDidClose</c> on
        /// the previously-selected mod's handler so plugins like
        /// NotEnoughRosters can persist their state (it writes
        /// <c>trains.json</c> from that hook). The newly-selected mod's
        /// handler receives <c>ModTabDidOpen</c> on the next rebuild.
        /// </summary>
        private void BuildLegacyModsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var hostedPlugins = FuseLegacyAssemblyHost
                .EnumerateAllHostedPlugins()
                .Where(info => info.Plugin is IModTabHandler)
                .Select(info => new TabHandlerEntry(
                    BuildTabHandlerSignature(info.Manifest, info.PluginType),
                    info.Manifest,
                    info.PluginType,
                    (IModTabHandler)info.Plugin))
                .OrderBy(entry => entry.DisplayLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            builder.AddSection("Legacy Mods Settings");
            AddWrappedField(
                builder,
                "Scope",
                "Settings tabs declared by legacy-loader plugins (Railloader IModTabHandler). Only mods that expose at least one tab option appear here.",
                52f);
            AddValueField(builder, "Mods Found", hostedPlugins.Count.ToString());

            if (hostedPlugins.Count == 0)
            {
                // No eligible mod means no signature should remain open.
                CloseAllOpenTabHandlers("no legacy mods with settings");
                AddWrappedField(
                    builder,
                    "Status",
                    "No hosted legacy plugin implements IModTabHandler. Mods that only register console commands or mixintos appear in the Mods tab instead.",
                    52f);
                builder.Spacer(8f);
                return;
            }

            // Resolve which mod is selected. If the stored selection is stale
            // (mod no longer hosted) fall back to the first available entry.
            var selected = hostedPlugins.FirstOrDefault(entry =>
                               string.Equals(entry.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase))
                           ?? hostedPlugins[0];
            _selectedLegacyModSignature = selected.Signature;

            // Close any other mod's handler so only the selected one is "open".
            var keepOnlySelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                selected.Signature
            };
            CloseTabHandlersExcept(keepOnlySelected, "legacy mods selection changed");

            // Mod picker.
            var labels = hostedPlugins.Select(entry => entry.DisplayLabel).ToList();
            var selectedIndex = Math.Max(0, hostedPlugins.FindIndex(entry =>
                string.Equals(entry.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase)));
            builder.AddField(
                "Mod",
                builder.AddDropdown(labels, selectedIndex, index =>
                {
                    if (index < 0 || index >= hostedPlugins.Count)
                    {
                        return;
                    }

                    var chosen = hostedPlugins[index];
                    if (string.Equals(chosen.Signature, _selectedLegacyModSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    _selectedLegacyModSignature = chosen.Signature;
                    RebuildWindow();
                })).Height(32f);
            builder.Spacer(6f);

            // Selected mod's panel.
            builder.AddSection(selected.DisplayLabel);
            AddWrappedField(builder, "Mod Id", selected.ManifestIdOrFallback, 28f);
            if (!string.IsNullOrWhiteSpace(selected.ManifestVersion))
            {
                AddValueField(builder, "Version", selected.ManifestVersion);
            }
            builder.Spacer(4f);

            _openTabHandlers[selected.Signature] = selected.Plugin;
            try
            {
                selected.Plugin.ModTabDidOpen(builder);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"Legacy plugin '{selected.PluginType.FullName}' threw from ModTabDidOpen while FUSE was rendering its settings tab",
                    ex);
                AddWrappedField(
                    builder,
                    "Plugin Error",
                    $"{selected.PluginType.Name} threw {ex.GetType().Name} from ModTabDidOpen: {ex.GetBaseException().Message}",
                    54f);
            }

            builder.Spacer(8f);
        }

        private sealed class TabHandlerEntry
        {
            public TabHandlerEntry(string signature, FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType, IModTabHandler plugin)
            {
                Signature = signature ?? string.Empty;
                Manifest = manifest;
                PluginType = pluginType;
                Plugin = plugin;
                DisplayLabel = BuildDisplayLabel(manifest, pluginType);
            }

            public string Signature { get; }
            public FUSE.Loading.FuseLegacyAssemblyManifest Manifest { get; }
            public Type PluginType { get; }
            public IModTabHandler Plugin { get; }
            public string DisplayLabel { get; }

            public string ManifestIdOrFallback
            {
                get
                {
                    if (Manifest != null && !string.IsNullOrWhiteSpace(Manifest.Id))
                    {
                        return Manifest.Id;
                    }

                    return PluginType == null ? "(unknown)" : (PluginType.FullName ?? PluginType.Name);
                }
            }

            public string ManifestVersion => Manifest == null ? string.Empty : (Manifest.Version ?? string.Empty);

            private static string BuildDisplayLabel(FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType)
            {
                var modName = manifest == null
                    ? null
                    : (!string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : manifest.Id);
                var typeName = pluginType == null ? "(unnamed plugin)" : (pluginType.Name ?? pluginType.FullName);
                if (string.IsNullOrWhiteSpace(modName))
                {
                    return typeName;
                }

                return string.Equals(modName, typeName, StringComparison.OrdinalIgnoreCase)
                    ? modName
                    : modName + " | " + typeName;
            }
        }

        /// <summary>
        /// Calls <c>ModTabDidClose</c> on any tracked handlers whose signature is
        /// NOT in <paramref name="keepSignatures"/>, and forgets them. Plugins
        /// can rely on this to persist state when the user navigates away from
        /// their tab. Exceptions are logged but never bubble — a misbehaving
        /// plugin must not break FUSE's UI teardown.
        /// </summary>
        private void CloseTabHandlersExcept(HashSet<string> keepSignatures, string reason)
        {
            if (_openTabHandlers.Count == 0)
            {
                return;
            }

            var toClose = _openTabHandlers
                .Where(pair => keepSignatures == null || !keepSignatures.Contains(pair.Key))
                .ToArray();
            foreach (var entry in toClose)
            {
                _openTabHandlers.Remove(entry.Key);
                try
                {
                    entry.Value?.ModTabDidClose();
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"Legacy plugin handler '{entry.Key}' threw from ModTabDidClose ({reason})",
                        ex);
                }
            }
        }

        private void CloseAllOpenTabHandlers(string reason)
        {
            CloseTabHandlersExcept(null, reason);
        }

        private static string BuildTabHandlerSignature(FUSE.Loading.FuseLegacyAssemblyManifest manifest, Type pluginType)
        {
            var packageKey = manifest == null
                ? string.Empty
                : (manifest.Id ?? manifest.FolderPath ?? string.Empty);
            var typeKey = pluginType == null ? string.Empty : (pluginType.FullName ?? pluginType.Name ?? string.Empty);
            return packageKey + "|" + typeKey;
        }

        private static FuseLoadedMod[] GetLoadedDefinitionsForPackage(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return Array.Empty<FuseLoadedMod>();
            }

            return FuseModLoader.GetLoadedModsInOrder()
                .Where(loaded => loaded?.Definition != null)
                .Where(loaded =>
                    string.Equals(NormalizePath(loaded.FolderPath), NormalizePath(manifest.FolderPath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loaded.Definition.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) ||
                    loaded.Definition.Id.StartsWith((manifest.Id ?? string.Empty) + ".", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static int CountPackageSettings(IEnumerable<FuseLoadedMod> definitions)
        {
            return (definitions ?? Enumerable.Empty<FuseLoadedMod>())
                .Where(loaded => loaded?.Definition?.Settings != null)
                .Sum(loaded => loaded.Definition.Settings.Count);
        }

        private static bool HasPackageSettings(FusePackageManifestSnapshot manifest)
        {
            return CountPackageSettings(GetLoadedDefinitionsForPackage(manifest)) > 0;
        }

        private static string BuildSelectedPackageReport(FusePackageManifestSnapshot manifest, IReadOnlyList<FuseLoadedMod> definitions)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE selected mod");
            builder.AppendLine("Name: " + PackageDisplayName(manifest));
            builder.AppendLine("Id: " + manifest.Id);
            builder.AppendLine("Version: " + BlankAs(manifest.Version, "?"));
            builder.AppendLine("Status: " + PackageStatusText(manifest));
            builder.AppendLine("Folder: " + manifest.FolderPath);
            builder.AppendLine("Definitions: " + (definitions?.Count ?? 0));
            builder.AppendLine("Settings: " + CountPackageSettings(definitions));
            if (manifest.Faults.Length > 0)
            {
                builder.AppendLine("Faults: " + string.Join("; ", manifest.Faults));
            }

            foreach (var loaded in definitions ?? Array.Empty<FuseLoadedMod>())
            {
                builder.AppendLine("Definition: " + loaded.Definition.Id);
            }

            return builder.ToString();
        }

        private static string PackageDisplayName(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id : manifest.DisplayName;
        }

        private static string PackageStatusText(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return "unknown";
            }

            if (manifest.Disabled)
            {
                return "disabled";
            }

            if (manifest.Faults.Length > 0)
            {
                return manifest.Faults.Length + " fault(s)";
            }

            return manifest.IsLegacyConverted ? "ready | legacy" : "ready";
        }

        private static string TrimPackageLabel(string value, int maxLength)
        {
            value = value ?? string.Empty;
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(1, maxLength - 3)) + "...";
        }

        private static string NormalizePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void BuildAssetsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var diagnostics = FuseAssetPackRegistry.GetDiagnostics();
            builder.AddSection("Asset Resolution");
            AddValueField(builder, "Mode", AssetPackModeText());
            AddValueField(builder, "Stores Scanned", diagnostics.StoreFolders.Length.ToString());
            AddValueField(builder, "Runtime Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            AddValueField(builder, "Unique Asset Keys", diagnostics.UniqueAssetKeys.ToString());
            AddValueField(builder, "Duplicate Keys", diagnostics.DuplicateKeys.Length.ToString());
            AddValueField(builder, "Failed Definitions", diagnostics.FailedDefinitionLoads.Length.ToString());
            AddValueField(builder, "Last Direct Mount", FusePerformanceMetrics.FormatTiming("direct asset pack stores"));
            AddWrappedField(
                builder,
                "Duplicates",
                diagnostics.DuplicateKeys.Length == 0
                    ? "None detected."
                    : "Overlap diagnostics, not automatic errors. Export the report to see every duplicate key and source.",
                44f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Export Asset Report", () =>
                {
                    RunAction("export asset diagnostics", () => ExportAssetDiagnostics(diagnostics));
                });
                row.AddButtonCompact("Copy Asset Summary", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildAssetSummary(diagnostics);
                    _lastAction = "Copied FUSE asset summary to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 34f);
            builder.Spacer(4f);

            if (!FuseSettings.ShowAdvancedHealthDetails && diagnostics.DuplicateKeys.Length == 0 && diagnostics.FailedDefinitionLoads.Length == 0)
            {
                AddValueField(builder, "Status", "No asset issues detected");
            }
            else if (!FuseSettings.ShowAdvancedHealthDetails)
            {
                AddWrappedField(
                    builder,
                    "Details",
                    "Enable Advanced Details in Settings to view duplicate winners, overridden sources, and store paths inside this panel.",
                    48f);
            }
            else
            {
                builder.AddSection("Duplicate Asset Keys");
                if (diagnostics.DuplicateKeys.Length == 0)
                {
                    AddValueField(builder, "Status", "None detected");
                }
                else
                {
                    foreach (var duplicate in diagnostics.DuplicateKeys.Take(20))
                    {
                        AddWrappedLabel(builder, BuildDuplicateAssetPreview(duplicate), 52f);
                    }

                    if (diagnostics.DuplicateKeys.Length > 20)
                    {
                        AddWrappedField(
                            builder,
                            "More",
                            (diagnostics.DuplicateKeys.Length - 20) + " hidden. Export Asset Report for all duplicate keys.",
                            34f);
                    }
                }

                builder.Spacer(4f);
                builder.AddSection("Asset Stores");
                foreach (var folder in diagnostics.StoreFolders.Take(40))
                {
                    AddWrappedLabel(builder, InsertBreakHints(Path.GetFileName(folder)), 26f);
                }

                if (diagnostics.StoreFolders.Length > 40)
                {
                    AddWrappedField(
                        builder,
                        "More",
                        (diagnostics.StoreFolders.Length - 40) + " hidden. Export Asset Report for all store paths.",
                        34f);
                }
            }

            builder.Spacer(8f);
        }

        private void BuildRuntimeContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Runtime Objects");
            AddValueField(builder, "Track Nodes", SafeCount(() => TrackAPI.GetAllNodes().Count()).ToString());
            AddValueField(builder, "Track Segments", SafeCount(() => TrackAPI.GetAllSegments().Count()).ToString());
            AddValueField(builder, "Track Spans", SafeCount(() => TrackAPI.GetAllSpans().Count()).ToString());
            AddValueField(builder, "Areas", SafeCount(() => TrackAPI.GetAllAreas().Count()).ToString());
            AddValueField(builder, "Loads", SafeCount(() => LoadAPI.GetAllLoads().Count()).ToString());
            AddValueField(builder, "Industries", SafeCount(() => IndustryAPI.GetAllIndustries().Count()).ToString());
            AddValueField(builder, "Loaders", SafeCount(() => LoaderAPI.GetAllLoaders().Count()).ToString());
            AddValueField(builder, "Stations", SafeCount(() => StationAPI.GetAllStationAgents().Count()).ToString());
            AddValueField(builder, "Passenger Stops", SafeCount(() => StationAPI.GetAllPassengerStops().Count()).ToString());
            AddValueField(builder, "Turntables", SafeCount(() => TurntableAPI.GetAllTurntables().Count()).ToString());
            AddValueField(builder, "Scenery", SafeCount(() => SceneryAPI.GetAllScenery().Count()).ToString());
            AddValueField(builder, "Scene Clones", SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()).ToString());
            AddValueField(builder, "Splineys", SafeCount(() => SplineyAPI.GetAllSplineys().Count()).ToString());
            AddValueField(builder, "Map Labels", SafeCount(() => MapAPI.GetAllMapLabels().Count()).ToString());
            AddValueField(builder, "Map Masks", SafeCount(() => MapAPI.GetAllMapMasks().Count()).ToString());
            AddValueField(builder, "Progressions", SafeCount(() => ProgressionAPI.GetAllProgressions().Count()).ToString());
            AddValueField(builder, "Map Features", SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count()).ToString());
            builder.Spacer(6f);

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                builder.AddSection("Registry");
                AddValueField(builder, "Exclusive Claims", FUSE.Registry.FuseRegistry.ExclusiveClaimCount.ToString());
                AddValueField(builder, "Shared Claims", FUSE.Registry.FuseRegistry.SharedClaimCount.ToString());
                AddValueField(builder, "Conflicts", FUSE.Registry.FuseRegistry.Conflicts.Count.ToString());
            }
            else
            {
                AddWrappedField(
                    builder,
                    "Advanced",
                    "Enable Advanced Details in Settings to show registry claim counts and lower-level runtime diagnostics.",
                    44f);
            }
            builder.Spacer(8f);
        }

        private void BuildLogsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            var report = LoadReportJson();
            builder.AddSection("Error Drilldown");
            AddProblemSummary(builder, report, "packages", "faults", "Package Faults", true);
            AddProblemSummary(builder, report, null, "conflicts", "Conflicts", true);
            AddProblemSummary(builder, report, null, "unknownSceneryAssets", "Unknown Assets", true);
            AddProblemSummary(builder, report, null, "graphPostBindIssues", "Graph Issues", true);
            AddProblemSummary(builder, report, null, "progressionTransferSkips", "Transfer Skips", true);
            AddProblemSummary(builder, report, null, "notices", "Notices", true);
            builder.Spacer(4f);

            builder.AddSection("Export");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Health Report", () =>
                {
                    GUIUtility.systemCopyBuffer = FuseLoadReport.GetLastDetailReport();
                    _lastAction = "Copied FUSE health report to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Export JSON", () =>
                {
                    RunAction("export health report", ExportHealthReportJson);
                });
                row.AddButtonCompact("Export Mod Manifest", () =>
                {
                    RunAction("export active mod-set manifest", () => "Exported active mod-set manifest: " + FuseModSetService.ExportActiveManifest());
                });
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 36f);
            builder.Spacer(4f);

            if (FuseSettings.ShowAdvancedHealthDetails || HasReportProblems(report))
            {
                builder.AddSection("Last FUSE Log Lines");
                var lines = ReadLastLogLines(50);
                AddWrappedLabel(builder, lines.Length == 0 ? "No FUSE.log lines available yet." : string.Join("\n", lines), Math.Min(620f, Math.Max(80f, lines.Length * 18f)));
            }
            else
            {
                AddWrappedField(builder, "Log Tail", "Hidden while FUSE is healthy. Enable Advanced Details in Settings to show live log lines.", 44f);
            }
            builder.Spacer(8f);
        }

        private void BuildInspectorContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Object Inspector");
            AddWrappedField(
                builder,
                "Scope",
                "Read-only inspector for FUSE-indexed runtime objects and loaded Unity scene objects. Search by id, name, scene path, or component type.",
                52f);
            builder.AddField(
                "Search",
                builder.AddInputField(_inspectorSearchTerm ?? string.Empty, value =>
                {
                    _inspectorSearchTerm = value ?? string.Empty;
                })).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Search", RebuildWindow);
                row.AddButtonCompact("Clear", () =>
                {
                    _inspectorSearchTerm = string.Empty;
                    _inspectorSelectedSignature = string.Empty;
                    RebuildWindow();
                });
                row.AddButtonCompact("Copy Detail", () =>
                {
                    var target = ResolveSelectedInspectorTarget();
                    GUIUtility.systemCopyBuffer = BuildInspectorReport(target);
                    _lastAction = target == null
                        ? "No inspector target selected."
                        : "Copied inspector detail to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);

            var term = (_inspectorSearchTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                AddWrappedField(builder, "Hint", "Enter at least 2 characters, then Search.", 34f);
                return;
            }

            var targets = BuildInspectorTargets(term, 120);
            if (targets.Count == 0)
            {
                AddWrappedField(builder, "Results", "No matching runtime or scene objects.", 34f);
                return;
            }

            if (string.IsNullOrWhiteSpace(_inspectorSelectedSignature) ||
                targets.All(target => !string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)))
            {
                _inspectorSelectedSignature = targets[0].Signature;
            }

            var selectedIndex = Math.Max(0, targets.FindIndex(target =>
                string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)));
            var labels = targets
                .Select(target => target.DropdownLabel)
                .ToList();
            builder.AddField(
                "Target",
                builder.AddDropdown(labels, selectedIndex, index =>
                {
                    if (index >= 0 && index < targets.Count)
                    {
                        _inspectorSelectedSignature = targets[index].Signature;
                        RebuildWindow();
                    }
                })).Height(32f);
            AddValueField(builder, "Matches", targets.Count.ToString());
            builder.Spacer(4f);

            BuildInspectorDetail(builder, targets[selectedIndex]);
            builder.Spacer(8f);
        }

        private void BuildAuditsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var findings = BuildAuditFindings();
            var blocking = findings.Count(finding => finding.Severity == "Critical" || finding.Severity == "High");
            var warnings = findings.Count(finding => finding.Severity == "Medium" || finding.Severity == "Low");

            builder.AddSection("Runtime Audits");
            AddWrappedField(
                builder,
                "Scope",
                "Read-only checks for common Railroader/FUSE failure modes. These do not mutate the world; they produce actionable diagnostics.",
                52f);
            AddValueField(builder, "Findings", findings.Count.ToString());
            AddValueField(builder, "Blocking", blocking.ToString());
            AddValueField(builder, "Warnings", warnings.ToString());
            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Audits", RebuildWindow);
                row.AddButtonCompact("Copy Report", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildAuditReport(findings);
                    _lastAction = "Copied FUSE audit report to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Export Report", () =>
                {
                    RunAction("export audit report", () => ExportAuditReport(findings));
                });
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 34f);
            builder.Spacer(4f);

            if (findings.Count == 0)
            {
                AddValueField(builder, "Status", "No audit findings.");
                builder.Spacer(8f);
                return;
            }

            builder.AddSection("Findings");
            foreach (var finding in findings.Take(30))
            {
                AddWrappedLabel(
                    builder,
                    $"{finding.Severity} | {finding.Title} | {finding.ObjectId} | {finding.Detail}",
                    58f);
                AddWrappedField(builder, "Action", finding.Action, 42f);
            }

            if (findings.Count > 30)
            {
                AddWrappedField(builder, "More", (findings.Count - 30) + " hidden. Copy or export the report for all findings.", 34f);
            }

            builder.Spacer(8f);
        }

        private void BuildAdvancedContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            builder.AddSection("Developer Workbench");
            AddWrappedField(
                builder,
                "Mode",
                "Advanced tools are for live Unity/Railroader inspection, cache rebuilds, and FUSE compatibility debugging. They are intentionally separated from the stream-ready status pages.",
                58f);
            AddSettingToggle(
                builder,
                "Advanced Details",
                FuseSettings.ShowAdvancedHealthDetails ? "enabled" : "disabled",
                FuseSettings.ShowAdvancedHealthDetails ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    RebuildWindow();
                });
            builder.Spacer(4f);

            builder.AddSection("Unity Runtime");
            AddUnityRuntimeFields(builder);
            builder.Spacer(4f);

            builder.AddSection("Railroader Runtime");
            AddRailroaderRuntimeFields(builder);
            builder.Spacer(4f);

            builder.AddSection("Object Finder");
            BuildAdvancedObjectFinder(builder);
            builder.Spacer(4f);

            builder.AddSection("FUSE Registry");
            AddValueField(builder, "Exclusive Claims", FUSE.Registry.FuseRegistry.ExclusiveClaimCount.ToString());
            AddValueField(builder, "Shared Claims", FUSE.Registry.FuseRegistry.SharedClaimCount.ToString());
            AddValueField(builder, "Conflicts", FUSE.Registry.FuseRegistry.Conflicts.Count.ToString());
            AddValueField(builder, "Asset Stores", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            builder.Spacer(4f);

            builder.AddSection("Runtime Actions");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Reload Track/Data", () =>
                {
                    RunAction("reload track and data", () =>
                    {
                        var applied = FuseRuntimeReloadService.ReloadTrackAndData("FUSE advanced page reload track/data");
                        return $"Reload Track/Data complete. Applied {applied} resident definition(s).";
                    });
                });
                row.AddButtonCompact("Reload Terrain", () =>
                {
                    RunAction("reload terrain", () =>
                        FuseRuntimeReloadService.ReloadTerrain("FUSE advanced page reload terrain")
                            ? "Reload Terrain complete."
                            : "Reload Terrain skipped or failed. See FUSE.log.");
                });
                row.AddButtonCompact("Rebuild Caches", () =>
                {
                    RunAction("rebuild caches", () =>
                    {
                        FuseCacheRegistry.RebuildAll();
                        return "Rebuilt FUSE runtime caches.";
                    });
                });
            }, 6f).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Copy Runtime Snapshot", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildRuntimeSnapshotText();
                    _lastAction = "Copied FUSE runtime snapshot to clipboard.";
                    RebuildWindow();
                });
                row.AddButtonCompact("Export Debug Bundle", () =>
                {
                    RunAction("export debug bundle", ExportDebugBundle);
                });
                row.AddButtonCompact("Refresh", RebuildWindow);
            }, 6f).Height(32f);
            AddWrappedLabel(builder, _lastAction, 36f);
            builder.Spacer(4f);

            builder.AddSection("Debug Overlays");
            AddSettingToggle(
                builder,
                "Track Probe",
                FuseSettings.ShowTrackDebugOverlay ? "enabled on hover" : "disabled",
                FuseSettings.ShowTrackDebugOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowTrackDebugOverlay(!FuseSettings.ShowTrackDebugOverlay);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Track Span Paths",
                FuseSettings.ShowTrackDebugSpanPaths
                    ? (FuseSettings.ShowTrackDebugOverlay ? "shown in overlay" : "shown when track probe is on")
                    : "hidden",
                FuseSettings.ShowTrackDebugSpanPaths ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowTrackDebugSpanPaths(!FuseSettings.ShowTrackDebugSpanPaths);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Probe",
                FuseSettings.ShowSceneryDebugOverlay ? "enabled on hover" : "disabled",
                FuseSettings.ShowSceneryDebugOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugOverlay(!FuseSettings.ShowSceneryDebugOverlay);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Details",
                FuseSettings.ShowSceneryDebugAdvanced
                    ? (FuseSettings.ShowSceneryDebugOverlay ? "shown in overlay" : "shown when scenery probe is on")
                    : "hidden",
                FuseSettings.ShowSceneryDebugAdvanced ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugAdvanced(!FuseSettings.ShowSceneryDebugAdvanced);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "World Labels",
                FuseSettings.ShowWorldLabelsOverlay ? "color-coded labels on every visible entity" : "disabled",
                FuseSettings.ShowWorldLabelsOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowWorldLabelsOverlay(!FuseSettings.ShowWorldLabelsOverlay);
                    RebuildWindow();
                });
            if (FuseSettings.ShowWorldLabelsOverlay)
            {
                AddSettingToggle(
                    builder,
                    "  Labels: Scenery",
                    FuseSettings.WorldLabelsShowScenery ? "shown (orange=FUSE, gray=vanilla)" : "hidden",
                    FuseSettings.WorldLabelsShowScenery ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowScenery(!FuseSettings.WorldLabelsShowScenery);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Scene Clones",
                    FuseSettings.WorldLabelsShowSceneClones ? "shown (cyan)" : "hidden",
                    FuseSettings.WorldLabelsShowSceneClones ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowSceneClones(!FuseSettings.WorldLabelsShowSceneClones);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Industries",
                    FuseSettings.WorldLabelsShowIndustries ? "shown (pink)" : "hidden",
                    FuseSettings.WorldLabelsShowIndustries ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowIndustries(!FuseSettings.WorldLabelsShowIndustries);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Track Nodes",
                    FuseSettings.WorldLabelsShowTrackNodes ? "shown (green) — dense" : "hidden",
                    FuseSettings.WorldLabelsShowTrackNodes ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowTrackNodes(!FuseSettings.WorldLabelsShowTrackNodes);
                        RebuildWindow();
                    });
                AddSettingToggle(
                    builder,
                    "  Labels: Track Segments",
                    FuseSettings.WorldLabelsShowTrackSegments ? "shown (yellow) — dense" : "hidden",
                    FuseSettings.WorldLabelsShowTrackSegments ? "Hide" : "Show",
                    () =>
                    {
                        FuseSettings.SetWorldLabelsShowTrackSegments(!FuseSettings.WorldLabelsShowTrackSegments);
                        RebuildWindow();
                    });
            }
            builder.Spacer(4f);

            builder.AddSection("Experimental");
            AddSettingToggle(
                builder,
                "Early Suppression",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "enabled next map load" : "disabled",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetEnableExperimentalEarlyScenePathSuppression(!FuseSettings.EnableExperimentalEarlyScenePathSuppression);
                    RebuildWindow();
                });
            AddWrappedField(
                builder,
                "Inspector Roadmap",
                "Next step: add a safe scene/object inspector inspired by UnityRuntimeEditor, scoped to Railroader objects, FUSE claims, component health, and non-destructive property probes.",
                58f);
            builder.Spacer(8f);
        }

        private void BuildSettingsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("General");
            AddValueField(builder, "Asset Packs", AssetPackModeText());
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddSettingToggle(
                builder,
                "Multiplayer",
                FuseSettings.BlockNonHostMultiplayerClientWorldApply ? "Strict non-host block" : "Compatibility mode",
                FuseSettings.BlockNonHostMultiplayerClientWorldApply ? "Use Compat" : "Use Strict",
                () =>
                {
                    FuseSettings.SetBlockNonHostMultiplayerClientWorldApply(!FuseSettings.BlockNonHostMultiplayerClientWorldApply);
                    RebuildWindow();
                });
            builder.Spacer(4f);

            builder.AddSection("Reporting");
            AddSettingToggle(
                builder,
                "Verbose Report",
                FuseSettings.VerboseApplyReportDetails ? "enabled" : "disabled",
                FuseSettings.VerboseApplyReportDetails ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetVerboseApplyReportDetails(!FuseSettings.VerboseApplyReportDetails);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Advanced Details",
                FuseSettings.ShowAdvancedHealthDetails ? "visible in panels" : "hidden by default",
                FuseSettings.ShowAdvancedHealthDetails ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    RebuildWindow();
                });
            AddWrappedField(builder, "User Config", FuseSettings.GetUserSettingsPath(), 42f);
            builder.Spacer(6f);

            builder.AddSection("Package Settings");
            AddWrappedField(
                builder,
                "Location",
                "Mod-specific settings now live on each mod page. Open Mods, select a package, and its settings will appear in that package detail view.",
                52f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Open Mods", () => SetPage(Page.Packages));
            }, 6f).Height(32f);
            builder.Spacer(6f);

            builder.AddSection("Last Action");
            AddWrappedField(builder, "Last Action", _lastAction, 52f);
            AddWrappedField(builder, "Mod Settings", FuseModSettingsStore.LastStatus, 42f);
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            builder.Spacer(8f);
        }

        private void BuildPackageSettingsContent(UIPanelBuilder builder)
        {
            builder.AddSection("Mod Settings");
            AddWrappedField(
                builder,
                "Storage",
                "Package settings are stored outside mod folders at " + FuseModSettingsStore.GetStorePath(),
                48f);

            var packages = FuseModLoader.GetLoadedModsInOrder()
                .Where(loaded => loaded?.Definition?.Settings != null && loaded.Definition.Settings.Count > 0)
                .OrderBy(loaded => loaded.Definition.Name ?? loaded.Definition.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packages.Length == 0)
            {
                AddWrappedField(
                    builder,
                    "Status",
                    "No loaded package has declared settings yet. Add a top-level settings object to a FUSE definition to make controls appear here.",
                    52f);
                AddWrappedField(
                    builder,
                    "Schema",
                    "Supported controls: bool, enum, number, path, color, and text. Supported scopes: user, profile, and server.",
                    42f);
                builder.Spacer(6f);
                return;
            }

            AddValueField(builder, "Packages", packages.Length.ToString());
            foreach (var loaded in packages)
            {
                var visibleSettings = loaded.Definition.Settings
                    .Where(pair => pair.Value != null)
                    .Where(pair => FuseSettings.ShowAdvancedHealthDetails || !pair.Value.Advanced)
                    .OrderBy(pair => GetSettingLabel(pair.Key, pair.Value), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var hiddenAdvanced = loaded.Definition.Settings.Count(pair => pair.Value?.Advanced == true) - visibleSettings.Count(pair => pair.Value?.Advanced == true);
                if (visibleSettings.Length == 0)
                {
                    continue;
                }

                builder.Spacer(4f);
                builder.AddSection(PackageSettingsTitle(loaded));
                if (hiddenAdvanced > 0)
                {
                    AddWrappedField(builder, "Hidden", hiddenAdvanced + " advanced setting(s). Enable Advanced Details to show them.", 34f);
                }

                foreach (var pair in visibleSettings)
                {
                    BuildPackageSettingControl(builder, loaded.Definition, pair.Key, pair.Value);
                }
            }

            builder.Spacer(6f);
        }

        private void BuildPackageSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var type = FuseModSettingsStore.NormalizeType(setting?.Type);
            switch (type)
            {
                case "bool":
                    BuildBoolSettingControl(builder, definition, key, setting);
                    break;
                case "enum":
                    BuildEnumSettingControl(builder, definition, key, setting);
                    break;
                case "number":
                    BuildTextSettingControl(builder, definition, key, setting, isNumber: true);
                    break;
                case "path":
                case "color":
                case "text":
                default:
                    BuildTextSettingControl(builder, definition, key, setting, isNumber: false);
                    break;
            }

            if (FuseSettings.ShowAdvancedHealthDetails)
            {
                AddWrappedField(builder, " ", DescribePackageSetting(key, setting), 34f);
            }
            else if (!string.IsNullOrWhiteSpace(setting?.Description))
            {
                AddWrappedField(builder, " ", setting.Description, 34f);
            }
        }

        private void BuildBoolSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddToggle(
                    () => FuseModSettingsStore.GetBoolValue(definition, key, setting),
                    value =>
                    {
                        FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value));
                        RebuildWindow();
                    });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(30f);
        }

        private void BuildEnumSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            var values = (setting?.Values ?? Array.Empty<string>())
                .Where(value => value != null)
                .ToList();
            if (values.Count == 0)
            {
                BuildTextSettingControl(builder, definition, key, setting, isNumber: false);
                return;
            }

            var current = FuseModSettingsStore.GetStringValue(definition, key, setting);
            var selected = Math.Max(0, values.FindIndex(value => string.Equals(value, current, StringComparison.Ordinal)));
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddDropdown(values, selected, index =>
                {
                    if (index < 0 || index >= values.Count)
                    {
                        return;
                    }

                    FuseModSettingsStore.SetValue(definition, key, setting, new JValue(values[index]));
                    RebuildWindow();
                });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(32f);
        }

        private void BuildTextSettingControl(UIPanelBuilder builder, FuseModDefinition definition, string key, FuseModSettingDefinition setting, bool isNumber)
        {
            var current = isNumber
                ? FuseModSettingsStore.GetNumberValue(definition, key, setting).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                : FuseModSettingsStore.GetStringValue(definition, key, setting);
            builder.HStack(row =>
            {
                AddSettingRowLabel(row, GetSettingLabel(key, setting));
                row.AddInputField(current, value =>
                {
                    SaveTextSetting(definition, key, setting, value, isNumber);
                });
                AddResetSettingButton(row, definition, key, setting);
            }, 8f).Height(32f);
        }

        private void SaveTextSetting(FuseModDefinition definition, string key, FuseModSettingDefinition setting, string value, bool isNumber)
        {
            if (isNumber)
            {
                double parsed;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    _lastAction = $"Setting '{key}' was not saved because '{value}' is not a number.";
                    return;
                }

                FuseModSettingsStore.SetValue(definition, key, setting, new JValue(parsed));
                return;
            }

            FuseModSettingsStore.SetValue(definition, key, setting, new JValue(value ?? string.Empty));
        }

        private static void AddSettingRowLabel(UIPanelBuilder row, string label)
        {
            row.AddLabel(label, text =>
            {
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.Right;
            });
        }

        private void AddResetSettingButton(UIPanelBuilder row, FuseModDefinition definition, string key, FuseModSettingDefinition setting)
        {
            row.AddButtonCompact("Reset", () =>
            {
                FuseModSettingsStore.ResetValue(definition, key, setting);
                RebuildWindow();
            });
        }

        private static string PackageSettingsTitle(FuseLoadedMod loaded)
        {
            var definition = loaded?.Definition;
            if (definition == null)
            {
                return "Package Settings";
            }

            var name = string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name;
            return string.IsNullOrWhiteSpace(name) ? "Package Settings" : name;
        }

        private static string GetSettingLabel(string key, FuseModSettingDefinition setting)
        {
            return string.IsNullOrWhiteSpace(setting?.Label) ? key : setting.Label.Trim();
        }

        private static string DescribePackageSetting(string key, FuseModSettingDefinition setting)
        {
            var parts = new List<string>
            {
                "key=" + key,
                "type=" + FuseModSettingsStore.NormalizeType(setting?.Type),
                "scope=" + FuseModSettingsStore.DescribeScope(setting)
            };

            if (FuseModSettingsStore.FormatValue(setting?.Default).Length > 0)
            {
                parts.Add("default=" + FuseModSettingsStore.FormatValue(setting.Default));
            }

            if (setting?.ReloadRequired == true)
            {
                parts.Add("reload required");
            }

            if (!string.IsNullOrWhiteSpace(setting?.Description))
            {
                parts.Add(setting.Description);
            }

            return string.Join(" | ", parts.ToArray());
        }

        private void BuildModSetsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            builder.AddSection("Active Mod Set");
            AddValueField(builder, "Selected", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", FuseModSetService.GetActiveSetFingerprint());
            AddWrappedField(builder, "Enabled Mods", FuseModSetService.GetActiveSetPackageSummary(), 42f);
            AddWrappedField(
                builder,
                "Guide",
                "Use mod sets as server profiles. UMM decides which mods exist; FUSE sets only choose from UMM-active mods. If no set is selected, everything UMM-active is enabled.",
                58f);
            AddWrappedField(
                builder,
                "Apply",
                "Changes take effect on the next map load or FUSE reload. Share the profile hash and exported manifest with multiplayer players.",
                48f);
            AddWrappedLabel(builder, FuseModSetService.LastStatus, 34f);
            BuildModSetHealth(builder);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Create From Current", () =>
                {
                    FuseModSetService.CreateSetFromCurrentActiveMods();
                    RebuildWindow();
                });
                row.AddButtonCompact("Use All Active Mods", () =>
                {
                    FuseModSetService.ClearActiveSet();
                    RebuildWindow();
                });
                row.AddButtonCompact("Refresh", RebuildWindow);
                row.AddButtonCompact("Export Manifest", () =>
                {
                    RunAction("export active mod-set manifest", () => "Exported active mod-set manifest: " + FuseModSetService.ExportActiveManifest());
                });
            }, 6f).Height(32f);
            builder.Spacer(6f);

            builder.AddSection("Saved Sets");
            var sets = FuseModSetService.GetSets();
            if (sets.Count == 0)
            {
                AddValueField(builder, "Sets", "None");
            }
            else
            {
                foreach (var set in sets)
                {
                    var captured = set;
                    builder.HStack(row =>
                    {
                        row.AddLabel(
                            (string.Equals(FuseModSetService.ActiveSetId, captured.Id, StringComparison.OrdinalIgnoreCase) ? "* " : string.Empty) +
                            $"{captured.Name} ({captured.EnabledFolderNames.Length} mod folder(s))",
                            text =>
                            {
                                text.enableWordWrapping = false;
                                text.overflowMode = TextOverflowModes.Ellipsis;
                            });
                        row.AddButtonCompact("Select", () =>
                        {
                            FuseModSetService.SetActive(captured.Id);
                            RebuildWindow();
                        });
                        row.AddButtonCompact("Delete", () =>
                        {
                            FuseModSetService.DeleteSet(captured.Id);
                            RebuildWindow();
                        });
                    }, 6f).Height(30f);
                }
            }

            builder.Spacer(6f);
            builder.AddSection("UMM Active Mods");
            var activeMods = FuseModSetService.GetVisibleUmmMods();
            if (activeMods.Count == 0)
            {
                AddValueField(builder, "Mods", "None found through UMM");
            }
            else
            {
                foreach (var mod in activeMods)
                {
                    var captured = mod;
                    var enabled = FuseModSetService.IsModEnabledInActiveSet(captured);
                    builder.HStack(row =>
                    {
                        var version = string.IsNullOrWhiteSpace(captured.Version) ? string.Empty : " v" + captured.Version;
                        row.AddLabel(
                            $"{captured.DisplayName}{version} ({captured.FolderName})",
                            text =>
                            {
                                text.enableWordWrapping = false;
                                text.overflowMode = TextOverflowModes.Ellipsis;
                            });
                        row.AddButtonCompact(enabled ? "On" : "Off", () =>
                        {
                            FuseModSetService.ToggleModInActiveSet(captured);
                            RebuildWindow();
                        });
                    }, 6f).Height(30f);
                }
            }

            builder.Spacer(8f);
        }

        private static void BuildDependencyGraph(UIPanelBuilder builder, IReadOnlyList<FusePackageManifestSnapshot> manifests)
        {
            if (manifests == null || manifests.Count == 0)
            {
                AddValueField(builder, "Dependencies", "No packages discovered");
                return;
            }

            var byId = manifests
                .GroupBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var rows = 0;
            foreach (var manifest in manifests)
            {
                var hasEdges = manifest.LoadAfter.Length > 0 || manifest.LoadBefore.Length > 0 || manifest.Faults.Length > 0;
                if (!hasEdges && !FuseSettings.ShowAdvancedHealthDetails)
                {
                    continue;
                }

                if (!hasEdges)
                {
                    continue;
                }

                AddWrappedLabel(builder, InsertBreakHints(manifest.Id), 28f);
                foreach (var dependency in manifest.LoadAfter)
                {
                    AddWrappedLabel(builder, "     after -> " + InsertBreakHints(FormatDependencyEdge(dependency, byId)), 28f);
                    rows++;
                }

                foreach (var dependency in manifest.LoadBefore)
                {
                    AddWrappedLabel(builder, "     before -> " + InsertBreakHints(FormatDependencyEdge(dependency, byId)), 28f);
                    rows++;
                }

                foreach (var fault in manifest.Faults)
                {
                    AddWrappedLabel(builder, "     fault -> " + InsertBreakHints(fault), 42f);
                    rows++;
                }
            }

            if (rows == 0)
            {
                AddValueField(builder, "Dependencies", "No package dependency edges in current profile");
            }
        }

        private static string FormatDependencyEdge(string dependencyId, IDictionary<string, FusePackageManifestSnapshot> packages)
        {
            if (string.IsNullOrWhiteSpace(dependencyId))
            {
                return "(blank) | missing";
            }

            if (packages != null && packages.TryGetValue(dependencyId, out var dependency))
            {
                return dependency.Disabled
                    ? dependencyId + " | disabled"
                    : dependencyId + " | ready";
            }

            return dependencyId + " | missing";
        }

        private static void BuildModSetHealth(UIPanelBuilder builder)
        {
            var visible = FuseModSetService.GetVisibleUmmMods();
            var enabled = visible.Count(FuseModSetService.IsModEnabledInActiveSet);
            var disabledByProfile = Math.Max(0, visible.Count - enabled);

            builder.Spacer(4f);
            builder.AddSection("Profile Health");
            AddValueField(builder, "UMM Active", visible.Count.ToString());
            AddValueField(builder, "Enabled By Profile", enabled.ToString());
            AddValueField(builder, "Disabled By Profile", disabledByProfile.ToString());
            AddValueField(builder, "Mode", FuseModSetService.HasActiveSet ? "Server profile filter active" : "All UMM-active mods");
            AddWrappedField(
                builder,
                "Server Use",
                "Share the profile hash and exported manifest. FUSE does not change UMM enablement; it only filters UMM-active packages for this profile.",
                48f);
        }

        private void AddUnityRuntimeFields(UIPanelBuilder builder)
        {
            builder.AddField("FPS", () => _fpsAverage <= 0f ? "warming up" : _fpsAverage.ToString("0.0"), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Frame Time", () => _frameMilliseconds <= 0f ? "warming up" : _frameMilliseconds.ToString("0.0") + " ms", UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Managed Memory", () => FormatBytes(_managedMemoryBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Allocated", () => FormatBytes(_unityAllocatedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Reserved", () => FormatBytes(_unityReservedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            AddValueField(builder, "Active Scene", ActiveSceneName());
            AddValueField(builder, "Loaded Scenes", LoadedSceneSummary());
            AddValueField(builder, "Scene Roots", SafeCount(CountSceneRootObjects).ToString());
            AddValueField(builder, "GameObjects", SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length).ToString());
        }

        private static void AddRailroaderRuntimeFields(UIPanelBuilder builder)
        {
            AddValueField(builder, "Track Nodes", SafeCount(() => TrackAPI.GetAllNodes().Count()).ToString());
            AddValueField(builder, "Track Segments", SafeCount(() => TrackAPI.GetAllSegments().Count()).ToString());
            AddValueField(builder, "Track Spans", SafeCount(() => TrackAPI.GetAllSpans().Count()).ToString());
            AddValueField(builder, "Areas", SafeCount(() => TrackAPI.GetAllAreas().Count()).ToString());
            AddValueField(builder, "Loads", SafeCount(() => LoadAPI.GetAllLoads().Count()).ToString());
            AddValueField(builder, "Industries", SafeCount(() => IndustryAPI.GetAllIndustries().Count()).ToString());
            AddValueField(builder, "Loaders", SafeCount(() => LoaderAPI.GetAllLoaders().Count()).ToString());
            AddValueField(builder, "Stations", SafeCount(() => StationAPI.GetAllStationAgents().Count()).ToString());
            AddValueField(builder, "Passenger Stops", SafeCount(() => StationAPI.GetAllPassengerStops().Count()).ToString());
            AddValueField(builder, "Turntables", SafeCount(() => TurntableAPI.GetAllTurntables().Count()).ToString());
            AddValueField(builder, "Scenery", SafeCount(() => SceneryAPI.GetAllScenery().Count()).ToString());
            AddValueField(builder, "Scene Clones", SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()).ToString());
            AddValueField(builder, "Splineys", SafeCount(() => SplineyAPI.GetAllSplineys().Count()).ToString());
            AddValueField(builder, "Map Labels", SafeCount(() => MapAPI.GetAllMapLabels().Count()).ToString());
            AddValueField(builder, "Map Masks", SafeCount(() => MapAPI.GetAllMapMasks().Count()).ToString());
            AddValueField(builder, "Progressions", SafeCount(() => ProgressionAPI.GetAllProgressions().Count()).ToString());
            AddValueField(builder, "Map Features", SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count()).ToString());
        }

        private void BuildAdvancedObjectFinder(UIPanelBuilder builder)
        {
            AddWrappedField(
                builder,
                "Scope",
                "Read-only search across FUSE runtime indexes and loaded Unity scene objects. Use ids, names, or scene path fragments.",
                48f);
            builder.AddField(
                "Search",
                builder.AddInputField(_advancedSearchTerm ?? string.Empty, value =>
                {
                    _advancedSearchTerm = value ?? string.Empty;
                })).Height(32f);
            builder.HStack(row =>
            {
                row.AddButtonCompact("Run Search", RebuildWindow);
                row.AddButtonCompact("Clear", () =>
                {
                    _advancedSearchTerm = string.Empty;
                    RebuildWindow();
                });
                row.AddButtonCompact("Copy Results", () =>
                {
                    GUIUtility.systemCopyBuffer = BuildObjectSearchReport(_advancedSearchTerm);
                    _lastAction = "Copied FUSE object search results to clipboard.";
                    RebuildWindow();
                });
            }, 6f).Height(32f);

            var term = (_advancedSearchTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                AddWrappedField(builder, "Results", "Enter at least 2 characters, then Run Search.", 34f);
                return;
            }

            var results = BuildObjectSearchResults(term, 35);
            if (results.Count == 0)
            {
                AddWrappedField(builder, "Results", "No matching runtime or scene objects.", 34f);
                return;
            }

            AddValueField(builder, "Matches", results.Count.ToString());
            foreach (var result in results.Take(18))
            {
                AddWrappedLabel(builder, InsertBreakHints(result), 38f);
            }

            if (results.Count > 18)
            {
                AddWrappedField(builder, "More", (results.Count - 18) + " hidden. Copy Results for a longer report.", 34f);
            }
        }

        private static void AddReadinessRow(UIPanelBuilder builder, string label, bool ok, string okText, string problemText)
        {
            AddValueField(builder, label, ok ? "OK | " + okText : "Review | " + problemText);
        }

        private static void AddValueField(UIPanelBuilder builder, string label, string value)
        {
            builder.AddField(label, value).Height(26f);
        }

        private static string BuildDependencySummary(FusePackageManifestSnapshot manifest)
        {
            if (manifest == null)
            {
                return string.Empty;
            }

            var parts = new[]
            {
                manifest.LoadAfter.Length == 0 ? string.Empty : "after: " + string.Join(", ", manifest.LoadAfter),
                manifest.LoadBefore.Length == 0 ? string.Empty : "before: " + string.Join(", ", manifest.LoadBefore)
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

            return parts.Length == 0 ? string.Empty : "dependencies | " + string.Join(" | ", parts);
        }

        private static string BlankAs(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int SafeCount(Func<int> count)
        {
            try
            {
                return Math.Max(0, count());
            }
            catch
            {
                return 0;
            }
        }

        private static string ActiveSceneName()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                return scene.IsValid() ? BlankAs(scene.name, "(unnamed)") : "none";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string LoadedSceneSummary()
        {
            try
            {
                var names = new List<string>();
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        names.Add(BlankAs(scene.name, "(unnamed)"));
                    }
                }

                return names.Count == 0
                    ? "0"
                    : names.Count + " | " + string.Join(", ", names.Take(3).ToArray()) + (names.Count > 3 ? " +" + (names.Count - 3) : string.Empty);
            }
            catch
            {
                return "unknown";
            }
        }

        private static int CountSceneRootObjects()
        {
            var total = 0;
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                total += roots == null ? 0 : roots.Length;
            }

            return total;
        }

        private static string BuildStreamReadinessReport(JObject report)
        {
            var counts = report?["counts"] as JObject ?? new JObject();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Readiness");
            builder.AppendLine("State: " + (ReadBool(report?["hasProblems"], false) ? "Needs Attention" : "Ready"));
            builder.AppendLine("Summary: " + ReadString(report?["summary"], FuseLoadReport.LastSummary));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.AppendLine("Profile: " + FuseModSetService.ActiveSetName);
            builder.AppendLine("Profile Hash: " + FuseModSetService.GetActiveSetFingerprint());
            builder.AppendLine("Loaded Packages: " + ReadInt(counts["loadedPackages"]));
            builder.AppendLine("Applied Packages: " + ReadInt(counts["appliedPackages"]));
            builder.AppendLine("Faults: " + ReadInt(counts["faultedPackages"]));
            builder.AppendLine("Conflicts: " + ReadInt(counts["conflicts"]));
            builder.AppendLine("Unknown Assets: " + ReadInt(counts["unknownSceneryAssets"]));
            builder.AppendLine("Graph Issues: " + ReadInt(counts["graphIssues"]));
            builder.AppendLine("Transfer Skips: " + ReadInt(counts["progressionTransferSkips"]));
            builder.AppendLine("Suppressions: " + ReadInt(counts["suppressions"]));
            builder.AppendLine("Map Load: " + FusePerformanceMetrics.FormatTiming("map load total"));
            builder.AppendLine("Runtime Apply: " + FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            return builder.ToString().TrimEnd();
        }

        private string BuildRuntimeSnapshotText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Runtime Snapshot");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Version: FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion);
            builder.AppendLine();
            builder.AppendLine("Unity");
            builder.AppendLine("FPS: " + (_fpsAverage <= 0f ? "warming up" : _fpsAverage.ToString("0.0")));
            builder.AppendLine("Frame Time: " + (_frameMilliseconds <= 0f ? "warming up" : _frameMilliseconds.ToString("0.0") + " ms"));
            builder.AppendLine("Managed Memory: " + FormatBytes(_managedMemoryBytes));
            builder.AppendLine("Unity Allocated: " + FormatBytes(_unityAllocatedBytes));
            builder.AppendLine("Unity Reserved: " + FormatBytes(_unityReservedBytes));
            builder.AppendLine("Active Scene: " + ActiveSceneName());
            builder.AppendLine("Loaded Scenes: " + LoadedSceneSummary());
            builder.AppendLine("Scene Roots: " + SafeCount(CountSceneRootObjects));
            builder.AppendLine("GameObjects: " + SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length));
            builder.AppendLine();
            builder.AppendLine("Railroader");
            builder.AppendLine("Track Nodes: " + SafeCount(() => TrackAPI.GetAllNodes().Count()));
            builder.AppendLine("Track Segments: " + SafeCount(() => TrackAPI.GetAllSegments().Count()));
            builder.AppendLine("Track Spans: " + SafeCount(() => TrackAPI.GetAllSpans().Count()));
            builder.AppendLine("Areas: " + SafeCount(() => TrackAPI.GetAllAreas().Count()));
            builder.AppendLine("Loads: " + SafeCount(() => LoadAPI.GetAllLoads().Count()));
            builder.AppendLine("Industries: " + SafeCount(() => IndustryAPI.GetAllIndustries().Count()));
            builder.AppendLine("Loaders: " + SafeCount(() => LoaderAPI.GetAllLoaders().Count()));
            builder.AppendLine("Stations: " + SafeCount(() => StationAPI.GetAllStationAgents().Count()));
            builder.AppendLine("Passenger Stops: " + SafeCount(() => StationAPI.GetAllPassengerStops().Count()));
            builder.AppendLine("Scenery: " + SafeCount(() => SceneryAPI.GetAllScenery().Count()));
            builder.AppendLine();
            builder.AppendLine("FUSE Registry");
            builder.AppendLine("Exclusive Claims: " + FUSE.Registry.FuseRegistry.ExclusiveClaimCount);
            builder.AppendLine("Shared Claims: " + FUSE.Registry.FuseRegistry.SharedClaimCount);
            builder.AppendLine("Conflicts: " + FUSE.Registry.FuseRegistry.Conflicts.Count);
            return builder.ToString().TrimEnd();
        }

        private InspectorTarget ResolveSelectedInspectorTarget()
        {
            var targets = BuildInspectorTargets(_inspectorSearchTerm, 120);
            if (targets.Count == 0)
            {
                return null;
            }

            return targets.FirstOrDefault(target =>
                       string.Equals(target.Signature, _inspectorSelectedSignature, StringComparison.OrdinalIgnoreCase)) ??
                   targets[0];
        }

        private static List<InspectorTarget> BuildInspectorTargets(string rawTerm, int limit)
        {
            var results = new List<InspectorTarget>();
            var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var term = (rawTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return results;
            }

            limit = Math.Max(1, limit);
            AddInspectorIndexTargets(results, signatures, "Track Node", FuseNodeRuntimeIndex.Instance, FuseClaimKind.Node, term, limit);
            AddInspectorIndexTargets(results, signatures, "Track Segment", FuseSegmentRuntimeIndex.Instance, FuseClaimKind.Segment, term, limit);
            AddInspectorIndexTargets(results, signatures, "Track Span", FuseSpanRuntimeIndex.Instance, FuseClaimKind.Span, term, limit);
            AddInspectorIndexTargets(results, signatures, "Area", FuseAreaRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Load", FuseLoadRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Industry", FuseIndustryRuntimeIndex.Instance, FuseClaimKind.Industry, term, limit);
            AddInspectorIndexTargets(results, signatures, "Industry Component", FuseIndustryComponentRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Loader", FuseLoaderRuntimeIndex.Instance, FuseClaimKind.Loader, term, limit);
            AddInspectorIndexTargets(results, signatures, "Station", FuseStationRuntimeIndex.Instance, FuseClaimKind.Station, term, limit);
            AddInspectorIndexTargets(results, signatures, "Scenery", FuseSceneryRuntimeIndex.Instance, FuseClaimKind.Scenery, term, limit);
            AddInspectorIndexTargets(results, signatures, "Spliney", FuseSplineyRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Map Label", FuseMapLabelRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Progression", FuseProgressionRuntimeIndex.Instance, null, term, limit);
            AddInspectorIndexTargets(results, signatures, "Map Feature", FuseMapFeatureRuntimeIndex.Instance, null, term, limit);
            AddInspectorSceneTargets(results, signatures, term, limit);
            return results;
        }

        private static void AddInspectorIndexTargets<TCache>(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            string kind,
            FuseRuntimeIndex<TCache> index,
            FuseClaimKind? claimKind,
            string term,
            int limit)
            where TCache : FuseRuntimeIndex<TCache>
        {
            if (results == null || results.Count >= limit || index == null)
            {
                return;
            }

            foreach (var id in index.Ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var runtime = index[id];
                var gameObject = ResolveGameObject(runtime);
                var path = gameObject == null ? string.Empty : GetGameObjectPath(gameObject);
                var detail = FormatRuntimeObject(runtime);
                if (!MatchesSearch(id, term) &&
                    !MatchesSearch(path, term) &&
                    !MatchesSearch(detail, term) &&
                    !MatchesSearch(FormatComponentList(gameObject), term))
                {
                    continue;
                }

                AddInspectorTarget(
                    results,
                    signatures,
                    new InspectorTarget(kind, id, runtime, gameObject, path, claimKind));
            }
        }

        private static void AddInspectorSceneTargets(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            string term,
            int limit)
        {
            if (results == null || results.Count >= limit)
            {
                return;
            }

            GameObject[] objects;
            try
            {
                objects = Resources.FindObjectsOfTypeAll<GameObject>();
            }
            catch
            {
                return;
            }

            foreach (var gameObject in objects
                         .Where(IsLoadedSceneObject)
                         .OrderBy(GetGameObjectPath, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var path = GetGameObjectPath(gameObject);
                var components = FormatComponentList(gameObject);
                if (!MatchesSearch(gameObject.name, term) &&
                    !MatchesSearch(path, term) &&
                    !MatchesSearch(components, term))
                {
                    continue;
                }

                AddInspectorTarget(
                    results,
                    signatures,
                    new InspectorTarget("Scene Object", gameObject.name, gameObject, gameObject, path, null));
            }
        }

        private static void AddInspectorTarget(
            List<InspectorTarget> results,
            HashSet<string> signatures,
            InspectorTarget target)
        {
            if (results == null || signatures == null || target == null || !signatures.Add(target.Signature))
            {
                return;
            }

            results.Add(target);
        }

        private static GameObject ResolveGameObject(object runtime)
        {
            if (runtime is GameObject gameObject)
            {
                return gameObject;
            }

            if (runtime is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private static void BuildInspectorDetail(UIPanelBuilder builder, InspectorTarget target)
        {
            if (target == null)
            {
                AddValueField(builder, "Target", "None");
                return;
            }

            builder.AddSection("Selected Object");
            AddValueField(builder, "Kind", target.Kind);
            AddWrappedField(builder, "Id", target.Id, 36f);
            AddWrappedField(builder, "Scene Path", BlankAs(target.ScenePath, "not bound to a scene object"), 58f);
            AddValueField(builder, "Runtime Type", target.RuntimeObject == null ? "<null>" : target.RuntimeObject.GetType().FullName);
            AddValueField(builder, "Registry Claim", DescribeRegistryClaim(target));

            var gameObject = target.GameObject;
            if (gameObject == null)
            {
                AddWrappedField(builder, "Unity Object", "No GameObject is bound to this runtime entry.", 36f);
                return;
            }

            AddValueField(builder, "Active", $"self={gameObject.activeSelf} hierarchy={gameObject.activeInHierarchy}");
            AddValueField(builder, "Layer/Tag", $"{LayerMask.LayerToName(gameObject.layer)} ({gameObject.layer}) | {gameObject.tag}");
            AddValueField(builder, "Parent", gameObject.transform.parent == null ? "none" : GetGameObjectPath(gameObject.transform.parent.gameObject));
            AddValueField(builder, "Children", gameObject.transform.childCount.ToString());
            AddValueField(builder, "Position", FormatVector3(gameObject.transform.position));
            AddValueField(builder, "Rotation", FormatVector3(gameObject.transform.rotation.eulerAngles));
            AddValueField(builder, "Scale", FormatVector3(gameObject.transform.lossyScale));
            AddWrappedField(builder, "Components", FormatComponentList(gameObject), 54f);
            AddWrappedField(builder, "Children Preview", FormatChildPreview(gameObject), 54f);
        }

        private static string BuildInspectorReport(InspectorTarget target)
        {
            if (target == null)
            {
                return "FUSE Inspector\nNo target selected.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("FUSE Inspector");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Kind: " + target.Kind);
            builder.AppendLine("Id: " + target.Id);
            builder.AppendLine("Scene Path: " + BlankAs(target.ScenePath, "not bound to a scene object"));
            builder.AppendLine("Runtime Type: " + (target.RuntimeObject == null ? "<null>" : target.RuntimeObject.GetType().FullName));
            builder.AppendLine("Registry Claim: " + DescribeRegistryClaim(target));

            var gameObject = target.GameObject;
            if (gameObject == null)
            {
                builder.AppendLine("GameObject: none");
                return builder.ToString().TrimEnd();
            }

            builder.AppendLine("Active Self: " + gameObject.activeSelf);
            builder.AppendLine("Active In Hierarchy: " + gameObject.activeInHierarchy);
            builder.AppendLine("Layer: " + LayerMask.LayerToName(gameObject.layer) + " (" + gameObject.layer + ")");
            builder.AppendLine("Tag: " + gameObject.tag);
            builder.AppendLine("Parent: " + (gameObject.transform.parent == null ? "none" : GetGameObjectPath(gameObject.transform.parent.gameObject)));
            builder.AppendLine("Children: " + gameObject.transform.childCount);
            builder.AppendLine("Position: " + FormatVector3(gameObject.transform.position));
            builder.AppendLine("Rotation: " + FormatVector3(gameObject.transform.rotation.eulerAngles));
            builder.AppendLine("Scale: " + FormatVector3(gameObject.transform.lossyScale));
            builder.AppendLine("Components: " + FormatComponentList(gameObject));
            builder.AppendLine("Children Preview: " + FormatChildPreview(gameObject));
            return builder.ToString().TrimEnd();
        }

        private static List<string> BuildObjectSearchResults(string rawTerm, int limit)
        {
            var results = new List<string>();
            var term = (rawTerm ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return results;
            }

            limit = Math.Max(1, limit);
            AddRuntimeIndexMatches(results, "Track Node", FuseNodeRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Track Segment", FuseSegmentRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Track Span", FuseSpanRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Area", FuseAreaRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Load", FuseLoadRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Industry", FuseIndustryRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Industry Component", FuseIndustryComponentRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Loader", FuseLoaderRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Station", FuseStationRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Scenery", FuseSceneryRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Spliney", FuseSplineyRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Map Label", FuseMapLabelRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Progression", FuseProgressionRuntimeIndex.Instance, term, limit);
            AddRuntimeIndexMatches(results, "Map Feature", FuseMapFeatureRuntimeIndex.Instance, term, limit);
            AddSceneObjectMatches(results, term, limit);
            return results;
        }

        private static void AddRuntimeIndexMatches<TCache>(
            List<string> results,
            string kind,
            FuseRuntimeIndex<TCache> index,
            string term,
            int limit)
            where TCache : FuseRuntimeIndex<TCache>
        {
            if (results == null || results.Count >= limit || index == null)
            {
                return;
            }

            foreach (var id in index.Ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var runtime = index[id];
                var detail = FormatRuntimeObject(runtime);
                if (!MatchesSearch(id, term) && !MatchesSearch(detail, term))
                {
                    continue;
                }

                results.Add($"{kind} | {id} | {detail}");
            }
        }

        private static void AddSceneObjectMatches(List<string> results, string term, int limit)
        {
            if (results == null || results.Count >= limit)
            {
                return;
            }

            GameObject[] objects;
            try
            {
                objects = Resources.FindObjectsOfTypeAll<GameObject>();
            }
            catch
            {
                return;
            }

            foreach (var gameObject in objects
                         .Where(IsLoadedSceneObject)
                         .OrderBy(GetGameObjectPath, StringComparer.OrdinalIgnoreCase))
            {
                if (results.Count >= limit)
                {
                    return;
                }

                var path = GetGameObjectPath(gameObject);
                if (!MatchesSearch(gameObject.name, term) && !MatchesSearch(path, term))
                {
                    continue;
                }

                results.Add($"Scene Object | {path} | active={gameObject.activeInHierarchy} | components={FormatComponentList(gameObject)}");
            }
        }

        private static string BuildObjectSearchReport(string rawTerm)
        {
            var term = (rawTerm ?? string.Empty).Trim();
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Object Search");
            builder.AppendLine("Search: " + (term.Length == 0 ? "(blank)" : term));
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (term.Length < 2)
            {
                builder.AppendLine("Enter at least 2 characters before searching.");
                return builder.ToString().TrimEnd();
            }

            var results = BuildObjectSearchResults(term, 200);
            builder.AppendLine("Matches: " + results.Count);
            foreach (var result in results)
            {
                builder.AppendLine("- " + result);
            }

            return builder.ToString().TrimEnd();
        }

        private static List<AuditFinding> BuildAuditFindings()
        {
            var findings = new List<AuditFinding>();
            AddHealthReportAuditFindings(findings);
            AddTrackSpanAuditFindings(findings);
            AddIndustryAuditFindings(findings);
            AddLoaderAuditFindings(findings);
            AddPassengerAuditFindings(findings);
            AddSuppressionAuditFindings(findings);
            return findings
                .OrderBy(finding => SeverityRank(finding.Severity))
                .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(finding => finding.ObjectId, StringComparer.OrdinalIgnoreCase)
                .Take(300)
                .ToList();
        }

        private static void AddHealthReportAuditFindings(List<AuditFinding> findings)
        {
            var report = LoadReportJson();

            foreach (var fault in report["packages"]?["faults"] as JArray ?? new JArray())
            {
                AddFinding(
                    findings,
                    "Critical",
                    "Package fault",
                    ReadString(fault["packageId"], "(unknown package)"),
                    ReadString(fault["message"], "Package failed during load/apply."),
                    "Open Issues, inspect the package fault stage, then fix the source package or compatibility layer.");
            }

            foreach (var asset in report["unknownSceneryAssets"] as JArray ?? new JArray())
            {
                AddFinding(
                    findings,
                    "High",
                    "Unknown scenery asset",
                    ReadString(asset["sceneryId"], "(unknown scenery)"),
                    $"{ReadString(asset["packageId"], "(unknown package)")} references {ReadString(asset["assetIdentifier"], "(blank asset)")}",
                    "Check asset pack discovery and exact model identifier spelling before changing converter output.");
            }

            foreach (var issue in report["graphPostBindIssues"] as JArray ?? new JArray())
            {
                AddFinding(
                    findings,
                    "High",
                    "Graph post-bind issue",
                    "track graph",
                    issue.ToString(),
                    "Inspect the owning track package and verify deleted/replaced node, segment, and span ids.");
            }

            foreach (var skip in report["progressionTransferSkips"] as JArray ?? new JArray())
            {
                AddFinding(
                    findings,
                    "Medium",
                    "Progression transfer skip",
                    "progression",
                    skip.ToString(),
                    "Verify the referenced industry, load, scene object, or map feature exists after FUSE apply.");
            }

            foreach (var conflict in report["conflicts"] as JArray ?? new JArray())
            {
                AddFinding(
                    findings,
                    "Medium",
                    "Registry conflict",
                    ReadString(conflict["objectId"], "(unknown id)"),
                    $"{ReadString(conflict["ownerPackageId"], "(unknown owner)")} kept over {ReadString(conflict["attemptedPackageId"], "(unknown package)")}",
                    "Confirm whether both packages should layer shared data or whether one needs load-order/removal handling.");
            }
        }

        private static void AddTrackSpanAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var span in TrackAPI.GetAllSpans() ?? Enumerable.Empty<TrackSpan>())
                {
                    var id = span?.id ?? "(blank span)";
                    var definition = TrackAPI.GetDefinition(span);
                    if (definition?.Upper == null || definition.Lower == null)
                    {
                        AddFinding(findings, "High", "Invalid track span", id, "Span definition has missing upper/lower location.", "Inspect the source span and repair or remove invalid endpoints.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(definition.Upper.SegmentId) ||
                        string.IsNullOrWhiteSpace(definition.Lower.SegmentId))
                    {
                        AddFinding(findings, "High", "Invalid track span", id, "Span endpoint is missing a segment id.", "Repair the span endpoint segment references in the source package.");
                        continue;
                    }

                    if (TrackAPI.GetSegment(definition.Upper.SegmentId) == null ||
                        TrackAPI.GetSegment(definition.Lower.SegmentId) == null)
                    {
                        AddFinding(findings, "High", "Orphaned track span", id, $"References {definition.Upper.SegmentId} / {definition.Lower.SegmentId}.", "Make sure the referenced segments survive final graph merge, or remove this span.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Track span audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddIndustryAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var industry in IndustryAPI.GetAllIndustries() ?? Enumerable.Empty<Industry>())
                {
                    if (industry == null)
                    {
                        continue;
                    }

                    var id = BlankAs(industry.identifier, industry.name);
                    if (string.IsNullOrWhiteSpace(industry.identifier))
                    {
                        AddFinding(findings, "Medium", "Industry missing identifier", id, GetGameObjectPath(industry.gameObject), "Assign a stable industry identifier or remove the orphan scene object.");
                    }

                    var definition = IndustryAPI.GetDefinition(industry);
                    if (definition == null || definition.Components == null || definition.Components.Count == 0)
                    {
                        AddFinding(findings, "Low", "Industry has no components", id, GetGameObjectPath(industry.gameObject), "Verify whether this is scenery-only, a disabled vanilla industry, or a broken industry component binding.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Industry audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddLoaderAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var loader in LoaderAPI.GetAllLoaders() ?? Enumerable.Empty<GameObject>())
                {
                    if (loader == null)
                    {
                        continue;
                    }

                    var definition = LoaderAPI.GetDefinition(loader);
                    if (definition == null)
                    {
                        AddFinding(findings, "Medium", "Loader missing FUSE definition", loader.name, GetGameObjectPath(loader), "Check whether the loader came from a legacy plugin path that FUSE cannot rehydrate.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(definition.IndustryId) && IndustryAPI.GetIndustry(definition.IndustryId) == null)
                    {
                        AddFinding(findings, "High", "Loader industry missing", loader.name, $"industryId={definition.IndustryId}", "Create/restore the referenced industry before loader apply, or update the loader industry id.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Loader audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddPassengerAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                var stationCount = SafeCount(() => StationAPI.GetAllStationAgents().Count());
                var stopCount = SafeCount(() => StationAPI.GetAllPassengerStops().Count());
                if (stationCount > 0 && stopCount == 0)
                {
                    AddFinding(findings, "Critical", "No passenger stops", "passenger system", $"{stationCount} station(s), 0 passenger stop(s).", "Passenger cars need PassengerStop bindings. Check station apply and passenger stop creation.");
                }

                foreach (var station in StationAPI.GetAllStationAgents() ?? Enumerable.Empty<StationAgent>())
                {
                    if (station == null)
                    {
                        continue;
                    }

                    var definition = StationAPI.GetDefinition(station);
                    if (!string.IsNullOrWhiteSpace(definition?.PassengerStopId) &&
                        StationAPI.GetPassengerStop(definition.PassengerStopId) == null)
                    {
                        AddFinding(findings, "High", "Station passenger stop missing", station.name, $"passengerStopId={definition.PassengerStopId}", "Create/restore the passenger stop or update the station passengerStopId.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Passenger audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddSuppressionAuditFindings(List<AuditFinding> findings)
        {
            try
            {
                foreach (var path in FuseRegistry.GetClaimedIds(FuseClaimKind.SuppressedScenePath))
                {
                    var target = FusePrefabResolver.ResolveScenePath(path) ?? GameObject.Find(path);
                    if (target == null)
                    {
                        continue;
                    }

                    var visibleRenderers = target
                        .GetComponentsInChildren<Renderer>(true)
                        .Count(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff);
                    if (target.activeInHierarchy && visibleRenderers > 0)
                    {
                        AddFinding(findings, "Medium", "Suppressed scene object still visible", path, $"activeInHierarchy=true visibleRenderers={visibleRenderers}", "Run Advanced > Reload Track/Data or inspect the object; suppression may be missing a child renderer/culler path.");
                    }
                }
            }
            catch (Exception ex)
            {
                AddFinding(findings, "Low", "Suppression audit failed", "audit", ex.GetBaseException().Message, "Check FUSE.log for the exception and rerun audits after reload.");
            }
        }

        private static void AddFinding(List<AuditFinding> findings, string severity, string title, string objectId, string detail, string action)
        {
            findings?.Add(new AuditFinding(severity, title, objectId, detail, action));
        }

        private static int SeverityRank(string severity)
        {
            switch (severity)
            {
                case "Critical":
                    return 0;
                case "High":
                    return 1;
                case "Medium":
                    return 2;
                case "Low":
                    return 3;
                default:
                    return 4;
            }
        }

        private static string BuildAuditReport(IReadOnlyList<AuditFinding> findings)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Audit Report");
            builder.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Findings: " + (findings?.Count ?? 0));
            foreach (var finding in findings ?? Array.Empty<AuditFinding>())
            {
                builder.AppendLine();
                builder.AppendLine(finding.Severity + " | " + finding.Title);
                builder.AppendLine("Object: " + finding.ObjectId);
                builder.AppendLine("Detail: " + finding.Detail);
                builder.AppendLine("Action: " + finding.Action);
            }

            return builder.ToString().TrimEnd();
        }

        private static string ExportAuditReport(IReadOnlyList<AuditFinding> findings)
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-audit-report.json");
            var items = new JArray();
            foreach (var finding in findings ?? Array.Empty<AuditFinding>())
            {
                items.Add(new JObject
                {
                    ["severity"] = finding.Severity,
                    ["title"] = finding.Title,
                    ["objectId"] = finding.ObjectId,
                    ["detail"] = finding.Detail,
                    ["action"] = finding.Action
                });
            }

            File.WriteAllText(path, new JObject
            {
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["count"] = items.Count,
                ["findings"] = items
            }.ToString(Newtonsoft.Json.Formatting.Indented));
            return "Exported FUSE audit report: " + path;
        }

        private static string FormatRuntimeObject(object runtime)
        {
            if (runtime == null)
            {
                return "<null>";
            }

            if (runtime is GameObject gameObject)
            {
                return "GameObject " + GetGameObjectPath(gameObject);
            }

            if (runtime is Component component)
            {
                return component.GetType().Name + " " + GetGameObjectPath(component.gameObject);
            }

            return runtime.GetType().Name;
        }

        private static bool IsLoadedSceneObject(GameObject gameObject)
        {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        private static string GetGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            try
            {
                var names = new Stack<string>();
                var current = gameObject.transform;
                while (current != null)
                {
                    names.Push(BlankAs(current.name, "(unnamed)"));
                    current = current.parent;
                }

                var sceneName = gameObject.scene.IsValid() ? BlankAs(gameObject.scene.name, "(scene)") : "(no scene)";
                return sceneName + "/" + string.Join("/", names.ToArray());
            }
            catch
            {
                return gameObject.name ?? "<unnamed>";
            }
        }

        private static string FormatComponentList(GameObject gameObject)
        {
            try
            {
                if (gameObject == null)
                {
                    return "none";
                }

                var names = gameObject.GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().Name)
                    .Take(6)
                    .ToArray();
                return names.Length == 0 ? "none" : string.Join(",", names);
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string FormatChildPreview(GameObject gameObject)
        {
            try
            {
                if (gameObject == null || gameObject.transform.childCount == 0)
                {
                    return "none";
                }

                var names = new List<string>();
                for (var index = 0; index < gameObject.transform.childCount && names.Count < 8; index++)
                {
                    var child = gameObject.transform.GetChild(index);
                    if (child != null)
                    {
                        names.Add(child.name);
                    }
                }

                var suffix = gameObject.transform.childCount > names.Count
                    ? " +" + (gameObject.transform.childCount - names.Count)
                    : string.Empty;
                return names.Count == 0 ? "none" : string.Join(", ", names.ToArray()) + suffix;
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string FormatVector3(Vector3 value)
        {
            return value.x.ToString("0.###") + ", " + value.y.ToString("0.###") + ", " + value.z.ToString("0.###");
        }

        private static string DescribeRegistryClaim(InspectorTarget target)
        {
            if (target == null || !target.ClaimKind.HasValue || string.IsNullOrWhiteSpace(target.Id))
            {
                return "not claim-tracked";
            }

            var kind = target.ClaimKind.Value;
            if (kind == FuseClaimKind.Industry ||
                kind == FuseClaimKind.Scenery ||
                kind == FuseClaimKind.SuppressedArea ||
                kind == FuseClaimKind.SuppressedScenePath ||
                kind == FuseClaimKind.SuppressedTrackGroup)
            {
                var owners = FuseRegistry.GetSharedOwners(kind, target.Id).ToArray();
                return owners.Length == 0 ? "shared | unclaimed" : "shared | " + string.Join(", ", owners);
            }

            var owner = FuseRegistry.GetExclusiveOwner(kind, target.Id);
            return string.IsNullOrWhiteSpace(owner) ? "exclusive | unclaimed" : "exclusive | " + owner;
        }

        private static bool MatchesSearch(string value, string term)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(term) &&
                   value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExportHealthReportJson()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-health-report.json");
            File.WriteAllText(path, FuseLoadReport.GetLastJsonReport());
            return "Exported FUSE health JSON report: " + path;
        }

        private string ExportDebugBundle()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-debug-bundle.json");
            var diagnostics = FuseAssetPackRegistry.GetDiagnostics();
            var loadedScenes = new JArray();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded)
                {
                    loadedScenes.Add(new JObject
                    {
                        ["name"] = scene.name ?? string.Empty,
                        ["rootObjects"] = SafeCount(() => scene.GetRootGameObjects().Length)
                    });
                }
            }

            var bundle = new JObject
            {
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["version"] = ReadVersion(),
                ["schema"] = FuseMigration.CurrentVersion.ToString(),
                ["profile"] = FuseModSetService.ActiveSetName,
                ["profileHash"] = FuseModSetService.GetActiveSetFingerprint(),
                ["health"] = JObject.Parse(FuseLoadReport.GetLastJsonReport()),
                ["unity"] = new JObject
                {
                    ["fps"] = _fpsAverage,
                    ["frameMilliseconds"] = _frameMilliseconds,
                    ["managedMemoryBytes"] = _managedMemoryBytes,
                    ["unityAllocatedBytes"] = _unityAllocatedBytes,
                    ["unityReservedBytes"] = _unityReservedBytes,
                    ["activeScene"] = ActiveSceneName(),
                    ["loadedScenes"] = loadedScenes,
                    ["sceneRootObjects"] = SafeCount(CountSceneRootObjects),
                    ["gameObjects"] = SafeCount(() => Resources.FindObjectsOfTypeAll<GameObject>().Length)
                },
                ["railroader"] = new JObject
                {
                    ["trackNodes"] = SafeCount(() => TrackAPI.GetAllNodes().Count()),
                    ["trackSegments"] = SafeCount(() => TrackAPI.GetAllSegments().Count()),
                    ["trackSpans"] = SafeCount(() => TrackAPI.GetAllSpans().Count()),
                    ["areas"] = SafeCount(() => TrackAPI.GetAllAreas().Count()),
                    ["loads"] = SafeCount(() => LoadAPI.GetAllLoads().Count()),
                    ["industries"] = SafeCount(() => IndustryAPI.GetAllIndustries().Count()),
                    ["loaders"] = SafeCount(() => LoaderAPI.GetAllLoaders().Count()),
                    ["stations"] = SafeCount(() => StationAPI.GetAllStationAgents().Count()),
                    ["passengerStops"] = SafeCount(() => StationAPI.GetAllPassengerStops().Count()),
                    ["turntables"] = SafeCount(() => TurntableAPI.GetAllTurntables().Count()),
                    ["scenery"] = SafeCount(() => SceneryAPI.GetAllScenery().Count()),
                    ["sceneClones"] = SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()),
                    ["splineys"] = SafeCount(() => SplineyAPI.GetAllSplineys().Count()),
                    ["mapLabels"] = SafeCount(() => MapAPI.GetAllMapLabels().Count()),
                    ["mapMasks"] = SafeCount(() => MapAPI.GetAllMapMasks().Count()),
                    ["progressions"] = SafeCount(() => ProgressionAPI.GetAllProgressions().Count()),
                    ["mapFeatures"] = SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count())
                },
                ["registry"] = new JObject
                {
                    ["exclusiveClaims"] = FUSE.Registry.FuseRegistry.ExclusiveClaimCount,
                    ["sharedClaims"] = FUSE.Registry.FuseRegistry.SharedClaimCount,
                    ["conflicts"] = FUSE.Registry.FuseRegistry.Conflicts.Count
                },
                ["assets"] = new JObject
                {
                    ["mode"] = AssetPackModeText(),
                    ["storesScanned"] = diagnostics.StoreFolders?.Length ?? 0,
                    ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                    ["duplicateKeys"] = diagnostics.DuplicateKeys?.Length ?? 0,
                    ["failedDefinitions"] = diagnostics.FailedDefinitionLoads?.Length ?? 0
                },
                ["lastFuseLogLines"] = new JArray(ReadLastLogLines(80))
            };

            File.WriteAllText(path, bundle.ToString(Newtonsoft.Json.Formatting.Indented));
            return "Exported FUSE debug bundle: " + path;
        }

        private static string ExportAssetDiagnostics(FuseAssetPackDiagnostics diagnostics)
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-asset-diagnostics.json");

            var stores = new JArray();
            foreach (var folder in diagnostics.StoreFolders ?? Array.Empty<string>())
            {
                stores.Add(folder);
            }

            var duplicates = new JArray();
            foreach (var duplicate in diagnostics.DuplicateKeys ?? Array.Empty<FuseDuplicateAssetKey>())
            {
                var sources = new JArray();
                foreach (var source in duplicate.Sources ?? Array.Empty<string>())
                {
                    sources.Add(source);
                }

                duplicates.Add(new JObject
                {
                    ["key"] = duplicate.Key ?? string.Empty,
                    ["sourceCount"] = sources.Count,
                    ["winner"] = sources.Count > 0 ? sources[0] : string.Empty,
                    ["overridden"] = new JArray(sources.Skip(1)),
                    ["sources"] = sources
                });
            }

            var failedDefinitions = new JArray();
            foreach (var failure in diagnostics.FailedDefinitionLoads ?? Array.Empty<string>())
            {
                failedDefinitions.Add(failure);
            }

            var report = new JObject
            {
                ["exportedUtc"] = DateTime.UtcNow.ToString("O"),
                ["mode"] = AssetPackModeText(),
                ["storesScanned"] = stores.Count,
                ["uniqueAssetKeys"] = diagnostics.UniqueAssetKeys,
                ["duplicateKeyCount"] = duplicates.Count,
                ["failedDefinitionLoadCount"] = failedDefinitions.Count,
                ["stores"] = stores,
                ["duplicateKeys"] = duplicates,
                ["failedDefinitionLoads"] = failedDefinitions
            };

            File.WriteAllText(path, report.ToString(Newtonsoft.Json.Formatting.Indented));
            return "Exported FUSE asset diagnostics: " + path;
        }

        private static string BuildAssetSummary(FuseAssetPackDiagnostics diagnostics)
        {
            var builder = new StringBuilder();
            builder.AppendLine("FUSE Asset Summary");
            builder.AppendLine("Mode: " + AssetPackModeText());
            builder.AppendLine("Stores scanned: " + (diagnostics.StoreFolders?.Length ?? 0));
            builder.AppendLine("Unique asset keys: " + diagnostics.UniqueAssetKeys);
            builder.AppendLine("Duplicate keys: " + (diagnostics.DuplicateKeys?.Length ?? 0));
            builder.AppendLine("Failed definitions: " + (diagnostics.FailedDefinitionLoads?.Length ?? 0));

            var duplicates = diagnostics.DuplicateKeys ?? Array.Empty<FuseDuplicateAssetKey>();
            if (duplicates.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Duplicate preview:");
                foreach (var duplicate in duplicates.Take(10))
                {
                    builder.AppendLine("- " + BuildDuplicateAssetPreview(duplicate));
                }

                if (duplicates.Length > 10)
                {
                    builder.AppendLine("- " + (duplicates.Length - 10) + " more duplicate key(s); export the asset report for the full list.");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string AssetPackModeText()
        {
            if (FuseSettings.MirrorAssetPacksToLocalLow)
            {
                return "LocalLow mirror fallback";
            }

            return "Direct stores";
        }

        private static string BuildDuplicateAssetPreview(FuseDuplicateAssetKey duplicate)
        {
            if (duplicate == null)
            {
                return string.Empty;
            }

            var sources = duplicate.Sources ?? Array.Empty<string>();
            var preview = sources
                .Take(3)
                .Select(source =>
                {
                    var name = string.IsNullOrWhiteSpace(source) ? string.Empty : Path.GetFileName(source);
                    return string.IsNullOrWhiteSpace(name) ? source : name;
                })
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .ToArray();

            var suffix = sources.Length > preview.Length ? " +" + (sources.Length - preview.Length) + " more" : string.Empty;
            if (preview.Length == 0)
            {
                return InsertBreakHints(duplicate.Key);
            }

            var winner = preview[0];
            var overridden = preview.Skip(1).ToArray();
            return overridden.Length == 0
                ? $"{InsertBreakHints(duplicate.Key)} | winner {winner}{suffix}"
                : $"{InsertBreakHints(duplicate.Key)} | winner {winner} | overridden {string.Join(", ", overridden)}{suffix}";
        }

        private static string FriendlyTimingText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return InsertBreakHints(value
                .Replace("__merged-graph-rebuild__", "merged graph rebuild")
                .Replace("merged-single-graph-rebuild", "single graph rebuild")
                .Replace("apply-resident-definitions", "runtime apply"));
        }

        private static string BuildSlowestDetailText()
        {
            var package = FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPackage());
            var phase = FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPhase());
            if (string.IsNullOrWhiteSpace(phase))
            {
                return "No timing sample yet.";
            }

            if (NormalizeTimingName(package).StartsWith("merged graph rebuild", StringComparison.OrdinalIgnoreCase) &&
                NormalizeTimingName(phase).Contains("single graph rebuild"))
            {
                return "single graph rebuild inside merged graph rebuild";
            }

            return phase;
        }

        private static string NormalizeTimingName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var paren = value.IndexOf('(');
            return (paren >= 0 ? value.Substring(0, paren) : value).Trim();
        }

        private static string InsertBreakHints(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("_", "_ ")
                .Replace("-", "- ")
                .Replace(".", ". ")
                .Replace("/", " / ")
                .Replace("\\", " \\ ")
                .Replace("|", " | ");
        }

        private static string[] ReadLastLogLines(int maxLines)
        {
            try
            {
                var path = FuseLog.LogFilePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return Array.Empty<string>();
                }

                var lines = File.ReadAllLines(path);
                return lines.Skip(Math.Max(0, lines.Length - Math.Max(1, maxLines))).ToArray();
            }
            catch (Exception ex)
            {
                return new[] { "Could not read FUSE.log: " + ex.GetBaseException().Message };
            }
        }

        private static void AddWrappedField(UIPanelBuilder builder, string label, string value, float height)
        {
            builder.AddField(label, AddWrappedLabel(builder, value, height)).Height(height);
        }

        private static RectTransform AddWrappedLabel(UIPanelBuilder builder, string value, float height)
        {
            return builder.AddLabel(value ?? string.Empty, text =>
            {
                text.enableWordWrapping = true;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.Left;
            }).Height(height);
        }

        private static void AddCountField(UIPanelBuilder builder, string label, JObject counts, string key)
        {
            builder.AddField(label, () => ReadInt(counts[key]).ToString(), 0).Height(24f);
        }

        private static void AddSettingToggle(UIPanelBuilder builder, string label, string value, string buttonText, Action toggle)
        {
            builder.HStack(row =>
            {
                row.AddLabel(label, text =>
                {
                    text.enableWordWrapping = false;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.alignment = TextAlignmentOptions.Right;
                });
                row.AddLabel(value, text =>
                {
                    text.enableWordWrapping = false;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.alignment = TextAlignmentOptions.Left;
                });
                row.AddButtonCompact(buttonText, toggle);
            }, 8f).Height(30f);
        }

        private static int AddProblemSummary(UIPanelBuilder builder, JObject report, string parentKey, string key, string label, bool showZero)
        {
            JToken token = string.IsNullOrWhiteSpace(parentKey) ? report[key] : report[parentKey]?[key];
            var count = token is JArray array ? array.Count : 0;
            if (count == 0 && !showZero)
            {
                return 0;
            }

            builder.AddField(label, () => count == 0 ? "0" : count + " - see /fuse.report", 0).Height(24f);
            return 1;
        }

        private static bool HasReportProblems(JObject report)
        {
            if (report == null)
            {
                return false;
            }

            if (ReadBool(report["hasProblems"], false))
            {
                return true;
            }

            return CountArray(report["packages"]?["faults"]) > 0 ||
                   CountArray(report["conflicts"]) > 0 ||
                   CountArray(report["unknownSceneryAssets"]) > 0 ||
                   CountArray(report["graphPostBindIssues"]) > 0 ||
                   CountArray(report["progressionTransferSkips"]) > 0 ||
                   CountArray(report["notices"]) > 0;
        }

        private static int CountArray(JToken token)
        {
            return token is JArray array ? array.Count : 0;
        }

        private static long ReadProfilerMetric(Func<long> metric)
        {
            try
            {
                return metric();
            }
            catch
            {
                return 0L;
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0L)
            {
                return "n/a";
            }

            return (bytes / 1048576f).ToString("0.0") + " MB";
        }

        private void RunAction(string actionName, Func<string> action)
        {
            try
            {
                _lastAction = action();
                RebuildWindow();
            }
            catch (Exception ex)
            {
                _lastAction = $"FUSE {actionName} failed: {ex.GetBaseException().Message}";
                FuseLog.Exception($"FUSE health page action failed operation='{actionName}'", ex);
                RebuildWindow();
            }
        }

        private static JObject LoadReportJson()
        {
            try
            {
                return JObject.Parse(FuseLoadReport.GetLastJsonReport());
            }
            catch
            {
                return new JObject
                {
                    ["summary"] = FuseLoadReport.LastSummary,
                    ["hasProblems"] = false,
                    ["counts"] = new JObject()
                };
            }
        }

        private static Sprite LoadIconSprite()
        {
            if (_iconSprite != null)
            {
                return _iconSprite;
            }

            try
            {
                var path = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "assets", "fuse_icon.png");
                if (!File.Exists(path))
                {
                    FuseLog.Warning("FUSE health icon was not found; using an empty base-game image component.");
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(36, 36, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Destroy(texture);
                    FuseLog.Warning("FUSE health icon could not be decoded.");
                    return null;
                }

                _iconSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                return _iconSprite;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE health icon load failed: {ex.GetBaseException().Message}");
                return null;
            }
        }

        private static string ReadVersion()
        {
            try
            {
                var infoPath = Path.Combine(FusePlugin.ModEntry?.Path ?? string.Empty, "Info.json");
                if (!File.Exists(infoPath))
                {
                    return "unknown";
                }

                var info = JObject.Parse(File.ReadAllText(infoPath));
                return ReadString(info["Version"], "unknown");
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ReadString(JToken token, string fallback)
        {
            var value = token == null ? string.Empty : token.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int ReadInt(JToken token)
        {
            return token != null && int.TryParse(token.ToString(), out var value) ? value : 0;
        }

        private static bool ReadBool(JToken token, bool fallback)
        {
            return token != null && bool.TryParse(token.ToString(), out var value) ? value : fallback;
        }

        private string GetWindowTitle()
        {
            switch (_activePage)
            {
                case Page.ModSets:
                    return "FUSE Profiles";
                case Page.Inspector:
                    return "FUSE Inspector";
                case Page.Audits:
                    return "FUSE Audits";
                case Page.Advanced:
                    return "FUSE Advanced";
                default:
                    return "FUSE Health";
            }
        }

        private sealed class InspectorTarget
        {
            public InspectorTarget(
                string kind,
                string id,
                object runtimeObject,
                GameObject gameObject,
                string scenePath,
                FuseClaimKind? claimKind)
            {
                Kind = kind ?? "Object";
                Id = id ?? string.Empty;
                RuntimeObject = runtimeObject;
                GameObject = gameObject;
                ScenePath = scenePath ?? string.Empty;
                ClaimKind = claimKind;
                Signature = Kind + "|" + Id + "|" + ScenePath + "|" + (runtimeObject == null ? "<null>" : runtimeObject.GetHashCode().ToString());
                DropdownLabel = Kind + " | " + BlankAs(Id, "(blank)") + " | " + BlankAs(ScenePath, "no scene path");
            }

            public string Kind { get; }
            public string Id { get; }
            public object RuntimeObject { get; }
            public GameObject GameObject { get; }
            public string ScenePath { get; }
            public FuseClaimKind? ClaimKind { get; }
            public string Signature { get; }
            public string DropdownLabel { get; }
        }

        private sealed class AuditFinding
        {
            public AuditFinding(string severity, string title, string objectId, string detail, string action)
            {
                Severity = string.IsNullOrWhiteSpace(severity) ? "Low" : severity;
                Title = title ?? string.Empty;
                ObjectId = objectId ?? string.Empty;
                Detail = detail ?? string.Empty;
                Action = action ?? string.Empty;
            }

            public string Severity { get; }
            public string Title { get; }
            public string ObjectId { get; }
            public string Detail { get; }
            public string Action { get; }
        }

        private enum Page
        {
            Health,
            Packages,
            Assets,
            Runtime,
            Logs,
            Inspector,
            Audits,
            Advanced,
            Settings,
            ModSets,
            LegacyMods
        }
    }
}
