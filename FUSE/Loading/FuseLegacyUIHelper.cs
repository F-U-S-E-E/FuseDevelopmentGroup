using System;
using FUSE.Infrastructure;
using Railloader;
using UI;
using UI.Builder;
using UI.Common;
using UnityEngine;

namespace FUSE.Loading
{
    /// <summary>
    /// FUSE-owned implementation of the legacy window service. Hosted plugins
    /// receive this object through constructor injection; no legacy loader code
    /// is required at runtime.
    /// </summary>
    internal sealed class FuseLegacyUIHelper : IUIHelper
    {
        internal static FuseLegacyUIHelper Shared { get; } = new FuseLegacyUIHelper();

        private FuseLegacyUIHelper()
        {
        }

        public Window CreateWindow(int width, int height, Window.Position position)
        {
            return WindowCreatorHelper.CreateWindow(width, height, position);
        }

        public Window CreateWindow(string identifier, int width, int height, Window.Position position)
        {
            return WindowCreatorHelper.CreateWindow(identifier, width, height, position);
        }

        public Window CreateWindow<TWindow>(
            string identifier,
            int width,
            int height,
            Window.Position position,
            Action<TWindow> configure = null)
            where TWindow : Component, IBuilderWindow
        {
            var creator = FindCreator();
            var window = InstantiateWindow(creator);
            if (window == null)
            {
                return null;
            }

            window.SetInitialPositionSize(
                identifier,
                new Vector2(width, height),
                position,
                Window.Sizing.Fixed(new Vector2Int(width, height)));
            window.name = typeof(TWindow).FullName ?? typeof(TWindow).Name;

            var component = window.gameObject.AddComponent<TWindow>();
            component.BuilderAssets = creator.builderAssets;
            configure?.Invoke(component);
            window.CloseWindow();
            return window;
        }

        public Window CreateWindow<TWindow>(Action<TWindow> configure = null)
            where TWindow : Component, IProgrammaticWindow
        {
            var creator = FindCreator();
            var window = InstantiateWindow(creator);
            if (window == null)
            {
                return null;
            }

            window.name = typeof(TWindow).FullName ?? typeof(TWindow).Name;
            var component = window.gameObject.AddComponent<TWindow>();
            component.BuilderAssets = creator.builderAssets;
            configure?.Invoke(component);
            window.SetInitialPositionSize(
                component.WindowIdentifier,
                component.DefaultSize,
                component.DefaultPosition,
                component.Sizing);
            window.CloseWindow();
            return window;
        }

        public UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure)
        {
            return WindowCreatorHelper.PopulateWindow(window, closure);
        }

        private static ProgrammaticWindowCreator FindCreator()
        {
            return UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(true);
        }

        private static Window InstantiateWindow(ProgrammaticWindowCreator creator)
        {
            if (creator == null || creator.windowPrefab == null)
            {
                FuseLog.Warning(
                    "FUSE legacy UI helper could not create a window because the game's " +
                    "ProgrammaticWindowCreator is not available yet.");
                return null;
            }

            return UnityEngine.Object.Instantiate(creator.windowPrefab, creator.transform, false);
        }
    }
}
