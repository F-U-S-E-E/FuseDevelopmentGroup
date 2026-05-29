using System;
using FUSE.Infrastructure;
using Game.Events;
using HarmonyLib;
using UI.Builder;
using UI.Common;
using UI.CompanyWindow;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(GoalsPanelBuilder), nameof(GoalsPanelBuilder.Build))]
    internal static class FuseGoalsPanelGameModePatch
    {
        private static void Postfix(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            try
            {
                builder.RebuildOnEvent<GameModeDidChange>();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to attach Milestones panel game-mode rebuild observer.", ex);
            }
        }
    }
}
