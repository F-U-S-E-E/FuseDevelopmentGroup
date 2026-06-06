using System;
using System.Collections.Generic;
using System.Linq;
using FUSE.Infrastructure;
using Game;
using Game.State;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using UnityEngine;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// FUSE-native implementation of the "Pay4Resource" industry-component
    /// concept that several base-game scenery packs (most visibly Foxy's
    /// Kirkland Purchasable Coal Patch) reference via the legacy
    /// <c>ConfusingSupplements.IndustryComponents.Pay4Resource</c> type
    /// name.
    ///
    /// The behaviour is a self-contained per-track refuelling depot:
    /// empty cars that match the configured car-type filter and load
    /// type, parked on any of this component's track spans, are filled
    /// gradually toward a target percentage of their capacity while the
    /// player's balance is debited at a configured price-per-unit. The
    /// component runs only during the configured operating-hours window
    /// (intended use: limit purchases to "business hours"), uses no
    /// shared industry storage of its own, and ledgers each tick's
    /// payment through <see cref="Model.Ops.Industry.ApplyToBalance"/>
    /// with a randomly-chosen memo from the configured pool so the
    /// bookkeeping log varies.
    ///
    /// Field names below are part of the legacy JSON contract for this
    /// component type and are bound reflectively by
    /// <see cref="IndustryAPI.ApplyCustomIndustryComponentFields"/> — keep
    /// the names intact.
    /// </summary>
    public class FusePay4ResourceIndustryComponent : IndustryComponent
    {
        [Tooltip("Load the component will fill cars with (e.g. 'coal' for a locomotive-fuelling depot).")]
        public Load load;

        [Tooltip("Per-car transfer rate expressed in units per game day. RateToValue scales this to a per-tick increment.")]
        public float carLoadRate = 1f;

        [Tooltip("Dollars charged per unit transferred. Sub-cent values are supported via a per-component cents accumulator that batches whole-dollar deductions.")]
        public float costPerUnit;

        [Tooltip("Earliest hour-of-day (0..24, inclusive) at which the component will operate.")]
        public float notBefore;

        [Tooltip("Latest hour-of-day (0..24, exclusive) at which the component will operate.")]
        public float notAfter = 24f;

        [Tooltip("Fraction of car capacity (0..1) at which a car is considered 'done'. Cars already at or above this are skipped.")]
        public float fillPercentage = 1f;

        [Tooltip("Human-friendly title shown in the industry UI; falls back to the GameObject name.")]
        public string title;

        [Tooltip("Pool of ledger memos. One entry is picked at random per debit so the bookkeeping log doesn't repeat verbatim.")]
        public string[] bookReasons;

        [SerializeField]
        private Ledger.Category ledgerCategory = default;

        // Sub-cent unit cost (e.g. 0.0005 $/unit on Kirkland coal) means a
        // single tick's loaded quantity often owes less than one whole
        // dollar. Industry.ApplyToBalance takes int dollars only, so we
        // accumulate the fractional dollars here and flush whole dollars
        // out as soon as the accumulator crosses an integer boundary.
        // Without this the player would be loaded for free at sub-cent
        // costs.
        [NonSerialized]
        private float _pendingDollars;

        // Per-car carry: RateToValue can return a sub-unit amount per tick
        // at low time-multiplier, and IOpsCar.Load discards tiny
        // increments below its zero-threshold. Tracking unflushed units
        // per car lets us amortize those across ticks so a slow
        // time-multiplier doesn't stall transfers entirely.
        [NonSerialized]
        private readonly Dictionary<string, float> _pendingUnitsPerCar = new Dictionary<string, float>();

        public override string DisplayName => string.IsNullOrWhiteSpace(title) ? name : title;

        public override bool IsVisible
        {
            get
            {
                if (ProgressionDisabled)
                {
                    return false;
                }

                return trackSpans != null && trackSpans.Length > 0;
            }
        }

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            // We're a loader (we fill empty cars with the configured
            // load), so we want to appear in the car-inspector "Load"
            // auto-destination dropdown — that dropdown filters by
            // <c>AutoDestinationType.Empty</c> ("send empty cars here to
            // be loaded"). We never accept loaded cars for unloading, so
            // <c>AutoDestinationType.Load</c> stays off.
            return type == AutoDestinationType.Empty;
        }

        public override bool AcceptsCarsWithLoad(Load checkLoad)
        {
            // Compatible only with our configured load; everything else
            // belongs at a normal loader/unloader elsewhere on the layout.
            return load != null && checkLoad == load;
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            // No background ordering — we don't deliver cars here, we
            // service whatever the player parks.
        }

        public override void Service(IIndustryContext ctx)
        {
            if (ctx == null || load == null || Industry == null)
            {
                return;
            }

            if (!IsWithinOperatingHours(ctx))
            {
                return;
            }

            var unitsPerTick = RateToValue(carLoadRate, ctx.DeltaTime);
            if (unitsPerTick <= 0f && _pendingUnitsPerCar.Count == 0)
            {
                return;
            }

            var paidCarsThisTick = 0;
            foreach (var car in EnumerateCars(ctx, requireWaybill: false))
            {
                if (car == null || !car.IsEmptyOrContains(load))
                {
                    continue;
                }

                var (currentQty, capacity) = car.QuantityOfLoad(load);
                if (capacity <= 0f)
                {
                    continue;
                }

                var targetQty = capacity * Mathf.Clamp01(fillPercentage);
                if (currentQty >= targetQty - 0.0001f)
                {
                    _pendingUnitsPerCar.Remove(car.Id);
                    continue;
                }

                _pendingUnitsPerCar.TryGetValue(car.Id, out var carry);
                var requestedThisTick = Mathf.Min(unitsPerTick + carry, targetQty - currentQty);
                if (requestedThisTick <= 0.0001f)
                {
                    _pendingUnitsPerCar[car.Id] = requestedThisTick;
                    continue;
                }

                var actuallyLoaded = car.Load(load, requestedThisTick);
                var leftover = requestedThisTick - actuallyLoaded;
                if (leftover > 0.0001f)
                {
                    _pendingUnitsPerCar[car.Id] = leftover;
                }
                else
                {
                    _pendingUnitsPerCar.Remove(car.Id);
                }

                if (actuallyLoaded <= 0f || costPerUnit <= 0f)
                {
                    continue;
                }

                _pendingDollars += actuallyLoaded * costPerUnit;
                paidCarsThisTick++;
            }

            // Flush whole dollars only — keep the fractional remainder
            // for the next tick.
            var dollarsToCharge = Mathf.FloorToInt(_pendingDollars);
            if (dollarsToCharge > 0)
            {
                _pendingDollars -= dollarsToCharge;
                Industry.ApplyToBalance(
                    -dollarsToCharge,
                    ledgerCategory,
                    PickRandomBookReason(),
                    paidCarsThisTick,
                    quiet: true);
            }
        }

        private bool IsWithinOperatingHours(IIndustryContext ctx)
        {
            // Treat (0, 0) and (0, 24) as "always open" so a malformed
            // window can't silently stall the depot.
            if (notAfter <= 0f && notBefore <= 0f)
            {
                return true;
            }

            if (notBefore <= 0f && notAfter >= 24f)
            {
                return true;
            }

            float hour;
            try
            {
                hour = ctx.Now.Hours;
            }
            catch
            {
                return true;
            }

            if (notBefore <= notAfter)
            {
                // Same-day window (e.g. 6..22 == business hours).
                return hour >= notBefore && hour < notAfter;
            }

            // Wrap-around window (e.g. 22..6 == overnight). Inclusive on
            // the lower end, exclusive on the upper, to match the
            // daytime branch.
            return hour >= notBefore || hour < notAfter;
        }

        private string PickRandomBookReason()
        {
            if (bookReasons == null)
            {
                return null;
            }

            var candidates = bookReasons.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (candidates.Length == 0)
            {
                return null;
            }

            return candidates[UnityEngine.Random.Range(0, candidates.Length)];
        }

        protected override void ValidateIndustryComponent()
        {
            if (load == null)
            {
                FuseLog.Warning(
                    $"FUSE Pay4Resource component '{Identifier}' has no Load configured; " +
                    "it will remain inert until the definition is fixed.");
            }

            if (trackSpans == null || trackSpans.Length == 0)
            {
                FuseLog.Warning(
                    $"FUSE Pay4Resource component '{Identifier}' has no track spans; " +
                    "there is nowhere to place cars so the component will not fill anything.");
            }

            if (costPerUnit < 0f)
            {
                FuseLog.Warning(
                    $"FUSE Pay4Resource component '{Identifier}' has negative costPerUnit={costPerUnit}; " +
                    "this would credit the player for each transfer, which is almost certainly not the intended behaviour.");
            }
        }
    }
}
