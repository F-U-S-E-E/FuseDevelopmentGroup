using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FUSE.Profiler.Instrumentation
{
    /// <summary>
    /// Turns target specs and search-panel input into patchable MethodBase
    /// instances, and decides which methods are safe to instrument at all.
    /// </summary>
    internal static class MethodResolver
    {
        private static readonly Assembly OwnAssembly = typeof(MethodResolver).Assembly;

        /// <summary>
        /// Resolve a spec of the form "Namespace.Type:Method". For coroutine
        /// specs the declared method is only the iterator factory; the
        /// measurable body is the compiler-generated MoveNext.
        /// </summary>
        internal static MethodBase Resolve(TargetSpec spec, out string error)
        {
            error = null;
            MethodInfo declared;
            try
            {
                declared = AccessTools.Method(spec.MethodSpec);
            }
            catch (Exception ex)
            {
                error = spec.MethodSpec + ": " + ex.Message;
                return null;
            }

            if (declared == null)
            {
                error = spec.MethodSpec + ": method not found";
                return null;
            }

            if (!spec.Coroutine)
            {
                return Profilable(declared, spec.MethodSpec, ref error);
            }

            MethodInfo moveNext;
            try
            {
                moveNext = AccessTools.EnumeratorMoveNext(declared);
            }
            catch (Exception ex)
            {
                error = spec.MethodSpec + " (coroutine): " + ex.Message;
                return null;
            }

            if (moveNext == null)
            {
                error = spec.MethodSpec + ": no iterator MoveNext found (not a coroutine?)";
                return null;
            }

            return Profilable(moveNext, spec.MethodSpec + " (coroutine)", ref error);
        }

        /// <summary>
        /// Every instrumentable method a type declares (used by type- and
        /// assembly-wide profiling).
        /// </summary>
        internal static IEnumerable<MethodInfo> ProfilableDeclaredMethods(Type type)
        {
            MethodInfo[] declared;
            try
            {
                declared = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            }
            catch
            {
                yield break;
            }

            for (var i = 0; i < declared.Length; i++)
            {
                if (IsProfilable(declared[i]))
                {
                    yield return declared[i];
                }
            }
        }

        /// <summary>
        /// Conservative safety filter: only concrete methods with real IL
        /// bodies, closed over all type parameters, and outside the profiler
        /// itself (self-instrumentation would recurse through the wrappers).
        /// </summary>
        internal static bool IsProfilable(MethodBase method)
        {
            if (method == null || method.IsAbstract)
            {
                return false;
            }

            if (method.ContainsGenericParameters)
            {
                return false;
            }

            var declaringType = method.DeclaringType;
            if (declaringType == null || declaringType.ContainsGenericParameters)
            {
                return false;
            }

            if (declaringType.Assembly == OwnAssembly)
            {
                return false;
            }

            // Delegate plumbing and MarshalByRef proxies patch badly.
            if (typeof(Delegate).IsAssignableFrom(declaringType))
            {
                return false;
            }

            try
            {
                if (method.GetMethodBody() == null)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Assemblies that make sense as profiling targets. Excludes the
        /// runtime/engine/patching infrastructure whose methods are either
        /// uninstrumentable or would measure the measurement. Matching is
        /// exact-name or dot-terminated prefix so legitimately named mods
        /// (e.g. "Monorail", "SystemsOverhaul") are not caught by bare
        /// prefixes.
        /// </summary>
        internal static bool IsProfilableAssemblyName(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            if (simpleName == OwnAssembly.GetName().Name)
            {
                return false;
            }

            return !MatchesInfrastructure(simpleName, "System") &&
                   !MatchesInfrastructure(simpleName, "mscorlib") &&
                   !MatchesInfrastructure(simpleName, "netstandard") &&
                   !MatchesInfrastructure(simpleName, "Unity") &&
                   !MatchesInfrastructure(simpleName, "UnityEngine") &&
                   !MatchesInfrastructure(simpleName, "Mono") &&
                   !MatchesInfrastructure(simpleName, "MonoMod") &&
                   !MatchesInfrastructure(simpleName, "0Harmony") &&
                   !MatchesInfrastructure(simpleName, "dnlib");
        }

        private static bool MatchesInfrastructure(string simpleName, string blocked)
        {
            return string.Equals(simpleName, blocked, StringComparison.Ordinal) ||
                   (simpleName.Length > blocked.Length &&
                    simpleName[blocked.Length] == '.' &&
                    simpleName.StartsWith(blocked, StringComparison.Ordinal));
        }

        private static MethodBase Profilable(MethodInfo method, string describedAs, ref string error)
        {
            if (!IsProfilable(method))
            {
                error = describedAs + ": not instrumentable (abstract/generic/no body)";
                return null;
            }

            return method;
        }
    }
}
