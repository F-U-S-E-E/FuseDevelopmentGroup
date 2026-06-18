using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Reflective smoke test for every Harmony patch FUSE ships.
    /// Discovers patch classes by scanning FUSE.dll for the
    /// <c>[HarmonyPatch]</c> attribute and asserts that each class's
    /// target method actually resolves against the game DLLs at test
    /// time.
    ///
    /// This is the cheapest possible regression net for the failure
    /// mode that has bitten us most often on Railroader updates:
    /// a patch silently detaches because the game type or method it
    /// targets was renamed / removed / had its generic specialisation
    /// change. Harmony swallows resolution failures at runtime
    /// (logging a warning and skipping the patch), so the only way
    /// to know is to inspect the live log AFTER deployment — by
    /// which point the regression is in users' hands.
    ///
    /// Handles all three targeting forms Harmony supports:
    ///
    ///   1. <c>[HarmonyPatch]</c> + <c>static MethodBase TargetMethod()</c>
    ///      → invoke the method, expect a non-null result.
    ///   2. <c>[HarmonyPatch]</c> + <c>static IEnumerable&lt;MethodBase&gt; TargetMethods()</c>
    ///      → invoke, expect a non-empty enumeration of non-null entries.
    ///   3. One or more <c>[HarmonyPatch(typeof(X), "method", ...)]</c>
    ///      attributes → merge them and resolve via
    ///      <see cref="AccessTools.Method(Type, string, Type[], Type[])"/>.
    ///
    /// New patch classes are picked up automatically — no manual
    /// registration. A patch whose target legitimately can't resolve
    /// in a test context (e.g. requires an asset-bundle load) can opt
    /// out by adding its full type name to <see cref="ExpectedSkippable"/>
    /// with a written justification; we deliberately do not provide
    /// an opt-out attribute to keep the silent-skip surface narrow.
    /// </summary>
    public class FusePatchTargetingTests
    {
        /// <summary>
        /// Patch types whose target resolution is acceptably brittle
        /// in a unit-test context (e.g. the target type is loaded
        /// from an asset bundle that isn't on disk during tests).
        /// Empty for now — every shipped patch's target SHOULD
        /// resolve given the game DLLs the test project already
        /// pulls in via HintPath in FUSE.Tests.csproj.
        ///
        /// Add an entry here only after writing down WHY the patch
        /// can't be smoke-tested. Don't use this as a "make the test
        /// pass" escape hatch — a silently-skipped patch will detach
        /// the same way in production.
        /// </summary>
        private static readonly HashSet<string> ExpectedSkippable =
            new HashSet<string>(StringComparer.Ordinal);

        public static IEnumerable<object[]> AllPatchClasses()
        {
            // Anchor on a known FUSE.Patches type so we load the
            // right assembly even when the test runner shadow-copies.
            var fuseAssembly = typeof(FUSE.Patches.FuseAssetPackPatchHelpers).Assembly;

            foreach (var type in fuseAssembly.GetTypes()
                                              .Where(IsHarmonyPatchClass)
                                              .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                yield return new object[] { type.FullName, type };
            }
        }

        /// <summary>
        /// A patch class is any class decorated with
        /// <c>[HarmonyPatch]</c> directly. Subclasses inherit
        /// targeting but Harmony only patches the decorated class
        /// itself, so we only enumerate those.
        /// </summary>
        private static bool IsHarmonyPatchClass(Type type)
        {
            if (type == null) return false;
            // We deliberately do NOT use HarmonyPatch.GetType() because
            // it returns the runtime type; we want the [HarmonyPatch]
            // attribute usage, regardless of HarmonyPatchCategory etc.
            return type.IsClass &&
                   type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0;
        }

        [Theory]
        [MemberData(nameof(AllPatchClasses))]
        public void PatchClass_TargetResolvesAgainstGameDlls(string patchTypeName, Type patchType)
        {
            if (ExpectedSkippable.Contains(patchTypeName))
            {
                // Acknowledged opt-out; the entry in ExpectedSkippable
                // is required to carry a comment justifying why.
                return;
            }

            var resolution = ResolveTargets(patchType);

            Assert.True(
                resolution.AllResolved,
                $"FUSE patch '{patchTypeName}' failed to resolve its Harmony target(s). " +
                $"Targeting form: {resolution.Form}. " +
                $"Details: {resolution.Diagnostic}. " +
                $"This means the patch will silently detach on the current game DLLs — " +
                $"either the targeted game type/method was renamed, the generic " +
                $"specialisation changed shape, or a TypeByName lookup returns null.");
        }

        // ----- target resolution -----

        private enum TargetForm
        {
            TargetMethod,
            TargetMethods,
            AttributeBased,
            Unknown
        }

        private struct Resolution
        {
            public TargetForm Form;
            public bool AllResolved;
            public string Diagnostic;
        }

        private static Resolution ResolveTargets(Type patchType)
        {
            // Order matters: Harmony's own resolver checks
            // TargetMethod / TargetMethods before falling back to
            // class-level [HarmonyPatch] attributes. Mirror that
            // order here so the test reports the SAME shape the
            // production patcher sees.
            var targetMethodFn = patchType.GetMethod(
                "TargetMethod",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetMethodFn != null)
            {
                return ResolveViaTargetMethod(targetMethodFn);
            }

            var targetMethodsFn = patchType.GetMethod(
                "TargetMethods",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetMethodsFn != null)
            {
                return ResolveViaTargetMethods(targetMethodsFn);
            }

            // The "multi-method patch class" shape: parent class
            // carries a bare [HarmonyPatch], each method inside
            // carries its own [HarmonyPatch(typeof(X), "Y")] +
            // [HarmonyPostfix]/[HarmonyPrefix]. Harmony patches
            // every such method individually. We detect this by
            // looking for method-level [HarmonyPatch] attributes
            // and resolving each.
            var methodLevelPatches = patchType.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0)
                .ToList();
            if (methodLevelPatches.Count > 0)
            {
                return ResolveViaMethodLevelAttributes(patchType, methodLevelPatches);
            }

            return ResolveViaAttributes(patchType);
        }

        private static Resolution ResolveViaTargetMethod(MethodInfo fn)
        {
            try
            {
                var result = fn.Invoke(null, null);
                if (result == null)
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethod,
                        AllResolved = false,
                        Diagnostic = "TargetMethod() returned null"
                    };
                }

                if (!(result is MethodBase mb))
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethod,
                        AllResolved = false,
                        Diagnostic = $"TargetMethod() returned a non-MethodBase '{result.GetType().FullName}'"
                    };
                }

                return new Resolution
                {
                    Form = TargetForm.TargetMethod,
                    AllResolved = true,
                    Diagnostic = $"{mb.DeclaringType?.FullName}.{mb.Name}"
                };
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                return new Resolution
                {
                    Form = TargetForm.TargetMethod,
                    AllResolved = false,
                    Diagnostic = $"TargetMethod() threw {inner.GetType().FullName}: {inner.Message}"
                };
            }
        }

        private static Resolution ResolveViaTargetMethods(MethodInfo fn)
        {
            try
            {
                var raw = fn.Invoke(null, null);
                if (raw == null)
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethods,
                        AllResolved = false,
                        Diagnostic = "TargetMethods() returned null"
                    };
                }

                if (!(raw is IEnumerable enumerable))
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethods,
                        AllResolved = false,
                        Diagnostic = $"TargetMethods() returned non-enumerable '{raw.GetType().FullName}'"
                    };
                }

                var entries = enumerable.Cast<object>().ToList();
                if (entries.Count == 0)
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethods,
                        AllResolved = false,
                        Diagnostic = "TargetMethods() yielded zero entries (every target failed to resolve)"
                    };
                }

                if (entries.Any(e => !(e is MethodBase)))
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethods,
                        AllResolved = false,
                        Diagnostic = "TargetMethods() yielded a non-MethodBase entry"
                    };
                }

                var resolved = entries.Cast<MethodBase>().ToList();
                if (resolved.Any(m => m == null))
                {
                    return new Resolution
                    {
                        Form = TargetForm.TargetMethods,
                        AllResolved = false,
                        Diagnostic = "TargetMethods() yielded a null MethodBase entry"
                    };
                }

                return new Resolution
                {
                    Form = TargetForm.TargetMethods,
                    AllResolved = true,
                    Diagnostic = $"{resolved.Count} target(s): {string.Join(", ", resolved.Select(m => $"{m.DeclaringType?.FullName}.{m.Name}"))}"
                };
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                return new Resolution
                {
                    Form = TargetForm.TargetMethods,
                    AllResolved = false,
                    Diagnostic = $"TargetMethods() threw {inner.GetType().FullName}: {inner.Message}"
                };
            }
        }

        private static Resolution ResolveViaAttributes(Type patchType)
        {
            // Harmony merges all [HarmonyPatch(...)] attributes on the
            // class into a single descriptor by accumulating non-null
            // fields. We mirror that aggregation here so a patch that
            // splits its declaration across multiple attributes (e.g.
            // [HarmonyPatch(typeof(X))] [HarmonyPatch("Method")]) still
            // resolves the same way Harmony will at apply time.
            var attrs = patchType.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                                 .Cast<HarmonyPatch>()
                                 .ToList();
            if (attrs.Count == 0)
            {
                return new Resolution
                {
                    Form = TargetForm.Unknown,
                    AllResolved = false,
                    Diagnostic = "no [HarmonyPatch] attribute"
                };
            }

            Type declaringType = null;
            string methodName = null;
            Type[] argumentTypes = null;
            MethodType? methodType = null;

            foreach (var attr in attrs)
            {
                if (attr.info == null) continue;
                if (attr.info.declaringType != null) declaringType = attr.info.declaringType;
                if (!string.IsNullOrWhiteSpace(attr.info.methodName)) methodName = attr.info.methodName;
                if (attr.info.argumentTypes != null) argumentTypes = attr.info.argumentTypes;
                if (attr.info.methodType.HasValue) methodType = attr.info.methodType;
            }

            if (declaringType == null)
            {
                return new Resolution
                {
                    Form = TargetForm.AttributeBased,
                    AllResolved = false,
                    Diagnostic = "[HarmonyPatch] attribute(s) did not declare a target type"
                };
            }

            return ResolveMethod(declaringType, methodName, argumentTypes, methodType, TargetForm.AttributeBased);
        }

        private static Resolution ResolveViaMethodLevelAttributes(Type patchType, List<MethodInfo> patchedMethods)
        {
            // Each method-level patch resolves independently. The
            // parent type's bare [HarmonyPatch] is just a "scan me"
            // marker for Harmony. All sub-targets must resolve for
            // the class as a whole to be considered patchable; one
            // silent detach is enough to ship a regression.
            var diagnostics = new List<string>();
            var resolvedAll = true;
            foreach (var method in patchedMethods)
            {
                Type subDeclaring = null;
                string subMethodName = null;
                Type[] subArgs = null;
                MethodType? subMethodType = null;
                foreach (var attr in method.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Cast<HarmonyPatch>())
                {
                    if (attr.info == null) continue;
                    if (attr.info.declaringType != null) subDeclaring = attr.info.declaringType;
                    if (!string.IsNullOrWhiteSpace(attr.info.methodName)) subMethodName = attr.info.methodName;
                    if (attr.info.argumentTypes != null) subArgs = attr.info.argumentTypes;
                    if (attr.info.methodType.HasValue) subMethodType = attr.info.methodType;
                }

                var subResolution = ResolveMethod(subDeclaring, subMethodName, subArgs, subMethodType, TargetForm.AttributeBased);
                if (subResolution.AllResolved)
                {
                    diagnostics.Add($"{method.Name} -> {subResolution.Diagnostic}");
                }
                else
                {
                    resolvedAll = false;
                    diagnostics.Add($"{method.Name} FAILED: {subResolution.Diagnostic}");
                }
            }

            return new Resolution
            {
                Form = TargetForm.AttributeBased,
                AllResolved = resolvedAll,
                Diagnostic = $"{patchedMethods.Count} method-level patch(es): " + string.Join("; ", diagnostics)
            };
        }

        /// <summary>
        /// Shared resolver for "declaring type + method name +
        /// optional argument types + optional MethodType" — used by
        /// both the class-level and method-level attribute paths.
        /// Handles MethodType.Normal, Setter, Getter, Constructor,
        /// and StaticConstructor by delegating to the appropriate
        /// AccessTools entry point.
        /// </summary>
        private static Resolution ResolveMethod(
            Type declaringType,
            string methodName,
            Type[] argumentTypes,
            MethodType? methodType,
            TargetForm form)
        {
            if (declaringType == null)
            {
                return new Resolution
                {
                    Form = form,
                    AllResolved = false,
                    Diagnostic = "no declaring type"
                };
            }

            var effectiveType = methodType ?? MethodType.Normal;
            MethodBase resolved = null;
            switch (effectiveType)
            {
                case MethodType.Normal:
                    if (string.IsNullOrWhiteSpace(methodName))
                    {
                        return new Resolution
                        {
                            Form = form,
                            AllResolved = false,
                            Diagnostic = $"target type {declaringType.FullName} resolved but no method name was supplied"
                        };
                    }
                    resolved = argumentTypes != null
                        ? AccessTools.Method(declaringType, methodName, argumentTypes)
                        : AccessTools.Method(declaringType, methodName);
                    break;
                case MethodType.Setter:
                    if (string.IsNullOrWhiteSpace(methodName))
                    {
                        return new Resolution
                        {
                            Form = form,
                            AllResolved = false,
                            Diagnostic = $"target type {declaringType.FullName} resolved but no property name was supplied for a Setter patch"
                        };
                    }
                    resolved = AccessTools.PropertySetter(declaringType, methodName);
                    break;
                case MethodType.Getter:
                    if (string.IsNullOrWhiteSpace(methodName))
                    {
                        return new Resolution
                        {
                            Form = form,
                            AllResolved = false,
                            Diagnostic = $"target type {declaringType.FullName} resolved but no property name was supplied for a Getter patch"
                        };
                    }
                    resolved = AccessTools.PropertyGetter(declaringType, methodName);
                    break;
                case MethodType.Constructor:
                    resolved = argumentTypes != null
                        ? AccessTools.Constructor(declaringType, argumentTypes)
                        : AccessTools.Constructor(declaringType);
                    break;
                case MethodType.StaticConstructor:
                    resolved = AccessTools.GetDeclaredConstructors(declaringType)
                                          .FirstOrDefault(c => c.IsStatic);
                    break;
                default:
                    return new Resolution
                    {
                        Form = form,
                        AllResolved = false,
                        Diagnostic = $"unsupported MethodType '{effectiveType}' — extend ResolveMethod"
                    };
            }

            if (resolved == null)
            {
                var argsLabel = argumentTypes != null
                    ? ", [" + string.Join(", ", argumentTypes.Select(t => t.FullName)) + "]"
                    : string.Empty;
                return new Resolution
                {
                    Form = form,
                    AllResolved = false,
                    Diagnostic = $"AccessTools could not resolve {effectiveType} '{declaringType.FullName}.{methodName}'{argsLabel}"
                };
            }

            return new Resolution
            {
                Form = form,
                AllResolved = true,
                Diagnostic = $"{resolved.DeclaringType?.FullName}.{resolved.Name} ({effectiveType})"
            };
        }
    }
}
