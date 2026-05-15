using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Builder;
using UI.Common;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Infrastructure
{
    public class WindowCreatorHelper
    {
        private static WindowCreatorHelper _instance;
        public static WindowCreatorHelper Shared => _instance ?? (_instance = new WindowCreatorHelper());

        public static bool CanCreateWindow =>
            UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(true) != null;

        private static Window CreateWindowInternal(string identifier, int width, int height, Window.Position position)
        {
            var creator = UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(true);
            if (creator == null)
                return null;

            var window = creator.windowPrefab;
            if (window == null)
                return null;

            var instance = UnityEngine.Object.Instantiate(window, creator.transform, false);
            instance.SetInitialPositionSize(identifier, new Vector2(width, height), position, Window.Sizing.Fixed(new Vector2Int(width, height)));
            instance.name = identifier;

            return instance;
        }

        private static UIPanel PopulateWindowInternal(Window window, Action<UIPanelBuilder> closure)
        {
            var creator = UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(true);
            if (creator == null || window == null || window.gameObject == null || window.contentRectTransform == null)
                return null;

            PrepareWindowContent(window.contentRectTransform);
            return UIPanel.Create(window.contentRectTransform, creator.builderAssets, closure);
        }

        private static void PrepareWindowContent(RectTransform content)
        {
            var layout = content.GetComponent<LayoutGroup>();
            if (layout == null)
            {
                layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            if (layout is HorizontalOrVerticalLayoutGroup stack)
            {
                stack.childAlignment = TextAnchor.UpperLeft;
                stack.childControlWidth = true;
                stack.childControlHeight = true;
                stack.childForceExpandWidth = true;
                stack.childForceExpandHeight = false;
                stack.spacing = 4f;
                stack.padding = new RectOffset(10, 10, 6, 10);
            }
        }

        public Window CreateWindow(string identifier, int width, int height, Window.Position position)
        {
            var safeIdentifier = string.IsNullOrWhiteSpace(identifier)
                ? "FUSE.Window." + DateTime.Now.Ticks
                : identifier.Trim();
            return CreateWindowInternal(safeIdentifier, width, height, position);
        }

        public Window CreateWindow(int width, int height, Window.Position position)
        {
            // Generate a unique identifier for the window
            string identifier = "FUSE.Window." + DateTime.Now.Ticks;
            return CreateWindowInternal(identifier, width, height, position);
        }

        public UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure)
        {
            return PopulateWindowInternal(window, closure);
        }
    }
}
