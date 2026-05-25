using System;
using FUSE.Infrastructure;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using RollingStock;
using RollingStock.Controls;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Diagnostic Harmony patches around the <see cref="KeyValueBoolAnimator"/>
    /// pipeline that drives industry loader / water column / coal chute /
    /// turntable animations. Gated behind
    /// <see cref="FuseSettings.VerboseApplyReportDetails"/>.
    ///
    /// Animation chain (from game IL): the animator does
    /// <c>GetComponentInParent&lt;KeyValueObject&gt;()</c> in
    /// <c>OnEnableWithProperties</c>, then observes a specific bool
    /// <c>key</c>. When the bool flips, <see cref="KeyValueBoolAnimator.PropertyChanged"/>
    /// recomputes playback speed/time. If the animator can't find a parent
    /// <see cref="KeyValueObject"/>, it never observes and never plays — the
    /// loader stays static. FUSE has exactly one
    /// <c>AddComponent&lt;KeyValueObject&gt;</c> call in the entire repo
    /// (for roundhouse roots), which is a candidate cause for mod-added
    /// industries whose loaders never animate.
    ///
    /// What we capture:
    ///   - <c>OnEnableWithProperties</c> postfix logs the animator's
    ///     GameObject path, the bool key it observes, and whether a
    ///     KeyValueObject was found in the parent chain.
    ///   - <c>PropertyChanged</c> postfix logs every observed flip.
    /// </summary>
    [HarmonyPatch(typeof(KeyValueBoolAnimator), "OnEnableWithProperties")]
    internal static class FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch
    {
        private static void Postfix(KeyValueBoolAnimator __instance)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var kvo = __instance.GetComponentInParent<KeyValueObject>();
                var path = FormatTransformPath(__instance.transform);
                var key = __instance.key ?? "<null>";
                var keyValueObjectPath = kvo != null ? FormatTransformPath(kvo.transform) : "<not-found>";
                var currentValue = kvo != null ? kvo[__instance.key].BoolValue : (bool?)null;
                FuseLog.Info(
                    $"FUSE diag KeyValueBoolAnimator.OnEnableWithProperties path='{path}' key='{key}' " +
                    $"parentKVO='{keyValueObjectPath}' currentBool={(currentValue.HasValue ? currentValue.Value.ToString() : "<n/a>")} " +
                    $"invert={__instance.invert} active={__instance.gameObject.activeInHierarchy}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag KeyValueBoolAnimator OnEnable postfix failed: {ex.Message}");
            }
        }

        internal static string FormatTransformPath(Transform transform)
        {
            if (transform == null) return "<null>";
            var segments = new System.Collections.Generic.List<string>();
            var cursor = transform;
            var depth = 0;
            while (cursor != null && depth < 16)
            {
                segments.Add(cursor.name);
                cursor = cursor.parent;
                depth++;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }
    }

    [HarmonyPatch(typeof(KeyValueBoolAnimator), "PropertyChanged")]
    internal static class FuseKeyValueBoolAnimatorPropertyChangedDiagnosticPatch
    {
        private static void Postfix(KeyValueBoolAnimator __instance, Value value)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var path = FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(__instance.transform);
                FuseLog.Info(
                    $"FUSE diag KeyValueBoolAnimator.PropertyChanged path='{path}' key='{__instance.key ?? "<null>"}' " +
                    $"newBool={value.BoolValue} invert={__instance.invert}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag KeyValueBoolAnimator PropertyChanged postfix failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Diagnostic on <see cref="KeyValuePickableToggle.Activate"/> — the
    /// click handler for water columns / fueling stands / coal towers /
    /// roundhouse stall doors. When the player clicks the GameObject and
    /// the raycast resolves to this <c>IPickable</c>, Activate fires and
    /// toggles the bool in the parent <c>KeyValueObject</c>. If we never
    /// see this log when the user expects a click to take effect, the
    /// click is being lost upstream — usually because the GameObject has
    /// no active Collider, the raycast distance check fails, or the
    /// IPickable is overridden by a closer pickable.
    /// </summary>
    [HarmonyPatch(typeof(KeyValuePickableToggle), nameof(KeyValuePickableToggle.Activate))]
    internal static class FuseKeyValuePickableToggleActivateDiagnosticPatch
    {
        private static void Postfix(KeyValuePickableToggle __instance, PickableActivateEvent evt)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var path = FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(__instance.transform);
                var kvo = __instance.GetComponentInParent<KeyValueObject>();
                var kvoPath = kvo != null ? FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(kvo.transform) : "<not-found>";
                FuseLog.Info(
                    $"FUSE diag KeyValuePickableToggle.Activate fired path='{path}' key='{__instance.key ?? "<null>"}' " +
                    $"parentKVO='{kvoPath}' activation={evt.Activation} ctrl={evt.IsControlDown} shift={evt.IsShiftDown}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag KeyValuePickableToggle.Activate postfix failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Diagnostic on <see cref="CarLoaderSequencer"/>.<c>OnEnable</c> — this
    /// is the bridge between a click toggling the <c>request</c> bool and
    /// the animator playing. Without an active CarLoaderSequencer observing
    /// the parent KVO, clicks flip <c>request</c> but nothing ever writes
    /// <c>prepareLoad</c> / <c>animateLoad</c> — so the loader appears
    /// dead. The sequencer only attaches its observers when
    /// <c>StateManager.IsHost</c> is true and its <c>keyValueObject</c>
    /// SerializeField reference is non-null.
    /// </summary>
    [HarmonyPatch(typeof(CarLoaderSequencer), "OnEnable")]
    internal static class FuseCarLoaderSequencerOnEnableDiagnosticPatch
    {
        private static void Postfix(CarLoaderSequencer __instance)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var path = FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(__instance.transform);
                var kvo = __instance.keyValueObject;
                var kvoPath = kvo != null ? FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(kvo.transform) : "<not-assigned>";
                FuseLog.Info(
                    $"FUSE diag CarLoaderSequencer.OnEnable path='{path}' kvoRef='{kvoPath}' " +
                    $"readWantsLoadingKey='{__instance.readWantsLoadingKey}' readIsLoadingKey='{__instance.readIsLoadingKey}' " +
                    $"writeCanLoadKey='{__instance.writeCanLoadKey}' writePrepareLoadKey='{__instance.writePrepareLoadKey}' " +
                    $"writeAnimateLoadKey='{__instance.writeAnimateLoadKey}' " +
                    $"isHost={StateManager.IsHost} active={__instance.gameObject.activeInHierarchy} " +
                    $"enabled={__instance.enabled}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag CarLoaderSequencer.OnEnable postfix failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Diagnostic on <c>GlobalKeyValueObject.OnEnable</c>. Critical for
    /// diagnosing "HandlePropertyChange: Unknown object &lt;id&gt;" — this
    /// is the OnEnable that calls
    /// <c>StateManager.Shared.RegisterPropertyObject(globalObjectId,...)</c>
    /// and registers a KVO so PropertyChange messages can route to it.
    /// If the StateManager doesn't know about an id at click time, this
    /// log line reveals whether registration was ever attempted, what
    /// state the GameObject was in, and whether StateManager.Shared was
    /// available.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseGlobalKeyValueObjectOnEnableDiagnosticPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            var type = Type.GetType("RollingStock.Controls.GlobalKeyValueObject, Assembly-CSharp");
            return type?.GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        private static void Postfix(MonoBehaviour __instance)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var type = __instance.GetType();
                var globalIdField = type.GetField("globalObjectId");
                var globalId = globalIdField?.GetValue(__instance) as string;
                var path = FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(__instance.transform);
                var stateManagerReady = StateManager.Shared != null;
                FuseLog.Info(
                    $"FUSE diag GlobalKeyValueObject.OnEnable path='{path}' globalObjectId='{globalId ?? "<null>"}' " +
                    $"stateManagerShared={stateManagerReady} active={__instance.gameObject.activeInHierarchy} enabled={__instance.enabled}.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag GlobalKeyValueObject.OnEnable postfix failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class FuseGlobalKeyValueObjectOnDisableDiagnosticPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            var type = Type.GetType("RollingStock.Controls.GlobalKeyValueObject, Assembly-CSharp");
            return type?.GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        }

        private static void Postfix(MonoBehaviour __instance)
        {
            if (!FuseSettings.VerboseApplyReportDetails || __instance == null)
            {
                return;
            }

            try
            {
                var type = __instance.GetType();
                var globalIdField = type.GetField("globalObjectId");
                var globalId = globalIdField?.GetValue(__instance) as string;
                var path = FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(__instance.transform);
                FuseLog.Info(
                    $"FUSE diag GlobalKeyValueObject.OnDisable (UNREGISTERS) path='{path}' globalObjectId='{globalId ?? "<null>"}' " +
                    $"caller='{FuseKeyValuePickableToggleActivateDiagnosticPatch_FormatShortCaller()}'.");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE diag GlobalKeyValueObject.OnDisable postfix failed: {ex.Message}");
            }
        }

        // Local helper because the original FormatShortCaller is on a sibling patch class.
        private static string FuseKeyValuePickableToggleActivateDiagnosticPatch_FormatShortCaller()
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(2, false);
                for (var i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    if (method == null) continue;
                    var typeName = method.DeclaringType?.FullName ?? "<unknown>";
                    if (typeName.StartsWith("HarmonyLib", StringComparison.Ordinal)) continue;
                    if (typeName.Contains("DynamicMethodDefinition")) continue;
                    if (typeName.StartsWith("FUSE.Patches.FuseGlobalKeyValueObject", StringComparison.Ordinal)) continue;
                    return $"{typeName}.{method.Name}";
                }
            }
            catch
            {
            }
            return "<unavailable>";
        }
    }
}
