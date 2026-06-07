using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using Game.AccessControl;
using Game.State;
using HarmonyLib;
using Helpers;
using KeyValue.Runtime;
using Model;
using Model.Definition;
using Model.Definition.Data;
using RollingStock.Controls;
using UnityEngine;
using DefinitionComponent = Model.Definition.Component;

namespace FUSE.Patches
{
    /// <summary>
    /// Adds the missing runtime context that vanilla scenery component setup
    /// omits: model AnimationMap/MaterialMap lookup and KeyValue observers.
    /// Keep this isolated from diagnostics and editor tooling; it is a runtime
    /// compatibility patch for animated/toggleable scenery definitions.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetInstance), "SetupComponents")]
    internal static class FuseSceneryAnimationSetupComponentsPatch
    {
        private static readonly FieldInfo DefinitionField =
            typeof(SceneryAssetInstance).GetField("_definition", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ModelField =
            typeof(SceneryAssetInstance).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Type AnimationMapType = Type.GetType("AssetPack.Common.AnimationMap, AssetPack.Common");

        private static readonly Type MaterialMapType = Type.GetType("AssetPack.Common.MaterialMap, AssetPack.Common");

        private static readonly FieldInfo ContextAnimationMapField =
            typeof(ComponentSetup.Context).GetField("AnimationMap");

        private static readonly FieldInfo ContextMaterialMapField =
            typeof(ComponentSetup.Context).GetField("MaterialMap");

        private static readonly Dictionary<SceneryAssetInstance, SceneryKeyValueState> KeyValueStates =
            new Dictionary<SceneryAssetInstance, SceneryKeyValueState>();

        private static bool Prefix(SceneryAssetInstance __instance, ComponentLifetime lifetime)
        {
            if (__instance == null)
            {
                return false;
            }

            if (DefinitionField == null || ModelField == null)
            {
                FuseLog.Warning("FUSE scenery animation fix could not find SceneryAssetInstance private fields; falling back to vanilla setup.");
                return true;
            }

            var definition = DefinitionField.GetValue(__instance) as SceneryDefinition;
            if (definition == null)
            {
                return false;
            }

            var model = ModelField.GetValue(__instance) as GameObject;
            var modelRoot = model != null ? model.transform : null;
            var lookupRoot = lifetime == ComponentLifetime.Model && modelRoot != null
                ? modelRoot
                : __instance.transform;

            var setupContext = CreateSetupContext(lookupRoot, __instance.transform);

            DisposeObservers(__instance, lifetime);

            Action<string, Action<Value>> observeProperty = (key, observer) =>
                ObserveSceneryProperty(__instance, lifetime, key, observer);

            foreach (var component in definition.EnabledComponentsForLifetime(lifetime))
            {
                SetupComponent(__instance, lifetime, modelRoot, component, setupContext, observeProperty);
            }

            return false;
        }

        private static void SetupComponent(
            SceneryAssetInstance instance,
            ComponentLifetime lifetime,
            Transform modelRoot,
            DefinitionComponent component,
            ComponentSetup.Context setupContext,
            Action<string, Action<Value>> observeProperty)
        {
            Transform parent;
            switch (lifetime)
            {
                case ComponentLifetime.Static:
                    parent = instance.transform;
                    break;
                case ComponentLifetime.Model:
                    if (modelRoot == null)
                    {
                        return;
                    }

                    parent = modelRoot.ResolveTransform(component.Parent, defaultReturnsReceiver: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
            }

            try
            {
                ComponentSetup.Setup(instance.identifier, component, setupContext, parent, observeProperty, null);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE scenery animation fix failed to setup component '{component?.Name ?? "<null>"}' " +
                    $"on scenery '{FormatSceneryName(instance)}'", ex);
            }
        }

        private static ComponentSetup.Context CreateSetupContext(Transform preferredRoot, Transform fallbackRoot)
        {
            object boxedContext = default(ComponentSetup.Context);

            SetContextField(
                boxedContext,
                ContextAnimationMapField,
                FindComponent(preferredRoot, fallbackRoot, AnimationMapType));
            SetContextField(
                boxedContext,
                ContextMaterialMapField,
                FindComponent(preferredRoot, fallbackRoot, MaterialMapType));

            return (ComponentSetup.Context)boxedContext;
        }

        private static UnityEngine.Component FindComponent(Transform preferredRoot, Transform fallbackRoot, Type componentType)
        {
            if (componentType == null)
            {
                return null;
            }

            var result = preferredRoot != null
                ? preferredRoot.GetComponentInChildren(componentType, true)
                : null;
            return result != null || fallbackRoot == null
                ? result
                : fallbackRoot.GetComponentInChildren(componentType, true);
        }

        private static void SetContextField(object boxedContext, FieldInfo field, object value)
        {
            if (field == null || value == null)
            {
                return;
            }

            try
            {
                field.SetValue(boxedContext, value);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE scenery animation fix could not set ComponentSetup.Context.{field.Name}: " +
                    ex.GetBaseException().Message);
            }
        }

