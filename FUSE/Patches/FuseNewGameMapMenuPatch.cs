using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using FUSE.Infrastructure;
using FUSE.Loading;
using Game.State;
using HarmonyLib;
using Network;
using UI.Builder;
using UI.Common;
using UI.Menu;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Adds a world/map dropdown to New Game while preserving Railroader's
    /// existing map control as a separate starting-progression dropdown. The
    /// world selection travels with the new-game setup as a temporary marker
    /// and is consumed immediately before launch, keeping Back/cancel flows
    /// from leaving a stale active map.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseNewGameMapMenuPatch
    {
        private sealed class MenuSelection
        {
            internal string MapId = string.Empty;
        }

        private static readonly ConditionalWeakTable<NewGameMenu, MenuSelection> Selections =
            new ConditionalWeakTable<NewGameMenu, MenuSelection>();

        private static readonly FieldInfo ProgressionIdField =
            AccessTools.Field(typeof(NewGameMenu), "_progressionId");

        private static readonly FieldInfo SetupIdField =
            AccessTools.Field(typeof(NewGameMenu), "_setupId");

        private static readonly MethodInfo SelectProgressionIdMethod =
            AccessTools.Method(
                typeof(NewGameMenu),
                "SelectProgressionId",
                new[] { typeof(string) });

        [HarmonyPatch(typeof(NewGameMenu), "BuildPanelContent")]
        [HarmonyTranspiler]
        private static List<CodeInstruction> BuildPanelContentTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            var addFieldMethod = AccessTools.Method(
                typeof(UIPanelBuilder),
                "AddField",
                new[] { typeof(string), typeof(RectTransform) });
            var addWorldFieldMethod = AccessTools.Method(
                typeof(FuseNewGameMapMenuPatch),
                nameof(AddWorldField));

            var modeLabelIndex = result.FindIndex(instruction =>
                instruction.opcode == OpCodes.Ldstr &&
                string.Equals(
                    instruction.operand as string,
                    "Mode",
                    StringComparison.Ordinal));
            var insertionIndex = FindInsertionAfterField(
                result,
                modeLabelIndex,
                addFieldMethod);

            if (insertionIndex < 0 || addWorldFieldMethod == null)
            {
                FuseLog.Warning(
                    "FUSE could not place the New Game world selector; " +
                    "Railroader's NewGameMenu layout no longer matched the expected shape.");
                return result;
            }

            result.InsertRange(
                insertionIndex,
                new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, addWorldFieldMethod),
                });

            for (var index = insertionIndex + 3; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Ldstr &&
                    string.Equals(
                        result[index].operand as string,
                        "Map",
                        StringComparison.Ordinal))
                {
                    result[index].operand = "Starting Progression";
                    break;
                }
            }

            return result;
        }

        [HarmonyPatch(typeof(NewGameMenu), "BuildMapSelect")]
        [HarmonyPrefix]
        private static bool BuildMapSelectPrefix(
            NewGameMenu __instance,
            UIPanelBuilder builder,
            ref RectTransform __result)
        {
            if (__instance == null ||
                ProgressionIdField == null ||
                SetupIdField == null ||
                SelectProgressionIdMethod == null)
            {
                return true;
            }

            try
            {
                var selection = Selections.GetValue(
                    __instance,
                    CreateMenuSelection);
                if (string.IsNullOrWhiteSpace(selection.MapId))
                {
                    return true;
                }

                if (!FuseMapPackageRegistry.TryGetMap(
                        selection.MapId,
                        out var map) ||
                    !map.IsValid)
                {
                    selection.MapId = string.Empty;
                    SelectStockMap(__instance);
                    return true;
                }

                var options = FuseNewGameProgressionOption.Build(map);
                var currentProgressionId =
                    ProgressionIdField.GetValue(__instance) as string;
                var selectedIndex = FindProgressionIndex(
                    options,
                    currentProgressionId);
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                    SelectProgression(
                        __instance,
                        options[selectedIndex].ProgressionId);
                }

                __result = builder.AddDropdown(
                    options.Select(option => option.DisplayName).ToList(),
                    selectedIndex,
                    index =>
                    {
                        if (index >= 0 && index < options.Count)
                        {
                            SelectProgression(
                                __instance,
                                options[index].ProgressionId);
                        }
                    });
                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE failed to build the selected custom map's progression dropdown; " +
                    "using Railroader's stock options.",
                    ex);
                return true;
            }
        }

        [HarmonyPatch(typeof(NewGameMenu), "StartButtonClicked")]
        [HarmonyPrefix]
        private static void StartButtonClickedPrefix(
            NewGameMenu __instance,
            out Action<string, NewGameSetup> __state)
        {
            var continuation = __instance?.OnContinue;
            __state = continuation;
            if (continuation == null ||
                !Selections.TryGetValue(__instance, out var selection) ||
                string.IsNullOrEmpty(selection.MapId))
            {
                return;
            }

            var mapId = selection.MapId;
            __instance.OnContinue = (saveName, setup) =>
                continuation(
                    saveName,
                    FuseNewGameMapOption.MarkSelection(setup, mapId));
        }

        [HarmonyPatch(typeof(NewGameMenu), "StartButtonClicked")]
        [HarmonyPostfix]
        private static void StartButtonClickedPostfix(
            NewGameMenu __instance,
            Action<string, NewGameSetup> __state)
        {
            if (__instance != null)
            {
                __instance.OnContinue = __state;
            }
        }

        [HarmonyPatch(
            typeof(MenuManager),
            "Launch",
            new[]
            {
                typeof(GameSetup?),
                typeof(INetworkSetup),
                typeof(GlobalGameManager.SceneLoadSetup),
            })]
        [HarmonyPrefix]
        private static bool LaunchPrefix(ref GameSetup? gameSetup)
        {
            try
            {
                if (!TryPrepareMapLaunch(ref gameSetup, out var error))
                {
                    FuseLog.Warning(
                        "FUSE custom map launch blocked: " + error);
                    Toast.Present(
                        "FUSE map launch failed: " + error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                FuseMapSession.Deactivate(
                    "native map dropdown launch preparation threw");
                FuseLog.Exception(
                    "FUSE failed to prepare the selected custom map; launch was blocked.",
                    ex);
                Toast.Present(
                    "FUSE map launch failed; see FUSE.log.");
                return false;
            }
        }

        internal static bool TryPrepareMapLaunch(
            ref GameSetup? gameSetup,
            out string error)
        {
            error = string.Empty;
            if (!gameSetup.HasValue ||
                !gameSetup.Value.NewGameSetup.HasValue ||
                !FuseNewGameMapOption.TryParseSelectionMarker(
                    gameSetup.Value.NewGameSetup.Value.SetupId,
                    out var mapId))
            {
                FuseMapSession.Deactivate(
                    "stock map selected in New Game");
                return true;
            }

            if (!FuseMapPackageRegistry.TryGetMap(mapId, out var map))
            {
                FuseMapSession.Deactivate(
                    "selected New Game map is no longer registered");
                error =
                    $"Map package '{mapId}' is not installed or enabled.";
                return false;
            }

            if (!map.IsValid)
            {
                FuseMapSession.Deactivate(
                    "selected New Game map is faulted");
                error =
                    $"Map package '{mapId}' cannot load: {map.FaultReason}";
                return false;
            }

            var updatedGameSetup = gameSetup.Value;
            updatedGameSetup.NewGameSetup =
                FuseNewGameMapOption.ClearSelectionMarker(
                    updatedGameSetup.NewGameSetup.Value);
            gameSetup = updatedGameSetup;
            FuseMapSession.Activate(map.MapId);
            return true;
        }

        private static int FindInsertionAfterField(
            List<CodeInstruction> instructions,
            int startIndex,
            MethodInfo addFieldMethod)
        {
            if (startIndex < 0 || addFieldMethod == null)
            {
                return -1;
            }

            for (var index = startIndex + 1;
                 index < instructions.Count;
                 index++)
            {
                if (!instructions[index].Calls(addFieldMethod))
                {
                    continue;
                }

                var insertionIndex = index + 1;
                if (insertionIndex < instructions.Count &&
                    instructions[insertionIndex].opcode == OpCodes.Pop)
                {
                    insertionIndex++;
                }

                return insertionIndex;
            }

            return -1;
        }

        private static int FindSelectedIndex(
            IReadOnlyList<FuseNewGameMapOption> options,
            string selectedMapId)
        {
            if (string.IsNullOrEmpty(selectedMapId))
            {
                return 0;
            }

            for (var index = 1; index < options.Count; index++)
            {
                if (string.Equals(
                        options[index].MapId,
                        selectedMapId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindProgressionIndex(
            IReadOnlyList<FuseNewGameProgressionOption> options,
            string progressionId)
        {
            for (var index = 0; index < options.Count; index++)
            {
                if (string.Equals(
                        options[index].ProgressionId,
                        progressionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static MenuSelection CreateMenuSelection(NewGameMenu _)
        {
            return new MenuSelection();
        }

        private static void AddWorldField(
            NewGameMenu menu,
            UIPanelBuilder builder)
        {
            try
            {
                FuseDataPackageDiscovery.LoadPackagesFromDisk(false);
                var options = FuseNewGameMapOption.Build(
                    FuseMapPackageRegistry.GetRegisteredMaps());
                var selection = Selections.GetValue(
                    menu,
                    CreateMenuSelection);
                var selectedIndex = FindSelectedIndex(
                    options,
                    selection.MapId);
                if (selectedIndex < 0)
                {
                    selection.MapId = string.Empty;
                    selectedIndex = 0;
                }

                builder.AddField(
                    "Map",
                    builder.AddDropdown(
                        options
                            .Select(option => option.DisplayName)
                            .ToList(),
                        selectedIndex,
                        index =>
                        {
                            if (index < 0 || index >= options.Count)
                            {
                                return;
                            }

                            selection.MapId = options[index].MapId;
                            SelectDefaultProgression(
                                menu,
                                selection.MapId);
                            builder.Rebuild();
                        }));
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE failed to add the New Game world selector.",
                    ex);
            }
        }

        private static void SelectDefaultProgression(
            NewGameMenu menu,
            string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                SelectStockMap(menu);
                return;
            }

            if (!FuseMapPackageRegistry.TryGetMap(
                    mapId,
                    out var map) ||
                !map.IsValid)
            {
                SelectProgression(menu, null);
                return;
            }

            var option = FuseNewGameProgressionOption.Build(map)[0];
            SelectProgression(menu, option.ProgressionId);
        }

        private static void SelectProgression(
            NewGameMenu menu,
            string progressionId)
        {
            ProgressionIdField.SetValue(menu, progressionId);
            SetupIdField.SetValue(menu, null);
        }

        private static void SelectStockMap(NewGameMenu menu)
        {
            SelectProgressionIdMethod.Invoke(
                menu,
                new object[] { FuseNewGameMapOption.StockProgressionId });
        }
    }
}
