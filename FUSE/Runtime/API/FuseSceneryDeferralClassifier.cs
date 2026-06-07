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
    /// camera and bake their terrain as each piece streams in. Once loaded they are held
    /// resident (never unloaded) by FuseSceneryCullingDebouncePatch as a safety net, while
    /// MapAPI.DecoupleAttachedMapMasks moves the terrain mask onto a persistent object — so
    /// the building and its baked terrain mask survive the player moving or teleporting away
    /// and back.
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
        /// True when the scenery identified by <paramref name="assetIdentifier"/> is a
        /// plain static/visual asset whose activation can be deferred. Fail-safe:
        /// returns false whenever the definition cannot be resolved or inspected.
        /// </summary>
        internal static bool CanDefer(string assetIdentifier)
        {
            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return false;
            }

            var manager = SceneryAssetManager.Shared;
            if (manager == null)
            {
                return false;
            }

            SceneryDefinition definition;
            try
            {
                if (!manager.TryGetSceneryDefinition(assetIdentifier, out definition) || definition == null)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred-scenery classifier could not resolve definition '{assetIdentifier}'", ex);
                return false;
            }

            try
            {
                return !DeclaresEagerOnlyComponent(definition, ComponentLifetime.Static)
                    && !DeclaresEagerOnlyComponent(definition, ComponentLifetime.Model);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred-scenery classifier could not inspect '{assetIdentifier}'", ex);
                return false;
            }
        }

        /// <summary>
        /// True when the scenery identified by <paramref name="assetIdentifier"/> declares a
        /// map-mask component. Used to tag mask-bearing scenery at creation so the cull
        /// debounce can hold it resident — masked scenery must never unload, because the game
        /// fails to reload it after a teleport (and a missed reload also drops its baked
        /// terrain mask). Fail-safe: false when the definition cannot be resolved/inspected.
        /// </summary>
        internal static bool HasMaskComponent(string assetIdentifier)
        {
            if (string.IsNullOrWhiteSpace(assetIdentifier))
            {
                return false;
            }

            var manager = SceneryAssetManager.Shared;
            if (manager == null)
            {
                return false;
            }

            SceneryDefinition definition;
            try
            {
                if (!manager.TryGetSceneryDefinition(assetIdentifier, out definition) || definition == null)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred-scenery classifier could not resolve definition '{assetIdentifier}' for mask check", ex);
                return false;
            }

            try
            {
                return DeclaresMaskComponent(definition, ComponentLifetime.Static)
                    || DeclaresMaskComponent(definition, ComponentLifetime.Model);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE deferred-scenery classifier could not inspect '{assetIdentifier}' for mask components", ex);
                return false;
            }
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
            // as it loads, and FuseSceneryCullingDebouncePatch then holds it resident so
            // it never unloads (and therefore never has to reload). Only stateful scenery
            // stays eager.
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
    }
}
