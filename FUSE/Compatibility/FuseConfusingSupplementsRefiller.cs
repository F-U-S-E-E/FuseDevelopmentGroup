using System;
using System.Linq;
using FUSE.Infrastructure;
using Game.State;
using Model;
using Model.Definition;
using Model.Definition.Data;
using Model.Ops;
using Model.Physics;
using Railloader.Extensions;
using UnityEngine;

namespace FUSE.Compatibility
{
    internal sealed class FuseConfusingSupplementsRefillerComponent : Model.Definition.Component
    {
        internal const string ComponentKind = "ConfusingSupplements.Refiller";

        public override string Kind => ComponentKind;

        public int TransferRate { get; set; } = 36000;
    }

    internal sealed class FuseConfusingSupplementsRefillerBuilder : ComponentBuilder<FuseConfusingSupplementsRefillerComponent>
    {
        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsRefillerComponent component)
        {
            var car = context.GameObject?.GetComponentInParent<Car>();
            if (car == null)
            {
                FuseLog.Warning(
                    $"FUSE could not attach a legacy refiller to '{context.ObjectName ?? "<unknown car>"}' " +
                    "because its car object was unavailable.");
                return;
            }

            var runtime = car.GetComponent<FuseConfusingSupplementsRefillerRuntime>() ??
                          car.gameObject.AddComponent<FuseConfusingSupplementsRefillerRuntime>();
            runtime.TransferPerSecond = Mathf.Max(0f, (component?.TransferRate ?? 0) / 3600f);
        }
    }

    internal sealed class FuseConfusingSupplementsRefillerRuntime : MonoBehaviour
    {
        internal float TransferPerSecond { get; set; }

        private Car _source;
        private float _nextUpdate;

        private void Awake()
        {
            _source = GetComponent<Car>();
        }

        private void Update()
        {
            if (!StateManager.IsHost || _source == null || Time.time < _nextUpdate)
            {
                return;
            }

            _nextUpdate = Time.time + 1f;
            try
            {
                var target = FindNearestCompatibleCar(_source, Car.LogicalEnd.A) ??
                             FindNearestCompatibleCar(_source, Car.LogicalEnd.B);
                if (target != null)
                {
                    TransferLoads(_source, target, TransferPerSecond);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE contained a rolling-stock refiller error on '{_source.DisplayName ?? _source.id}': " +
                    ex.GetBaseException().Message);
            }
        }

        internal static bool CanReceiveFrom(CarDefinition source, CarDefinition target)
        {
            if (source?.LoadSlots == null || target?.LoadSlots == null)
            {
                return false;
            }

            return target.LoadSlots.Any(targetSlot =>
                !string.IsNullOrWhiteSpace(targetSlot?.RequiredLoadIdentifier) &&
                source.LoadSlots.Any(sourceSlot => string.Equals(
                    sourceSlot?.RequiredLoadIdentifier,
                    targetSlot.RequiredLoadIdentifier,
                    StringComparison.OrdinalIgnoreCase)));
        }

        private static Car FindNearestCompatibleCar(Car source, Car.LogicalEnd end)
        {
            var set = source.set;
            var index = set?.IndexOfCar(source);
            if (!index.HasValue)
            {
                return null;
            }

            var cursor = index.Value;
            var stop = false;
            while (!stop)
            {
                var candidate = set.NextCarConnected(
                    ref cursor,
                    end,
                    IntegrationSet.EnumerationCondition.AirConnected,
                    out stop);
                if (candidate == null)
                {
                    break;
                }

                if (candidate == source)
                {
                    continue;
                }

                if (IsSupportedTarget(candidate) && CanReceiveFrom(source.Definition, candidate.Definition))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSupportedTarget(Car car)
        {
            return car != null &&
                   (car.Archetype == CarArchetype.LocomotiveDiesel ||
                    car.Archetype == CarArchetype.LocomotiveSteam ||
                    car.Archetype == CarArchetype.Tender);
        }

        private static void TransferLoads(Car source, Car target, float maximumTransfer)
        {
            if (maximumTransfer <= 0f || source?.Definition?.LoadSlots == null || target?.Definition?.LoadSlots == null)
            {
                return;
            }

            for (var targetIndex = 0; targetIndex < target.Definition.LoadSlots.Count; targetIndex++)
            {
                var targetSlot = target.Definition.LoadSlots[targetIndex];
                var loadId = targetSlot?.RequiredLoadIdentifier;
                if (string.IsNullOrWhiteSpace(loadId))
                {
                    continue;
                }

                var sourceIndex = source.Definition.LoadSlots.FindIndex(slot => string.Equals(
                    slot?.RequiredLoadIdentifier,
                    loadId,
                    StringComparison.OrdinalIgnoreCase));
                if (sourceIndex < 0)
                {
                    continue;
                }

                var sourceLoad = source.GetLoadInfo(sourceIndex);
                if (!sourceLoad.HasValue || sourceLoad.Value.Quantity <= 0f)
                {
                    continue;
                }

                var targetLoad = target.GetLoadInfo(targetIndex);
                var targetQuantity = targetLoad?.Quantity ?? 0f;
                var availableCapacity = Mathf.Max(0f, targetSlot.MaximumCapacity - targetQuantity);
                var quantity = Mathf.Min(maximumTransfer, Mathf.Min(availableCapacity, sourceLoad.Value.Quantity));
                if (quantity <= 0f)
                {
                    continue;
                }

                var remainingSource = sourceLoad.Value;
                remainingSource.Quantity -= quantity;
                target.SetLoadInfo(targetIndex, new CarLoadInfo(loadId, targetQuantity + quantity));
                source.SetLoadInfo(sourceIndex, remainingSource);
            }
        }
    }
}
