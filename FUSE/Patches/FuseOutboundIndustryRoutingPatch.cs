using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using FUSE.Loading;
using FUSE.Runtime.Events;
using Game;
using HarmonyLib;
using Model.Ops;
using Model.Ops.Definition;
using Track;
using Track.Search;
using UnityEngine;

namespace FUSE.Patches
{
    internal enum FuseOutboundRoutingMode
    {
        Disabled,
        Absolute,
        Configurable,
    }

    [HarmonyPatch]
    internal static class FuseOutboundIndustryRoutingPatch
    {
        private const float MinimumTripDistanceMeters = 200f;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(OpsController), nameof(OpsController.AddOrderForOutboundEmptyCar))]
        private static bool RouteEmpty(
            OpsController __instance,
            IOpsCar car,
            OpsCarPosition carPosition,
            string orderTag,
            bool noPayment)
        {
            return TryRoute(__instance, car, carPosition, orderTag, noPayment, loaded: false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(OpsController), nameof(OpsController.AddOrderForOutboundLoadedCar))]
        private static bool RouteLoaded(
            OpsController __instance,
            IOpsCar car,
            OpsCarPosition carPosition,
            string orderTag,
            bool noPayment)
        {
            return TryRoute(__instance, car, carPosition, orderTag, noPayment, loaded: true);
        }

        private static bool TryRoute(
            OpsController controller,
            IOpsCar car,
            OpsCarPosition origin,
            string orderTag,
            bool noPayment,
            bool loaded)
        {
            var mode = ResolveMode(
                FuseSettings.EnableOutboundIndustryRerouting,
                FuseLegacyCapabilityActivation.IsRequested("Zamu.AbsoluteMadness", "AbsoluteMadness"),
                FuseLegacyCapabilityActivation.IsRequested("Zamu.SomeKindOfMadness", "SomeKindOfMadness"));
            if (mode == FuseOutboundRoutingMode.Disabled || controller == null || car == null)
            {
                return true;
            }

            if (mode == FuseOutboundRoutingMode.Configurable &&
                UnityEngine.Random.value >= FuseSettings.OutboundIndustryRerouteChance)
            {
                return true;
            }

            try
            {
                var candidates = CollectCandidates(controller, car, origin, loaded, mode);
                var context = new FuseOutboundRoutingContext(car, origin, loaded, candidates);
                FuseOutboundRoutingEvents.RaisePreparing(context);
                var selectedIndex = SelectWeightedIndex(
                    candidates.Select(candidate => candidate.Weight).ToArray(),
                    UnityEngine.Random.value);
                if (selectedIndex < 0 || selectedIndex >= candidates.Count)
                {
                    return true;
                }

                var selected = candidates[selectedIndex];
                var destination = selected.Component;
                var multiplier = mode == FuseOutboundRoutingMode.Absolute
                    ? 1f
                    : FuseSettings.OutboundIndustryPaymentMultiplier;
                var payment = noPayment
                    ? 0
                    : Mathf.RoundToInt(
                        selected.ProposedPayment ??
                        (controller.PaymentForMove(origin, destination, car.WeightInTons) * multiplier));
                var graceDays = selected.ProposedGraceDays ?? controller.CalculateGraceDays(origin, destination);
                var waybill = new Waybill(
                    TimeWeather.Now,
                    origin,
                    destination,
                    payment,
                    false,
                    orderTag,
                    graceDays);

                car.SetWaybill(waybill, destination, "FUSE outbound industry routing");
                return false;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE outbound industry routing failed safely; the base-game interchange route will be used: " +
                    ex.GetBaseException().Message);
                return true;
            }
        }

        internal static FuseOutboundRoutingMode ResolveMode(
            bool explicitOptIn,
            bool absoluteRequested,
            bool configurableRequested)
        {
            if (explicitOptIn || configurableRequested)
            {
                return FuseOutboundRoutingMode.Configurable;
            }

            return absoluteRequested ? FuseOutboundRoutingMode.Absolute : FuseOutboundRoutingMode.Disabled;
        }

        private static List<FuseOutboundRoutingCandidate> CollectCandidates(
            OpsController controller,
            IOpsCar car,
            OpsCarPosition origin,
            bool loaded,
            FuseOutboundRoutingMode mode)
        {
            var candidates = new List<FuseOutboundRoutingCandidate>();
            var fillFactor = mode == FuseOutboundRoutingMode.Absolute
                ? 1f
                : FuseSettings.OutboundIndustryFillFactor;
            var allowShortTrips = mode == FuseOutboundRoutingMode.Absolute || FuseSettings.OutboundIndustryAllowShortTrips;

            foreach (var industry in controller.AllIndustries ?? Array.Empty<Industry>())
            {
                if (industry == null || industry.ProgressionDisabled ||
                    (mode == FuseOutboundRoutingMode.Configurable && !industry.ShouldOrderCars()))
                {
                    continue;
                }

                foreach (var pair in industry.EnumerateComponentContexts(0f))
                {
                    var component = pair.Item1;
                    var componentContext = pair.Item2;
                    if (component == null || component.ProgressionDisabled ||
                        component.carTypeFilter == null ||
                        !component.carTypeFilter.Matches(car.CarType) ||
                        !TryGetTarget(component, loaded, out var load, out var maxStorage))
                    {
                        continue;
                    }

                    if (loaded && car.QuantityOfLoad(load).Item1 <= load.ZeroThreshold)
                    {
                        continue;
                    }

                    if (!allowShortTrips && !IsLongEnoughTrip(origin, component))
                    {
                        continue;
                    }

                    var committed = componentContext.QuantityInStorage(load) +
                                    componentContext.QuantityOnOrder(load) +
                                    componentContext.AvailableCapacityInCars(component.carTypeFilter, load);
                    var remaining = (maxStorage * fillFactor) - committed;
                    if (remaining > load.ZeroThreshold)
                    {
                        candidates.Add(new FuseOutboundRoutingCandidate(component, remaining));
                    }
                }
            }

            return candidates;
        }

        private static bool TryGetTarget(
            IndustryComponent component,
            bool loaded,
            out Load load,
            out float maxStorage)
        {
            load = null;
            maxStorage = 0f;
            if (!loaded && component is IndustryLoaderBase loader && loader.orderEmpties)
            {
                load = loader.load;
                maxStorage = loader.maxStorage;
                return load != null;
            }

            if (loaded && component is IndustryUnloader unloader && unloader.orderLoads)
            {
                load = unloader.load;
                maxStorage = unloader.maxStorage;
                return load != null;
            }

            return false;
        }

        private static bool IsLongEnoughTrip(OpsCarPosition origin, IndustryComponent destination)
        {
            if (origin.Spans == null || origin.Spans.Length == 0 ||
                destination.trackSpans == null || destination.trackSpans.Length == 0)
            {
                return false;
            }

            return Graph.Shared.TryFindDistance(
                       (Location)origin,
                       (Location)(OpsCarPosition)destination,
                       out var distance,
                       out _) &&
                   distance > MinimumTripDistanceMeters;
        }

        internal static int SelectWeightedIndex(IReadOnlyList<float> weights, float unitSample)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            var minimum = weights.Min();
            var offset = minimum < 0f ? -minimum : 0f;
            var total = weights.Sum(weight => Math.Max(0f, weight + offset));
            if (total <= 0f || float.IsNaN(total))
            {
                return Math.Min(weights.Count - 1, Mathf.FloorToInt(Mathf.Clamp01(unitSample) * weights.Count));
            }

            var cursor = Mathf.Clamp01(unitSample) * total;
            for (var index = 0; index < weights.Count; index++)
            {
                cursor -= Math.Max(0f, weights[index] + offset);
                if (cursor <= 0f)
                {
                    return index;
                }
            }

            return weights.Count - 1;
        }

    }

    [HarmonyPatch(typeof(OpsController), "InterchangeForPosition", new[] { typeof(OpsCarPosition), typeof(OpsCarPosition?) })]
    internal static class FuseOutboundIndustryDirectionPatch
    {
        private static void Prefix(ref OpsCarPosition? origin)
        {
            var requested = FuseLegacyCapabilityActivation.IsRequested(
                "Zamu.SomeKindOfMadness",
                "SomeKindOfMadness");
            if ((FuseSettings.EnableOutboundIndustryRerouting || requested) &&
                FuseSettings.OutboundIndustryIgnoreOrigin)
            {
                origin = null;
            }
        }
    }

    [HarmonyPatch(typeof(IndustryContext), "AddOrderedCars")]
    internal static class FuseOutboundIndustryBlockingPatch
    {
        private static readonly object RandomGate = new object();
        private static readonly System.Random Random = new System.Random();

        private static void Prefix(List<IOrder> orders)
        {
            var requested = FuseLegacyCapabilityActivation.IsRequested(
                "Zamu.SomeKindOfMadness",
                "SomeKindOfMadness");
            if (orders == null || orders.Count < 2 ||
                !(FuseSettings.EnableOutboundIndustryRerouting || requested) ||
                !FuseSettings.OutboundIndustryPreventBlocking)
            {
                return;
            }

            lock (RandomGate)
            {
                Shuffle(orders, Random);
            }
        }

        internal static void Shuffle<T>(IList<T> items, System.Random random)
        {
            if (items == null || random == null)
            {
                return;
            }

            for (var index = items.Count - 1; index > 0; index--)
            {
                var swap = random.Next(index + 1);
                var value = items[index];
                items[index] = items[swap];
                items[swap] = value;
            }
        }
    }
}