        private static void ObserveSceneryProperty(
            SceneryAssetInstance instance,
            ComponentLifetime lifetime,
            string key,
            Action<Value> observer)
        {
            if (instance == null || string.IsNullOrWhiteSpace(key) || observer == null)
            {
                return;
            }

            try
            {
                var keyValueObject = EnsureKeyValueObject(instance);
                if (keyValueObject == null)
                {
                    return;
                }

                var subscription = keyValueObject.Observe(key, observer, true);
                GetState(instance).Add(lifetime, subscription);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE scenery animation fix failed to observe key '{key}' on scenery '{FormatSceneryName(instance)}'", ex);
            }
        }

        private static KeyValueObject EnsureKeyValueObject(SceneryAssetInstance instance)
        {
            // Invariant: the KeyValueObject (and its StateManager registration) is hosted on
            // the scenery's PERSISTENT root GameObject, never inside the streamed model. That
            // is what lets save-state survive the model streaming out and back: the value
            // persists on the root while the model is gone, and Model-lifetime observers
            // re-subscribe to it on reload (see WillUnloadModel + SetupComponents). Terrain
            // masks follow this same persistent-world-contribution principle, via
            // MapAPI.DecoupleAttachedMapMasks.
            var keyValueObject = instance.GetComponent<KeyValueObject>();
            if (keyValueObject == null)
            {
                keyValueObject = instance.gameObject.AddComponent<KeyValueObject>();
            }

            if (keyValueObject.RegisteredId == null && StateManager.Shared != null)
            {
                var objectId = BuildPropertyObjectId(instance);
                StateManager.Shared.RegisterPropertyObject(
                    objectId,
                    keyValueObject,
                    AuthorizationRequirement.MinimumLevelPassenger,
                    null);
                GetState(instance).RegisteredObjectId = objectId;

                if (FuseSettings.VerboseApplyReportDetails)
                {
                    FuseLog.Info(
                        $"FUSE scenery animation fix registered KeyValueObject '{objectId}' " +
                        $"for scenery '{FormatSceneryName(instance)}'.");
                }
            }

            return keyValueObject;
        }

        private static SceneryKeyValueState GetState(SceneryAssetInstance instance)
        {
            SceneryKeyValueState state;
            if (!KeyValueStates.TryGetValue(instance, out state))
            {
                state = new SceneryKeyValueState();
                KeyValueStates[instance] = state;
            }

            return state;
        }

        internal static void TearDown(SceneryAssetInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            SceneryKeyValueState state;
            if (!KeyValueStates.TryGetValue(instance, out state))
            {
                return;
            }

            state.DisposeObservers();

            var keyValueObject = instance.GetComponent<KeyValueObject>();
            if (keyValueObject != null &&
                !string.IsNullOrWhiteSpace(state.RegisteredObjectId) &&
                string.Equals(keyValueObject.RegisteredId, state.RegisteredObjectId, StringComparison.Ordinal) &&
                StateManager.Shared != null)
            {
                StateManager.Shared.UnregisterPropertyObject(state.RegisteredObjectId);
            }

            KeyValueStates.Remove(instance);
        }

        internal static void WillUnloadModel(SceneryAssetInstance instance)
        {
            DisposeObservers(instance, ComponentLifetime.Model);
        }

        private static void DisposeObservers(SceneryAssetInstance instance, ComponentLifetime lifetime)
        {
            if (instance == null)
            {
                return;
            }

            SceneryKeyValueState state;
            if (KeyValueStates.TryGetValue(instance, out state))
            {
                state.DisposeObservers(lifetime);
            }
        }

        private static string BuildPropertyObjectId(SceneryAssetInstance instance)
        {
            var name = !string.IsNullOrWhiteSpace(instance.name) ? instance.name : instance.identifier;
            if (instance.GetComponent<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>() != null)
            {
                return "fuse.scenery." + SanitizeToken(name);
            }

            var position = instance.transform.position;
            return string.Format(
                "fuse.scenery.{0}.{1}.{2}.{3}",
                SanitizeToken(instance.identifier),
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.y),
                Mathf.RoundToInt(position.z));
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var chars = new List<char>(value.Length);
            foreach (var character in value)
            {
                chars.Add(char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.'
                    ? character
                    : '_');
            }

            return chars.Count == 0 ? "unnamed" : new string(chars.ToArray());
        }

        private static string FormatSceneryName(SceneryAssetInstance instance)
        {
            if (instance == null)
            {
                return "<null>";
            }

            return $"{instance.name ?? "<unnamed>"}:{instance.identifier ?? "<null>"}";
        }

        private sealed class SceneryKeyValueState
        {
            private readonly Dictionary<ComponentLifetime, List<IDisposable>> _observers =
                new Dictionary<ComponentLifetime, List<IDisposable>>();

            public string RegisteredObjectId { get; set; }

