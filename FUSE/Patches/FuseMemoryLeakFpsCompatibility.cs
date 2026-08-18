using System;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Memory Leak &amp; FPS Fix 1.0.1 was compiled against Enviro's former
    /// RenderGlobalReflectionProbe(bool) API. Current Enviro exposes
    /// RenderGlobalReflectionProbe(bool, bool), causing the mod's Start
    /// postfix and world-shift listener to throw MissingMethodException.
    /// </summary>
    internal static class FuseMemoryLeakFpsCompatibility
    {
        private const string ModAssemblyName = "MemoryLeakFPSfix";
        private const string StartPatchTypeName =
            "MemoryLeakFPSfix.Patches.EnviroUpdateOnPositionPatch";
        private const string ModTypeName = "MemoryLeakFPSfix.MemoryLeakFPSfix";

        private static readonly MethodInfo FuseStartPostfix = AccessTools.DeclaredMethod(
            typeof(FuseMemoryLeakFpsCompatibility),
            nameof(EnviroStartPostfix));

        private static bool _startPostfixInstalled;
        private static bool _worldMoveInstalled;
        private static bool _loggedRuntimeFailure;

        internal static bool Installed => _startPostfixInstalled && _worldMoveInstalled;

        internal static string EnsureInstalled(Harmony harmony)
        {
            if (Installed)
            {
                return "installed";
            }

            if (harmony == null)
            {
                return "unavailable (no harmony)";
            }

            var managerType = AccessTools.TypeByName("Enviro.EnviroManager");
            var startTarget = managerType == null
                ? null
                : AccessTools.DeclaredMethod(managerType, "Start", Type.EmptyTypes);
            var modType = AccessTools.TypeByName(ModTypeName);
            if (startTarget == null || modType == null)
            {
                return "idle (not present)";
            }

            if (!_startPostfixInstalled)
            {
                var stalePostfixes = Harmony.GetPatchInfo(startTarget)?.Postfixes
                    ?.Where(patch => IsLegacyStartPostfix(patch?.PatchMethod))
                    .Select(patch => patch.PatchMethod)
                    .Distinct()
                    .ToArray() ?? Array.Empty<MethodInfo>();
                if (stalePostfixes.Length > 0)
                {
                    foreach (var stalePostfix in stalePostfixes)
                    {
                        harmony.Unpatch(startTarget, stalePostfix);
                    }

                    if (!IsFusePostfixInstalled(startTarget))
                    {
                        harmony.Patch(
                            startTarget,
                            postfix: new HarmonyMethod(FuseStartPostfix));
                    }

                    _startPostfixInstalled = true;
                }
            }

            if (!_worldMoveInstalled)
            {
                var worldMoveTarget = AccessTools.DeclaredMethod(modType, "WorldDidMove");
                if (worldMoveTarget != null)
                {
                    harmony.Patch(
                        worldMoveTarget,
                        prefix: new HarmonyMethod(
                            typeof(FuseMemoryLeakFpsCompatibility),
                            nameof(WorldDidMovePrefix)));
                    _worldMoveInstalled = true;
                }
            }

            if (Installed)
            {
                FuseLog.Info(
                    "FUSE updated Memory Leak & FPS Fix's Enviro reflection refresh " +
                    "calls for the current two-argument API.");
                return "installed";
            }

            return _worldMoveInstalled
                ? "waiting (legacy Start postfix not applied yet)"
                : "idle (surface changed)";
        }

        internal static bool IsLegacyStartPostfix(MethodInfo method)
        {
            return IsLegacyStartPostfix(
                method?.Module?.Assembly?.GetName()?.Name,
                method?.DeclaringType?.FullName,
                method?.Name);
        }

        internal static bool IsLegacyStartPostfix(
            string assemblyName,
            string declaringTypeName,
            string methodName)
        {
            return string.Equals(assemblyName, ModAssemblyName, StringComparison.Ordinal) &&
                   string.Equals(declaringTypeName, StartPatchTypeName, StringComparison.Ordinal) &&
                   string.Equals(methodName, "Postfix", StringComparison.Ordinal);
        }

        internal static object[] CurrentRenderArguments(bool forced)
        {
            return new object[] { forced, false };
        }

        private static bool IsFusePostfixInstalled(MethodBase target)
        {
            return Harmony.GetPatchInfo(target)?.Postfixes?.Any(
                patch => patch?.PatchMethod == FuseStartPostfix) == true;
        }

        private static void EnviroStartPostfix(object __instance)
        {
            RefreshReflection(__instance);
        }

        private static bool WorldDidMovePrefix()
        {
            try
            {
                var managerType = AccessTools.TypeByName("Enviro.EnviroManager");
                var manager = managerType == null
                    ? null
                    : AccessTools.Property(managerType, "instance")?.GetValue(null, null);
                RefreshReflection(manager);
            }
            catch (Exception ex)
            {
                LogRuntimeFailure(ex);
            }

            return false;
        }

        private static void RefreshReflection(object manager)
        {
            if (manager == null)
            {
                return;
            }

            try
            {
                var reflections = AccessTools.Field(manager.GetType(), "Reflections")
                    ?.GetValue(manager);
                if (reflections == null)
                {
                    return;
                }

                var render = AccessTools.DeclaredMethod(
                    reflections.GetType(),
                    "RenderGlobalReflectionProbe",
                    new[] { typeof(bool), typeof(bool) });
                render?.Invoke(reflections, CurrentRenderArguments(true));
            }
            catch (Exception ex)
            {
                LogRuntimeFailure(ex);
            }
        }

        private static void LogRuntimeFailure(Exception exception)
        {
            if (_loggedRuntimeFailure)
            {
                return;
            }

            _loggedRuntimeFailure = true;
            var actual = exception is TargetInvocationException invocation &&
                         invocation.InnerException != null
                ? invocation.InnerException
                : exception;
            FuseLog.Warning(
                "FUSE could not refresh Enviro's global reflection probe for " +
                $"Memory Leak & FPS Fix: {actual.Message}");
        }
    }
}
