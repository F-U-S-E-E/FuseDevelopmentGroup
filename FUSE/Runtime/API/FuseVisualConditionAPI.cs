using System;
using System.Globalization;
using System.Reflection;
using FUSE.Infrastructure;
using Game.State;
using KeyValue.Runtime;
using Model;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Per-car visual-condition override: lets players choose how weathered a
    /// car looks independent of its mechanical repair state. The override is
    /// stored in the car's replicated key-value store, so it persists in
    /// saves and syncs to multiplayer peers like any other car property.
    /// Rendering integration lives in FuseVisualConditionPatches.
    /// </summary>
    public static class FuseVisualConditionAPI
    {
        public const string VisualConditionKey = "fuse.condition.visual";

        // Strange-Customs era saves persist the per-car visual condition
        // under this key (1 = pristine, 0 = fully weathered — the game's own
        // condition scale). FUSE writes the prefixed key to keep its config
        // namespace clean, but must keep reading the legacy one so cars keep
        // the look players gave them before migrating.
        public const string LegacyVisualConditionKey = "_visualCondition";

        // Reflective handle for the car's private material refresh. The game
        // re-derives the shader wear blend only inside this method, so making
        // an override (or a flipped decouple setting) visible means re-running
        // it for the affected car.
        private static readonly MethodInfo CarUpdateMaterialsMethod =
            typeof(Car).GetMethod("UpdateMaterialsForCondition", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Picks the stored override, preferring the FUSE key over the legacy
        /// Strange-Customs key. Null means "no override" (vanilla look).
        /// </summary>
        internal static float? ResolveOverride(float? fuseValue, float? legacyValue)
        {
            var value = fuseValue ?? legacyValue;
            return value.HasValue ? Clamp01(value.Value) : (float?)null;
        }

        /// <summary>
        /// Condition value the material refresh should use. By default the
        /// override only caps the look — a car can be made to look more worn
        /// than it mechanically is, never less — so appearance stays an
        /// honest lower bound on repair state. Decoupled mode uses the
        /// override verbatim.
        /// </summary>
        internal static float EffectiveCondition(float actualCondition, float visualCondition, bool decoupled)
        {
            var visual = Clamp01(visualCondition);
            return decoupled ? visual : Math.Min(Clamp01(actualCondition), visual);
        }

        /// <summary>
        /// Maps a raw slider position to what should be stored: null to clear
        /// the override, or a percent-quantized value to write. Quantizing
        /// makes drag ticks inside the same displayed percent equal-value
        /// writes the key-value layer swallows, which keeps replication
        /// chatter down without losing the live preview. In capped mode a
        /// 100% override is indistinguishable from "no override", so it is
        /// stored as a clear — a parked 1.0 would otherwise silently force
        /// the car pristine if the player later decouples visual condition
        /// from repair state.
        /// </summary>
        internal static float? NormalizeForStore(float value, bool decoupled)
        {
            var quantized = (float)(Math.Round(Clamp01(value) * 100f) / 100f);
            if (!decoupled && quantized >= 1f)
            {
                return null;
            }

            return quantized;
        }

        /// <summary>
        /// Maps a 0..1 random roll onto the spawn-randomization range
        /// configured in <c>FuseSettings</c>. Bounds are clamped to 0..1 and
        /// normalized if the user entered them reversed (min &gt; max), so
        /// any settings combination yields a valid condition. Pure function —
        /// the roll is injected so the mapping is unit-testable without the
        /// engine RNG.
        /// </summary>
        internal static float ComputeSpawnCondition(float min, float max, float roll01)
        {
            var lo = Clamp01(min);
            var hi = Clamp01(max);
            if (lo > hi)
            {
                var swap = lo;
                lo = hi;
                hi = swap;
            }

            return lo + (hi - lo) * Clamp01(roll01);
        }

        internal static string FormatPercent(float value)
        {
            return (Clamp01(value) * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        internal static float Clamp01(float value)
        {
            // A NaN from a corrupt save or a hostile peer must not reach the
            // material math; treat it as the neutral "pristine" value.
            if (float.IsNaN(value))
            {
                return 1f;
            }

            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        /// <summary>
        /// Reads the stored visual-condition override for a car, or null when
        /// the car has none.
        /// </summary>
        public static float? TryGetVisualCondition(Car car)
        {
            var kvo = car != null ? car.KeyValueObject : null;
            if (kvo == null)
            {
                return null;
            }

            return ResolveOverride(ReadFloat(kvo, VisualConditionKey), ReadFloat(kvo, LegacyVisualConditionKey));
        }

        /// <summary>
        /// Slider position for the inspector row: the stored override, or 1
        /// (a pristine cap changes nothing) when none is stored.
        /// </summary>
        public static float GetSliderValue(Car car)
        {
            return TryGetVisualCondition(car) ?? 1f;
        }

        /// <summary>
        /// Stores (or clears, see <see cref="NormalizeForStore"/>) the
        /// override in the car's key-value object. The local set replicates
        /// through the game's property-sync layer the same way the FUSE audio
        /// selections do, and the observer installed by
        /// FuseCarVisualConditionObserverPatch reapplies car materials when
        /// the value lands.
        /// </summary>
        public static void SetVisualCondition(Car car, float value)
        {
            var kvo = car != null ? car.KeyValueObject : null;
            if (kvo == null)
            {
                return;
            }

            var store = NormalizeForStore(value, FuseSettings.DecoupleVisualConditionLimits);
            kvo[VisualConditionKey] = store.HasValue ? Value.Float(store.Value) : Value.Null();

            // A cleared FUSE key would let any Strange-Customs era value
            // beneath it resurface, so retire the legacy key once the player
            // touches the slider. Its underscore prefix is host-writable
            // only; non-host clients leave it in place (they could not change
            // it under the legacy loader either).
            if (StateManager.IsHost && !kvo[LegacyVisualConditionKey].IsNull)
            {
                kvo[LegacyVisualConditionKey] = Value.Null();
            }
        }

        /// <summary>
        /// Re-runs the game's material refresh for one car so a changed
        /// override or setting is reflected immediately.
        /// </summary>
        public static void RefreshCarMaterials(Car car)
        {
            if (car == null || CarUpdateMaterialsMethod == null)
            {
                return;
            }

            try
            {
                CarUpdateMaterialsMethod.Invoke(car, null);
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException
                    : ex;
                FuseLog.Warning(
                    $"FUSE visual condition refresh failed car='{car.id ?? string.Empty}' message='{cause.Message}'.");
            }
        }

        /// <summary>
        /// Repaints every live car that carries an override. Needed when the
        /// decouple setting flips: that changes the effective look everywhere
        /// without any per-car value changing, so no key-value observer fires
        /// and on-screen cars would otherwise keep their stale wear until a
        /// condition change or a culling visibility transition.
        /// </summary>
        public static void RefreshAllOverriddenCars()
        {
            try
            {
                var cars = TrainController.Shared != null ? TrainController.Shared.Cars : null;
                if (cars == null)
                {
                    return;
                }

                foreach (var car in cars)
                {
                    if (car != null && !car.ghost && TryGetVisualCondition(car).HasValue)
                    {
                        RefreshCarMaterials(car);
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE visual condition repaint sweep failed: {ex.Message}");
            }
        }

        private static float? ReadFloat(KeyValueObject kvo, string key)
        {
            var value = kvo[key];
            return value.IsNull ? (float?)null : (float?)value.FloatValueOrDefault(1f);
        }
    }
}
