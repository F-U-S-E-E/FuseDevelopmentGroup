using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string LibraryTypeName =
            "LegosLibraryOfStuff.LibraryOfStuff";

        private static readonly MethodInfo SerializerSettingsMethod =
            AccessTools.Method(typeof(ContainerSerialization), "JsonSerializerSettings");

        private static bool _cloneCompatibilityInstalled;
        private static bool _detailModelCompatibilityInstalled;
        private static bool _containerFastPathInstalled;
        private static int _cloneFailures;
        private static int _detailModelFailures;
        private static MethodInfo _loadJsonDefinitionsMethod;
        private static FieldInfo _definitionIdentifiersField;
        private static HashSet<string> _definitionIdentifiers;
        private static int _definitionIdentifierCount = -1;
        private static bool _definitionsPrimed;
        private static int _containersSkipped;

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

                var originalPostfix = AccessTools.DeclaredMethod(patchType, "Postfix");
                var libraryType = AccessTools.TypeByName(LibraryTypeName);
                _loadJsonDefinitionsMethod = libraryType == null
                    ? null
                    : AccessTools.DeclaredMethod(libraryType, "LoadJsonDefinitions");
                _definitionIdentifiersField = libraryType == null
                    ? null
                    : AccessTools.Field(libraryType, "definitionIdentifiers");
                if (!_containerFastPathInstalled &&
                    originalPostfix != null &&
                    _loadJsonDefinitionsMethod != null &&
                    _definitionIdentifiersField != null)
                {
                    harmony.Patch(
                        originalPostfix,
                        prefix: new HarmonyMethod(
                            typeof(FuseLegosLibraryCompatibility),
                            nameof(ContainerPostfixPrefix)));
                    _containerFastPathInstalled = true;
                }
            }

            if (detailModelPatchType != null)
            {
                _detailModelCompatibilityInstalled = TryEnsureDetailModelCompatibility(
                    harmony,
                    detailModelPatchType,
                    installFusePostfix: !_detailModelCompatibilityInstalled);
            }

            if (_cloneCompatibilityInstalled &&
                _detailModelCompatibilityInstalled &&
                _containerFastPathInstalled)
            {
                return "installed";
            }

            if (_cloneCompatibilityInstalled || _detailModelCompatibilityInstalled)
            {
                return $"partial (clone={_cloneCompatibilityInstalled} detailModel={_detailModelCompatibilityInstalled} containerFastPath={_containerFastPathInstalled})";
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
            _cloneCompatibilityInstalled = false;
            _detailModelCompatibilityInstalled = false;
            _containerFastPathInstalled = false;
            _cloneFailures = 0;
            _detailModelFailures = 0;
            _loadJsonDefinitionsMethod = null;
            _definitionIdentifiersField = null;
            _definitionIdentifiers = null;
            _definitionIdentifierCount = -1;
            _definitionsPrimed = false;
            _containersSkipped = 0;
        }

        internal static int ContainersSkippedByFastPath => _containersSkipped;

        internal static bool ContainerMayContainEditedDefinition(
            Container container,
            ISet<string> identifiers)
        {
            if (container?.Objects == null || identifiers == null || identifiers.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < container.Objects.Count; index++)
            {
                var identifier = container.Objects[index]?.Identifier;
                if (!string.IsNullOrEmpty(identifier) && identifiers.Contains(identifier))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainerPostfixPrefix(ref Container __0)
        {
            try
            {
                if (!TryGetDefinitionIdentifiers(out var identifiers))
                {
                    return true;
                }

                if (ContainerMayContainEditedDefinition(__0, identifiers))
                {
                    return true;
                }

                _containersSkipped++;
                return false;
            }
            catch
            {
                // The optimization is optional. Lego's original postfix remains
                // the compatibility authority whenever its reflected surface moves.
                return true;
            }
        }

        private static bool TryGetDefinitionIdentifiers(out HashSet<string> identifiers)
        {
            identifiers = null;
            if (_loadJsonDefinitionsMethod == null || _definitionIdentifiersField == null)
            {
                return false;
            }

            if (!_definitionsPrimed)
            {
                _loadJsonDefinitionsMethod.Invoke(null, null);
                _definitionsPrimed = true;
            }

            if (!(_definitionIdentifiersField.GetValue(null) is IEnumerable source))
            {
                return false;
            }

            var count = source is ICollection collection ? collection.Count : -1;
            if (_definitionIdentifiers != null && count >= 0 && count == _definitionIdentifierCount)
            {
                identifiers = _definitionIdentifiers;
                return true;
            }

            var rebuilt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in source)
            {
                if (value is string identifier && !string.IsNullOrEmpty(identifier))
                {
                    rebuilt.Add(identifier);
                }
            }

            _definitionIdentifiers = rebuilt;
            _definitionIdentifierCount = count >= 0 ? count : rebuilt.Count;
            identifiers = rebuilt;
            return true;
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
