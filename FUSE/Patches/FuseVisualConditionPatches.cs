using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using FUSE.Runtime.API;
using Game.State;
using HarmonyLib;
using Model;
using UI.Builder;
using UI.CarInspector;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Routes the wear value the game bakes into car materials through the
    /// FUSE visual-condition override. The game derives the shader wear blend
    /// from the car's mechanical condition every time it refreshes materials
    /// (condition changes, visibility/cull-in); swapping the condition field
    /// for the duration of that refresh reuses the game's own renderer and
    /// shader handling instead of duplicating it here.
    /// </summary>
    [HarmonyPatch(typeof(Car), "UpdateMaterialsForCondition")]
    internal static class FuseCarVisualConditionMaterialPatch
    {
        private static readonly FieldInfo ConditionField = AccessTools.Field(typeof(Car), "_condition");

        private static void Prefix(Car __instance, out float? __state)
        {
            __state = null;
            try
            {
                if (ConditionField == null || __instance == null || __instance.ghost || __instance.KeyValueObject == null)
                {
                    return;
                }

                var visual = FuseVisualConditionAPI.TryGetVisualCondition(__instance);
                if (!visual.HasValue)
                {
                    return;
                }

                var actual = (float)ConditionField.GetValue(__instance);
                var effective = FuseVisualConditionAPI.EffectiveCondition(
                    actual, visual.Value, FuseSettings.DecoupleVisualConditionLimits);
                if (Mathf.Approximately(effective, actual))
                {
                    return;
                }

                __state = actual;
                ConditionField.SetValue(__instance, effective);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual condition override failed; using mechanical condition " +
                    $"car='{(__instance != null ? __instance.id : null) ?? string.Empty}' message='{ex.Message}'.");
            }
        }

        // Finalizer rather than Postfix so the mechanical condition is
        // restored even when the material refresh throws mid-way. A void
        // finalizer leaves any original exception untouched.
        private static void Finalizer(Car __instance, float? __state)
        {
            if (!__state.HasValue || ConditionField == null || __instance == null)
            {
                return;
            }

            try
            {
                ConditionField.SetValue(__instance, __state.Value);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual condition restore failed car='{__instance.id ?? string.Empty}' message='{ex.Message}'.");
            }
        }
    }

    /// <summary>
    /// Refreshes car materials whenever a visual-condition override arrives —
    /// from the inspector slider, a loaded save (the key-value reset fires
    /// per-key observers), or a multiplayer peer. The observers join the same
    /// set the car uses for its own key-value subscriptions, so they are
    /// disposed with the car.
    /// </summary>
    [HarmonyPatch(typeof(Car), "SetupKeyValueObject")]
    internal static class FuseCarVisualConditionObserverPatch
    {
        private static void Postfix(Car __instance, HashSet<IDisposable> ___Observers)
        {
            try
            {
                if (__instance == null || __instance.KeyValueObject == null || ___Observers == null)
                {
                    return;
                }

                ___Observers.Add(__instance.KeyValueObject.Observe(
                    FuseVisualConditionAPI.VisualConditionKey,
                    _ => FuseVisualConditionAPI.RefreshCarMaterials(__instance),
                    callInitial: false));
                ___Observers.Add(__instance.KeyValueObject.Observe(
                    FuseVisualConditionAPI.LegacyVisualConditionKey,
                    _ => FuseVisualConditionAPI.RefreshCarMaterials(__instance),
                    callInitial: false));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual condition observer install failed " +
                    $"car='{(__instance != null ? __instance.id : null) ?? string.Empty}' message='{ex.Message}'.");
            }
        }
    }

    /// <summary>
    /// Adds the per-car "Visual Condition" slider to the car inspector's
    /// Equipment tab, under the stock Condition and Mileage rows.
    /// </summary>
    [HarmonyPatch(typeof(CarInspector), "PopulateEquipmentPanel")]
    internal static class FuseCarInspectorVisualConditionSliderPatch
    {
        private static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            try
            {
                var car = ____car;
                if (car == null || car.ghost || car.KeyValueObject == null)
                {
                    return;
                }

                // The patch classes apply independently; if the rendering
                // half failed to install, do not offer a control that writes
                // persistent, replicated keys nothing will ever honor.
                if (RenderingPatchesFailed())
                {
                    return;
                }

                if (!CanEditVisualCondition(car))
                {
                    // The property-sync layer would reject this player's
                    // writes host-side and the slider would rubber-band, so
                    // give unauthorized players the read-only treatment the
                    // game uses for controls they may not change. Only worth
                    // a row when there is an override to report.
                    if (FuseVisualConditionAPI.TryGetVisualCondition(car).HasValue)
                    {
                        var label = builder.AddField(
                            "Visual Condition",
                            () => FuseVisualConditionAPI.FormatPercent(FuseVisualConditionAPI.GetSliderValue(car)),
                            UIPanelBuilder.Frequency.Periodic);
                        label.RectTransform.SetSiblingIndex(2);
                    }

                    return;
                }

                var row = builder.AddField("Visual Condition", builder.AddSlider(
                    () => FuseVisualConditionAPI.GetSliderValue(car),
                    () => FuseVisualConditionAPI.FormatPercent(FuseVisualConditionAPI.GetSliderValue(car)),
                    value => FuseVisualConditionAPI.SetVisualCondition(car, value),
                    0f, 1f));
                row.Tooltip(
                    "Visual Condition",
                    "How weathered this car looks. Purely cosmetic — repair state and performance are unaffected. " +
                    "100% removes the override. By default the look can only be made worse than the mechanical " +
                    "condition; toggle \"Visual Condition\" in FUSE settings to also let worn cars look fresh.");
                // The equipment panel ends with an expanding spacer and the
                // Customize button, so an appended row would hug the window
                // bottom. Tuck it under the stock Condition and Mileage rows
                // instead, where it reads as part of the condition block.
                row.RectTransform.SetSiblingIndex(2);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE visual condition slider failed to attach " +
                    $"car='{(____car != null ? ____car.id : null) ?? string.Empty}' message='{ex.Message}'.");
            }
        }

        private static bool RenderingPatchesFailed()
        {
            var material = typeof(FuseCarVisualConditionMaterialPatch).FullName;
            var observer = typeof(FuseCarVisualConditionObserverPatch).FullName;
            foreach (var failed in FusePatchResilience.Failed)
            {
                if (failed != null && (failed.TypeName == material || failed.TypeName == observer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanEditVisualCondition(Car car)
        {
            try
            {
                return StateManager.CheckAuthorizedToChangeProperty(car.id, FuseVisualConditionAPI.VisualConditionKey);
            }
            catch (Exception ex)
            {
                // Fail closed: if the state layer cannot answer, offering a
                // control that writes replicated persistent keys is worse
                // than the read-only treatment.
                FuseLog.Warning(
                    $"FUSE visual condition auth check failed " +
                    $"car='{(car != null ? car.id : null) ?? string.Empty}' message='{ex.Message}'.");
                return false;
            }
        }
    }

    /// <summary>
    /// Randomizes the visual condition of freshly spawned cars when the
    /// <see cref="FuseSettings.RandomizeVisualConditionOnSpawn"/> setting is
    /// on. The hook returns the just-created cars after their key-value
    /// objects are registered, so the writes land on live cars and the
    /// observer patch above repaints them immediately.
    ///
    /// <para>Host-only: the values replicate to clients through the
    /// property-sync layer, so letting clients roll their own would
    /// double-write conflicting conditions. The per-car writes are wrapped
    /// in a single transaction scope so a multi-car spawn replicates as one
    /// batch; the scope is null in single player, which <c>using</c>
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
                        if (car == null || car.ghost)
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
