using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using FUSE.Infrastructure;
using HarmonyLib;
using Model;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Runtime registry used by the RailLoader compatibility context. Legacy library
    /// mods register JSON discriminator values and component builders during startup;
    /// FUSE forwards those registrations into the live game instead of merely allowing
    /// the plugin assembly to load.
    /// </summary>
    internal static class FuseLegacyTypeRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, Dictionary<string, Type>> Registrations =
            new Dictionary<Type, Dictionary<string, Type>>();
        private static int _registrationCount;

        internal static bool HasRegistrations => Volatile.Read(ref _registrationCount) > 0;

        internal static void RegisterSubType(Type baseType, string identifier, Type implementationType)
        {
            if (baseType == null)
            {
                throw new ArgumentNullException(nameof(baseType));
            }

            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException("A legacy subtype identifier is required.", nameof(identifier));
            }

            if (implementationType == null)
            {
                throw new ArgumentNullException(nameof(implementationType));
            }

            if (!baseType.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException(
                    $"Legacy subtype '{implementationType.FullName}' is not assignable to '{baseType.FullName}'.",
                    nameof(implementationType));
            }

            Type replaced = null;
            lock (Gate)
            {
                if (!Registrations.TryGetValue(baseType, out var byIdentifier))
                {
                    byIdentifier = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                    Registrations[baseType] = byIdentifier;
                }

                byIdentifier.TryGetValue(identifier.Trim(), out replaced);
                byIdentifier[identifier.Trim()] = implementationType;
                if (replaced == null)
                {
                    Interlocked.Increment(ref _registrationCount);
                }
            }

            if (replaced != null && replaced != implementationType)
            {
                FuseLog.Warning(
                    $"FUSE legacy subtype '{identifier}' for '{baseType.FullName}' was re-registered from " +
                    $"'{replaced.FullName}' to '{implementationType.FullName}'. The latest registration will be used.");
            }
            else
            {
                FuseLog.Info(
                    $"FUSE registered legacy subtype '{identifier}' for '{baseType.FullName}' as " +
                    $"'{implementationType.FullName}'.");
            }
        }

        internal static void RegisterComponent(Type componentType, Type builderType, string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Legacy component kinds must not be blank.", nameof(kind));
            }

            if (componentType == null)
            {
                throw new ArgumentNullException(nameof(componentType));
            }

            if (!typeof(Model.Definition.Component).IsAssignableFrom(componentType))
            {
                throw new ArgumentException(
                    $"Legacy component '{componentType.FullName}' does not derive from Model.Definition.Component.",
                    nameof(componentType));
            }

            if (builderType == null)
            {
                throw new ArgumentNullException(nameof(builderType));
            }

            if (!typeof(IComponentBuilder).IsAssignableFrom(builderType))
            {
                throw new ArgumentException(
                    $"Legacy component builder '{builderType.FullName}' does not implement Model.IComponentBuilder.",
                    nameof(builderType));
            }

            var builder = Activator.CreateInstance(builderType);
            if (!(builder is IComponentBuilder typedBuilder))
            {
                throw new InvalidOperationException(
                    $"Legacy component builder '{builderType.FullName}' could not be created as Model.IComponentBuilder.");
            }

            var prepare = AccessTools.Method(typeof(ComponentFactory), "PrepareBuildersIfNeeded");
            var buildersField = AccessTools.Field(typeof(ComponentFactory), "_builders");
            if (prepare == null || buildersField == null)
            {
                throw new MissingMemberException(
                    typeof(ComponentFactory).FullName,
                    "PrepareBuildersIfNeeded/_builders");
            }

            prepare.Invoke(null, null);
            var builders = buildersField.GetValue(null) as IDictionary;
            if (builders == null)
            {
                throw new InvalidOperationException("The game's component-builder registry was unavailable after initialization.");
            }

            builders[componentType] = typedBuilder;
            RegisterSubType(typeof(Model.Definition.Component), kind, componentType);
            Loading.FuseAssetPackRegistry.OnLegacyComponentKindRegistered(kind);
            FuseLog.Info(
                $"FUSE registered legacy component kind '{kind}' with builder '{builderType.FullName}'.");
        }

        internal static IReadOnlyList<KeyValuePair<string, Type>> Snapshot(Type baseType)
        {
            if (baseType == null)
            {
                return Array.Empty<KeyValuePair<string, Type>>();
            }

            lock (Gate)
            {
                if (!Registrations.TryGetValue(baseType, out var byIdentifier))
                {
                    return Array.Empty<KeyValuePair<string, Type>>();
                }

                return byIdentifier.ToArray();
            }
        }
    }

    /// <summary>
    /// Adds compatibility registrations to JsonSubTypes at lookup time. Keeping the
    /// dynamic entries outside JsonSubTypes' private cache means registrations made by
    /// a late-starting hosted plugin become visible immediately.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseLegacyJsonSubtypeRegistryPatch
    {
        private static Type _knownSubTypeAttributeType;
        private static ConstructorInfo _knownSubTypeConstructor;
        private static PropertyInfo _associatedValueProperty;

        private static MethodInfo TargetMethod()
        {
            var jsonSubtypes =
                AccessTools.TypeByName("JsonSubTypes.JsonSubtypes") ??
                Type.GetType("JsonSubTypes.JsonSubtypes, JsonSubTypes", throwOnError: false);
            if (jsonSubtypes == null)
            {
                return null;
            }

            _knownSubTypeAttributeType = jsonSubtypes.GetNestedType(
                "KnownSubTypeAttribute",
                BindingFlags.Public | BindingFlags.NonPublic);
            _knownSubTypeConstructor = _knownSubTypeAttributeType?.GetConstructor(new[] { typeof(Type), typeof(object) });
            _associatedValueProperty = _knownSubTypeAttributeType?.GetProperty(
                "AssociatedValue",
                BindingFlags.Instance | BindingFlags.Public);
            // JsonSubTypes exposes both GetAttributes(Type) and
            // GetAttributes<T>(Type). AccessTools.Method sees the identical
            // parameter lists as ambiguous, so select the non-generic method
            // explicitly. An ambiguous target makes Harmony detach the patch
            // even though the runtime DLL is otherwise compatible.
            return jsonSubtypes
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .SingleOrDefault(method =>
                    string.Equals(method.Name, "GetAttributes", StringComparison.Ordinal) &&
                    !method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(Type));
        }

        private static void Postfix(Type typeInfo, ref IEnumerable<object> __result)
        {
            if (!FuseLegacyTypeRegistry.HasRegistrations)
            {
                return;
            }

            var registrations = FuseLegacyTypeRegistry.Snapshot(typeInfo);
            if (registrations.Count == 0 || _knownSubTypeConstructor == null)
            {
                return;
            }

            var replacementIds = new HashSet<string>(
                registrations.Select(pair => pair.Key),
                StringComparer.OrdinalIgnoreCase);
            var combined = (__result ?? Enumerable.Empty<object>())
                .Where(attribute => !IsReplacedKnownSubType(attribute, replacementIds))
                .ToList();

            foreach (var registration in registrations)
            {
                combined.Add(_knownSubTypeConstructor.Invoke(new object[]
                {
                    registration.Value,
                    registration.Key
                }));
            }

            __result = combined;
        }

        private static bool IsReplacedKnownSubType(object attribute, HashSet<string> replacementIds)
        {
            if (attribute == null || _knownSubTypeAttributeType == null ||
                !_knownSubTypeAttributeType.IsInstanceOfType(attribute))
            {
                return false;
            }

            var associatedValue = _associatedValueProperty?.GetValue(attribute, null) as string;
            return associatedValue != null && replacementIds.Contains(associatedValue);
        }
    }
}
