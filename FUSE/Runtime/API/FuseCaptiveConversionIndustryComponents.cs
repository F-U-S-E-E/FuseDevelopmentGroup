using System;
using FUSE.Infrastructure;
using Game;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// FUSE-owned implementation of the legacy captive conversion loader
    /// contract. A source material held in industry storage is converted into
    /// a different load while it is transferred into captive-service cars.
    /// The public field names intentionally match the legacy JSON surface so
    /// <see cref="IndustryAPI"/> can bind converted definitions without the
    /// Confusing Supplements assembly being installed.
    /// </summary>
    public sealed class FuseCaptiveConversionLoader : IndustryLoaderBase
    {
        public Load convertedLoad;
        public float carLoadRate = 1f;
        public string title;

        public override string DisplayName => string.IsNullOrWhiteSpace(title) ? name : title;

        public override void OrderCars(IIndustryContext ctx)
        {
            // Captive service never requests cars from an interchange.
        }

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            return type == AutoDestinationType.Empty;
        }

        public override bool AcceptsCarsWithLoad(Load checkLoad)
        {
            return convertedLoad != null && checkLoad == convertedLoad;
        }

        public override void Service(IIndustryContext ctx)
        {
            if (ctx == null || Industry == null || load == null || convertedLoad == null)
            {
                return;
            }

            var available = ctx.QuantityInStorage(load);
            if (available <= load.ZeroThreshold)
            {
                return;
            }

            var contractMultiplier = Industry.GetContractMultiplier();
            var transferBudget = RateToValue(Mathf.Max(0f, carLoadRate) * contractMultiplier, ctx.DeltaTime);
            if (transferBudget <= 0f)
            {
                return;
            }

            foreach (var car in EnumerateCars(ctx, requireWaybill: true))
            {
                if (car == null || !car.IsEmptyOrContains(convertedLoad))
                {
                    continue;
                }

                var requested = Mathf.Min(available, transferBudget);
                if (requested <= load.ZeroThreshold)
                {
                    break;
                }

                var transferred = car.Load(convertedLoad, requested);
                if (transferred > 0f)
                {
                    ctx.RemoveFromStorage(load, transferred);
                    available -= transferred;
                    transferBudget -= transferred;
                }

                if (car.IsFull(convertedLoad))
                {
                    car.SetWaybill(null, this, "Full");
                }

                if (available <= load.ZeroThreshold || transferBudget <= 0f)
                {
                    break;
                }
            }
        }

        protected override void ValidateIndustryComponent()
        {
            base.ValidateIndustryComponent();
            ValidateLoads("loader", load, convertedLoad);
        }

        internal static void ValidateLoads(string role, Load source, Load converted)
        {
            if (source == null || converted == null)
            {
                FuseLog.Warning($"FUSE captive conversion {role} is missing its source or converted load and will remain inert.");
                return;
            }

            if (source.units != converted.units)
            {
                FuseLog.Warning(
                    $"FUSE captive conversion {role} cannot convert '{source.id}' to '{converted.id}' because their units differ.");
            }
        }
    }

    /// <summary>
    /// FUSE-owned counterpart to <see cref="FuseCaptiveConversionLoader"/>.
    /// It removes the captive-car load, deposits the configured source
    /// material in industry storage, and settles the converted load's daily
    /// receivable through the game's industry context.
    /// </summary>
    public sealed class FuseCaptiveConversionUnloader : IndustryComponent
    {
        public Load load;
        public Load convertedLoad;
        public float maxStorage;
        public float carUnloadRate = 1f;
        public string title;

        public override string DisplayName => string.IsNullOrWhiteSpace(title) ? name : title;

        private string UnloadedCounterKey => "fuse-captive-unloaded-" + (convertedLoad?.id ?? subIdentifier ?? "load");

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            return type == AutoDestinationType.Load;
        }

        public override bool AcceptsCarsWithLoad(Load checkLoad)
        {
            return convertedLoad != null && checkLoad == convertedLoad;
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            // Captive service never orders cars; it only services cars placed
            // on its configured spans.
        }

        public override void Service(IIndustryContext ctx)
        {
            if (ctx == null || Industry == null || load == null || convertedLoad == null)
            {
                return;
            }

            var contractMultiplier = Industry.GetContractMultiplier();
            var transferBudget = RateToValue(Mathf.Max(0f, carUnloadRate) * contractMultiplier, ctx.DeltaTime);
            var effectiveStorage = Mathf.Max(0f, maxStorage * contractMultiplier);
            var zeroThreshold = Mathf.Max(load.ZeroThreshold, convertedLoad.ZeroThreshold);
            if (transferBudget > 0f && transferBudget < zeroThreshold)
            {
                transferBudget = zeroThreshold * 2f;
            }

            var unloadedThisTick = 0f;
            foreach (var car in EnumerateCars(ctx, requireWaybill: true))
            {
                if (car == null || !car.IsEmptyOrContains(convertedLoad))
                {
                    continue;
                }

                var remainingStorage = effectiveStorage - ctx.QuantityInStorage(load);
                var requested = Mathf.Min(transferBudget, remainingStorage);
                if (requested < zeroThreshold)
                {
                    break;
                }

                var transferred = car.Unload(convertedLoad, requested);
                if (transferred > 0f)
                {
                    ctx.AddToStorage(load, transferred, effectiveStorage);
                    transferBudget -= transferred;
                    unloadedThisTick += transferred;
                }

                var remainingLoad = car.QuantityOfLoad(convertedLoad).Item1;
                if (remainingLoad < zeroThreshold)
                {
                    car.SetWaybill(null, this, "Empty completed");
                }

                if (transferBudget <= 0f)
                {
                    break;
                }
            }

            if (unloadedThisTick > 0f && convertedLoad.payPerQuantity > 0f)
            {
                ctx.CounterIncrement(UnloadedCounterKey, unloadedThisTick);
            }
        }

        public override void DailyReceivables(GameDateTime now, IIndustryContext ctx)
        {
            if (ctx == null || convertedLoad == null || convertedLoad.payPerQuantity <= 0f)
            {
                return;
            }

            var unloaded = ctx.CounterIncrement(UnloadedCounterKey, 0f);
            if (unloaded < 1f)
            {
                return;
            }

            ctx.PayLoad(convertedLoad, unloaded);
            ctx.CounterClear(UnloadedCounterKey);
        }

        protected override void ValidateIndustryComponent()
        {
            base.ValidateIndustryComponent();
            FuseCaptiveConversionLoader.ValidateLoads("unloader", load, convertedLoad);
        }
    }
}
