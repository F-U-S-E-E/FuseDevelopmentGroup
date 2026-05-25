using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game;
using Game.State;
using KeyValue.Runtime;
using Model.Ops;
using Model.Ops.Definition;
using Network;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public class FuseInterchangedIndustryUnloader : IndustryComponent
    {
        private static readonly PropertyInfo IndustryKeyValueObjectProperty =
            typeof(Industry).GetProperty("KeyValueObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo KeyValueObjectItemProperty =
            typeof(IKeyValueObject).GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private Interchange _interchange;
        private bool? _hasInterchange;

        public Load load;

        [SerializeField]
        private Ledger.Category ledgerCategory = default;

        public override string DisplayName
        {
            get
            {
                var interchange = Interchange;
                return interchange == null
                    ? name
                    : interchange.DisplayName + " to " + name;
            }
        }

        private string KeyBardoCars => "br-" + subIdentifier;

        private Interchange Interchange
        {
            get
            {
                if (!_hasInterchange.HasValue)
                {
                    _interchange = Industry == null ? null : Industry.GetComponentInChildren<Interchange>();
                    _hasInterchange = _interchange != null;
                }

                return _interchange;
            }
        }

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            return type == AutoDestinationType.Load;
        }

        public override void Service(IIndustryContext ctx)
        {
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            var interchange = Interchange;
            if (interchange == null || ctx == null)
            {
                return;
            }

            foreach (var item in EnumerateBardoCars())
            {
                if (!(item.returnTime > ctx.Now))
                {
                    interchange.OrderReturnFromBardo(item.carId);
                }
            }
        }

        public void ServeInterchange(IIndustryContext ctx, Interchange interchange)
        {
            if (ctx == null || load == null)
            {
                return;
            }

            var cars = EnumerateCars(ctx, true)
                .Where(car => car.IsFull(load))
                .ToList();
            if (cars.Count == 0)
            {
                return;
            }

            var paid = 0;
            var paidCars = 0;
            var returnTime = ctx.Now.AddingDays(23f / 24f);
            foreach (var car in cars)
            {
                var quantity = car.QuantityOfLoad(load);
                var amount = quantity.Item1;
                var capacity = quantity.Item2;
                if (amount < capacity)
                {
                    Debug.LogWarning("Car ID: " + car.Id + ", name: " + car.DisplayName + " is not full, but FUSE will accept it at interchange export.");
                }

                var payment = Mathf.RoundToInt(amount * load.payPerQuantity);
                if (payment > 0)
                {
                    paid += payment;
                    paidCars++;
                }

                car.Unload(load, amount);
                car.SetWaybill(null, this, "Empty completed");
                ctx.MoveToBardo(car);
                ScheduleReturnFromBardo(car, returnTime);
            }

            if (paid > 0 && Industry != null)
            {
                Industry.ApplyToBalance(paid, ledgerCategory, null, paidCars, true);
                Multiplayer.Broadcast($"Sold {paidCars} car(s) of {load.description} at {DisplayName} for {paid:C0}. Expected return: 1 day.");
            }
        }

        public void InterchangeDidEmptyReturnFromBardoOrder(string carId)
        {
            SetBardoCarsValue(carId, Value.Null());
        }

        private void ScheduleReturnFromBardo(IOpsCar car, GameDateTime returnTime)
        {
            SetBardoCarsValue(car.Id, (int)returnTime.TotalSeconds);
        }

        private void SetBardoCarsValue(string key, Value value)
        {
            if (Industry == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var existing = GetIndustryValue(KeyBardoCars).DictionaryValue;
            var dictionary = existing == null
                ? new Dictionary<string, Value>()
                : existing.ToDictionary(pair => pair.Key, pair => pair.Value);

            if (value.IsNull)
            {
                dictionary.Remove(key);
            }
            else
            {
                dictionary[key] = value;
            }

            SetIndustryValue(
                KeyBardoCars,
                dictionary.Any()
                    ? Value.Dictionary(dictionary)
                    : Value.Null());
        }

        private IEnumerable<(string carId, GameDateTime returnTime)> EnumerateBardoCars()
        {
            if (Industry == null)
            {
                yield break;
            }

            var value = GetIndustryValue(KeyBardoCars);
            var dictionary = value.DictionaryValue;
            if (dictionary == null)
            {
                yield break;
            }

            foreach (var item in dictionary)
            {
                yield return (item.Key, new GameDateTime(item.Value.FloatValue));
            }
        }

        private Value GetIndustryValue(string key)
        {
            var keyValueObject = IndustryKeyValueObjectProperty?.GetValue(Industry, null);
            if (keyValueObject == null || KeyValueObjectItemProperty == null)
            {
                return Value.Null();
            }

            return (Value)KeyValueObjectItemProperty.GetValue(keyValueObject, new object[] { key });
        }

        private void SetIndustryValue(string key, Value value)
        {
            var keyValueObject = IndustryKeyValueObjectProperty?.GetValue(Industry, null);
            if (keyValueObject == null || KeyValueObjectItemProperty == null)
            {
                return;
            }

            KeyValueObjectItemProperty.SetValue(keyValueObject, value, new object[] { key });
        }
    }
}
