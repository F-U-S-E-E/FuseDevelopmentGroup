using System;
using FUSE.Infrastructure;
using HarmonyLib;
using Track;

namespace FUSE.Patches
{
    /// <summary>
    /// Captures and logs exceptions from
    /// <see cref="SwitchGeometry.Calculate(TrackNode, SegmentProxy, SegmentProxy, out SegmentProxy, out SegmentProxy, out System.Collections.Generic.List{SegmentProxy})"/>.
    /// The vanilla call site in
    /// <c>TrackObjectManager.BuildDescriptors</c> wraps this method in a
    /// <c>catch (Exception)</c> that only logs <c>"Error calculating switch
    /// &lt;nodeId&gt; geometry:"</c> with the exception object as context —
    /// the actual message and stack are not in the player log, just the
    /// "something failed here" stub. When the calc throws (commonly
    /// <c>"Switch tracks do not intersect: &lt;segA&gt; and &lt;segB&gt;"</c>
    /// from the intersection check around line 78 of
    /// <c>SwitchGeometry.Calculate</c>), the switch is silently dropped
    /// from the mesh build along with every segment touching the switch
    /// node — and from the outside the only visible symptom is "rails
    /// missing where the data says they should be." This Finalizer
    /// surfaces the actual exception message + the two segment IDs the
    /// calculation was given, so we can tell apart "tracks don't
    /// intersect within tolerance" from any other geometry failure.
    /// </summary>
    [HarmonyPatch(typeof(SwitchGeometry), nameof(SwitchGeometry.Calculate))]
    internal static class FuseSwitchGeometryDiagnostic
    {
        // Harmony Finalizer pattern: runs whether or not the method
        // threw. Returning the original exception lets it propagate
        // back to the vanilla catch, preserving game behaviour; we
        // just inject a log line on the way through.
        private static Exception Finalizer(TrackNode node, SegmentProxy a, SegmentProxy b, Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            try
            {
                var nodeId = node?.id ?? "<null>";
                var aId = a.Segment != null ? (a.Segment.id ?? a.Segment.name ?? "<unnamed>") : "<null>";
                var bId = b.Segment != null ? (b.Segment.id ?? b.Segment.name ?? "<unnamed>") : "<null>";
                FuseLog.Warning(
                    $"FUSE captured SwitchGeometry.Calculate failure node='{nodeId}' " +
                    $"segmentA='{aId}' segmentB='{bId}' " +
                    $"exception='{__exception.GetType().Name}: {__exception.Message}'.");
            }
            catch
            {
                // Never let our diagnostic itself break the vanilla
                // exception flow.
            }

            return __exception;
        }
    }
}
