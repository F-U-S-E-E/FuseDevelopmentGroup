using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Core;
using FUSE.Infrastructure;
using HarmonyLib;
using UnityEngine;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Removes the dominant cold-load cost in FUSE.NarrowGauge 0.4.x without
    /// taking ownership of that optional companion assembly.
    ///
    /// NarrowGauge's special-work intersection prototype samples every rail at
    /// 0.2 m, then compares the resulting segments pairwise. On the E&amp;A graph
    /// that produces a repeatable 33-second main-thread analysis for 13
    /// special-work objects. A 4.0 m chord stays inside the 45 mm shared-rail
    /// tolerance even at a tight 50 m railroad radius (about 40 mm sagitta),
    /// while reducing the quadratic candidate grid by about 99.75%.
    ///
    /// The target assembly loads after FUSE, so this installer listens for that
    /// one assembly and applies a tightly checked transpiler only when the
    /// expected private method and its two inlined 0.2f constants are present.
    /// Unknown versions fail open and retain the companion's original behavior.
    /// </summary>
    internal static class FuseNarrowGaugePerformanceCompatibility
    {
        internal const float OriginalSampleSpacingMeters = 0.2f;
        internal const float OptimizedSampleSpacingMeters = 4.0f;
        internal const int ExpectedConstantReplacements = 2;

        private const string TargetAssemblyName = "NarrowGaugeMod";
        private const string TargetTypeName = "NarrowGaugeMod.RailIntersectionPrototype";
        private const string TargetMethodName = "ProjectRail";
        private const float CurveOverlapSampleSpacingMeters = 0.1f;
        private const float CurveOverlapToleranceMeters = 0.085f;

        private static readonly object Gate = new object();
        [ThreadStatic]
        private static List<LineSegment> _curveSegments;
        private static Harmony _harmony;
        private static bool _installed;
        private static bool _attempted;

        internal static bool IsInstalled => _installed;

        internal static void Initialize(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            lock (Gate)
            {
                _harmony = harmony;
                _installed = false;
                _attempted = false;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryInstall(assembly);
                if (_installed)
                {
                    break;
                }
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                _harmony = null;
                _installed = false;
                _attempted = false;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryInstall(args?.LoadedAssembly);
        }

        private static void TryInstall(Assembly assembly)
        {
            if (assembly == null ||
                !string.Equals(
                    assembly.GetName().Name,
                    TargetAssemblyName,
                    StringComparison.Ordinal))
            {
                return;
            }

            lock (Gate)
            {
                if (_installed || _attempted || _harmony == null)
                {
                    return;
                }

                var targetType = assembly.GetType(TargetTypeName, throwOnError: false);
                var targetMethod = targetType == null
                    ? null
                    : AccessTools.DeclaredMethod(targetType, TargetMethodName);
                if (targetMethod == null)
                {
                    // AssemblyLoad may run before all type metadata is available.
                    // Leave the attempt armed so the startup assembly scan can retry.
                    return;
                }

                _attempted = true;
                try
                {
                    var transpiler = AccessTools.DeclaredMethod(
                        typeof(FuseNarrowGaugePerformanceCompatibility),
                        nameof(Transpiler));
                    _harmony.Patch(
                        targetMethod,
                        transpiler: new HarmonyMethod(transpiler));
                    _installed = true;
                    TryInstallCurveOverlapAccelerator(assembly);
                    TryInstallVerboseLogFilter(assembly);
                    FuseLog.Info(
                        "FUSE installed NarrowGauge special-work load accelerator: " +
                        $"railSampleSpacingMeters={OriginalSampleSpacingMeters:0.##}->" +
                        $"{OptimizedSampleSpacingMeters:0.##}. " +
                        "Scenery and track rendering distances are unchanged.");
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        "FUSE could not install the optional NarrowGauge load accelerator; " +
                        "the companion will retain its original analysis behavior",
                        ex);
                }
            }
        }

        private static List<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var rewritten = instructions.ToList();
            var replacements = RewriteSampleSpacing(rewritten);
            if (replacements != ExpectedConstantReplacements)
            {
                FuseLog.Warning(
                    "FUSE NarrowGauge load accelerator found an unexpected " +
                    $"{replacements} sampling constant(s), expected " +
                    $"{ExpectedConstantReplacements}; review the installed companion version.");
            }

            return rewritten;
        }

        internal static int RewriteSampleSpacing(IList<CodeInstruction> instructions)
        {
            if (instructions == null)
            {
                return 0;
            }

            var replacements = 0;
            for (var index = 0; index < instructions.Count; index++)
            {
                var instruction = instructions[index];
                if (instruction.opcode != OpCodes.Ldc_R4 ||
                    !(instruction.operand is float value) ||
                    Math.Abs(value - OriginalSampleSpacingMeters) > 0.0001f)
                {
                    continue;
                }

                instruction.operand = OptimizedSampleSpacingMeters;
                replacements++;
            }

            return replacements;
        }

        private static void TryInstallCurveOverlapAccelerator(Assembly assembly)
        {
            try
            {
                var targetType = assembly.GetType(
                    "NarrowGaugeMod.SectionedSpecialWorkBuilder",
                    throwOnError: false);
                var target = targetType == null
                    ? null
                    : AccessTools.DeclaredMethod(targetType, "CurveOverlapLength");
                var parameters = target?.GetParameters();
                if (target == null ||
                    target.ReturnType != typeof(float) ||
                    parameters == null ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(LineCurve) ||
                    parameters[1].ParameterType != typeof(LineCurve))
                {
                    FuseLog.Warning(
                        "FUSE NarrowGauge curve-overlap accelerator could not find the expected method shape.");
                    return;
                }

                var prefix = AccessTools.DeclaredMethod(
                    typeof(FuseNarrowGaugePerformanceCompatibility),
                    nameof(CurveOverlapLengthPrefix));
                _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                FuseLog.Info(
                    "FUSE installed NarrowGauge allocation-free curve-overlap accelerator.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE could not install the optional NarrowGauge curve-overlap accelerator; " +
                    "the companion will retain its original overlap calculation",
                    ex);
            }
        }

        private static void TryInstallVerboseLogFilter(Assembly assembly)
        {
            try
            {
                var mainType = assembly.GetType(
                    "NarrowGaugeMod.Main",
                    throwOnError: false);
                var target = mainType == null
                    ? null
                    : AccessTools.DeclaredMethod(
                        mainType,
                        "Log",
                        new[] { typeof(string) });
                if (target == null || target.ReturnType != typeof(void))
                {
                    FuseLog.Warning(
                        "FUSE NarrowGauge verbose-log filter could not find " +
                        "the expected Main.Log(string) method.");
                    return;
                }

                var prefix = AccessTools.DeclaredMethod(
                    typeof(FuseNarrowGaugePerformanceCompatibility),
                    nameof(NarrowGaugeLogPrefix));
                _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                FuseLog.Info(
                    "FUSE installed NarrowGauge verbose-log filter; warnings, errors, " +
                    "validation results, and aggregate timing remain enabled.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE could not install the optional NarrowGauge verbose-log filter; " +
                    "the companion will retain its original logging",
                    ex);
            }
        }

        private static bool NarrowGaugeLogPrefix(string message)
        {
            return ShouldForwardNarrowGaugeInfo(message);
        }

        internal static bool ShouldForwardNarrowGaugeInfo(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return true;
            }

            return message.StartsWith(
                       "Gauge graph scan timing:",
                       StringComparison.Ordinal) ||
                   message.StartsWith(
                       "Special-work analysis:",
                       StringComparison.Ordinal) ||
                   message.StartsWith(
                       "Gauge graph validation ",
                       StringComparison.Ordinal) ||
                   message.StartsWith(
                       "Loaded as a FUSE companion module.",
                       StringComparison.Ordinal) ||
                   message.StartsWith(
                       "Enabled.",
                       StringComparison.Ordinal) ||
                   message.StartsWith(
                       "Disabled.",
                       StringComparison.Ordinal);
        }

        private static bool CurveOverlapLengthPrefix(
            LineCurve a,
            LineCurve b,
            ref float __result)
        {
            if (a == null || b == null)
            {
                return true;
            }

            try
            {
                __result = MeasureCurveOverlap(a, b);
                return false;
            }
            catch
            {
                // Preserve the companion's original behavior if an unexpected
                // LineCurve implementation cannot be materialized.
                return true;
            }
        }

        internal static float MeasureCurveOverlap(LineCurve a, LineCurve b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            // NarrowGauge's original DistancePointToCurve enumerates a.Segments
            // through LINQ Min for every 0.1m endpoint. Reuse one list per thread
            // so every pairwise overlap test does not allocate another iterator,
            // closure, and segment array.
            var segments = _curveSegments;
            if (segments == null)
            {
                segments = new List<LineSegment>(64);
                _curveSegments = segments;
            }

            segments.Clear();
            try
            {
                foreach (var item in a.Segments)
                {
                    segments.Add(item.Item2);
                }

                if (segments.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Cannot measure overlap against a curve with no segments.");
                }

                var length = b.Length;
                var overlap = 0f;
                var count = Mathf.Max(
                    2,
                    Mathf.CeilToInt(length / CurveOverlapSampleSpacingMeters) + 1);
                for (var index = 0; index + 1 < count; index++)
                {
                    var startDistance = Mathf.Min(
                        length,
                        index * CurveOverlapSampleSpacingMeters);
                    var endDistance = index == count - 2
                        ? length
                        : Mathf.Min(
                            length,
                            (index + 1) * CurveOverlapSampleSpacingMeters);
                    if (DistancePointToSegments(
                            b.LinePointAtDistance(startDistance).point,
                            segments) <= CurveOverlapToleranceMeters &&
                        DistancePointToSegments(
                            b.LinePointAtDistance(endDistance).point,
                            segments) <= CurveOverlapToleranceMeters)
                    {
                        overlap += endDistance - startDistance;
                    }
                }

                return overlap;
            }
            finally
            {
                segments.Clear();
            }
        }

        private static float DistancePointToSegments(
            Vector3 point,
            List<LineSegment> segments)
        {
            var minimum = float.MaxValue;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var start = segment.a.point;
                var end = segment.b.point;
                var delta = end - start;
                var distance = delta.sqrMagnitude <= 0.000001f
                    ? Vector3.Distance(point, start)
                    : Vector3.Distance(
                        point,
                        start + delta * Mathf.Clamp01(
                            Vector3.Dot(point - start, delta) /
                            delta.sqrMagnitude));
                minimum = Mathf.Min(minimum, distance);
            }

            return minimum;
        }
    }
}
