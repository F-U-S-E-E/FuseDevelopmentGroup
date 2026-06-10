using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FUSE.Infrastructure;
using FUSE.Runtime.API;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using UI.Builder;
using UI.CarInspector;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Re-runs the car's material/wear refresh whenever its visual
    /// condition changes. The game only refreshes wear when the
    /// MECHANICAL condition key changes, so without these observers a
    /// visual-condition write (slider drag, spawn randomization, or a
    /// replicated change on a multiplayer client) would not show until
    /// the next unrelated condition update. Observers are added to the
    /// car's own observer set so they're disposed with the car.
    /// </summary>
    [HarmonyPatch(typeof(Car), "SetupKeyValueObject")]
    internal static class FuseCarVisualConditionObserverPatch
    {
        private static readonly MethodInfo UpdateMaterialsMethod =
            AccessTools.Method(typeof(Car), "UpdateMaterialsForCondition");

        private static void Postfix(Car __instance, HashSet<IDisposable> ___Observers)
        {
            try
            {
                if (__instance == null || __instance.KeyValueObject == null ||
                    ___Observers == null || UpdateMaterialsMethod == null)
                {
                    return;
                }

                void Refresh(Value _)
                {
                    try
                    {
                        UpdateMaterialsMethod.Invoke(__instance, null);
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE visual-condition material refresh failed softly: {ex.GetBaseException().Message}");
                    }
                }

                ___Observers.Add(__instance.KeyValueObject.Observe(
                    FuseVisualConditionAPI.VisualConditionKey, Refresh, false));
                ___Observers.Add(__instance.KeyValueObject.Observe(
                    FuseVisualConditionAPI.LegacyVisualConditionKey, Refresh, false));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual-condition observer wiring failed softly: {ex.GetBaseException().Message}");
            }
        }
    }

    /// <summary>
    /// Blends the visual condition into the wear amount the car shader
    /// renders. The game computes wear inside a compiler-generated local
    /// function of <c>Car.UpdateMaterialsForCondition</c> as
    /// <c>Mathf.InverseLerp(1f, 0.25f, condition)</c> using the
    /// mechanical condition; this transpiler routes that condition input
    /// through <see cref="FuseVisualConditionAPI.EffectiveWearCondition"/>
    /// (the lower of mechanical and visual wins) right before the
    /// InverseLerp call. If the anchor can't be found after a game
    /// update, the method is left untouched and vanilla wear behavior is
    /// kept — visual condition then simply has no rendered effect rather
    /// than breaking material updates.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseCarWearVisualConditionPatch
    {
        private static MethodInfo TargetMethod()
        {
            // The local function keeps its parent method's name in the
            // compiler-generated form "<UpdateMaterialsForCondition>g__Apply|...",
            // which is stable across the numeric suffix changing between
            // game builds.
            return AccessTools.GetDeclaredMethods(typeof(Car))
                .FirstOrDefault(method => method.Name.Contains("<UpdateMaterialsForCondition>g__Apply"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var inverseLerp = AccessTools.Method(typeof(Mathf), nameof(Mathf.InverseLerp));
            var adjust = AccessTools.Method(
                typeof(FuseCarWearVisualConditionPatch), nameof(AdjustWearCondition));

            var index = codes.FindIndex(code => code.Calls(inverseLerp));
            if (index < 0)
            {
                FuseLog.Warning(
                    "FUSE visual-condition wear patch could not locate the InverseLerp anchor; " +
                    "vanilla wear behavior is kept.");
                return codes;
            }

            // Stack at the anchor is [edge0, edge1, condition]; push the
            // Car instance (the local function is an instance method) and
            // swap the condition for the blended value.
            codes.Insert(index, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(index + 1, new CodeInstruction(OpCodes.Call, adjust));
            return codes;
        }

        private static float AdjustWearCondition(float mechanicalCondition, Car car)
        {
            try
            {
                return FuseVisualConditionAPI.EffectiveWearCondition(mechanicalCondition, car);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual-condition wear blend failed softly: {ex.GetBaseException().Message}");
                return mechanicalCondition;
            }
        }
    }

    /// <summary>
    /// Adds a "Visual Condition" slider to the car inspector's Equipment
    /// tab. Writes route through <see cref="FuseVisualConditionAPI"/> so
    /// they replicate and persist like any other car property; the
    /// readout falls back to the legacy key so saves migrated from the
    /// legacy mod show their existing weathering.
    /// </summary>
    [HarmonyPatch(typeof(CarInspector), "PopulateEquipmentPanel")]
    internal static class FuseCarInspectorVisualConditionSliderPatch
    {
        private static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            try
            {
                var car = ____car;
                if (car == null || car.KeyValueObject == null)
                {
                    return;
                }

                builder.AddField("Visual Condition", builder.AddSlider(
                    () => FuseVisualConditionAPI.GetVisualCondition(car),
                    () => (FuseVisualConditionAPI.GetVisualCondition(car) * 100f).ToString("0") + "%",
                    value => FuseVisualConditionAPI.SetVisualCondition(car, value),
                    0f,
                    1f));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual-condition slider failed softly: {ex.GetBaseException().Message}");
            }
        }
    }

    /// <summary>
    /// Randomizes the visual condition of freshly spawned cars when the
    /// <see cref="FuseSettings.RandomizeVisualConditionOnSpawn"/> setting
    /// is on. The hook returns the just-created cars, after the game has
    /// registered their key-value objects, so the writes land on live
    /// cars.
    ///
    /// <para>Host-only: the values replicate to clients through the
    /// state manager, so letting clients roll their own would double-write
    /// conflicting conditions. The per-car writes are wrapped in a single
    /// transaction scope so a multi-car spawn replicates as one batch;
    /// the scope is null in single player, which <c>using</c>
    /// tolerates.</para>
    /// </summary>
    [HarmonyPatch(typeof(TrainController), "HandleCreateCarsAsTrain")]
    internal static class FuseTrainControllerSpawnVisualConditionPatch
    {
        private static void Postfix(List<Car> __result)
        {
            try
            {
                if (!FuseSettings.RandomizeVisualConditionOnSpawn ||
                    __result == null || __result.Count == 0 ||
                    !StateManager.IsHost)
                {
                    return;
                }

                using (StateManager.TransactionScope())
                {
                    foreach (var car in __result)
                    {
                        if (car == null)
                        {
                            continue;
                        }

                        var condition = FuseVisualConditionAPI.ComputeSpawnCondition(
                            FuseSettings.RandomVisualConditionMin,
                            FuseSettings.RandomVisualConditionMax,
                            UnityEngine.Random.value);
                        FuseVisualConditionAPI.SetVisualCondition(car, condition);
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE spawn visual-condition randomization failed softly: {ex.GetBaseException().Message}");
            }
        }
    }
}
