﻿using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UI.Common;
using UI.Builder;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using TMPro;
using UnityEngine.EventSystems;

namespace FUSE.Editor
{
    class MainEditorWindow : MonoBehaviour
    {
        private Window _window;
        private UIPanel _panel;
        private static MainEditorWindow _instance;
        private bool _isMapLoaded = false;

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
            // Register for map events
            Messenger.Default.Register<MapDidLoadEvent>(this, OnMapLoaded);
            Messenger.Default.Register<MapWillUnloadEvent>(this, OnMapUnload);
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

        private void OnMapLoaded(MapDidLoadEvent _)
        {
            _isMapLoaded = true;
            CreateWindow(); // Create window after map loads
        }

        private void OnMapUnload(MapWillUnloadEvent _)
        {
            _isMapLoaded = false;

            if (_panel != null)
            {
                _panel.Dispose();
                _panel = null;
            }

            if (IsWindowValid() && _window.IsShown)
            {
                _window.CloseWindow();
            }

            // The UI parent can be rebuilt after teleport/map unload.
            // Force lazy recreation next time the editor opens.
            _window = null;
        }

        public static void Toggle()
        {
            // Only allow toggling if map is loaded
            if (!Shared._isMapLoaded) return;

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
            if (!_isMapLoaded) return;
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
    }
}