using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using FUSE.Runtime.API;

namespace FUSE.Loading
{
    internal sealed class FuseTrackAssemblyBridgeApplySummary
    {
        public FuseTrackAssemblyBridgeApplySummary(
            bool nativeGraphMutationsApplied,
            IReadOnlyDictionary<string, string> failures)
        {
            NativeGraphMutationsApplied = nativeGraphMutationsApplied;
            Failures = failures ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static FuseTrackAssemblyBridgeApplySummary Empty { get; } =
            new FuseTrackAssemblyBridgeApplySummary(
                nativeGraphMutationsApplied: false,
                failures: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public bool NativeGraphMutationsApplied { get; }

        public IReadOnlyDictionary<string, string> Failures { get; }

        public bool HasFailures => Failures.Count > 0;
    }

    internal static class FuseTrackAssemblyBridgeHost
    {
        private const string SourceExtensionKey = "trackAssembly.source";
        private const string BridgeTypeName = "FUSE.Runtime.TrackAssembly.FuseTrackAssemblyPackageBridge";
        private const string ApplyLoadedPackageMethodName = "ApplyLoadedPackage";
        private const string RetainLoadedPackageVisualsMethodName = "RetainLoadedPackageVisuals";
        private const string FaultStage = "trackAssembly bridge";

        public static FuseTrackAssemblyBridgeApplySummary ApplyLoadedPackages(
            IEnumerable<string> packageIds,
            string reason)
        {
            var sourcePackageIds = GetTrackAssemblySourcePackageIds(packageIds).ToArray();
            var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var bridgeType = FindBridgeType();
            if (bridgeType == null)
            {
                if (sourcePackageIds.Length == 0)
                {
                    return FuseTrackAssemblyBridgeApplySummary.Empty;
                }

                var message =
                    $"TrackAssembly source extension '{SourceExtensionKey}' is present, but bridge type " +
                    $"'{BridgeTypeName}' is not loaded. Install/load TrackAssembly.Fuse before applying " +
                    "TrackAssembly package source.";
                foreach (var packageId in sourcePackageIds)
                {
                    RecordFailure(failures, packageId, message, null);
                }

                return new FuseTrackAssemblyBridgeApplySummary(false, failures);
            }

            var applyMethod = FindApplyLoadedPackageMethod(bridgeType);
            if (applyMethod == null && sourcePackageIds.Length > 0)
            {
                var message =
                    $"TrackAssembly bridge type '{BridgeTypeName}' does not expose the expected " +
                    $"{ApplyLoadedPackageMethodName}(string packageId, string reason, options) method.";
                foreach (var packageId in sourcePackageIds)
                {
                    RecordFailure(failures, packageId, message, null);
                }

                return new FuseTrackAssemblyBridgeApplySummary(false, failures);
            }

            object bridge;
            try
            {
                bridge = Activator.CreateInstance(bridgeType);
            }
            catch (Exception ex)
            {
                var message = $"TrackAssembly bridge type '{BridgeTypeName}' could not be constructed: {ex.Message}";
                if (sourcePackageIds.Length == 0)
                {
                    FuseLog.Exception(
                        "FUSE TrackAssembly bridge could not be constructed while clearing stale custom visuals",
                        ex);
                    return FuseTrackAssemblyBridgeApplySummary.Empty;
                }

                foreach (var packageId in sourcePackageIds)
                {
                    RecordFailure(failures, packageId, message, ex);
                }

                return new FuseTrackAssemblyBridgeApplySummary(false, failures);
            }

            var nativeGraphMutationsApplied = false;
            if (sourcePackageIds.Length > 0)
            {
                TrackAPI.BeginBatch();
                try
                {
                    foreach (var packageId in sourcePackageIds)
                    {
                        try
                        {
                            var result = applyMethod.Invoke(bridge, new object[] { packageId, reason, null });
                            if (result == null)
                            {
                                RecordFailure(
                                    failures,
                                    packageId,
                                    $"TrackAssembly bridge returned no result for package '{packageId}'.",
                                    null);
                                continue;
                            }

                            var hasPackageData = ReadBoolProperty(result, "HasPackageData");
                            if (!hasPackageData)
                            {
                                FuseLog.Info(
                                    "FUSE TrackAssembly bridge skipped package " +
                                    $"'{packageId}' reason='{ReadStringProperty(result, "SkipReason")}'.");
                                continue;
                            }

                            var applyResult = ReadProperty(result, "ApplyResult");
                            var packageMutatedNativeGraph =
                                ReadBoolProperty(applyResult, "NativeGraphMutationApplied");
                            nativeGraphMutationsApplied |= packageMutatedNativeGraph;
                            FuseLog.Info(
                                "FUSE TrackAssembly bridge applied package " +
                                $"'{packageId}' assemblies={ReadIntProperty(result, "AssemblyCount")} " +
                                $"nativeObjects={ReadIntProperty(result, "NativeObjectCount")} " +
                                $"descriptorDirectives={ReadIntProperty(result, "NativeDescriptorDirectiveCount")} " +
                                $"nativeGraphMutationApplied={packageMutatedNativeGraph}.");
                        }
                        catch (TargetInvocationException ex)
                        {
                            var inner = ex.InnerException ?? ex;
                            RecordFailure(failures, packageId, inner.Message, inner);
                        }
                        catch (Exception ex)
                        {
                            RecordFailure(failures, packageId, ex.Message, ex);
                        }
                    }
                }
                finally
                {
                    TrackAPI.EndBatch(false);
                }
            }

            RetainLoadedPackageVisuals(bridge, bridgeType, sourcePackageIds);
            return new FuseTrackAssemblyBridgeApplySummary(nativeGraphMutationsApplied, failures);
        }

        private static IEnumerable<string> GetTrackAssemblySourcePackageIds(IEnumerable<string> packageIds)
        {
            if (packageIds == null)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in packageIds)
            {
                if (string.IsNullOrWhiteSpace(packageId) || !seen.Add(packageId))
                {
                    continue;
                }

                var definition = FuseModLoader.GetLoadedDefinition(packageId);
                if (definition?.Extensions == null)
                {
                    continue;
                }

                if (definition.Extensions.TryGetValue(SourceExtensionKey, out var source) && source != null)
                {
                    yield return packageId;
                }
            }
        }

        private static Type FindBridgeType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(BridgeTypeName, throwOnError: false);
                }
                catch
                {
                    continue;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static MethodInfo FindApplyLoadedPackageMethod(Type bridgeType)
        {
            return bridgeType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, ApplyLoadedPackageMethodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 3
                           && parameters[0].ParameterType == typeof(string)
                           && parameters[1].ParameterType == typeof(string);
                });
        }

