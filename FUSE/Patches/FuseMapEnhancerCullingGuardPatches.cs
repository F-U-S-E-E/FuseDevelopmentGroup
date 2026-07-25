using System;
using System.Collections;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;
using Helpers;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Keeps Map Enhancer's junction-marker culling functional when its
    /// marker rebuild hits a broken switch descriptor.
    ///
    /// Observed field failure chain: one throwing descriptor aborts the
    /// marker rebuild BEFORE the culling-sphere array is allocated, so the
    /// per-world-move sphere refresh then dereferences a null array on every
    /// floating-origin shift for the rest of the session (14/14 world moves
    /// in the captured logs), and the same rebuild throw also unwinds
    /// through the game's deferred track-rebuild coroutine. The refresh
    /// additionally carries a latent count-drift hazard independent of any
    /// other mod: it loops to the LIVE switch-dictionary count while
    /// indexing marker/sphere collections sized at rebuild time, so a
    /// partial track-graph edit between rebuilds can push the index past
    /// either collection.
    ///
    /// Two layers, installed manually by <see cref="FuseThirdPartyGuardInstaller"/>:
    /// a replacement prefix on the sphere refresh (null state no-ops instead
    /// of throwing; the loop is clamped to the collections it actually
    /// indexes, fixing the count drift), and a storm-breaker finalizer on
    /// the marker rebuild (one bad descriptor truncates the marker set
    /// instead of killing the whole rebuild and its callers). With the
    /// switch-geometry rail backfill (<see cref="FuseSwitchGeometryRailBackfillPatch"/>)
    /// healing descriptors at the source, both layers should sit idle; they
    /// exist for version skew and for the drift bug the backfill cannot
    /// reach.
    ///
    /// Deliberately NOT a [HarmonyPatch] class: the target is a third-party
    /// mod resolved by name at runtime (resolve-or-idle, same contract as
    /// the optional-type handling in the scenery decal scrub), so the
    /// attribute-driven apply pass and the patch-targeting smoke test must
    /// never try to resolve it on machines where the mod is legitimately
    /// absent. All member binding is fail-open: if the mod's internal
    /// layout differs from the reverse-engineered runtime shape this guard
    /// understands, the replacement prefix stays uninstalled and vanilla
    /// behavior is untouched (the storm-breaker finalizer needs no field
    /// access and installs whenever the method resolves).
    /// </summary>
    internal static class FuseMapEnhancerCullingGuardPatches
    {
        private static bool _prefixInstalled;
        private static bool _finalizerInstalled;

        // Bound once at install time; the prefix is only installed when every
        // binding below succeeded, so the hot path never null-checks them.
        private static AccessTools.FieldRef<object, IList> _junctionMarkersRef;
        private static AccessTools.FieldRef<object, BoundingSphere[]> _cullingSpheresRef;
        // The per-marker descriptor is a non-public struct on the game side, so
        // these two hops stay classic reflection; the boxed cost only runs on
        // world-origin shifts and rebuilds, which are rare by construction.
        private static FieldInfo _entryDescriptorField;
        private static FieldInfo _descriptorGeometryField;

        /// <summary>Guard interventions since startup (diagnostics).</summary>
        internal static long GuardedEvents => FuseRuntimeGuardCounters.MapEnhancerCullingGuarded;

        internal static bool Installed => _prefixInstalled || _finalizerInstalled;

        /// <summary>
        /// Idempotent. Re-resolves on every call while the target mod is
        /// absent (it may load after FUSE), latches once anything installed.
        /// Returns a short status token for the installer's summary line.
        /// </summary>
        internal static string EnsureInstalled(Harmony harmony)
        {
            if (_prefixInstalled && _finalizerInstalled)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var mapEnhancerType = AccessTools.TypeByName("MapEnhancer.MapEnhancer");
            if (mapEnhancerType == null)
            {
                return "idle (not present)";
            }

            var updateCullingSpheres = AccessTools.Method(mapEnhancerType, "UpdateCullingSpheres");
            var createSwitches = AccessTools.Method(mapEnhancerType, "CreateSwitches");
            if (updateCullingSpheres == null && createSwitches == null)
            {
                // A future release that reshaped the surface gets no patch at
                // all rather than a guard aimed at methods that no longer exist.
                return "idle (surface changed)";
            }

            // Storm breaker first: it needs no field binding, so it protects
            // the rebuild even when the layout drifted under the prefix.
            if (!_finalizerInstalled && createSwitches != null)
            {
                harmony.Patch(
                    createSwitches,
                    finalizer: new HarmonyMethod(
                        typeof(FuseMapEnhancerCullingGuardPatches),
                        nameof(CreateSwitchesFinalizer)));
                _finalizerInstalled = true;
            }

            if (!_prefixInstalled && updateCullingSpheres != null && TryBindFields(mapEnhancerType))
            {
                harmony.Patch(
                    updateCullingSpheres,
                    prefix: new HarmonyMethod(
                        typeof(FuseMapEnhancerCullingGuardPatches),
                        nameof(UpdateCullingSpheresPrefix)));
                _prefixInstalled = true;
            }

            if (_prefixInstalled && _finalizerInstalled)
            {
                return "installed";
            }

            if (_finalizerInstalled)
            {
                return "installed (finalizer only; refresh layout unrecognized)";
            }

            return _prefixInstalled
                ? "installed (prefix only; rebuild method unresolved)"
                : "idle (binding failed)";
        }

        private static bool TryBindFields(Type mapEnhancerType)
        {
            try
            {
                var entryType = AccessTools.Inner(mapEnhancerType, "Entry");
                var descriptorField = entryType != null
                    ? AccessTools.Field(entryType, "SwitchDescriptor")
                    : null;
                var geometryField = descriptorField != null
                    ? AccessTools.Field(descriptorField.FieldType, "geometry")
                    : null;
                if (geometryField == null || geometryField.FieldType != typeof(SwitchGeometry))
                {
                    return false;
                }

                _junctionMarkersRef = AccessTools.FieldRefAccess<IList>(mapEnhancerType, "junctionMarkers");
                _cullingSpheresRef = AccessTools.FieldRefAccess<BoundingSphere[]>(mapEnhancerType, "cullingSpheres");
                _entryDescriptorField = descriptorField;
                _descriptorGeometryField = geometryField;
                return _junctionMarkersRef != null && _cullingSpheresRef != null;
            }
            catch (Exception ex)
            {
                // Fail open: without bindings the prefix never installs and the
                // refresh keeps its vanilla behavior.
                FuseLog.Exception(
                    "FUSE Map Enhancer culling guard could not bind the marker/sphere state; " +
                    "the refresh replacement is unavailable",
                    ex);
                return false;
            }
        }

        /// <summary>
        /// Replacement for the sphere refresh. Vanilla loops to the live
        /// switch-dictionary count and would (a) throw on the null sphere
        /// array a failed rebuild leaves behind, forever, once per world
        /// move, and (b) overrun the rebuild-time collections after a
        /// partial track-graph edit. The replacement no-ops on null state
        /// and clamps to the collections it indexes; the healthy path with
        /// matching counts is behavior-identical to a successful vanilla
        /// run, so it records nothing.
        /// </summary>
        private static bool UpdateCullingSpheresPrefix(object __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                var markers = _junctionMarkersRef(__instance);
                var spheres = _cullingSpheresRef(__instance);
                if (markers == null || spheres == null)
                {
                    var guarded = FuseRuntimeGuardCounters.RecordMapEnhancerCullingGuarded();
                    if (FuseGuardLog.ShouldLog(guarded))
                    {
                        FuseLog.Warning(
                            $"FUSE skipped Map Enhancer culling-sphere refresh #{guarded}: its marker " +
                            "state is uninitialized (a failed junction-marker rebuild leaves the sphere " +
                            "array null, and vanilla would throw here on every world-origin shift). " +
                            "Markers resume after the next successful rebuild.");
                    }

                    return false;
                }

                var limit = Math.Min(markers.Count, spheres.Length);
                for (var i = 0; i < limit; i++)
                {
                    var entry = markers[i];
                    var descriptor = entry != null ? _entryDescriptorField.GetValue(entry) : null;
                    if (descriptor == null)
                    {
                        continue;
                    }

                    var geometry = (SwitchGeometry)_descriptorGeometryField.GetValue(descriptor);
                    spheres[i] = new BoundingSphere(WorldTransformer.GameToWorld(geometry.switchHome), 1f);
                }

                if (markers.Count != spheres.Length)
                {
                    // Count drift between the rebuild-time collections: vanilla's
                    // live-dictionary loop bound would have overrun one of them.
                    var guarded = FuseRuntimeGuardCounters.RecordMapEnhancerCullingGuarded();
                    if (FuseGuardLog.ShouldLog(guarded))
                    {
                        FuseLog.Warning(
                            $"FUSE clamped Map Enhancer culling-sphere refresh #{guarded} to " +
                            $"{limit} entries (markers={markers.Count} spheres={spheres.Length}); " +
                            "the counts re-align on its next full rebuild.");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                // Fail open: let vanilla run rather than leave a half-written
                // sphere set behind a suppressed replacement failure.
                FuseLog.Exception(
                    "FUSE Map Enhancer culling-sphere replacement failed; letting the original run",
                    ex);
                return true;
            }
        }

        /// <summary>
        /// Storm breaker on the junction-marker rebuild: suppressing lets the
        /// caller's rebuild continue to allocate culling state for the
        /// markers that were created before the bad descriptor, instead of
        /// one throw producing zero markers, a null sphere array, and an
        /// unwind through whatever game code invoked the rebuild.
        /// </summary>
        private static Exception CreateSwitchesFinalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            var guarded = FuseRuntimeGuardCounters.RecordMapEnhancerCullingGuarded();
            if (FuseGuardLog.ShouldLog(guarded))
            {
                FuseLog.Exception(
                    $"FUSE contained Map Enhancer junction-marker rebuild exception #{guarded}; the " +
                    "marker set may be truncated at the failing switch descriptor, but culling state " +
                    "still gets allocated and the callers above the rebuild keep running. A switch " +
                    "descriptor with unset rail curves is the known trigger (see the switch-geometry " +
                    "rail backfill guard)",
                    __exception);
            }

            return null;
        }
    }
}