            public void Add(ComponentLifetime lifetime, IDisposable observer)
            {
                if (observer == null)
                {
                    return;
                }

                List<IDisposable> observers;
                if (!_observers.TryGetValue(lifetime, out observers))
                {
                    observers = new List<IDisposable>();
                    _observers[lifetime] = observers;
                }

                observers.Add(observer);
            }

            public void DisposeObservers()
            {
                foreach (var pair in _observers)
                {
                    DisposeObserverList(pair.Value);
                }

                _observers.Clear();
            }

            public void DisposeObservers(ComponentLifetime lifetime)
            {
                List<IDisposable> observers;
                if (!_observers.TryGetValue(lifetime, out observers))
                {
                    return;
                }

                DisposeObserverList(observers);
                _observers.Remove(lifetime);
            }

            private static void DisposeObserverList(IEnumerable<IDisposable> observers)
            {
                foreach (var observer in observers)
                {
                    try
                    {
                        observer?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            "FUSE scenery animation fix could not dispose a KeyValue observer: " +
                            ex.GetBaseException().Message);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(KeyValueBoolAnimator), "Awake")]
    internal static class FuseSceneryKeyValueBoolAnimatorAwakePatch
    {
        private static readonly FieldInfo PlayableField =
            typeof(KeyValueBoolAnimator).GetField("_playable", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo AnimationClipField =
            typeof(KeyValueBoolAnimator).GetField("animationClip", BindingFlags.Instance | BindingFlags.Public);

        private static readonly Type AnimatorType = Type.GetType("UnityEngine.Animator, UnityEngine.AnimationModule");

        private static readonly MethodInfo PlayableGraphAdapterMethod =
            Type.GetType("Helpers.Animation.AnimatorExtensionForPlayableGraph, Assembly-CSharp")
                ?.GetMethod("PlayableGraphAdapter", BindingFlags.Static | BindingFlags.Public);

        private static bool Prefix(KeyValueBoolAnimator __instance)
        {
            if (!IsSceneryAnimator(__instance) || GetAnimationClip(__instance) != null)
            {
                return true;
            }

            return false;
        }

        internal static void EnsurePlayable(KeyValueBoolAnimator instance)
        {
            var animationClip = GetAnimationClip(instance);
            if (!IsSceneryAnimator(instance) || animationClip == null || PlayableField == null)
            {
                return;
            }

            try
            {
                if (PlayableField.GetValue(instance) != null)
                {
                    return;
                }

                if (AnimatorType == null || PlayableGraphAdapterMethod == null)
                {
                    FuseLog.Warning("FUSE scenery animation fix could not find Unity Animator or PlayableGraphAdapter types.");
                    return;
                }

                var animator = instance.GetComponent(AnimatorType);
                if (animator == null)
                {
                    FuseLog.Warning(
                        $"FUSE scenery animation fix could not create playable for '{FormatPath(instance.transform)}' because no Animator was found.");
                    return;
                }

                var adapter = PlayableGraphAdapterMethod.Invoke(null, new[] { animator });
                var addPlayable = adapter?.GetType().GetMethod("AddPlayable", new[] { AnimationClipField.FieldType });
                var playable = addPlayable?.Invoke(adapter, new[] { animationClip });
                if (playable == null)
                {
                    FuseLog.Warning(
                        $"FUSE scenery animation fix could not create playable for '{FormatPath(instance.transform)}'.");
                    return;
                }

                PlayableField.SetValue(instance, playable);
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE scenery animation fix could not create playable for '{FormatPath(instance.transform)}'", ex);
            }
        }

        internal static bool IsSceneryAnimator(MonoBehaviour component)
        {
            return component != null && component.GetComponentInParent<SceneryAssetInstance>() != null;
        }

        private static object GetAnimationClip(KeyValueBoolAnimator instance)
        {
            return instance != null ? AnimationClipField?.GetValue(instance) : null;
        }

        private static string FormatPath(Transform transform)
        {
            return FuseKeyValueBoolAnimatorOnEnableDiagnosticPatch.FormatTransformPath(transform);
        }
    }

    [HarmonyPatch(typeof(KeyValueBoolAnimator), "OnEnableWithProperties")]
    internal static class FuseSceneryKeyValueBoolAnimatorOnEnablePatch
    {
        private static void Prefix(KeyValueBoolAnimator __instance)
        {
            FuseSceneryKeyValueBoolAnimatorAwakePatch.EnsurePlayable(__instance);
        }
    }

    [HarmonyPatch(typeof(SceneryAssetInstance), "WillUnloadModel")]
    internal static class FuseSceneryAnimationWillUnloadModelPatch
    {
        private static void Prefix(SceneryAssetInstance __instance)
        {
            try
            {
                FuseSceneryAnimationSetupComponentsPatch.WillUnloadModel(__instance);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery animation fix failed during model unload", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SceneryAssetInstance), "TearDown")]
    internal static class FuseSceneryAnimationTearDownPatch
    {
        private static void Prefix(SceneryAssetInstance __instance)
        {
            try
            {
                FuseSceneryAnimationSetupComponentsPatch.TearDown(__instance);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery animation fix failed during teardown", ex);
            }
        }
    }
}
