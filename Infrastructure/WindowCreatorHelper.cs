using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.Builder;
using UI.Common;
using UI;
using UnityEngine;

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
                throw new NullReferenceException();

            var window = creator.windowPrefab;
            var instance = UnityEngine.Object.Instantiate(window, creator.transform, false);
            instance.SetInitialPositionSize(identifier, new Vector2(width, height), position, Window.Sizing.Fixed(new Vector2Int(width, height)));
            instance.name = identifier;

            return instance;
        }

        private static UIPanel PopulateWindowInternal(Window window, Action<UIPanelBuilder> closure)
        {
            var creator = UnityEngine.Object.FindObjectOfType<ProgrammaticWindowCreator>(true);
            if (creator == null)
                throw new NullReferenceException();

            return UIPanel.Create(window.contentRectTransform, creator.builderAssets, closure);
        }

        public Window CreateWindow(int width, int height, Window.Position position)
        {
            // Generate a unique identifier for the window
            string identifier = "ligma" + DateTime.Now.Ticks;
            return CreateWindowInternal(identifier, width, height, position);
        }

        public UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure)
        {
            return PopulateWindowInternal(window, closure);
        }
    }
}

