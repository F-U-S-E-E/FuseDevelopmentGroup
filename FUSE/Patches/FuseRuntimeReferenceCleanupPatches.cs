using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.API;
using FUSE.Infrastructure;
using HarmonyLib;
using Model.Ops;
using Track;
using Track.Signals;
using UnityEngine;
using static FUSE.Patches.FuseRuntimeReferenceCleanup;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(Industry), "Tick")]
    internal static class FuseIndustryTickCacheScrubPatch
    {
        private static void Prefix(Industry __instance)
        {
            try
            {
                IndustryAPI.ScrubIndustryComponentCache(__instance, "Industry.Tick prefix");
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE industry tick cache scrub failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CTCAutoSignal), "CalculateAspect")]
    internal static class FuseCtcAutoSignalReferenceScrubPatch
    {
        private static void Prefix(CTCAutoSignal __instance)
        {
            try
            {
                if (__instance?.blocks == null || __instance.blocks.Count == 0)
                {
                    return;
                }

                var removed = __instance.blocks.RemoveAll(block => !IsLive(block));
                if (removed > 0)
                {
                    FuseLog.Warning(
                        $"FUSE scrubbed stale CTC auto-signal block reference(s) " +
                        $"signal='{SafeName(__instance)}' removed={removed} reason='CTCAutoSignal.CalculateAspect prefix'.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE CTC auto-signal reference scrub failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class FuseCtcAutoSignalNextBlockReferenceScrubPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CTCAutoSignal), "AspectForBlockAndNextSignal");
        }

        private static bool Prefix(
            CTCAutoSignal __instance,
            ref IReadOnlyCollection<CTCBlock> nextBlocks,
            ref CTCSignal nextSignal,
            bool lined,
            ref SemaphoreHeadController.Aspect __result)
        {
            try
            {
                if (!lined)
                {
                    return true;
                }

                if (!IsLive(nextSignal))
                {
                    nextSignal = null;
                }

                if (nextBlocks == null || nextBlocks.Count == 0)
                {
                    return true;
                }

                List<CTCBlock> liveBlocks = null;
                var removed = 0;
                foreach (var block in nextBlocks)
                {
                    if (IsLive(block))
                    {
                        liveBlocks?.Add(block);
                        continue;
                    }

                    removed++;
                    if (liveBlocks == null)
                    {
                        liveBlocks = new List<CTCBlock>(nextBlocks.Count);
                        foreach (var existing in nextBlocks)
                        {
                            if (ReferenceEquals(existing, block))
                            {
                                break;
                            }

                            liveBlocks.Add(existing);
                        }
                    }
                }

                if (removed > 0)
                {
                    nextBlocks = liveBlocks != null
                        ? (IReadOnlyCollection<CTCBlock>)liveBlocks
                        : Array.Empty<CTCBlock>();
                    FuseLog.Warning(
                        $"FUSE scrubbed stale CTC auto-signal next-block reference(s) " +
                        $"signal='{SafeName(__instance)}' removed={removed} reason='CTCAutoSignal.AspectForBlockAndNextSignal prefix'.");
                }

                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE CTC auto-signal next-block reference scrub failed closed " +
                    $"signal='{SafeName(__instance)}': {ex.Message}");
                __result = SemaphoreHeadController.Aspect.Red;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(CTCPredicateSignal), "CalculateAspect")]
    internal static class FuseCtcPredicateSignalReferenceScrubPatch
    {
        private static void Prefix(CTCPredicateSignal __instance)
        {
            try
            {
                if (__instance?.heads == null || __instance.heads.Count == 0)
                {
                    return;
                }

                var removed = 0;
                foreach (var head in __instance.heads)
                {
                    if (head?.predicates == null)
                    {
                        continue;
                    }

                    foreach (var predicate in head.predicates)
                    {
                        if (predicate?.blocks == null || predicate.blocks.Count == 0)
                        {
                            continue;
                        }

                        removed += predicate.blocks.RemoveAll(block => !IsLive(block));
                    }
                }

                if (removed > 0)
                {
                    FuseLog.Warning(
                        $"FUSE scrubbed stale CTC predicate-signal block reference(s) " +
                        $"signal='{SafeName(__instance)}' removed={removed} reason='CTCPredicateSignal.CalculateAspect prefix'.");
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE CTC predicate-signal reference scrub failed: {ex.Message}");
            }
        }
    }

    internal static class FuseRuntimeReferenceCleanup
    {
        public static bool IsLive(Component component)
        {
            if (component == null)
            {
                return false;
            }

            try
            {
                return component.gameObject != null;
            }
            catch
            {
                return false;
            }
        }

        public static string SafeName(Component component)
        {
            if (component == null)
            {
                return string.Empty;
            }

            try
            {
                return component.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
