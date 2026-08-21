using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FUSE.Loading;
using FUSE.Infrastructure;
using Game.Messages;
using Game.Progression;
using Game.Scripting.Interactive;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using UI.Common;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Moves the stock East Whittier Company starter equipment into an
    /// interactive placement queue when Appalachian Railway's Whittier start
    /// package is active. This lets the player choose live track after the
    /// progression and tutorial have settled instead of depending on one fixed
    /// scene marker.
    /// </summary>
    [HarmonyPatch(typeof(CompanyModeSetup), nameof(CompanyModeSetup.Setup))]
    internal static class FuseCompanyStarterPlacementPatch
    {
        private const string AppalachianWhittierStartId =
            "KingG.Appalachian-Railway.start-whit";
        private static readonly Queue<SetupDescriptor.CarPlacement>
            PendingPlacements =
                new Queue<SetupDescriptor.CarPlacement>();
        private static bool _presenting;

        [HarmonyPostfix]
        private static void SetupPostfix(
            SetupDescriptor setupDescriptor,
            ref IEnumerator __result)
        {
            var shouldQueue = __result != null
                              && setupDescriptor != null
                              && ShouldQueueStarterSetup(
                                  setupDescriptor.identifier,
                                  FuseModLoader.GetLoadedMods());
            if (TryPrepareStarterQueue(
                    PendingPlacements,
                    shouldQueue,
                    _presenting))
            {
                __result = RepairAfterInitialDelay(__result, setupDescriptor);
            }
        }

        internal static bool TryPrepareStarterQueue<T>(
            Queue<T> placementQueue,
            bool shouldQueue,
            bool presentationActive)
        {
            if (!shouldQueue || presentationActive)
            {
                return false;
            }

            placementQueue?.Clear();
            return true;
        }

        private static IEnumerator RepairAfterInitialDelay(
            IEnumerator original,
            SetupDescriptor setupDescriptor)
        {
            var repairPending = true;
            while (original.MoveNext())
            {
                yield return original.Current;
                if (!repairPending)
                {
                    continue;
                }

                repairPending = false;
                QueueStarterPlacements(
                    setupDescriptor,
                    PendingPlacements);
            }

            if (PendingPlacements.Count > 0)
            {
                yield return null;
                while (InteractiveBookWindow.Shared != null
                       && InteractiveBookWindow.Shared.IsShown)
                {
                    yield return null;
                }
                PresentNextPlacement();
            }
        }

        internal static bool ShouldQueueStarterSetup(
            string setupIdentifier,
            IEnumerable<string> loadedDefinitionIds)
        {
            return !string.IsNullOrWhiteSpace(setupIdentifier)
                   && setupIdentifier.StartsWith(
                       "ewh-",
                       StringComparison.OrdinalIgnoreCase)
                   && (loadedDefinitionIds ?? Array.Empty<string>()).Any(id =>
                       string.Equals(
                           id,
                           AppalachianWhittierStartId,
                           StringComparison.OrdinalIgnoreCase));
        }

        internal static int QueueStarterPlacements(
            SetupDescriptor setupDescriptor,
            Queue<SetupDescriptor.CarPlacement> placementQueue)
        {
            var source = setupDescriptor?.placements ??
                         Array.Empty<SetupDescriptor.CarPlacement>();
            if (source.Length == 0)
            {
                return 0;
            }

            var queued = 0;
            foreach (var placement in source)
            {
                placementQueue?.Enqueue(placement);
                queued++;
                FuseLog.Info(
                    $"FUSE queued Company starter placement " +
                    $"setup='{setupDescriptor.identifier ?? string.Empty}' cars={placement?.carIdentifier?.Length ?? 0}. " +
                    "The local player will choose its track location.");
            }

            // Every placement moves to the interactive queue, so the setup keeps none.
            setupDescriptor.placements = Array.Empty<SetupDescriptor.CarPlacement>();
            return queued;
        }

        private static void PresentNextPlacement()
        {
            if (_presenting || PendingPlacements.Count == 0)
            {
                return;
            }

            var placement = PendingPlacements.Peek();
            var preparationSucceeded = TryBuildDescriptors(
                placement,
                out var descriptors);
            var consumedEmpty = TryConsumeConfirmedEmpty(
                PendingPlacements,
                preparationSucceeded,
                descriptors.Count);
            if (!preparationSucceeded)
            {
                Toast.Present(
                    "Starter equipment retained. Run /fuse.starters when "
                    + "placement is available.",
                    ToastPosition.Middle);
                return;
            }
            if (consumedEmpty)
            {
                PresentNextPlacement();
                return;
            }

            var placer = ConsistPlacer.Instance();
            if (placer == null)
            {
                FuseLog.Warning(
                    $"FUSE could not present {PendingPlacements.Count} queued Company starter cut(s): " +
                    "the game's consist placer is unavailable.");
                return;
            }

            _presenting = true;
            var trainController = TrainController.Shared;
            if (trainController != null)
            {
                // The current game build reports `placed=true` even when its
                // caught PlaceTrain call failed. Clearing and checking this
                // public result prevents that false callback from consuming
                // the retained starter cut.
                trainController.LastPlacedTrain = null;
            }
            try
            {
                placer.Present(
                    descriptors,
                    null,
                    placed =>
                    {
                        _presenting = false;
                        var placedCount = TrainController.Shared?
                            .LastPlacedTrain?.Count ?? 0;
                        if (!WasPlacementCommitted(
                                placed,
                                descriptors.Count,
                                placedCount))
                        {
                            FuseLog.Info(
                                "FUSE retained "
                                + PendingPlacements.Count
                                + " Appalachian Railway starter cut(s) after "
                                + (placed
                                    ? "the game did not confirm every car was created."
                                    : "placement was cancelled."));
                            Toast.Present(
                                "Starter equipment retained. Run /fuse.starters "
                                + "when you are ready to place it.",
                                ToastPosition.Middle);
                            return;
                        }

                        if (PendingPlacements.Count > 0
                            && ReferenceEquals(
                                PendingPlacements.Peek(),
                                placement))
                        {
                            PendingPlacements.Dequeue();
                        }
                        placer.StartCoroutine(PresentAfterFrame());
                    });
            }
            catch (Exception ex)
            {
                _presenting = false;
                FuseLog.Exception(
                    "FUSE could not open Appalachian Railway starter placement; "
                    + "the cut remains queued.",
                    ex);
                Toast.Present(
                    "Starter equipment retained. Run /fuse.starters when "
                    + "placement is available.",
                    ToastPosition.Middle);
            }
        }

        internal static bool WasPlacementCommitted(
            bool callbackReportedPlaced,
            int expectedCarCount,
            int placedCarCount)
        {
            return callbackReportedPlaced
                   && expectedCarCount > 0
                   && placedCarCount == expectedCarCount;
        }

        internal static bool TryConsumeConfirmedEmpty<T>(
            Queue<T> queue,
            bool preparationSucceeded,
            int descriptorCount)
        {
            if (!preparationSucceeded
                || descriptorCount != 0
                || queue == null
                || queue.Count == 0)
            {
                return false;
            }

            queue.Dequeue();
            return true;
        }

        private static IEnumerator PresentAfterFrame()
        {
            yield return null;
            PresentNextPlacement();
        }

        internal static string ResumePendingPlacements()
        {
            if (PendingPlacements.Count == 0)
                return "No Appalachian Railway starter equipment is pending.";
            if (_presenting)
            {
                return "Starter equipment placement is already active ("
                       + PendingPlacements.Count
                       + " cut(s) remaining).";
            }
            if (InteractiveBookWindow.Shared != null
                && InteractiveBookWindow.Shared.IsShown)
            {
                return "Close the tutorial, then run /fuse.starters again. "
                       + PendingPlacements.Count
                       + " starter cut(s) are safely retained.";
            }

            PresentNextPlacement();
            return "Opened Appalachian Railway starter equipment placement ("
                   + PendingPlacements.Count
                   + " cut(s) remaining).";
        }

        private static bool TryBuildDescriptors(
            SetupDescriptor.CarPlacement placement,
            out List<CarDescriptor> descriptors)
        {
            descriptors = new List<CarDescriptor>();
            try
            {
                var identifiers = placement?.carIdentifier ?? Array.Empty<string>();
                descriptors = StateManager.DescriptorsForIdentifiers(identifiers)
                    .Select(descriptor =>
                    {
                        descriptor.Properties["oiled"] = placement.oiled;
                        return descriptor;
                    })
                    .ToList();
                if (placement.wreck)
                {
                    ApplyWreckState(descriptors);
                }

                if (placement.load != null && placement.loadPercent > 0f)
                {
                    ApplyInitialLoad(
                        descriptors,
                        placement.load,
                        placement.loadPercent);
                }

                ApplyRequiredInitialLoads(
                    descriptors,
                    1f);

                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE failed to prepare a queued Company starter cut; "
                    + "the cut remains queued.",
                    ex);
                descriptors = new List<CarDescriptor>();
                return false;
            }
        }

        private static void ApplyWreckState(
            IEnumerable<CarDescriptor> descriptors)
        {
            var derailmentKey = PropertyChange.KeyForControl(
                PropertyChange.Control.Derailment);
            var conditionKey = PropertyChange.KeyForControl(
                PropertyChange.Control.Condition);
            foreach (var descriptor in descriptors)
            {
                if (descriptor.DefinitionInfo.Definition.Archetype ==
                    CarArchetype.Tender)
                {
                    descriptor.Properties["load.0"] =
                        new CarLoadInfo("coal", 0f).AsPropertyValue;
                    descriptor.Properties["load.1"] =
                        new CarLoadInfo("water", 0f).AsPropertyValue;
                }

                descriptor.Properties[derailmentKey] = Value.Float(0.5f);
                descriptor.Properties[conditionKey] = Value.Float(0.7f);
            }
        }

        private static void ApplyInitialLoad(
            IEnumerable<CarDescriptor> descriptors,
            Load load,
            float loadPercent)
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.DefinitionInfo.Definition.LoadSlots.Count == 0)
                {
                    continue;
                }

                var loadSlot = descriptor.DefinitionInfo.Definition.LoadSlots[0];
                descriptor.Properties["load.0"] = new CarLoadInfo(
                    load.id,
                    loadSlot.MaximumCapacity * loadPercent).AsPropertyValue;
            }
        }

        private static void ApplyRequiredInitialLoads(
            IEnumerable<CarDescriptor> descriptors,
            float loadPercent)
        {
            foreach (var descriptor in descriptors)
            {
                var archetype = descriptor.DefinitionInfo.Definition.Archetype;
                if (archetype != CarArchetype.LocomotiveSteam &&
                    archetype != CarArchetype.Tender &&
                    archetype != CarArchetype.LocomotiveDiesel)
                {
                    continue;
                }

                var loadSlots = descriptor.DefinitionInfo.Definition.LoadSlots;
                for (var index = 0; index < loadSlots.Count; index++)
                {
                    var slot = loadSlots[index];
                    var key = $"load.{index}";
                    if (string.IsNullOrWhiteSpace(slot.RequiredLoadIdentifier) ||
                        descriptor.Properties.ContainsKey(key))
                    {
                        continue;
                    }

                    descriptor.Properties[key] = new CarLoadInfo(
                        slot.RequiredLoadIdentifier,
                        slot.MaximumCapacity * loadPercent).AsPropertyValue;
                }
            }
        }
    }
}
