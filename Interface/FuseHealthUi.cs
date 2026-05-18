using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.API;
using FUSE.Infrastructure;
using FUSE.Lifecycle;
using FUSE.Loading;
using FUSE.Migrations;
using Newtonsoft.Json.Linq;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.Profiling;
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

            _window = WindowCreatorHelper.Shared.CreateWindow(WindowIdentifier, 740, 660, Window.Position.UpperLeft);
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
                row.AddButtonCompact(_activePage == Page.Health ? "[ Overview ]" : "Overview", () => SetPage(Page.Health));
                row.AddButtonCompact(_activePage == Page.Packages ? "[ Packages ]" : "Packages", () => SetPage(Page.Packages));
                row.AddButtonCompact(_activePage == Page.Assets ? "[ Assets ]" : "Assets", () => SetPage(Page.Assets));
                row.AddButtonCompact(_activePage == Page.Runtime ? "[ Runtime ]" : "Runtime", () => SetPage(Page.Runtime));
                row.AddButtonCompact(_activePage == Page.Logs ? "[ Logs ]" : "Logs", () => SetPage(Page.Logs));
                row.AddButtonCompact(_activePage == Page.Settings ? "[ Settings ]" : "Settings", () => SetPage(Page.Settings));
                row.AddButtonCompact(_activePage == Page.ModSets ? "[ Mod Sets ]" : "Mod Sets", () => SetPage(Page.ModSets));
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
                case Page.Settings:
                    BuildSettingsContent(builder);
                    return;
                case Page.ModSets:
                    BuildModSetsContent(builder);
                    return;
                case Page.Health:
                default:
                    BuildHealthContent(builder);
                    return;
            }
        }

        private void SetPage(Page page)
        {
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

            builder.FieldLabelWidth = 160f;
            builder.Spacing = 6f;

            builder.AddSection("Status");
            AddValueField(builder, "State", hasProblems ? "Needs Attention" : "Healthy");
            AddWrappedField(builder, "Summary", ReadString(report["summary"], FuseLoadReport.LastSummary), 44f);
            AddValueField(builder, "Version", "FUSE " + ReadVersion() + " | Schema " + FuseMigration.CurrentVersion + " | Converter 0.2.0");
            builder.Spacer(6f);

            builder.AddSection("Summary");
            AddCountField(builder, "Loaded Packages", counts, "loadedPackages");
            AddCountField(builder, "Applied Packages", counts, "appliedPackages");
            AddCountField(builder, "Faults", counts, "faultedPackages");
            AddCountField(builder, "Conflicts", counts, "conflicts");
            AddCountField(builder, "Unknown Assets", counts, "unknownSceneryAssets");
            AddCountField(builder, "Graph Issues", counts, "graphIssues");
            AddCountField(builder, "Transfer Skips", counts, "progressionTransferSkips");
            AddCountField(builder, "Suppressions", counts, "suppressions");
            builder.Spacer(6f);

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("Multiplayer Profile");
            AddValueField(builder, "Game Mode", multiplayer.Mode);
            AddValueField(builder, "Role", multiplayer.Role);
            AddValueField(builder, "Policy", multiplayer.MutationPolicy);
            AddValueField(builder, "Mod Set", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Packages", multiplayer.LocalPackageSummary, 38f);
            builder.Spacer(6f);

            builder.AddSection("Performance");
            builder.AddField("FPS", () => _fpsAverage <= 0f ? "warming up" : _fpsAverage.ToString("0.0"), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Frame Time", () => _frameMilliseconds <= 0f ? "warming up" : _frameMilliseconds.ToString("0.0") + " ms", UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Managed Memory", () => FormatBytes(_managedMemoryBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Allocated", () => FormatBytes(_unityAllocatedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            builder.AddField("Unity Reserved", () => FormatBytes(_unityReservedBytes), UIPanelBuilder.Frequency.Fast).Height(26f);
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            AddWrappedField(builder, "Slowest Operation", FriendlyTimingText(FusePerformanceMetrics.FormatSlowestApplyPackage()), 42f);
            AddWrappedField(builder, "Operation Detail", BuildSlowestDetailText(), 56f);
            AddValueField(builder, "Disk Load", FusePerformanceMetrics.FormatTiming("load packages from disk"));
            AddValueField(builder, "Asset Mirror", FusePerformanceMetrics.FormatTiming("asset pack registration"));
            AddValueField(builder, "Direct Asset Stores", FusePerformanceMetrics.FormatTiming("direct asset pack stores"));
            AddValueField(builder, "Asset Store Count", FusePerformanceMetrics.FormatCount("direct asset pack store count"));
            AddValueField(builder, "Map Mask Rebuild", FusePerformanceMetrics.FormatTiming("map mask rebuild"));
            AddValueField(builder, "Console Setup", FusePerformanceMetrics.FormatTiming("console registration"));
            builder.Spacer(6f);

            builder.AddSection("Runtime Tools");
            builder.HStack(row =>
            {
                row.AddButtonCompact("Reload Track", () =>
                {
                    RunAction("reload track", () =>
                    {
                        var applied = FuseRuntimeReloadService.ReloadTrackAndData("FUSE health page reload track");
                        return $"Reload Track complete. Applied {applied} resident definition(s).";
                    });
                });
                row.AddButtonCompact("Reload Terrain", () =>
                {
                    RunAction("reload terrain", () =>
                        FuseRuntimeReloadService.ReloadTerrain("FUSE health page reload terrain")
                            ? "Reload Terrain complete."
                            : "Reload Terrain skipped or failed. See FUSE.log.");
                });
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
            if (problemRows == 0)
            {
                AddValueField(builder, "Status", "None");
            }
            builder.Spacer(8f);
        }

        private void BuildPackagesContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 150f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("Load Order");
            AddValueField(builder, "Profile", FuseModSetService.ActiveSetName);
            AddValueField(builder, "Profile Hash", multiplayer.LocalPackageFingerprint);
            AddWrappedField(builder, "Package Filter", multiplayer.LocalPackageSummary, 42f);
            AddWrappedField(
                builder,
                "Multiplayer",
                "FUSE does not negotiate mods over the network yet. Server owners can share this profile hash and package order; every player should match it.",
                52f);
            builder.Spacer(4f);

            var manifests = FuseDataPackageDiscovery.GetPackageManifestSnapshots();
            if (manifests.Count == 0)
            {
                AddValueField(builder, "Packages", "No FUSE data packages discovered.");
            }
            else
            {
                foreach (var manifest in manifests)
                {
                    var status = manifest.Disabled
                        ? "disabled"
                        : manifest.Faults.Length > 0
                            ? "faulted"
                            : "ready";
                    var tag = manifest.IsLegacyConverted ? " | legacy-converted" : string.Empty;
                    AddWrappedLabel(
                        builder,
                        InsertBreakHints($"{manifest.Order:00}. {manifest.Id}"),
                        34f);
                    AddWrappedLabel(
                        builder,
                        InsertBreakHints($"     v{BlankAs(manifest.Version, "?")} | priority {manifest.Priority} | {status}{tag} | folder {manifest.FolderName}"),
                        34f);
                    var deps = BuildDependencySummary(manifest);
                    if (!string.IsNullOrWhiteSpace(deps))
                    {
                        AddWrappedLabel(builder, "     " + InsertBreakHints(deps), 28f);
                    }

                    if (manifest.Disabled && !string.IsNullOrWhiteSpace(manifest.DisabledReason))
                    {
                        AddWrappedLabel(builder, "     disabled: " + manifest.DisabledReason, 28f);
                    }

                    if (manifest.Faults.Length > 0)
                    {
                        AddWrappedLabel(builder, "     faults: " + string.Join("; ", manifest.Faults), 42f);
                    }
                }
            }

            builder.Spacer(4f);
            builder.AddSection("Dependency Graph");
            BuildDependencyGraph(builder, manifests);
            builder.Spacer(8f);
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

        private void BuildSettingsContent(UIPanelBuilder builder)
        {
            builder.FieldLabelWidth = 170f;
            builder.Spacing = 6f;

            var multiplayer = FuseMultiplayerGuard.GetStatus();
            builder.AddSection("Settings");
            AddValueField(builder, "Asset Packs", AssetPackModeText());
            AddValueField(builder, "Mod Set", FuseModSetService.ActiveSetName);
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
                FuseSettings.ShowAdvancedHealthDetails ? "enabled" : "disabled",
                FuseSettings.ShowAdvancedHealthDetails ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowAdvancedHealthDetails(!FuseSettings.ShowAdvancedHealthDetails);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Track Debug Overlay",
                FuseSettings.ShowTrackDebugOverlay ? "enabled (hover tracks)" : "disabled",
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
                    ? (FuseSettings.ShowTrackDebugOverlay ? "shown in overlay" : "shown when overlay on")
                    : "hidden",
                FuseSettings.ShowTrackDebugSpanPaths ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowTrackDebugSpanPaths(!FuseSettings.ShowTrackDebugSpanPaths);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Debug Overlay",
                FuseSettings.ShowSceneryDebugOverlay ? "enabled (hover scenery)" : "disabled",
                FuseSettings.ShowSceneryDebugOverlay ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugOverlay(!FuseSettings.ShowSceneryDebugOverlay);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Scenery Debug Details",
                FuseSettings.ShowSceneryDebugAdvanced
                    ? (FuseSettings.ShowSceneryDebugOverlay ? "shown in overlay" : "shown when overlay on")
                    : "hidden",
                FuseSettings.ShowSceneryDebugAdvanced ? "Hide" : "Show",
                () =>
                {
                    FuseSettings.SetShowSceneryDebugAdvanced(!FuseSettings.ShowSceneryDebugAdvanced);
                    RebuildWindow();
                });
            AddSettingToggle(
                builder,
                "Early Suppression",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "enabled" : "disabled",
                FuseSettings.EnableExperimentalEarlyScenePathSuppression ? "Disable" : "Enable",
                () =>
                {
                    FuseSettings.SetEnableExperimentalEarlyScenePathSuppression(!FuseSettings.EnableExperimentalEarlyScenePathSuppression);
                    RebuildWindow();
                });
            builder.Spacer(6f);

            builder.AddSection("Reload Result History");
            AddWrappedField(builder, "Last Action", _lastAction, 52f);
            AddValueField(builder, "FUSE Map Load", FusePerformanceMetrics.FormatTiming("map load total"));
            AddValueField(builder, "Runtime Apply", FusePerformanceMetrics.FormatTiming("apply resident definitions"));
            AddValueField(builder, "Disk Load", FusePerformanceMetrics.FormatTiming("load packages from disk"));
            AddValueField(builder, "Direct Asset Stores", FusePerformanceMetrics.FormatTiming("direct asset pack stores"));
            builder.Spacer(8f);
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

        private static string ExportHealthReportJson()
        {
            var root = Path.Combine(Application.persistentDataPath, "FUSE");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "fuse-health-report.json");
            File.WriteAllText(path, FuseLoadReport.GetLastJsonReport());
            return "Exported FUSE health JSON report: " + path;
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
            return _activePage == Page.ModSets ? "FUSE Mod Sets" : "FUSE Health";
        }

        private enum Page
        {
            Health,
            Packages,
            Assets,
            Runtime,
            Logs,
            Settings,
            ModSets
        }
    }
}
