using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using Helpers;
using Model.Definition;
using Model.Definition.Data;
using DefinitionComponent = Model.Definition.Component;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Decides whether a scenery asset is eligible to have its activation deferred
    /// off the map-load critical path (see <c>FuseDeferredSceneryActivator</c>).
    ///
    /// Only <b>stateful</b> scenery must stay eager (activated synchronously during
    /// apply): components that register KeyValue/StateManager property objects at
    /// activation (animated/toggleable props). Keeping these eager guarantees a save
    /// restore never races a not-yet-activated property object.
    ///
    /// <b>Mask-bearing</b> scenery USED to be forced eager too (so the one-shot map-load
    /// terrain bake saw its masks), but eager activation runs before the gameplay camera
    /// exists and registered the cull sphere camera-less, leaving masked objects stuck
    /// (never streaming in — confirmed on a lower-end PC where masked buildings never
    /// appeared). Masks now defer like plain scenery, so they activate against a live
    /// camera and bake their terrain as each piece streams in. The building then culls
    /// like any other scenery; MapAPI.DecoupleAttachedMapMasks moves the terrain mask
    /// onto a persistent object, so the baked terrain mask survives the player moving
    /// or teleporting away and back even while the model itself is unloaded.
    ///
    /// Everything else — plain meshes, materials, colorizers, ambient VFX/audio —
    /// is safe to defer; the worst case is brief cosmetic pop-in, exactly how the
    /// game already streams its own distant scenery.
    ///
    /// The classifier resolves the typed <see cref="SceneryDefinition"/> from
    /// <see cref="SceneryAssetManager"/> and inspects each declared component's
    /// runtime type name. The decision is fail-safe: if the definition cannot be
    /// resolved or inspected, the scenery is treated as NOT deferrable (eager).
    /// </summary>
    internal static class FuseSceneryDeferralClassifier
    {
        private static readonly object ClassificationCacheLock = new object();
        private static readonly Dictionary<string, Classification> ClassificationCache =
            new Dictionary<string, Classification>(StringComparer.OrdinalIgnoreCase);

        // Matched (case-insensitive) against each component's Type.FullName. FullName
        // is used so both the type name and its namespace participate (e.g.
        // "Model.Definition.Components.MapMasks.RectangleMapMaskComponent").

        // Mask components — must be live before the terrain bake.
        private static readonly string[] MaskTypeNameFragments = { "MapMask" };

        // Stateful components — register persistent KeyValue/StateManager property
        // objects when SetupComponents runs at activation (see
        // FuseSceneryAnimationSetupComponentsPatch). Deferring these could let a save
        // restore read state before the property object is registered. This list is
        // intentionally narrow and high-confidence; expand it if in-game inspection
        // (fuse scenery component listing / FuseSceneryDebugOverlay) reveals a
        // stateful component type not covered here.
        private static readonly string[] StatefulTypeNameFragments =
        {
            "KeyValue",
            "Animator",
            "Animation"
        };

        /// <summary>
        /// Shared fail-safe resolution of a <see cref="SceneryDefinition"/> from
        /// <see cref="SceneryAssetManager"/>. A null/empty id, a missing manager, a missing
        /// definition, or any thrown lookup all return false (callers treat that as "cannot
        /// classify"). <paramref name="operation"/> tags the failure log.
        /// </summary>
        private static bool TryResolveDefinition(string assetIdentifier, string operation, out SceneryDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return false;
            }

            var manager = SceneryAssetManager.Shared;
            if (manager == null)
            {
                return false;
            }

            try
            {
                if (!manager.TryGetSceneryDefinition(assetIdentifier, out definition) || definition == null)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred-scenery classifier could not resolve definition '{assetIdentifier}' for {operation}", ex);
                return false;
            }

            return true;
        }

        /// <summary>
        /// True when the scenery identified by <paramref name="assetIdentifier"/> is a
        /// plain static/visual asset whose activation can be deferred. Fail-safe:
        /// returns false whenever the definition cannot be resolved or inspected.
        /// </summary>
        internal static bool CanDefer(string assetIdentifier)
        {
            return TryGetClassification(assetIdentifier, out var classification) &&
                   classification.CanDefer;
        }

        /// <summary>
        /// True when the scenery identified by <paramref name="assetIdentifier"/> declares a
        /// map-mask component. Used to tag mask-bearing scenery at creation so the load
        /// throttle never defers it — its first load is what decouples the welded terrain
        /// masks onto persistent objects, so queueing it behind plain scenery leaves visibly
        /// wrong ground. Fail-safe: false when the definition cannot be resolved/inspected.
        /// </summary>
        internal static bool HasMaskComponent(string assetIdentifier)
        {
            return TryGetClassification(assetIdentifier, out var classification) &&
                   classification.HasMask;
        }

        internal static void InvalidateCache()
        {
            lock (ClassificationCacheLock)
            {
                ClassificationCache.Clear();
            }
        }

        private static bool TryGetClassification(
            string assetIdentifier,
            out Classification classification)
        {
            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                classification = default;
                return false;
            }

            lock (ClassificationCacheLock)
            {
                if (ClassificationCache.TryGetValue(assetIdentifier, out classification))
                {
                    return classification.Resolved;
                }
            }

            if (!TryResolveDefinition(assetIdentifier, "classification", out var definition))
            {
                classification = default;
                lock (ClassificationCacheLock)
                {
                    ClassificationCache[assetIdentifier] = classification;
                }
                return false;
            }

            try
            {
                classification = new Classification(
                    resolved: true,
                    canDefer:
                        !DeclaresEagerOnlyComponent(definition, ComponentLifetime.Static) &&
                        !DeclaresEagerOnlyComponent(definition, ComponentLifetime.Model),
                    hasMask:
                        DeclaresMaskComponent(definition, ComponentLifetime.Static) ||
                        DeclaresMaskComponent(definition, ComponentLifetime.Model));
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE deferred-scenery classifier could not inspect '{assetIdentifier}'",
                    ex);
                classification = default;
            }

            lock (ClassificationCacheLock)
            {
                ClassificationCache[assetIdentifier] = classification;
            }

            return classification.Resolved;
        }

        private static bool DeclaresMaskComponent(SceneryDefinition definition, ComponentLifetime lifetime)
        {
            IEnumerable<DefinitionComponent> components;
            try
            {
                components = definition.EnabledComponentsForLifetime(lifetime);
            }
            catch
            {
                return false;
            }

            if (components == null)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                if (IsMaskTypeName(component.GetType().FullName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DeclaresEagerOnlyComponent(SceneryDefinition definition, ComponentLifetime lifetime)
        {
            IEnumerable<DefinitionComponent> components;
            try
            {
                components = definition.EnabledComponentsForLifetime(lifetime);
            }
            catch
            {
                // If we cannot enumerate the components we cannot prove the scenery is
                // safe to defer — treat it as eager-only.
                return true;
            }

            if (components == null)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                if (IsEagerOnlyTypeName(component.GetType().FullName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Pure, unit-testable core: returns true when a component type name belongs to
        /// a scenery that must be activated eagerly (mask or stateful). An empty/unknown
        /// name is treated as eager-only (fail-safe).
        /// </summary>
        internal static bool IsEagerOnlyTypeName(string componentTypeFullName)
        {
            if (string.IsNullOrEmpty(componentTypeFullName))
            {
                return true;
            }

            // Mask-bearing scenery used to be forced eager so the one-shot map-load
            // terrain bake saw its masks. But eager activation happens during apply,
            // before the gameplay camera exists, which registers the cull sphere
            // camera-less and leaves the object STUCK — it never streams in (confirmed:
            // masked roundhouse pieces sat at band 3 while the unmasked office next to
            // them loaded). Masks now defer like plain scenery, so they activate against
            // a live camera and stream in correctly; each masked piece bakes its terrain
            // as it loads, and the decoupled standalone mask keeps that terrain applied
            // across later unloads/reloads. Only stateful scenery stays eager.
            return ContainsAny(componentTypeFullName, StatefulTypeNameFragments);
        }

        /// <summary>Pure, unit-testable: true when the type name is a map-mask component.</summary>
        internal static bool IsMaskTypeName(string componentTypeFullName)
        {
            return !string.IsNullOrEmpty(componentTypeFullName)
                && ContainsAny(componentTypeFullName, MaskTypeNameFragments);
        }

        private static bool ContainsAny(string value, string[] fragments)
        {
            for (var index = 0; index < fragments.Length; index++)
            {
                if (value.IndexOf(fragments[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Classification
        {
            internal Classification(bool resolved, bool canDefer, bool hasMask)
            {
                Resolved = resolved;
                CanDefer = canDefer;
                HasMask = hasMask;
            }

            internal bool Resolved { get; }

            internal bool CanDefer { get; }

            internal bool HasMask { get; }
        }
    }
}
