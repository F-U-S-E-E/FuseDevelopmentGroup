using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using Game.Messages;
using Game.Progression;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Moves the stock East Whittier Company starter equipment into an
    /// interactive placement queue, and does the same for invalid placements
    /// from any other setup. This lets the player choose live track after the
    /// progression has settled instead of depending on one fixed scene marker.
    /// </summary>
    [HarmonyPatch(typeof(CompanyModeSetup), nameof(CompanyModeSetup.Setup))]
    internal static class FuseCompanyStarterPlacementPatch
    {
        [HarmonyPostfix]
        private static void SetupPostfix(
            SetupDescriptor setupDescriptor,
            ref IEnumerator __result)
        {
            if (__result != null && setupDescriptor != null)
            {
                __result = RepairAfterInitialDelay(__result, setupDescriptor);
            }
        }

        private static IEnumerator RepairAfterInitialDelay(
            IEnumerator original,
            SetupDescriptor setupDescriptor)
        {
            var repairPending = true;
            var placementQueue = new Queue<SetupDescriptor.CarPlacement>();
            while (original.MoveNext())
            {
                yield return original.Current;
                if (!repairPending)
                {
                    continue;
                }

                repairPending = false;
                QueueStarterPlacements(setupDescriptor, placementQueue);
            }

            if (placementQueue.Count > 0)
            {
                yield return null;
                PresentNextPlacement(placementQueue);
            }
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
            var queueEntireSetup = IsStockEastWhittierSetup(setupDescriptor);
            var retained = new List<SetupDescriptor.CarPlacement>(source.Length);
            foreach (var placement in source)
            {
                if (!queueEntireSetup && PlacementIsUsable(placement))
                {
                    retained.Add(placement);
                    continue;
                }

                placementQueue?.Enqueue(placement);
                queued++;
                FuseLog.Info(
                    $"FUSE queued Company starter placement " +
                    $"setup='{setupDescriptor.identifier ?? string.Empty}' cars={placement?.carIdentifier?.Length ?? 0}. " +
                    "The local player will choose its track location.");
            }

            setupDescriptor.placements = retained.ToArray();
            return queued;
        }

        private static bool IsStockEastWhittierSetup(
            SetupDescriptor setupDescriptor)
        {
            return setupDescriptor != null &&
                   !string.IsNullOrWhiteSpace(setupDescriptor.identifier) &&
                   setupDescriptor.identifier.StartsWith(
                       "ewh-",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool PlacementIsUsable(
            SetupDescriptor.CarPlacement placement)
        {
            if (placement?.marker == null)
            {
                return false;
            }

            var location = placement.marker.Location;
            return location.HasValue &&
                   location.Value.IsValid &&
                   location.Value.segment != null &&
                   location.Value.segment.GroupEnabled &&
                   location.Value.segment.Available;
        }

        private static void PresentNextPlacement(
            Queue<SetupDescriptor.CarPlacement> placementQueue)
        {
            if (placementQueue == null || placementQueue.Count == 0)
            {
                return;
            }

            var placement = placementQueue.Dequeue();
            var descriptors = BuildDescriptors(placement);
            if (descriptors.Count == 0)
            {
                PresentNextPlacement(placementQueue);
                return;
            }

            var placer = ConsistPlacer.Instance();
            if (placer == null)
            {
                FuseLog.Warning(
                    $"FUSE could not present {placementQueue.Count + 1} queued Company starter cut(s): " +
                    "the game's consist placer is unavailable.");
                return;
            }

            placer.Present(
                descriptors,
                null,
                _ => placer.StartCoroutine(PresentAfterFrame(placementQueue)));
        }

        private static IEnumerator PresentAfterFrame(
            Queue<SetupDescriptor.CarPlacement> placementQueue)
        {
            yield return null;
            PresentNextPlacement(placementQueue);
        }

        private static List<CarDescriptor> BuildDescriptors(
            SetupDescriptor.CarPlacement placement)
        {
            try
            {
                var identifiers = placement?.carIdentifier ?? Array.Empty<string>();
                var descriptors = StateManager.DescriptorsForIdentifiers(identifiers)
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

                return descriptors;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE failed to prepare a queued Company starter cut.",
                    ex);
                return new List<CarDescriptor>();
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
