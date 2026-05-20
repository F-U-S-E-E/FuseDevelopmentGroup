﻿using FUSE.Infrastructure;
using FUSE.Loading;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UI.Builder;
using UI.Common;
using UI.CompanyWindow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace FUSE.Editor
{
    class MainEditorWindow : MonoBehaviour
    {
        private Window _window;
        private UIPanel _panel;
        private static MainEditorWindow _instance;

        private InputAction toggleMenuAction;

        private readonly UIState<string> _selectedTabState = new UIState<string>(null);

        public static MainEditorWindow Shared
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MainEditorWindow>();
                }
                return _instance;
            }
        }

        public static void Setup()
        {
            
        }

        private void Awake()
        {
            _window = GetComponent<Window>();

            if (_window == null)
            {
                CreateWindow();
            }

            toggleMenuAction = new InputAction("ToggleMenu", binding: "<Keyboard>/n");
            toggleMenuAction.performed += ctx => MainEditorWindow.Toggle();
            toggleMenuAction.Enable();
        }

        private bool IsWindowValid()
        {
            return _window != null && _window.gameObject != null && _window.contentRectTransform != null;
        }

        private bool EnsureWindow()
        {
            if (IsWindowValid())
                return true;

            _window = null;
            CreateWindow();
            return IsWindowValid();
        }

        private void OnEnable()
        {

        }

        private void OnDisable()
        {
            // Unregister from events
            Messenger.Default.Unregister(this);

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            // Teleport / UI rebuilds can invalidate the old ProgrammaticWindowCreator tree.
            // Never keep a stale Unity UI reference across disable/unload.
            _window = null;
        }

        public static void Toggle()
        {
            // Only allow toggling if map is loaded
            GameObject currentSelectedGameObject = EventSystem.current.currentSelectedGameObject;
            bool flag = currentSelectedGameObject != null && currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
            if (flag) return;

            if (!Shared.EnsureWindow())
                return;

            if (Shared._window.IsShown)
            {
                Shared.Close();
            }
            else
            {
                Shared.Show();
            }
        }

        public void Show()
        {
            if (!EnsureWindow()) return;

            Populate();

            if (IsWindowValid())
                _window.ShowWindow();
        }

        private void Populate()
        {
            if (!EnsureWindow()) return;

            _window.Title = "Node Editor";

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            _panel = WindowCreatorHelper.Shared.PopulateWindow(_window, builder =>
            {
                if (FuseEditor.Instance.ModSelected)
                {
                    builder.AddTabbedPanels(_selectedTabState, delegate (UITabbedPanelBuilder tabBuilder)
                    {
                        tabBuilder.AddTab("Mod Info", "modinfo", BuildModInfoPanel);
                        tabBuilder.AddTab("Tools", "tools", BuildToolsPanel);
                        tabBuilder.AddTab("Settings", "settings", BuildEditorSettingsPanel);
                    });
                }
                else
                {
                    BuildSelectModPanel(builder);
                }
            });
        }

        public void CreateWindow()
        {
            if (!WindowCreatorHelper.CanCreateWindow)
                return;

            _window = WindowCreatorHelper.Shared.CreateWindow(400, 500, Window.Position.Center);
            _window.Title = "FUSE Editor";
        }

        public void Close()
        {
            if (IsWindowValid() && _window.IsShown)
            {
                _window.CloseWindow();
            }

        }

        void BuildSelectModPanel(UIPanelBuilder builder)
        { 
            List<string> loadedMods = FuseModLoader.GetLoadedModsInOrder().Select(x => x.Definition.Id).ToList();

            loadedMods.Insert(0, "Select a Mod");

            int currentlySelected = FuseEditor.Instance.ModSelected ? loadedMods.IndexOf(FuseEditor.Instance.ActiveMod.Definition.Id) : 0;

            builder.AddDropdown(loadedMods, currentlySelected, delegate(int selected) {
                if (selected != 0)
                {
                    FuseEditor.Instance.SetActiveMod(FuseModLoader.GetLoadedMod(loadedMods[selected]));
                    builder.Rebuild();
                }
            });
        }

        void BuildModInfoPanel(UIPanelBuilder builder)
        {

        }

        void BuildToolsPanel(UIPanelBuilder builder)
        {

        }

        void BuildEditorSettingsPanel(UIPanelBuilder builder)
        {

        }
    }
}