        private static void RetainLoadedPackageVisuals(
            object bridge,
            Type bridgeType,
            string[] sourcePackageIds)
        {
            var retainMethod = FindRetainLoadedPackageVisualsMethod(bridgeType);
            if (retainMethod == null)
            {
                return;
            }

            try
            {
                var result = retainMethod.Invoke(bridge, new object[] { sourcePackageIds });
                if (result is int cleared && cleared > 0)
                {
                    FuseLog.Info(
                        $"FUSE TrackAssembly bridge cleared {cleared} stale custom visual package root(s) " +
                        $"after retaining {sourcePackageIds.Length} TrackAssembly source package(s).");
                }
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                FuseLog.Exception("FUSE TrackAssembly bridge failed while clearing stale custom visuals", inner);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE TrackAssembly bridge failed while clearing stale custom visuals", ex);
            }
        }

        private static MethodInfo FindRetainLoadedPackageVisualsMethod(Type bridgeType)
        {
            return bridgeType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, RetainLoadedPackageVisualsMethodName, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1;
                });
        }

        private static void RecordFailure(
            Dictionary<string, string> failures,
            string packageId,
            string message,
            Exception exception)
        {
            var resolvedMessage = string.IsNullOrWhiteSpace(message)
                ? "TrackAssembly bridge failed."
                : message;
            failures[packageId] = resolvedMessage;
            FusePackageFaultRegistry.RecordFault(packageId, FaultStage, resolvedMessage, exception);
        }

        private static object ReadProperty(object instance, string propertyName)
        {
            return instance == null
                ? null
                : instance
                    .GetType()
                    .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(instance, null);
        }

        private static bool ReadBoolProperty(object instance, string propertyName)
        {
            var value = ReadProperty(instance, propertyName);
            return value is bool boolValue && boolValue;
        }

        private static int ReadIntProperty(object instance, string propertyName)
        {
            var value = ReadProperty(instance, propertyName);
            return value is int intValue ? intValue : 0;
        }

        private static string ReadStringProperty(object instance, string propertyName)
        {
            return ReadProperty(instance, propertyName) as string ?? string.Empty;
        }
    }
}
