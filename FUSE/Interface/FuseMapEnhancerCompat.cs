using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using Helpers;
using Model;
using Model.Ops;
using Track;
using UnityEngine;

namespace FUSE.Interface
{
    /// <summary>
    /// Re-syncs Map Enhancer's industrial-segment caches after FUSE mutates
    /// industry components.
    ///
    /// Map Enhancer (third-party mod) registers each
    /// <see cref="IndustryComponent"/>'s cached track segments into its own
    /// static <c>_industrialSegments</c> / <c>_industrialSegmentColors</c>
    /// collections from a Harmony postfix on
    /// <c>IndustryComponent.Start</c>. That postfix runs exactly once per
    /// component — when the host GameObject first becomes active during scene
    /// load — and there is no public hook for replaying it. The map's
    /// <c>ColorForSegment</c> postfix then reads those collections by segment
    /// id when painting tracks; any segment that isn't in
    /// <c>_industrialSegments</c> is rendered as plain branch or unavailable
    /// track instead of an area-colored industrial track.
    ///
    /// FUSE's per-entry merge for industry-component TrackSpans patches in
    /// mod-added spans onto pre-existing components AFTER their Start
    /// callback has already fired. The new spans' segments therefore never
    /// hit Map Enhancer's registration path, and the map only highlights the
    /// component's original (pre-merge) tracks.
    ///
    /// This helper closes the gap by reflectively reaching into Map
    /// Enhancer's static collections and adding the missing
    /// (segmentId → area-color) entries for every span/segment FUSE has
    /// attached to a given industry. It mirrors Map Enhancer's own area
    /// lookup precisely (registry walk first, then position fallback to
    /// <see cref="OpsController.ClosestAreaForGamePosition(Vector2)"/>) so
    /// the colour matches what the postfix would have written.
    ///
    /// The integration is reflection-only and gracefully no-ops when Map
    /// Enhancer is not installed — FUSE has no compile-time dependency on
    /// the mod.
    /// </summary>
    internal static class FuseMapEnhancerCompat
    {
        // Map Enhancer's MonoBehaviour type. Resolved lazily because it lives
        // in a third-party assembly that may or may not be loaded into the
        // current AppDomain.
        private static readonly Lazy<Type> MapEnhancerType = new Lazy<Type>(ResolveMapEnhancerType);

        private static readonly Lazy<FieldInfo> IndustrialSegmentsField =
            new Lazy<FieldInfo>(() => GetStaticField("_industrialSegments"));

        private static readonly Lazy<FieldInfo> IndustrialSegmentColorsField =
            new Lazy<FieldInfo>(() => GetStaticField("_industrialSegmentColors"));

