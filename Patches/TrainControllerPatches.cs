using System;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Lifecycle;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(TrainController), "HandleSnapshotTurntables")]
    internal static class TrainControllerPatches
    {
        private static void Prefix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("before turntable restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE turntable rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("after turntable restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE turntable rebind (postfix) failed.", ex);
            }
        }
    }

    [HarmonyPatch(typeof(TrainController), "CheckForCarsAtPoint")]
    public static class CheckForCarsAtPointPatch
    {
        // Skip cars whose center is more than 4m from the point
        static bool IsWithinFourMeters(Vector3 point, Vector3 center)
        {
            return Mathf.Abs(center.y - point.y) <= 4f;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var getCenterPos =
                AccessTools.Method(typeof(Model.Car), "GetCenterPosition");

            var helper =
                AccessTools.Method(typeof(CheckForCarsAtPointPatch), nameof(IsWithinFourMeters));

            for (int i = 0; i < codes.Count; i++)
            {
                yield return codes[i];

                // After:
                // value.GetCenterPosition(graph)
                // stloc.3
                //
                // Inject:
                // if (!IsWithinFourMeters(point, centerPosition))
                //     continue;
                if (i > 0 &&
                    codes[i - 1].Calls(getCenterPos) &&
                    codes[i].opcode == OpCodes.Stloc_3)
                {
                    Label continueLabel = default;

                    // Find the existing continue target (IL_010d)
                    for (int j = i; j < codes.Count; j++)
                    {
                        if (codes[j].opcode == OpCodes.Brtrue ||
                            codes[j].opcode == OpCodes.Brtrue_S)
                        {
                            continueLabel = (Label)codes[j].operand;
                            break;
                        }
                    }

                    yield return new CodeInstruction(OpCodes.Ldarg_1); // point
                    yield return new CodeInstruction(OpCodes.Ldloc_3); // centerPosition
                    yield return new CodeInstruction(OpCodes.Call, helper);
                    yield return new CodeInstruction(OpCodes.Brfalse, continueLabel);
                }
            }
        }
    }
}
