using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KeyValue.Runtime;
using Model;
using Model.Ops;
using FUSE.Infrastructure;
using RollingStock;
using RollingStock.Controls;
using Track;
using UI.Map;
using UnityEngine;

namespace FUSE.API
{
    internal sealed class FusePrefabSanitizerResult
    {
        public List<string> Actions { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public void Log(string context)
        {
            if (Actions.Count > 0)
            {
                FuseLog.Info($"{context} sanitizer actions: {string.Join("; ", Actions.ToArray())}");
            }

            foreach (var warning in Warnings)
            {
                FuseLog.Warning($"{context} sanitizer warning: {warning}");
            }
        }
    }

    internal static class FusePrefabSanitizer
    {
        private static readonly FieldInfo StationAreaField = typeof(StationAgent).GetField("area", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StationPassengerStopField = typeof(StationAgent).GetField("passengerStop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StationSecondaryAreasField = typeof(StationAgent).GetField("secondaryAreas", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo IndustryHoverableIndustryField = typeof(IndustryContentHoverable).GetField("industry", BindingFlags.Instance | BindingFlags.NonPublic);

        public static FusePrefabSanitizerResult SanitizeSceneClone(GameObject root, string id)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "scene clone", id, result))
            {
                return result;
            }

            ReplaceGlobalObjectIds(root, id + ".sceneClone", result);
            DisableTrackMarkers(root, result);
            ClearCachedIdentityFields(root, result);
            ValidateRendererPresence(root, "scene clone", result);
            return result;
        }

