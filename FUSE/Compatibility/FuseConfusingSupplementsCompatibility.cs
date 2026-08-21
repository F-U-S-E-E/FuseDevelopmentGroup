using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using Model;

namespace FUSE.Compatibility
{
    /// <summary>
    /// Owns the legacy component discriminator and saved-property contracts that
    /// FUSE replaces when the retired Confusing Supplements package is suppressed.
    /// </summary>
    internal static class FuseConfusingSupplementsCompatibility
    {
        private static readonly object Gate = new object();
        private static bool _initialized;

        internal static IReadOnlyList<string> ImplementedComponentKinds { get; } = new[]
        {
            FuseConfusingSupplementsBodygroupsComponent.ComponentKind,
            FuseConfusingSupplementsDestinationSignComponent.ComponentKind,
            FuseConfusingSupplementsLabelPrinterComponent.ComponentKind,
            FuseConfusingSupplementsLiveryComponent.ComponentKind,
            FuseConfusingSupplementsRefillerComponent.ComponentKind
        };

        internal static void Initialize()
        {
            lock (Gate)
            {
                if (_initialized)
                {
                    return;
                }

                RegisterComponent(
                    typeof(FuseConfusingSupplementsBodygroupsComponent),
                    typeof(FuseConfusingSupplementsBodygroupsBuilder),
                    FuseConfusingSupplementsBodygroupsComponent.ComponentKind);
                RegisterComponent(
                    typeof(FuseConfusingSupplementsDestinationSignComponent),
                    typeof(FuseConfusingSupplementsDestinationSignBuilder),
                    FuseConfusingSupplementsDestinationSignComponent.ComponentKind);
                RegisterComponent(
                    typeof(FuseConfusingSupplementsLabelPrinterComponent),
                    typeof(FuseConfusingSupplementsLabelPrinterBuilder),
                    FuseConfusingSupplementsLabelPrinterComponent.ComponentKind);
                RegisterComponent(
                    typeof(FuseConfusingSupplementsLiveryComponent),
                    typeof(FuseConfusingSupplementsLiveryBuilder),
                    FuseConfusingSupplementsLiveryComponent.ComponentKind);
                RegisterComponent(
                    typeof(FuseConfusingSupplementsRefillerComponent),
                    typeof(FuseConfusingSupplementsRefillerBuilder),
                    FuseConfusingSupplementsRefillerComponent.ComponentKind);
                AppendTrainmasterPrefixes(
                    FuseConfusingSupplementsBodygroupsBuilder.SavedPropertyPrefix,
                    FuseConfusingSupplementsDestinationSignBuilder.SavedPropertyPrefix,
                    FuseConfusingSupplementsLabelPrinterBuilder.SavedPropertyPrefix,
                    FuseConfusingSupplementsLiveryBuilder.SavedPropertyKey);
                _initialized = true;
            }
        }

        internal static void Reset()
        {
            lock (Gate)
            {
                _initialized = false;
            }
        }

        private static void RegisterComponent(Type componentType, Type builderType, string kind)
        {
            try
            {
                FuseLegacyTypeRegistry.RegisterComponent(componentType, builderType, kind);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE could not register its replacement for legacy component '{kind}': " +
                    $"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
            }
        }

        private static void AppendTrainmasterPrefixes(params string[] requiredPrefixes)
        {
            try
            {
                var field = typeof(Car).GetField(
                    "TrainmasterPrefixes",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var current = field?.GetValue(null) as string[];
                if (field == null || current == null)
                {
                    FuseLog.Warning(
                        "FUSE could not extend the car property access list for legacy Confusing Supplements customization.");
                    return;
                }

                var merged = current
                    .Concat(requiredPrefixes ?? Array.Empty<string>())
                    .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!merged.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
                {
                    field.SetValue(null, merged);
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not extend the car property access list for legacy Confusing Supplements " +
                    $"customization: {ex.GetBaseException().Message}");
            }
        }
    }
}