        // The cached-segments list on TrackSpan and the on-demand recompute
        // method are private. We mirror Map Enhancer's reads here so we
        // observe the exact same TrackSegment[] sequence it would.
        private static readonly FieldInfo TrackSpanCachedSegmentsField =
            typeof(TrackSpan).GetField("_cachedSegments", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo TrackSpanUpdateCachedPointsMethod =
            typeof(TrackSpan).GetMethod("UpdateCachedPointsIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        // Map Enhancer special-cases this identifier (a Legos cross-traffic
        // global area that owns every industry it touches) and falls back to
        // a fixed yellow tint rather than an area tagColor. Match that here so
        // FUSE-injected segments don't drift to a different colour than the
        // postfix would have written.
        private const string LegosGlobalAreaIdentifier = "legos-global-industries";

        // Set once we've logged the result of the initial type/field probe.
        // Lets us spot install/version mismatches without spamming the log on
        // every industry refresh.
        private static bool _probeLogged;
        private static readonly object ProbeLogLock = new object();

        public static void RefreshIndustry(Industry industry, string operation)
        {
            if (industry == null)
            {
                return;
            }

            LogProbeOnce();

            if (MapEnhancerType.Value == null)
            {
                // Map Enhancer not installed; nothing to refresh.
                return;
            }

            if (IndustrialSegmentsField.Value == null || IndustrialSegmentColorsField.Value == null)
            {
                // Mod version surface drifted; we can't touch its internals safely.
                return;
            }

            // We don't gate on Map Enhancer's <c>_isMapFullyLoaded</c> flag.
            // Map Enhancer only purges its industrial-segment caches in
            // OnMapWillUnload (player leaving the world) — OnMapDidLoad does
            // NOT clear them, it merely refreshes per-segment <c>trackClass</c>
            // via ReclassifyIndustrialTracks. Adding entries here before
            // _isMapFullyLoaded flips to true is therefore safe: nothing in
            // the load flow erases them, and ColorForSegment uses the
            // populated dictionaries the moment MapBuilder repaints.

            HashSet<string> industrialSegments;
            IDictionary industrialSegmentColors;
            try
            {
                industrialSegments = IndustrialSegmentsField.Value.GetValue(null) as HashSet<string>;
                industrialSegmentColors = IndustrialSegmentColorsField.Value.GetValue(null) as IDictionary;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE Map Enhancer compat refresh skipped for industry='{industry.identifier ?? "<null>"}' " +
                    $"operation='{operation ?? "unspecified"}' message='{ex.Message}'.");
                return;
            }

            if (industrialSegments == null || industrialSegmentColors == null)
            {
                return;
            }

            var components = industry.GetComponentsInChildren<IndustryComponent>(true)
                .Where(c => c != null && !(c is ProgressionIndustryComponent))
                .ToArray();
            if (components.Length == 0)
            {
                return;
            }

            var segmentsAdded = 0;
            var componentsRefreshed = 0;
            foreach (var component in components)
            {
                if (component.gameObject == null || !component.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var area = FindAreaForComponent(component);
                var color = ResolveColorForArea(area);

                var anySegmentForComponent = false;
                foreach (var span in component.TrackSpans ?? Enumerable.Empty<TrackSpan>())
                {
                    if (span == null)
                    {
                        continue;
                    }

                    try
                    {
                        TrackSpanUpdateCachedPointsMethod?.Invoke(span, null);
                    }
                    catch
                    {
                        // Best effort: an invalid Location during cache compute
                        // throws and we just skip this span — Map Enhancer
                        // would have done the same.
                        continue;
                    }

                    if (!(TrackSpanCachedSegmentsField?.GetValue(span) is IList cachedSegments))
                    {
                        continue;
                    }

                    foreach (var raw in cachedSegments)
                    {
                        if (!(raw is TrackSegment segment) || segment == null || string.IsNullOrEmpty(segment.id))
                        {
                            continue;
                        }

                        if (industrialSegments.Add(segment.id))
                        {
                            segmentsAdded++;
                        }

                        // Always overwrite the colour — if the segment was
                        // already registered to a different area (e.g. a mod
                        // reparented the industry), we want the freshest
                        // area-derived colour.
                        industrialSegmentColors[segment.id] = color;
                        anySegmentForComponent = true;
                    }
                }

                if (anySegmentForComponent)
                {
                    componentsRefreshed++;
                }
            }

            FuseLog.Info(
                $"FUSE refreshed Map Enhancer industrial segments for industry='{industry.identifier ?? "<null>"}' " +
                $"operation='{operation ?? "unspecified"}' componentsRefreshed={componentsRefreshed} " +
                $"segmentsAdded={segmentsAdded} totalSegmentsAfter={industrialSegments.Count} " +
                $"(segmentsAdded>0 means post-Start TrackSpan merges had previously left these segments out of " +
                $"Map Enhancer's industrial-segment cache; map will paint them with the area's tagColor on next " +
                $"MapBuilder rebuild.).");
        }

        private static void LogProbeOnce()
        {
            if (_probeLogged)
            {
                return;
            }

            lock (ProbeLogLock)
            {
                if (_probeLogged)
                {
                    return;
                }

                _probeLogged = true;
                var type = MapEnhancerType.Value;
                if (type == null)
                {
                    FuseLog.Info(
                        "FUSE Map Enhancer compat probe: no MapEnhancer.MapEnhancer type found in loaded assemblies. " +
                        "If Map Enhancer is installed, industrial-segment colours for FUSE-merged TrackSpans will not be " +
                        "refreshed; if it is not installed, this message is informational only.");
                    return;
                }

                FuseLog.Info(
                    $"FUSE Map Enhancer compat probe: type='{type.AssemblyQualifiedName}' " +
                    $"industrialSegmentsField={(IndustrialSegmentsField.Value != null)} " +
                    $"industrialSegmentColorsField={(IndustrialSegmentColorsField.Value != null)}.");
            }
        }

        public static void RefreshAllIndustries(string operation)
        {
            if (MapEnhancerType.Value == null)
            {
                return;
            }

            foreach (var industry in UnityEngine.Object.FindObjectsOfType<Industry>(true))
            {
                RefreshIndustry(industry, operation);
            }
        }

        private static Color ResolveColorForArea(Area area)
        {
            if (area == null)
            {
                return Color.yellow;
            }

            if (string.Equals(area.identifier, LegosGlobalAreaIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return Color.yellow;
            }

            return area.tagColor == default(Color) ? Color.yellow : area.tagColor;
        }

        private static Area FindAreaForComponent(IndustryComponent component)
        {
            var ops = OpsController.Shared;
            if (ops == null)
            {
                return null;
            }

            var areas = ops.Areas;
            if (areas != null)
            {
                foreach (var area in areas)
                {
                    if (area?.Industries == null)
                    {
                        continue;
                    }

                    foreach (var industry in area.Industries)
                    {
                        var registered = industry?.Components;
                        if (registered == null)
                        {
                            continue;
                        }

                        foreach (var registeredComponent in registered)
                        {
                            if (ReferenceEquals(registeredComponent, component))
                            {
                                return area;
                            }
                        }
                    }
                }
            }

            try
            {
                var gamePos = WorldTransformer.WorldToGame(component.transform.position);
                return ops.ClosestAreaForGamePosition(new Vector2(gamePos.x, gamePos.z));
            }
            catch
            {
                return null;
            }
        }

        private static FieldInfo GetStaticField(string name)
        {
            var type = MapEnhancerType.Value;
            return type?.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        private static Type ResolveMapEnhancerType()
        {
            // The Map Enhancer assembly publishes a singleton MonoBehaviour
            // whose CLR type lives under namespace "MapEnhancer" and whose
            // type name is also "MapEnhancer". We scan the loaded
            // AppDomain rather than referencing the assembly directly so
            // FUSE has no compile-time dependency on the mod's binary.
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var type in types)
                    {
                        if (type == null)
                        {
                            continue;
                        }

                        if (type.FullName == "MapEnhancer.MapEnhancer")
                        {
                            return type;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE Map Enhancer compat type lookup failed", ex);
            }

            return null;
        }
    }
}