        public static FusePrefabSanitizerResult SanitizeLoader(GameObject root, string id, Industry industry)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "loader", id, result))
            {
                return result;
            }

            ReplaceGlobalObjectIds(root, id + ".loader", result);
            DisableTrackMarkers(root, result);
            ClearCachedIdentityFields(root, result);
            RelinkLoaderIndustry(root, industry, result);
            ValidateRequiredComponent<CarLoadTargetLoader>(root, "loader", result);
            ValidateRendererPresence(root, "loader", result);
            return result;
        }

        public static FusePrefabSanitizerResult SanitizeStation(
            GameObject root,
            string id,
            StationAgent stationAgent,
            Area area,
            PassengerStop passengerStop)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "station", id, result))
            {
                return result;
            }

            ReplaceGlobalObjectIds(root, id + ".station", result);
            DisableTrackMarkers(root, result);
            ClearCachedIdentityFields(root, result);
            RelinkStationAgent(stationAgent, area, passengerStop, result);
            ValidateRequiredComponent<StationAgent>(root, "station", result);
            ValidateRequiredComponent<MapIcon>(root, "station map icon", result);
            ValidateRendererPresence(root, "station", result);
            return result;
        }

        public static FusePrefabSanitizerResult SanitizeTurntable(GameObject root, string id, Turntable turntable)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "turntable", id, result))
            {
                return result;
            }

            ReplaceGlobalObjectIds(root, id + ".turntable", result);
            DisableTrackMarkers(root, result);
            ClearCachedIdentityFields(root, result);
            RelinkTurntableController(root, turntable, result);
            ForceLodZero(root, result);
            ValidateRequiredComponent<Turntable>(root, "turntable", result);
            ValidateRequiredComponent<TurntableController>(root, "turntable controller", result);
            ValidateRendererPresence(root, "turntable", result);
            return result;
        }

        public static FusePrefabSanitizerResult ValidateLoaderPostBind(GameObject root, string id, Industry expectedIndustry)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "loader", id, result))
            {
                return result;
            }

            ValidateSaneIdentityAndTransform(root, "loader", id, result);
            if (!root.activeSelf)
            {
                result.Warnings.Add("loader root is inactive after apply.");
            }

            if (LoaderAPI.GetLoader(id) == null)
            {
                result.Warnings.Add("loader is not registered in LoaderAPI runtime index.");
            }

            var targetLoader = root.GetComponentInChildren<CarLoadTargetLoader>(true);
            if (targetLoader == null)
            {
                result.Warnings.Add("loader has no CarLoadTargetLoader after bind.");
            }
            else if (expectedIndustry != null && !ReferenceEquals(targetLoader.sourceIndustry, expectedIndustry))
            {
                result.Warnings.Add("loader CarLoadTargetLoader.sourceIndustry does not match expected industry.");
            }

            return result;
        }

        public static FusePrefabSanitizerResult ValidateStationPostBind(
            GameObject root,
            string id,
            StationAgent stationAgent,
            Area expectedArea,
            PassengerStop expectedPassengerStop)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "station", id, result))
            {
                return result;
            }

            ValidateSaneIdentityAndTransform(root, "station", id, result);
            if (!root.activeSelf)
            {
                result.Warnings.Add("station root is inactive after apply.");
            }

            if (stationAgent == null)
            {
                result.Warnings.Add("station has no StationAgent after bind.");
                return result;
            }

            if (!ReferenceEquals(StationAPI.GetStationAgent(id), stationAgent))
            {
                result.Warnings.Add("station is not registered to the expected StationAgent.");
            }

            var area = StationAreaField?.GetValue(stationAgent) as Area;
            if (expectedArea != null && !ReferenceEquals(area, expectedArea))
            {
                result.Warnings.Add("station area reference does not match expected area.");
            }

            var stop = StationPassengerStopField?.GetValue(stationAgent) as PassengerStop;
            if (expectedPassengerStop != null && !ReferenceEquals(stop, expectedPassengerStop))
            {
                result.Warnings.Add("station passenger stop reference does not match expected passenger stop.");
            }

            if (root.GetComponentInChildren<MapIcon>(true) == null)
            {
                result.Warnings.Add("station has no MapIcon after bind.");
            }

            return result;
        }

        public static FusePrefabSanitizerResult ValidateTurntablePostBind(GameObject root, string id, Turntable turntable)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "turntable", id, result))
            {
                return result;
            }

            ValidateSaneIdentityAndTransform(root, "turntable", id, result);
            if (!root.activeSelf)
            {
                result.Warnings.Add("turntable root is inactive after apply.");
            }

            if (turntable == null)
            {
                result.Warnings.Add("turntable component is missing after bind.");
                return result;
            }

            if (!ReferenceEquals(TurntableAPI.GetTurntable(id), turntable))
            {
                result.Warnings.Add("turntable is not registered to the expected runtime object.");
            }

            var controllers = root.GetComponentsInChildren<TurntableController>(true);
            if (controllers.Length == 0)
            {
                result.Warnings.Add("turntable has no TurntableController after bind.");
            }

            for (var index = 0; index < controllers.Length; index++)
            {
                if (controllers[index] != null && !ReferenceEquals(controllers[index].turntable, turntable))
                {
                    result.Warnings.Add("turntable controller has stale turntable reference.");
                }
            }

            return result;
        }

        public static FusePrefabSanitizerResult ValidateSceneClonePostBind(GameObject root, string id)
        {
            var result = new FusePrefabSanitizerResult();
            if (!ValidateRoot(root, "scene clone", id, result))
            {
                return result;
            }

            ValidateSaneIdentityAndTransform(root, "scene clone", id, result);
            if (SceneCloneAPI.GetSceneClone(id) == null)
            {
                result.Warnings.Add("scene clone is not discoverable after bind.");
            }

            ValidateRendererPresence(root, "scene clone", result);
            return result;
        }

        private static bool ValidateRoot(GameObject root, string kind, string id, FusePrefabSanitizerResult result)
        {
            if (root != null)
            {
                return true;
            }

            result.Warnings.Add($"{kind} '{id}' root is null.");
            return false;
        }

        private static void ReplaceGlobalObjectIds(GameObject root, string prefix, FusePrefabSanitizerResult result)
        {
            var globals = root.GetComponentsInChildren<GlobalKeyValueObject>(true);
            for (var index = 0; index < globals.Length; index++)
            {
                var global = globals[index];
                if (global == null)
                {
                    continue;
                }

                global.globalObjectId = prefix + "." + BuildStableToken(root, global.transform, index);
            }

            if (globals.Length > 0)
            {
                result.Actions.Add($"replaced {globals.Length} global object id(s)");
            }
        }

        private static void DisableTrackMarkers(GameObject root, FusePrefabSanitizerResult result)
        {
            var markers = root.GetComponentsInChildren<TrackMarker>(true);
            for (var index = 0; index < markers.Length; index++)
            {
                if (markers[index] != null)
                {
                    markers[index].enabled = false;
                }
            }

            if (markers.Length > 0)
            {
                result.Actions.Add($"disabled {markers.Length} track marker(s)");
            }
        }

        private static void ClearCachedIdentityFields(GameObject root, FusePrefabSanitizerResult result)
        {
            var resetCount = 0;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                {
                    continue;
                }

                if (component is IndustryComponent)
                {
                    resetCount += ClearPrivateReferenceField(component, "_identifier");
                }

                resetCount += ClearPrivateReferenceField(component, "_cachedIndustry");
                resetCount += ClearPrivateReferenceField(component, "_cachedStation");
                resetCount += ClearPrivateReferenceField(component, "_cachedArea");
            }

            if (resetCount > 0)
            {
                result.Actions.Add($"cleared {resetCount} cached identity field(s)");
            }
        }

        private static int ClearPrivateReferenceField(Component component, string fieldName)
        {
            var field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType.IsValueType)
            {
                return 0;
            }

            try
            {
                field.SetValue(component, null);
                return 1;
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE prefab sanitizer could not clear field '{component.GetType().FullName}.{fieldName}': {ex.Message}");
                return 0;
            }
        }

        private static void RelinkLoaderIndustry(GameObject root, Industry industry, FusePrefabSanitizerResult result)
        {
            if (industry == null)
            {
                result.Warnings.Add("loader has no resolved industry reference.");
                return;
            }

            var targetLoader = root.GetComponentInChildren<CarLoadTargetLoader>(true);
            if (targetLoader != null)
            {
                targetLoader.sourceIndustry = industry;
                result.Actions.Add("relinked CarLoadTargetLoader.sourceIndustry");
            }
            else
            {
                result.Warnings.Add("loader is missing CarLoadTargetLoader.");
            }

            var hoverable = root.GetComponentInChildren<IndustryContentHoverable>(true);
            if (hoverable != null && IndustryHoverableIndustryField != null)
            {
                IndustryHoverableIndustryField.SetValue(hoverable, industry);
                result.Actions.Add("relinked IndustryContentHoverable.industry");
            }
            else
            {
                result.Warnings.Add("loader is missing IndustryContentHoverable or its industry field.");
            }
        }

        private static void RelinkStationAgent(
            StationAgent stationAgent,
            Area area,
            PassengerStop passengerStop,
            FusePrefabSanitizerResult result)
        {
            if (stationAgent == null)
            {
                result.Warnings.Add("station prefab is missing StationAgent.");
                return;
            }

            if (area != null && StationAreaField != null)
            {
                StationAreaField.SetValue(stationAgent, area);
                result.Actions.Add("relinked StationAgent.area");
            }
            else
            {
                result.Warnings.Add("station has no resolved Area reference.");
            }

            if (passengerStop != null && StationPassengerStopField != null)
            {
                StationPassengerStopField.SetValue(stationAgent, passengerStop);
                result.Actions.Add("relinked StationAgent.passengerStop");
            }
            else
            {
                result.Warnings.Add("station has no resolved PassengerStop reference.");
            }

            var secondaryAreas = StationSecondaryAreasField?.GetValue(stationAgent) as IList<Area>;
            if (secondaryAreas != null)
            {
                secondaryAreas.Clear();
                result.Actions.Add("cleared StationAgent.secondaryAreas");
            }
        }

        private static void RelinkTurntableController(GameObject root, Turntable turntable, FusePrefabSanitizerResult result)
        {
            if (turntable == null)
            {
                result.Warnings.Add("turntable root has no Turntable component.");
                return;
            }

            var controllers = root.GetComponentsInChildren<TurntableController>(true);
            if (controllers.Length == 0)
            {
                result.Warnings.Add("turntable visual root is missing TurntableController.");
                return;
            }

            for (var index = 0; index < controllers.Length; index++)
            {
                if (controllers[index] == null)
                {
                    continue;
                }

                controllers[index].enabled = true;
                controllers[index].turntable = turntable;
            }

            result.Actions.Add($"relinked {controllers.Length} TurntableController reference(s)");
        }

        private static void ForceLodZero(GameObject root, FusePrefabSanitizerResult result)
        {
            var lodGroups = root.GetComponentsInChildren<LODGroup>(true);
            for (var index = 0; index < lodGroups.Length; index++)
            {
                var lodGroup = lodGroups[index];
                if (lodGroup == null)
                {
                    continue;
                }

                lodGroup.enabled = true;
                lodGroup.ForceLOD(0);
                lodGroup.RecalculateBounds();
            }

            if (lodGroups.Length > 0)
            {
                result.Actions.Add($"forced LOD0 on {lodGroups.Length} LOD group(s)");
            }
        }

        private static void ValidateRequiredComponent<T>(GameObject root, string label, FusePrefabSanitizerResult result)
            where T : Component
        {
            if (root.GetComponentInChildren<T>(true) == null)
            {
                result.Warnings.Add($"{label} is missing required component {typeof(T).FullName}.");
            }
        }

        private static void ValidateRendererPresence(GameObject root, string label, FusePrefabSanitizerResult result)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                result.Warnings.Add($"{label} has no Renderer components.");
                return;
            }

            if (!renderers.Any(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff))
            {
                result.Warnings.Add($"{label} has renderers but none are currently enabled.");
            }
        }

        private static void ValidateSaneIdentityAndTransform(GameObject root, string kind, string id, FusePrefabSanitizerResult result)
        {
            if (string.IsNullOrWhiteSpace(root.name))
            {
                result.Warnings.Add($"{kind} '{id}' has a blank GameObject name.");
            }

            var position = root.transform.position;
            var scale = root.transform.lossyScale;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            {
                result.Warnings.Add($"{kind} '{id}' has a non-finite world position.");
            }

            if (!IsFinite(scale.x) || !IsFinite(scale.y) || !IsFinite(scale.z) ||
                Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
            {
                result.Warnings.Add($"{kind} '{id}' has an invalid world scale.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string BuildStableToken(GameObject root, Transform transform, int index)
        {
            var parts = new Stack<string>();
            var cursor = transform;
            while (cursor != null && cursor != root.transform)
            {
                parts.Push(SanitizeToken(cursor.name));
                cursor = cursor.parent;
            }

            var suffix = parts.Count == 0 ? "root" : string.Join(".", parts.ToArray());
            return index.ToString("D2") + "." + suffix;
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var chars = value
                .Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.')
                .ToArray();
            return chars.Length == 0 ? "unnamed" : new string(chars);
        }
    }
}
