using System;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Definition;
using Newtonsoft.Json;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Gives LegosLibraryOfStuff's definition clone operation the same Unity
    /// value converters used by the game. The library's default Newtonsoft
    /// clone walks calculated Vector2/Vector3 properties and can report a
    /// self-referencing loop, aborting the remainder of its catalog edits.
    /// </summary>
    internal static class FuseLegosLibraryCompatibility
    {
        private const string PatchTypeName =
            "LegosLibraryOfStuff.ContainerSerializationDeserializePatch";
        private const string DetailModelPatchTypeName =
            "LegosLibraryOfStuff.DetailModelConditionPatch";
        private const string ComponentGroupHandlerTypeName =
            "LegosLibraryOfStuff.ComponentGroupHandler";

        private static readonly MethodInfo SerializerSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static bool _cloneCompatibilityInstalled;
        private static bool _detailModelCompatibilityInstalled;
        private static int _cloneFailures;
        private static int _detailModelFailures;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var patchType = AccessTools.TypeByName(PatchTypeName);
            var detailModelPatchType = AccessTools.TypeByName(DetailModelPatchTypeName);
            if (patchType == null && detailModelPatchType == null)
            {
                return "idle (not present)";
            }

            if (!_cloneCompatibilityInstalled && patchType != null)
            {
                var cloneItem = AccessTools.DeclaredMethod(
                    patchType,
                    "CloneItem",
                    new[] { typeof(ContainerItem) });
                if (cloneItem != null && SerializerSettingsMethod != null)
                {
                    harmony.Patch(
                        cloneItem,
                        prefix: new HarmonyMethod(
                            typeof(FuseLegosLibraryCompatibility),
                            nameof(CloneItemPrefix)));
                    _cloneCompatibilityInstalled = true;
                }
            }

            if (detailModelPatchType != null)
            {
                _detailModelCompatibilityInstalled = TryEnsureDetailModelCompatibility(
                    harmony,
                    detailModelPatchType,
                    installFusePostfix: !_detailModelCompatibilityInstalled);
            }

            if (_cloneCompatibilityInstalled && _detailModelCompatibilityInstalled)
            {
                return "installed";
            }

            if (_cloneCompatibilityInstalled || _detailModelCompatibilityInstalled)
            {
                return $"partial (clone={_cloneCompatibilityInstalled} detailModel={_detailModelCompatibilityInstalled})";
            }

            return "idle (surface changed)";
        }

        private static bool TryEnsureDetailModelCompatibility(
            Harmony harmony,
            Type patchType,
            bool installFusePostfix)
        {
            var targetResolver = AccessTools.DeclaredMethod(patchType, "TargetMethod");
            var originalPostfix = AccessTools.DeclaredMethod(patchType, "Postfix");
            var handlerType = AccessTools.TypeByName(ComponentGroupHandlerTypeName);
            var handlerMethod = handlerType == null
                ? null
                : AccessTools.DeclaredMethod(handlerType, "DetailModelLoaded");
            if (targetResolver == null || originalPostfix == null || handlerMethod == null)
            {
                return false;
            }

            MethodBase target;
            try
            {
                target = targetResolver.Invoke(null, null) as MethodBase;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not resolve Lego's detail-model completion target: " +
                    ex.GetBaseException().Message);
                return false;
            }

            if (target == null)
            {
                return false;
            }

            // Lego's callback checks only the managed reference before calling
            // GetComponentInParent. Unity objects can retain that reference after
            // their native GameObject has been destroyed, which throws while a
            // detail model is cancelled or torn down. Remove that one callback and
            // retain its live-object behavior through the guarded postfix below.
            harmony.Unpatch(target, originalPostfix);
            if (installFusePostfix)
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(
                        typeof(FuseLegosLibraryCompatibility),
                        nameof(DetailModelConfigurePostfix)));
            }
            return true;
        }

        private static void DetailModelConfigurePostfix(object __instance)
        {
            if (__instance == null)
            {
                return;
            }

            try
            {
                var iteratorType = __instance.GetType();
                var stateField = iteratorType.GetField(
                    "<>1__state",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (stateField == null || !IsCompletedIteratorState(stateField.GetValue(__instance)))
                {
                    return;
                }

                var controllerField = iteratorType.GetField(
                    "<>4__this",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var controller = controllerField?.GetValue(__instance) as UnityEngine.Component;
                if (controller == null)
                {
                    return;
                }

                var handlerType = AccessTools.TypeByName(ComponentGroupHandlerTypeName);
                var handler = handlerType == null
                    ? null
                    : controller.GetComponentInParent(handlerType) as UnityEngine.Component;
                if (handler == null)
                {
                    return;
                }

                AccessTools.DeclaredMethod(handlerType, "DetailModelLoaded")?.Invoke(handler, null);
            }
            catch (Exception ex)
            {
                _detailModelFailures++;
                if (FuseGuardLog.ShouldLog(_detailModelFailures))
                {
                    FuseLog.Exception(
                        "FUSE skipped an unsafe Lego detail-model component-group refresh",
                        ex);
                }
            }
        }

        internal static bool IsCompletedIteratorState(object value)
        {
            return value is int state && state == -2;
        }

        internal static void ResetAfterSuccessfulUnpatch()
        {
            _installed = false;
        }

        private static bool CloneItemPrefix(ContainerItem item, ref ContainerItem __result)
        {
            if (item == null)
            {
                return true;
            }

            try
            {
                var settings = SerializerSettingsMethod.Invoke(null, null) as JsonSerializerSettings;
                if (settings == null)
                {
                    return true;
                }

                var json = JsonConvert.SerializeObject(item, Formatting.None, settings);
                var clone = JsonConvert.DeserializeObject<ContainerItem>(json, settings);
                if (clone == null)
                {
                    return true;
                }

                __result = clone;
                return false;
            }
            catch (Exception ex)
            {
                _cloneFailures++;
                if (FuseGuardLog.ShouldLog(_cloneFailures))
                {
                    FuseLog.Exception(
                        $"FUSE could not safely clone Lego definition '{item.Identifier}'; " +
                        "the library's original clone path will be attempted",
                        ex);
                }

                return true;
            }
        }
    }
}
