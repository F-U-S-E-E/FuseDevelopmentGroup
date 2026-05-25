using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Runtime.API;
using FUSE.Runtime.Cache;
using FUSE.Authoring.Data;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Loading;
using FUSE.Authoring.Migrations;
using FUSE.Runtime.Registry;
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
    internal sealed partial class FuseHealthUi : MonoBehaviour
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

        private static Vector2Int DefaultSize => new Vector2Int(740, 660);
        private static Vector2Int MaxSize => new Vector2Int(Screen.width, Screen.height);
        private static Window.Sizing DefaultSizing => Window.Sizing.Resizable(DefaultSize, MaxSize);
        private static Window.Position DefaultPosition => Window.Position.UpperLeft;

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

            _window = WindowCreatorHelper.CreateWindow(WindowIdentifier, DefaultSize.x, DefaultSize.y, DefaultPosition);
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
            _panel = WindowCreatorHelper.PopulateWindow(_window, BuildHealthPage);
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
