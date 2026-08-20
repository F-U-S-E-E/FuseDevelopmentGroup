using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Model.Ops;
using Model.Ops.Definition;
using UI.CarInspector;
using UnityEngine;

namespace FUSE.Patches
{
    internal static class FuseInterchangeToInterchangePolicy
    {
        internal static bool IsActive()
        {
            return FuseLegacyCapabilityActivation.IsRequested(
                "Zamu.Interchange2Interchange",
                "Interchange2Interchange");
        }

        internal static int ScaleMaximumCars(int configuredMaximum, float contractMultiplier)
        {
            if (configuredMaximum <= 0 || contractMultiplier <= 0f || float.IsNaN(contractMultiplier))
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(configuredMaximum * contractMultiplier),
                0,
                FuseSettings.InterchangeToInterchangeMaximumCarsLimit);
        }
    }

    [HarmonyPatch(typeof(OpsController), "RebuildCollections")]
    internal static class FuseInterchangeToInterchangeContractSetupPatch
    {
        private static void Postfix(OpsController __instance)
        {
            if (!FuseInterchangeToInterchangePolicy.IsActive() || __instance?.AllInterchanges == null)
            {
                return;
            }

            foreach (var interchange in __instance.AllInterchanges)
            {
                if (interchange?.Industry != null)
                {
                    interchange.Industry.usesContract = true;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Interchange), nameof(Interchange.OrderCars))]
    internal static class FuseInterchangeToInterchangeOrderPatch
    {
        private const string LastOrderTimeKey = "fuse.i2i.lastOrderTime";

        private static void Postfix(Interchange __instance, IIndustryContext ctx)
        {
            if (!FuseInterchangeToInterchangePolicy.IsActive() || __instance == null || ctx == null)
            {
                return;
            }

            try
            {
                AddInterchangeOrders(__instance, ctx);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not create interchange-to-interchange orders for " +
                    $"'{__instance.DisplayName}'; normal interchange service remains available: " +
                    ex.GetBaseException().Message);
            }
        }

        private static void AddInterchangeOrders(Interchange source, IIndustryContext ctx)
        {
            if (source.Industry == null)
            {
                return;
            }

            source.Industry.usesContract = true;
            var multiplier = source.Industry.GetContractMultiplier();
            var maximumCars = FuseInterchangeToInterchangePolicy.ScaleMaximumCars(
                FuseSettings.InterchangeToInterchangeMaximumCars,
                multiplier);
            if (maximumCars <= 0 ||
                ctx.Now.DaysSince(ctx.GetDateTime(LastOrderTimeKey, Game.GameDateTime.Zero)) < 1f)
            {
                return;
            }

            var controller = OpsController.Shared;
            var destinations = (controller?.EnabledInterchanges ?? Enumerable.Empty<Interchange>())
                .Where(candidate => candidate != null && candidate != source && candidate.Industry != null)
                .ToArray();
            var cargo = CollectCargo(controller).ToArray();
            if (destinations.Length == 0 || cargo.Length == 0)
            {
                return;
            }

            var added = 0;
            foreach (var destination in destinations)
            {
                var remaining = UnityEngine.Random.Range(0, maximumCars + 1);
                while (remaining > 0)
                {
                    var count = UnityEngine.Random.Range(1, Math.Min(3, remaining) + 1);
                    remaining -= count;
                    var selection = cargo[UnityEngine.Random.Range(0, cargo.Length)];
                    source.AddOrder(new Order(
                        new CarTypeFilter(selection.CarFilter),
                        selection.Load,
                        destination,
                        count,
                        null,
                        false));
                    added += count;
                }
            }

            if (added > 0)
            {
                ctx.SetDateTime(LastOrderTimeKey, ctx.Now);
                FuseLog.Info(
                    $"FUSE scheduled {added} interchange-to-interchange car(s) from '{source.DisplayName}'.");
            }
        }

        private static IEnumerable<CargoTarget> CollectCargo(OpsController controller)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var industry in controller?.AllIndustries ?? Array.Empty<Industry>())
            {
                if (industry == null || industry.ProgressionDisabled)
                {
                    continue;
                }

                foreach (var component in industry.Components ?? Array.Empty<IndustryComponent>())
                {
                    if (component == null || component.ProgressionDisabled || component.carTypeFilter == null ||
                        component.carTypeFilter.Matches("PB") || component.carTypeFilter.Matches("PBO"))
                    {
                        continue;
                    }

                    var load = LoadFor(component);
                    var filter = component.carTypeFilter.queryString;
                    var key = (filter ?? string.Empty) + "\n" + (load?.id ?? string.Empty);
                    if (load != null && !string.IsNullOrWhiteSpace(filter) && seen.Add(key))
                    {
                        yield return new CargoTarget(filter, load);
                    }
                }
            }
        }

        private static Load LoadFor(IndustryComponent component)
        {
            if (component is IndustryLoaderBase loader)
            {
                return loader.load;
            }

            if (component is IndustryUnloader unloader)
            {
                return unloader.load;
            }

            return (component as TeleportLoadingIndustry)?.load;
        }

        private readonly struct CargoTarget
        {
            internal CargoTarget(string carFilter, Load load)
            {
                CarFilter = carFilter;
                Load = load;
            }

            internal string CarFilter { get; }
            internal Load Load { get; }
        }
    }

    [HarmonyPatch]
    internal static class FuseInterchangeToInterchangeInspectorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(CarInspector))
                .SingleOrDefault(method =>
                {
                    if (method.Name.IndexOf("ShouldShowIndustry", StringComparison.Ordinal) < 0 ||
                        method.ReturnType != typeof(bool))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == typeof(Industry);
                });
        }

        private static void Postfix(Industry industry, ref bool __result)
        {
            if (!__result && FuseInterchangeToInterchangePolicy.IsActive() &&
                industry != null && !industry.ProgressionDisabled &&
                industry.Components.OfType<Interchange>().Any())
            {
                __result = true;
            }
        }
    }
}
