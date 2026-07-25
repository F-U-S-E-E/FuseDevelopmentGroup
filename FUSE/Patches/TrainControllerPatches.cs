using System;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
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
                    // Harvest the loop's real continue target from the radius
                    // early-out that immediately follows (a bgt-family branch:
                    // "sqrMagnitude > radius*radius -> skip this car"). The
                    // method's only brtrue is the foreach loop-back test, whose
                    // TARGET is the loop head — branching there re-enters the
                    // iteration without advancing the enumerator and hangs the
                    // main thread, so brtrue must never be harvested here.
                    Label continueLabel = default;
                    var foundContinue = false;
                    for (int j = i + 1; j < codes.Count; j++)
                    {
                        var op = codes[j].opcode;
                        if (op == OpCodes.Bgt || op == OpCodes.Bgt_S ||
                            op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
                        {
                            continueLabel = (Label)codes[j].operand;
                            foundContinue = true;
                            break;
                        }
                    }

                    if (!foundContinue)
                    {
                        // Fail open: no injection beats a wrong branch target.
                        FuseLog.Warning(
                            "FUSE CheckForCarsAtPoint height-skip not installed: the radius early-out " +
                            "branch was not found after GetCenterPosition (game loop shape changed).");
                        continue;
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
