using System;
using BezierCurveMesh;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Keeps one broken curve-mesh culling handler from silently killing scenery
    /// streaming for everything else.
    ///
    /// Unity's <c>CullingGroup.SendEvents</c> delivers a whole BATCH of culling state
    /// changes in one native→managed call. <c>CurveMeshBuilderBase.CullingSphereStateChanged</c>
    /// regenerates its mesh on those events, and a <c>TrackCurveMeshBuilder</c> whose
    /// <c>TrackMarker</c> location points at track that no longer exists (e.g. removed by a
    /// pack's track customizations) throws a <c>NullReferenceException</c> mid-batch — which
    /// aborts <c>SendEvents</c> and DROPS every remaining event in the batch. The visible
    /// result is brutal and looks completely unrelated: after a teleport, most scenery
    /// culling spheres never receive their re-load/re-show events, so buildings simply never
    /// stream back in (while their decoupled terrain masks stay applied — flattened ground
    /// with nothing on it).
    ///
    /// <see cref="FUSE.Runtime.API.TrackAPI.DisableInvalidTrackMarkers"/> disables the known
    /// breakage source (builders attached to invalid track markers) at apply time; this
    /// finalizer is the catch-all so that ANY curve-mesh handler failure — known or novel —
    /// costs one curve mesh instead of the whole culling batch. The exception is logged
    /// (throttled) and suppressed; the builder itself is left enabled so a transient failure
    /// (e.g. mid-rebuild) can recover on its next event.
    /// </summary>
    [HarmonyPatch(typeof(CurveMeshBuilderBase), "CullingSphereStateChanged")]
    internal static class FuseCurveMeshCullingGuardPatch
    {
        /// <summary>Exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.CurveMeshSuppressed;

        private static Exception Finalizer(Exception __exception, CurveMeshBuilderBase __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            var suppressed = FuseRuntimeGuardCounters.RecordCurveMeshSuppressed();
            // First few individually (enough to identify the offender), then heartbeat only —
            // a permanently-broken builder re-throws on every culling event it receives.
            if (suppressed <= 5 || suppressed % 100 == 0)
            {
                var name = "<destroyed>";
                try
                {
                    if (__instance != null)
                    {
                        var parent = __instance.transform.parent;
                        name = parent != null ? parent.name + "/" + __instance.name : __instance.name;
                    }
                }
                catch
                {
                    // Naming is best-effort; never let diagnostics re-throw inside the batch.
                }

                FuseLog.Warning(
                    $"FUSE suppressed curve-mesh culling exception #{suppressed} on '{name}': " +
                    $"{__exception.GetBaseException().Message}. Without this guard the exception would " +
                    "abort the whole CullingGroup event batch and stop other scenery from streaming.");
            }

            return null;
        }
    }
}
