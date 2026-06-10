using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Per-car "visual condition": a purely cosmetic 0..1 weathering value
    /// kept on the car's key-value object, independent of the mechanical
    /// condition the game tracks for repair purposes. The game derives the
    /// car-shader wear amount from the mechanical condition; FUSE blends
    /// this visual value in (see <c>FuseVisualConditionPatches</c>) so a
    /// car can be made to LOOK more weathered than its mechanical state
    /// without affecting how it drives or what the shops charge to fix it.
    ///
    /// <para>Values are stored under <see cref="VisualConditionKey"/>.
    /// Saves that predate FUSE may carry the same concept under
    /// <see cref="LegacyVisualConditionKey"/>; readers fall back to it so
    /// migrated saves keep their per-car weathering without a rewrite
    /// pass. Writers only ever use the FUSE key.</para>
    /// </summary>
    public static class FuseVisualConditionAPI
    {
        public const string VisualConditionKey = "fuse.condition.visual";

        /// <summary>
        /// Key used by the legacy mod generation for the same concept.
        /// Read-only compatibility: consulted when the FUSE key is unset.
        /// </summary>
        public const string LegacyVisualConditionKey = "_visualCondition";

        /// <summary>
        /// Reads the car's visual condition, falling back to the legacy
        /// key and finally to 1 (factory fresh) when neither key is set.
        /// </summary>
        public static float GetVisualCondition(Car car)
        {
            if (car == null || car.KeyValueObject == null)
            {
                return 1f;
            }

            var value = car.KeyValueObject[VisualConditionKey];
            if (value.IsNull)
            {
                value = car.KeyValueObject[LegacyVisualConditionKey];
            }

            return Clamp01(value.FloatValueOrDefault(1f));
        }

        /// <summary>
        /// Writes the car's visual condition (clamped to 0..1) as a
        /// property change routed through the state manager, so the value
        /// replicates to multiplayer clients and persists with the save
        /// like any other car key-value entry.
        /// </summary>
        public static void SetVisualCondition(Car car, float condition)
        {
            if (car == null || car.KeyValueObject == null)
            {
                return;
            }

            StateManager.ApplyLocal(
                new PropertyChange(
                    car.KeyValueObject.RegisteredId,
                    VisualConditionKey,
                    new FloatPropertyValue(Clamp01(condition))));
        }

        /// <summary>
        /// Condition the wear shader should render for <paramref name="car"/>.
        /// The visual value can only make a car look WORSE than its
        /// mechanical condition (the lower of the two wins); a car the
        /// player has let fall apart mechanically never renders pristine.
        /// Ghost preview cars keep vanilla behavior.
        /// </summary>
        public static float EffectiveWearCondition(float mechanicalCondition, Car car)
        {
            if (car == null || car.ghost || car.KeyValueObject == null)
            {
                return mechanicalCondition;
            }

            var value = car.KeyValueObject[VisualConditionKey];
            if (value.IsNull)
            {
                value = car.KeyValueObject[LegacyVisualConditionKey];
            }

            if (value.IsNull)
            {
                return mechanicalCondition;
            }

            var visual = Clamp01(value.FloatValue);
            return visual < mechanicalCondition ? visual : mechanicalCondition;
        }

        /// <summary>
        /// Maps a 0..1 random roll onto the configured spawn range. Bounds
        /// are clamped to 0..1 and normalized if the user entered them
        /// reversed (min &gt; max), so any settings combination yields a
        /// valid condition. Pure function — the roll is injected so the
        /// mapping is unit-testable without the engine's RNG.
        /// </summary>
        public static float ComputeSpawnCondition(float min, float max, float roll01)
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

        // Local clamp instead of Mathf.Clamp01 keeps the pure helpers free
        // of engine types so they run under the plain .NET test host.
        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
