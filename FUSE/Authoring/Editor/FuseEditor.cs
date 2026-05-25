using FUSE.Infrastructure;
using FUSE.Loading;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.CarEditor;
using UnityEngine;

namespace FUSE.Authoring.Editor
{
    public class FuseEditor : MonoBehaviour
    {
        static FuseEditor _instance;

        static DefinitionEditorModeController _cachedObject;

        MainEditorWindow mainEditor;

        [CanBeNull]
        public FuseLoadedMod ActiveMod { get; private set; } = null;

        public bool ModSelected => ActiveMod != null;

        public static FuseEditor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = GameObject.FindObjectOfType<FuseEditor>();
                }
                return _instance;
            }
        }

        public static bool InEditor
        {
            get
            {
                return _cachedObject != null;
            }
        }

        public static void OnFuseLoad()
        {
            if (_instance == null)
            {
                GameObject gameObject = new GameObject("Fuse Editor");

                DontDestroyOnLoad(gameObject);

                _instance = gameObject.AddComponent<FuseEditor>();

                Messenger.Default.Register<MapDidLoadEvent>(_instance, _instance.OnMapLoad);
                Messenger.Default.Register<MapDidUnloadEvent>(_instance, _instance.OnMapUnload);
            }
        }

        public static void OnFuseUnload()
        {
            if (_instance != null)
            {
                GameObject.Destroy(_instance.gameObject);
            }
        }

        public void OnMapLoad(MapDidLoadEvent mapDidLoadEvent)
        {
            _cachedObject = FindAnyObjectByType<DefinitionEditorModeController>();

            if (InEditor)
            {
                GameObject MainWindow = new GameObject("FUSE-EditorMainWindow");
                MainWindow.transform.SetParent(gameObject.transform, false);

                mainEditor = MainWindow.AddComponent<MainEditorWindow>();
            }
        }

        public void OnMapUnload(MapDidUnloadEvent mapDidUnloadEvent)
        {
            if (InEditor)
            {
                Destroy(mainEditor.gameObject);
            }

            _cachedObject = null;
        }

        public void SetActiveMod(FuseLoadedMod mod)
        {
            if (FuseModLoader.IsApplied(mod.Definition.Id))
            {
                ActiveMod = mod;
            }
            else
            {
                FuseLog.Info($"Unable to edit mod: {mod.Definition.Id}, mod is not loaded");
            }
        }
    }
}
