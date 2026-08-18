using System;
using System.Globalization;
using System.Reflection;
using FUSE.Infrastructure;
using Game;
using HarmonyLib;
using Model;
using RollingStock;
using RollingStock.Controls;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// RR Utilities 1.2.2 still references Helpers.CullingManager from an
    /// older Railroader build. The current game moved that type to
    /// Helpers.Culling.CullingManager, so JIT-compiling its map-load distance
    /// callback throws TypeLoadException before any settings can be applied.
    /// Replays the small settings update against current game types and lets
    /// the otherwise-compatible utility mod continue loading.
    /// </summary>
    internal static class FuseUtilitiesMapLoadCompatibility
    {
        private const string UtilitiesAssemblyName = "Utilities";
        private const string UtilitiesTypeName = "Utilities.UtilitiesMod";
        private const string MapLoadMethodName = "OnMapDidLoad";

        private static FieldInfo _mapStateField;
        private static FieldInfo _settingsField;
        private static MethodInfo _graphicsSettingsMethod;
        private static bool _installed;
        private static bool _loggedGraphicsFailure;

        internal static bool Installed => _installed;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_installed)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var utilitiesType = AccessTools.TypeByName(UtilitiesTypeName);
            if (utilitiesType == null ||
                !string.Equals(
                    utilitiesType.Assembly.GetName().Name,
                    UtilitiesAssemblyName,
                    StringComparison.Ordinal))
            {
                return "idle (not present)";
            }

            var target = AccessTools.DeclaredMethod(utilitiesType, MapLoadMethodName);
            _mapStateField = AccessTools.Field(utilitiesType, "<MapState>k__BackingField");
            _settingsField = AccessTools.Field(utilitiesType, "Settings");
            _graphicsSettingsMethod = AccessTools.DeclaredMethod(
                utilitiesType,
                "OnGraphicsSettingsChanged",
                Type.EmptyTypes);
            if (target == null || _mapStateField == null || _settingsField == null)
            {
                return "idle (surface changed)";
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(FuseUtilitiesMapLoadCompatibility),
                    nameof(OnMapDidLoadPrefix)));
            _installed = true;
            FuseLog.Info(
                "FUSE replaced RR Utilities' obsolete map-load distance callback " +
                "with the current Railroader culling API.");
            return "installed";
        }

        internal static bool IsLegacyUtilitiesMapLoadHandler(
            string assemblyName,
            string declaringTypeName,
            string methodName)
        {
            return string.Equals(assemblyName, UtilitiesAssemblyName, StringComparison.Ordinal) &&
                   string.Equals(declaringTypeName, UtilitiesTypeName, StringComparison.Ordinal) &&
                   string.Equals(methodName, MapLoadMethodName, StringComparison.Ordinal);
        }

        internal static bool ShouldApplyMapSettings(int mapState)
        {
            const int MapLoaded = 1;
            return mapState != MapLoaded;
        }

        internal static float NormalizeRadius(object value, float fallback)
        {
            try
            {
                var converted = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return float.IsNaN(converted) || float.IsInfinity(converted) || converted <= 0f
                    ? fallback
                    : converted;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool OnMapDidLoadPrefix(object __instance)
        {
            const int MapLoaded = 1;
            try
            {
                var currentState = Convert.ToInt32(
                    _mapStateField.GetValue(null),
                    CultureInfo.InvariantCulture);
                if (!ShouldApplyMapSettings(currentState))
                {
                    return false;
                }

                _mapStateField.SetValue(
                    null,
                    Enum.ToObject(_mapStateField.FieldType, MapLoaded));
                ApplyDistanceSettings(__instance);
                ApplyGraphicsSettings(__instance);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE RR Utilities map-load compatibility kept the mod active, " +
                    $"but one settings update was skipped: {Unwrap(ex).Message}");
            }

            return false;
        }

        private static void ApplyDistanceSettings(object instance)
        {
            var settings = _settingsField.GetValue(instance);
            var distanceSettings = ReadMember(settings, "distanceSettings");
            if (distanceSettings == null)
            {
                return;
            }

            ResizeCapsules<SwitchStand>(
                ReadRadius(distanceSettings, "SwitchStandRadius", 0.17f),
                radius => 1.94f + (radius - 0.17f) * 2f);
            ResizeCapsules<FlarePickable>(
                ReadRadius(distanceSettings, "FlareRadius", 0.09f),
                radius => 0.4f + (radius - 0.09f) * 2f);
            ResizeCapsules<CouplerPickable>(
                ReadRadius(distanceSettings, "CouplerRadius", 0.21f),
                radius => 0.53f + (radius - 0.21f) * 2f);
            ResizeCapsules<GladhandClickable>(
                ReadRadius(distanceSettings, "GladhandsRadius", 0.21f),
                radius => 0.53f + (radius - 0.21f) * 2f);

            var stationRadius = ReadRadius(distanceSettings, "StationRadius", 2.81f);
            foreach (var station in Resources.FindObjectsOfTypeAll<StationAgent>())
            {
                var collider = station?.GetComponentInChildren<BoxCollider>(true);
                if (collider == null)
                {
                    continue;
                }

                var adjustment = stationRadius - 2.81f;
                collider.size = new Vector3(
                    1f + adjustment,
                    collider.size.y,
                    2.81f + adjustment);
            }

            foreach (var toggle in Resources.FindObjectsOfTypeAll<KeyValuePickableToggle>())
            {
                var collider = toggle?.GetComponentInChildren<CapsuleCollider>(true);
                if (collider == null)
                {
                    continue;
                }

                float radius;
                float baseHeight;
                float baseRadius;
                switch (toggle.displayTitle)
                {
                    case "Coal Chute":
                        radius = ReadRadius(distanceSettings, "CoalRadius", 0.3f);
                        baseHeight = 4.13f;
                        baseRadius = 0.3f;
                        break;
                    case "Water Spout":
                    case "Water Column":
                        radius = ReadRadius(distanceSettings, "WaterRadius", 0.3f);
                        baseHeight = 4.13f;
                        baseRadius = 0.3f;
                        break;
                    case "Diesel Fueling Stand":
                        radius = ReadRadius(distanceSettings, "DieselRadius", 0.1f);
                        baseHeight = 3f;
                        baseRadius = 0.1f;
                        break;
                    default:
                        continue;
                }

                collider.radius = radius;
                collider.height = baseHeight + (radius - baseRadius) * 2f;
            }

            ApplyHoseRenderDistance(
                ReadRadius(distanceSettings, "HoseRenderDistance", 100f));
        }

        private static void ResizeCapsules<T>(float radius, Func<float, float> height)
            where T : Component
        {
            foreach (var component in Resources.FindObjectsOfTypeAll<T>())
            {
                var collider = component?.GetComponentInChildren<CapsuleCollider>(true);
                if (collider == null)
                {
                    continue;
                }

                collider.radius = radius;
                collider.height = height(radius);
            }
        }

        private static void ApplyHoseRenderDistance(float distance)
        {
            var currentType = AccessTools.TypeByName("Helpers.Culling.CullingManager");
            var hose = currentType == null
                ? null
                : AccessTools.Property(currentType, "Hose")?.GetValue(null, null);
            if (hose == null)
            {
                return;
            }

            var distancesField = AccessTools.Field(currentType, "_distances");
            var distances = distancesField?.GetValue(hose) as float[];
            if (distances == null || distances.Length == 0)
            {
                return;
            }

            var configure = AccessTools.DeclaredMethod(
                    currentType,
                    "Configure",
                    new[] { typeof(string), typeof(float[]) });
            if (configure == null)
            {
                return;
            }

            var updated = (float[])distances.Clone();
            updated[0] = distance;
            var managerName = AccessTools.Field(currentType, "_managerName")?.GetValue(hose) as string;
            configure.Invoke(hose, new object[] { managerName ?? "Hose", updated });
        }

        private static void ApplyGraphicsSettings(object instance)
        {
            if (_graphicsSettingsMethod == null)
            {
                return;
            }

            try
            {
                _graphicsSettingsMethod.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                if (_loggedGraphicsFailure)
                {
                    return;
                }

                _loggedGraphicsFailure = true;
                FuseLog.Warning(
                    "FUSE kept RR Utilities active, but its optional graphics settings " +
                    $"could not be applied on this game build: {Unwrap(ex).Message}");
            }
        }

        private static float ReadRadius(object settings, string memberName, float fallback)
        {
            return NormalizeRadius(ReadMember(settings, memberName), fallback);
        }

        private static object ReadMember(object instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            var type = instance.GetType();
            var field = AccessTools.Field(type, memberName);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            return AccessTools.Property(type, memberName)?.GetValue(instance, null);
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
