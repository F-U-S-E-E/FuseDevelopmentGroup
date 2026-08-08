using System;
using System.Collections.Generic;
using System.Reflection;
using Game.State;
using HarmonyLib;
using Network;
using FUSE.Infrastructure;
using UI.Menu;

namespace FUSE.Loading
{
    /// <summary>
    /// Launches a new sandbox session on a registered FUSE map. Same launch
    /// shape as the FUSE editor session (and the game's own singleplayer
    /// start): the stock map scene hosts the session while
    /// <see cref="FuseMapSession"/> + the MapStore redirect swap the terrain
    /// source, and the apply pipeline swaps the world content. Only usable
    /// from the main menu, where MenuManager exists.
    /// </summary>
    public static class FuseMapLauncher
    {
        private static readonly FieldInfo MenuManagerGameManagerField =
            AccessTools.Field(typeof(MenuManager), "gameManager");

        public const string DefaultReportingMark = "FUSE";

        public static bool TryLaunchMap(string mapId, string railroadName, string reportingMark, out string error)
        {
            error = null;

            if (!FuseMapPackageRegistry.TryGetMap(mapId, out var map))
            {
                error = $"No registered map '{mapId}'. Use /fuse.maps to list registered maps.";
                return false;
            }

            if (!map.IsValid)
            {
                error = $"Map '{map.MapId}' cannot launch: {map.FaultReason}";
                return false;
            }

            var menuManager = UnityEngine.Object.FindObjectOfType<MenuManager>();
            if (menuManager == null)
            {
                error = "Maps can only be launched from the main menu (MenuManager not found).";
                return false;
            }

            if (MenuManagerGameManagerField == null ||
                !(MenuManagerGameManagerField.GetValue(menuManager) is GlobalGameManager gameManager))
            {
                error = "Could not resolve GlobalGameManager from MenuManager.";
                return false;
            }

            // Same composition MenuManager uses for singleplayer: the stock
            // map scene as the active scene plus the environment scene.
            var sceneList = new List<SceneDescriptor>
            {
                SceneDescriptor.BushnellWhittier,
                SceneDescriptor.EnvironmentEnviro,
            };
            var sceneSetup = new GlobalGameManager.SceneLoadSetup(sceneList, SceneDescriptor.BushnellWhittier);

            var newGame = new NewGameSetup(
                railroadName: string.IsNullOrWhiteSpace(railroadName) ? map.DisplayName : railroadName.Trim(),
                reportingMark: string.IsNullOrWhiteSpace(reportingMark) ? DefaultReportingMark : reportingMark.Trim(),
                mode: GameMode.Sandbox,
                progressionId: null,
                setupId: null);
            var gameSetup = new GameSetup(saveName: null, setup: newGame);

            FuseMapSession.Activate(map.MapId);
            try
            {
                gameManager.Launch(sceneSetup, gameSetup, default(StartSingleplayerSetup));
            }
            catch (Exception)
            {
                FuseMapSession.Deactivate("map launch dispatch failed");
                throw;
            }

            FuseLog.Info($"FUSE map session launch dispatched map='{map.MapId}' displayName='{map.DisplayName}'.");
            return true;
        }
    }
}
