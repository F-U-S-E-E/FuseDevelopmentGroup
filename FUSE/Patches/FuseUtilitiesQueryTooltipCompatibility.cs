using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Replaces RR Utilities 1.2.2's obsolete query-tool prefix. That prefix
    /// still calls Graph.LocationFromWorldPoint(Vector3, float), which was
    /// replaced by TryGetLocationFromWorldPoint in current Railroader builds.
    ///
    /// Alina's Utilities invokes ObjectPicker.QueryTooltipInfo while creating
    /// its toolbox. The stale RR Utilities prefix therefore prevented the
    /// entire Alina window from opening, in addition to breaking RR Utilities'
    /// own query tool. FUSE removes only the identified stale prefix and
    /// installs the equivalent implementation against the current graph API.
    /// </summary>
    internal static class FuseUtilitiesQueryTooltipCompatibility
    {
        private const string UtilitiesAssemblyName = "Utilities";
        private const string UtilitiesPatchTypeName = "Utilities.QueryToolDistancePatch";
        private const string UtilitiesPatchMethodName = "Prefix";
        private const float DefaultQueryDistanceMeters = 1500f;

        private static readonly MethodInfo FusePrefixMethod = AccessTools.DeclaredMethod(
            typeof(FuseUtilitiesQueryTooltipCompatibility),
            nameof(QueryTooltipInfoPrefix));

        private static MethodInfo _queryTooltipInfo;
        private static FieldInfo _utilitiesSettingsField;
        private static FieldInfo _distanceSettingsField;
        private static FieldInfo _queryDistanceField;
        private static bool _installed;
        private static bool _loggedRuntimeFailure;

        internal static bool Installed => _installed;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var target = _queryTooltipInfo ?? (_queryTooltipInfo = AccessTools.DeclaredMethod(
                typeof(ObjectPicker),
                "QueryTooltipInfo",
                new[] { typeof(Ray) }));
            if (target == null || FusePrefixMethod == null)
            {
                return "idle (game surface changed)";
            }

            var patchInfo = Harmony.GetPatchInfo(target);
            var stalePrefixes = patchInfo?.Prefixes
                ?.Where(patch => IsLegacyUtilitiesQueryPrefix(patch?.PatchMethod))
                .Select(patch => patch.PatchMethod)
                .Distinct()
                .ToArray() ?? Array.Empty<MethodInfo>();

            if (stalePrefixes.Length == 0)
            {
                return _installed ? "installed" : "idle (not present)";
            }

            foreach (var stalePrefix in stalePrefixes)
            {
                harmony.Unpatch(target, stalePrefix);
            }

            if (!IsFusePrefixInstalled(target))
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(FusePrefixMethod)
                    {
                        priority = Priority.First,
                    });
            }

            _installed = true;
            FuseLog.Info(
                "FUSE replaced RR Utilities' obsolete track query hook with the current " +
                "TryGetLocationFromWorldPoint API; RR Utilities query tool and Alina's " +
                "Utilities toolbox are compatible with this Railroader build.");
            return "installed";
        }

        internal static bool IsLegacyUtilitiesQueryPrefix(MethodInfo method)
        {
            return IsLegacyUtilitiesQueryPrefix(
                method?.Module?.Assembly?.GetName()?.Name,
                method?.DeclaringType?.FullName,
                method?.Name);
        }

        internal static bool IsLegacyUtilitiesQueryPrefix(
            string assemblyName,
            string declaringTypeName,
            string methodName)
        {
            return string.Equals(assemblyName, UtilitiesAssemblyName, StringComparison.Ordinal) &&
                   string.Equals(declaringTypeName, UtilitiesPatchTypeName, StringComparison.Ordinal) &&
                   string.Equals(methodName, UtilitiesPatchMethodName, StringComparison.Ordinal);
        }

        internal static float NormalizeQueryDistance(object value)
        {
            if (value == null)
            {
                return DefaultQueryDistanceMeters;
            }

            try
            {
                var converted = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return float.IsNaN(converted) || float.IsInfinity(converted) || converted <= 0f
                    ? DefaultQueryDistanceMeters
                    : converted;
            }
            catch
            {
                return DefaultQueryDistanceMeters;
            }
        }

        private static bool IsFusePrefixInstalled(MethodBase target)
        {
            return Harmony.GetPatchInfo(target)?.Prefixes?.Any(
                patch => patch?.PatchMethod == FusePrefixMethod) == true;
        }

        private static bool QueryTooltipInfoPrefix(ref TooltipInfo __result, Ray ray)
        {
            try
            {
                var mask = (1 << Layers.Terrain) | (1 << Layers.Track);
                if (!Physics.Raycast(ray, out var hit, ReadQueryDistance(), mask) ||
                    hit.collider == null ||
                    hit.collider.gameObject.layer != Layers.Track)
                {
                    __result = TooltipInfo.Empty;
                    return false;
                }

                var graph = TrainController.Shared?.graph;
                if (graph == null ||
                    !graph.TryGetLocationFromWorldPoint(hit.point, 1f, out Location location))
                {
                    __result = TooltipInfo.Empty;
                    return false;
                }

                var curvature = graph.CurvatureAtLocation(location, (Graph.CurveQueryResolution)0);
                var grade = Mathf.Abs(graph.GradeAtLocation(location));
                __result = new TooltipInfo("Track", $"{grade:F1}%, {curvature:F0} deg");
                return false;
            }
            catch (Exception ex)
            {
                __result = TooltipInfo.Empty;
                if (!_loggedRuntimeFailure)
                {
                    _loggedRuntimeFailure = true;
                    FuseLog.Exception(
                        "FUSE RR Utilities query compatibility failed while reading track tooltip data",
                        ex);
                }

                return false;
            }
        }

        private static float ReadQueryDistance()
        {
            try
            {
                if (_utilitiesSettingsField == null)
                {
                    var loaderType = AccessTools.TypeByName("Utilities.UMM.Loader");
                    _utilitiesSettingsField = loaderType == null
                        ? null
                        : AccessTools.Field(loaderType, "Settings");
                }

                var settings = _utilitiesSettingsField?.GetValue(null);
                if (settings == null)
                {
                    return DefaultQueryDistanceMeters;
                }

                if (_distanceSettingsField == null)
                {
                    _distanceSettingsField = AccessTools.Field(settings.GetType(), "distanceSettings");
                }

                var distanceSettings = _distanceSettingsField?.GetValue(settings);
                if (distanceSettings == null)
                {
                    return DefaultQueryDistanceMeters;
                }

                if (_queryDistanceField == null)
                {
                    _queryDistanceField = AccessTools.Field(distanceSettings.GetType(), "QueryDistance");
                }

                return NormalizeQueryDistance(_queryDistanceField?.GetValue(distanceSettings));
            }
            catch
            {
                return DefaultQueryDistanceMeters;
            }
        }
    }
}
