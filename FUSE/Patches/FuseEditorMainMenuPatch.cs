using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Authoring.Editor;
using FUSE.Infrastructure;
using Game.State;
using HarmonyLib;
using TMPro;
using UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace FUSE.Patches
{
    /// <summary>
    /// Postfixes <c>UI.Menu.MainMenu.Awake</c> to inject a "FUSE Editor"
    /// entry alongside Singleplayer / Multiplayer / Editor in the main
    /// menu. Clicking sets <see cref="FuseEditorBridge.EditorSessionPending"/>
    /// and launches a new sandbox session with the editor scene additively
    /// loaded — same shape AlinasUtils uses for its autoload patch and
    /// MapEditor uses for additive editor-scene loading.
    ///
    /// FUSE.Editor's MapDidLoad handler consumes the pending flag and
    /// brings up the editor surface. Exit Editor (fired through
    /// <see cref="FuseEditorBridge.EditorExited"/>) routes back to the main
    /// menu via <see cref="GlobalGameManager.ReturnToMainMenu"/>.
    ///
    /// Skipped when no lifecycle provider is registered so users without
    /// FUSE.Editor.dll deployed don't see a dead button.
    /// </summary>
    [HarmonyPatch(typeof(MainMenu), "Awake")]
    internal static class FuseEditorMainMenuPatch
    {
        private const string InjectedButtonName = "FuseEditorMainMenuButton";
        private const string InjectedButtonLabel = "FUSE Editor";

        private static readonly FieldInfo MenuManagerGameManagerField =
            AccessTools.Field(typeof(MenuManager), "gameManager");

        private static bool _subscribedToExit;

        private static void Postfix(MainMenu __instance)
        {
            if (__instance == null)
            {
                return;
            }

            // Optional editor: when FUSE.Editor.dll isn't deployed the
            // bridge has no lifecycle provider, so the button would
            // accomplish nothing. Skip injection in that case.
            if (FuseEditorBridge.LifecycleProvider == null)
            {
                return;
            }

            try
            {
                EnsureExitSubscription();
                InjectButton(__instance);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to inject the FUSE Editor button into the main menu.", ex);
            }
        }

        private static void EnsureExitSubscription()
        {
            if (_subscribedToExit)
            {
                return;
            }

            FuseEditorBridge.EditorExited += OnEditorExited;
            _subscribedToExit = true;
        }

        private static void InjectButton(MainMenu mainMenu)
        {
            var existingButtons = mainMenu.GetComponentsInChildren<Button>(includeInactive: true);
            var template = existingButtons?.FirstOrDefault();
            if (template == null || template.transform == null || template.transform.parent == null)
            {
                FuseLog.Warning("FUSE skipped main-menu button injection: no template Button found in MainMenu hierarchy.");
                return;
            }

            var parent = template.transform.parent;

            // Idempotency: postfix may fire more than once if the menu
            // canvas is re-enabled (e.g. ReturnToMainMenu re-runs Awake).
            // Never create duplicate buttons.
            if (parent.Find(InjectedButtonName) != null)
            {
                return;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            clone.name = InjectedButtonName;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            var label = clone.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (label != null)
            {
                label.text = InjectedButtonLabel;
            }

            var cloneButton = clone.GetComponent<Button>();
            if (cloneButton == null)
            {
                FuseLog.Warning("FUSE main-menu button clone had no Button component; injection aborted.");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            cloneButton.interactable = true;
            cloneButton.onClick.RemoveAllListeners();
            cloneButton.onClick.AddListener(OnFuseEditorButtonClicked);

            FuseLog.Info("FUSE injected the FUSE Editor button into the main menu.");
        }

        private static void OnFuseEditorButtonClicked()
        {
            try
            {
                if (!TryLaunchEditorSession())
                {
                    FuseLog.Warning("FUSE Editor click was a no-op because the editor session could not be launched.");
                    return;
                }

                FuseEditorBridge.NotifyEnterEditor();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to launch the editor session from the main menu.", ex);
            }
        }

        private static bool TryLaunchEditorSession()
        {
            var menuManager = UnityEngine.Object.FindObjectOfType<MenuManager>();
            if (menuManager == null)
            {
                FuseLog.Warning("FUSE could not locate MenuManager; editor session launch aborted.");
                return false;
            }

            if (MenuManagerGameManagerField == null)
            {
                FuseLog.Warning("FUSE could not access MenuManager.gameManager; editor session launch aborted.");
                return false;
            }

            var gameManager = MenuManagerGameManagerField.GetValue(menuManager) as GlobalGameManager;
            if (gameManager == null)
            {
                FuseLog.Warning("FUSE could not resolve GlobalGameManager from MenuManager; editor session launch aborted.");
                return false;
            }

            // Same scene composition AlinasUtils uses for its autoload:
            // [Editor, map, environment]. Editor scene brings the in-game
            // editor mode controller (which FUSE.Editor disables on
            // MapDidLoad); the map is the playable world the editor sits
            // on top of; environment provides sky / lighting.
            var sceneList = new List<SceneDescriptor>
            {
                SceneDescriptor.Editor,
                SceneDescriptor.BushnellWhittier,
                SceneDescriptor.EnvironmentEnviro,
            };
            var sceneSetup = new GlobalGameManager.SceneLoadSetup(sceneList, SceneDescriptor.BushnellWhittier);

            var newGame = new NewGameSetup(
                railroadName: "FUSE Editor Session",
                reportingMark: "FUSE",
                mode: GameMode.Sandbox,
                progressionId: null,
                setupId: null);
            var gameSetup = new GameSetup(saveName: null, setup: newGame);

            FuseEditorBridge.EditorSessionPending = true;
            try
            {
                gameManager.Launch(sceneSetup, gameSetup, default(Network.StartSingleplayerSetup));
            }
            catch
            {
                FuseEditorBridge.EditorSessionPending = false;
                throw;
            }

            FuseLog.Info("FUSE editor session launch dispatched.");
            return true;
        }

        private static void OnEditorExited()
        {
            try
            {
                var menuManager = UnityEngine.Object.FindObjectOfType<MenuManager>();
                if (menuManager != null && MenuManagerGameManagerField != null)
                {
                    var gameManager = MenuManagerGameManagerField.GetValue(menuManager) as GlobalGameManager;
                    gameManager?.ReturnToMainMenu();
                    FuseLog.Info("FUSE editor exited; returning to main menu.");
                    return;
                }

                // Fallback: if MenuManager isn't around (e.g. exit fired
                // before the editor session ever launched), find
                // GlobalGameManager directly.
                var fallback = UnityEngine.Object.FindObjectOfType<GlobalGameManager>();
                if (fallback != null)
                {
                    fallback.ReturnToMainMenu();
                    FuseLog.Info("FUSE editor exited via fallback GlobalGameManager lookup; returning to main menu.");
                    return;
                }

                FuseLog.Warning("FUSE editor exited but no GlobalGameManager was found to handle ReturnToMainMenu.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to return to main menu after editor exit.", ex);
            }
        }
    }
}
