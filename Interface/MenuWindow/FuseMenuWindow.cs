using FUSE.Events;
using FUSE.Infrastructure;
using System;
using System.Collections;
using System.IO;
using TMPro;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Interface.MenuWindow
{
    internal class FuseMenuWindow : MonoBehaviour
    {
        private const string WindowIdentifier = "FUSE.Menu";
        private const string MenuButtonName = "FUSEMenuButton";

        private static GameObject _hostGO;
        private static Sprite _iconSprite;
        private Button _button;
        private Window _window;
        private UIPanel _panel;

        public static FuseMenuWindow Shared { get; private set; } = null;

        private Vector2Int DefaultSize => new(880, 600);
        private Vector2Int MaxSize => new(Screen.width, Screen.height);
        private Window.Sizing DefaultSizing => Window.Sizing.Resizable(DefaultSize, MaxSize);
        private Window.Position DefaultPosition => Window.Position.UpperLeft;

        private readonly UIState<string> _selectedTabState = new(null);
        private readonly UIState<string> _selectedStatusItem = new(null);
        private readonly UIState<string> _selectedModListItem = new(null);
        private readonly UIState<string> _selectedProfileItem = new(null);
        private readonly UIState<string> _selectedToolItem = new(null);
        private readonly UIState<string> _selectedSettingsItem = new(null);

        private string _lastBuiltTab = TabIdStatus;

        private const string TabIdStatus = "status";
        private const string TabIdMods = "mods";
        private const string TabIdProfiles = "profiles";
        private const string TabIdTools= "tools";
        private const string TabIdSettings = "settings";

        public static void Ensure()
        {
            if (_hostGO != null)
            {
                return;
            }

            _hostGO = new GameObject("FUSE Menu UI");
            DontDestroyOnLoad(_hostGO);
            _hostGO.hideFlags = HideFlags.HideAndDontSave;
            _hostGO.AddComponent<FuseMenuWindow>();
            FuseLog.Info("FUSE Menu UI initialized.");
        }

        public static void Shutdown()
        {
            if (_hostGO != null)
            {
                Destroy(_hostGO);
                _hostGO = null;
            }

            _iconSprite = null;
        }

        protected void Awake()
        {
            Shared = this;
        }

        protected void OnDestroy()
        {
            Shared = null;
        }

        protected void OnEnable()
        {
            FuseEvents.ModSetAdded += OnModSetAdded;
            FuseEvents.ModSetRemoved += OnModSetRemoved;
        }

        protected void OnDisable()
        {
            FuseEvents.ModSetAdded -= OnModSetAdded;
            FuseEvents.ModSetRemoved -= OnModSetRemoved;
        }

        private void OnModSetAdded(string modSetId)
        {
            if (_selectedTabState.Value == TabIdProfiles)
            {
                SetSelectedProfile(modSetId);
            }
        }

        private void OnModSetRemoved(string modSetId)
        {
            if (_selectedTabState.Value == TabIdProfiles)
            {
                RebuildWindow();
            }
        }

        private void Start()
        {
            TryInstallHudButton();
        }

        private void Update()
        {
            if (_button == null)
            {
                TryInstallHudButton();
            }
        }

        private void BuildFuseMenu(UIPanelBuilder builder)
        {
            builder.AddTabbedPanels(_selectedTabState, delegate (UITabbedPanelBuilder tabBuilder)
            {
                tabBuilder.AddTab("Status", TabIdStatus, b =>
                {
                    StatusPanelBuilder.Build(b, _selectedStatusItem);
                });
                tabBuilder.AddTab("Mods", TabIdMods, b =>
                {
                    ModsPanelBuilder.Build(b, _selectedModListItem);
                });
                tabBuilder.AddTab("Profiles", TabIdProfiles, b =>
                {
                    ProfilesPanelBuilder.Build(b, _selectedProfileItem);
                });
                tabBuilder.AddTab("Tools", TabIdTools, b =>
                {
                    ToolsPanelBuilder.Build(b, _selectedToolItem);
                });
                tabBuilder.AddTab("Settings", TabIdSettings, b =>
                {
                    FuseSettingsPanelBuilder.Build(b, _selectedSettingsItem);
                });
            });
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

                var existing = strip.Find(MenuButtonName);
                var buttonObject = existing == null
                    ? new GameObject(MenuButtonName, typeof(RectTransform))
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

                FuseLog.Info($"FUSE menu HUD button added to base-game TopRightArea strip at siblingIndex={buttonObject.transform.GetSiblingIndex()}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE menu HUD button install failed: {ex.GetBaseException().Message}");
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
                if (child == null || child.name == MenuButtonName)
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
                    FuseLog.Warning("FUSE menu icon was not found; using an empty base-game image component.");
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(36, 36, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Destroy(texture);
                    FuseLog.Warning("FUSE menu icon could not be decoded.");
                    return null;
                }

                _iconSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                return _iconSprite;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE menu icon load failed: {ex.GetBaseException().Message}");
                return null;
            }
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
                FuseLog.Warning("FUSE menu window could not open because ProgrammaticWindowCreator is not available yet.");
                return false;
            }

            _window = WindowCreatorHelper.Shared.CreateWindow(WindowIdentifier, DefaultSize.x, DefaultSize.y, DefaultPosition);
            if (_window == null)
            {
                FuseLog.Warning("FUSE menu window could not be created from the base-game window prefab.");
                return false;
            }

            _window.Title = GetWindowTitle();
            return true;
        }

        private string GetWindowTitle()
        {
            return _selectedTabState.Value switch
            {
                TabIdStatus => "FUSE Status",
                TabIdMods => "FUSE Mods",
                TabIdProfiles => "FUSE Profiles",
                TabIdTools => "FUSE Tools",
                TabIdSettings => "FUSE Settings",
                _ => "FUSE",
            };
        }

        private void RebuildWindow()
        {
            if (!EnsureWindow())
            {
                return;
            }

            var restoreScroll = _lastBuiltTab == _selectedTabState.Value;
            var scrollPosition = restoreScroll ? CaptureScrollPosition() : 1f;

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            _window.Title = GetWindowTitle();
            _panel = WindowCreatorHelper.Shared.PopulateWindow(_window, BuildFuseMenu);
            _lastBuiltTab = _selectedTabState.Value;

            WindowPersistence.SetInitialPositionSize(_window, WindowIdentifier, DefaultSize, DefaultPosition, DefaultSizing);

            if (restoreScroll)
            {
                RestoreScrollPosition(scrollPosition);
                StartCoroutine(RestoreScrollPositionNextFrame(scrollPosition));
            }
        }

        private float CaptureScrollPosition()
        {
            try
            {
                var scrollRect = FindMenuScrollRect();
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
                var scrollRect = FindMenuScrollRect();
                if (scrollRect == null)
                {
                    return;
                }

                scrollRect.verticalNormalizedPosition = Mathf.Clamp01(position);
                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE menu page could not restore scroll position: {ex.GetBaseException().Message}");
            }
        }

        private IEnumerator RestoreScrollPositionNextFrame(float position)
        {
            yield return null;
            RestoreScrollPosition(position);
            yield return null;
            RestoreScrollPosition(position);
        }

        private ScrollRect FindMenuScrollRect()
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

        public void SetSelectedProfile(string id)
        {
            _selectedProfileItem.Value = id;
            RebuildWindow();
        }

        public void SetSelectedStatusItem(string id)
        {
            _selectedStatusItem.Value = id;
            RebuildWindow();
        }
    }
}
