using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Effects.Decals;
using FUSE.Infrastructure;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Keeps one orphaned decal from turning the game's decal culling into a
    /// per-frame native memory leak.
    ///
    /// <c>DecalCullingManager.UpdateDecalVisibilityJob</c> allocates five
    /// <c>Allocator.TempJob</c> NativeArrays, then reads
    /// <c>entry.DecalProjector.transform</c> for every registered decal, and only
    /// Disposes the arrays at the end of the method — there is no try/finally. A
    /// projector that was destroyed while still registered (car lettering decals can
    /// be force-registered outside the enable/disable lifecycle by lettering mods,
    /// and <c>DecalProjectorHelper.OnDisable</c> skips its unregister when it throws
    /// — see <see cref="FuseDecalProjectorHelperDisableGuardPatch"/>) makes the
    /// gather loop throw a NullReferenceException BEFORE the Dispose calls, leaking
    /// all five arrays. The throw also exits <c>Update</c> before its
    /// <c>_timeSinceLastUpdate = 0</c> reset, so the normal ~0.25&#160;s cadence
    /// becomes EVERY FRAME: the registry is never pruned, so one bad entry means an
    /// NRE + five leaked TempJob arrays per frame for the rest of the session. In
    /// the field (Aspen, 2026-07-06) that was 21,947 consecutive throws over 30
    /// minutes, a saturated JobTemp allocator (67&#160;M overflow allocations), ~20&#160;GB
    /// process footprint, and ~13&#160;fps.
    ///
    /// The prefix validates the complete registry before EVERY invocation, before
    /// vanilla allocates any TempJob arrays. Harmony compiles the private-field
    /// accessors once, so the healthy path is an allocation-free indexed walk over
    /// the existing list. The finalizer remains as a storm breaker for layout drift
    /// or failures unrelated to a stale projector, allowing <c>Update</c>'s own timer
    /// reset to run instead of retrying (and leaking) every frame.
    ///
    /// All reflection is fail-open: if the game's private layout changes (guarded by
    /// the reflection-surface canary tests), the scrub no-ops and only the
    /// storm-breaker suppression remains active.
    /// </summary>
    [HarmonyPatch(typeof(DecalCullingManager), "UpdateDecalVisibilityJob")]
    internal static class FuseDecalCullingScrubPatch
    {
        private static readonly AccessTools.FieldRef<DecalCullingManager, IList> DecalProjectorsRef =
            BindDecalProjectorsRef();

        private static readonly AccessTools.FieldRef<object, UnityEngine.Object> EntryProjectorRef =
            BindEntryProjectorRef();

        private static readonly AccessTools.FieldRef<DecalCullingManager> SharedManagerRef =
            BindSharedManagerRef();

        /// <summary>Destroyed registry entries pruned since startup (diagnostics).</summary>
        internal static long ScrubbedEntries => FuseRuntimeGuardCounters.DecalRegistryScrubbed;

        /// <summary>Exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalVisibilitySuppressed;

        private static AccessTools.FieldRef<DecalCullingManager, IList> BindDecalProjectorsRef()
        {
            try
            {
                var field = AccessTools.Field(typeof(DecalCullingManager), "_decalProjectors");
                return field != null
                    ? AccessTools.FieldRefAccess<DecalCullingManager, IList>(field)
                    : null;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE decal culling could not bind the registry; the preflight scrub is unavailable",
                    ex);
                return null;
            }
        }

        private static AccessTools.FieldRef<object, UnityEngine.Object> BindEntryProjectorRef()
        {
            try
            {
                var entryType = AccessTools.Inner(typeof(DecalCullingManager), "Entry");
                return entryType != null
                    ? AccessTools.FieldRefAccess<UnityEngine.Object>(entryType, "DecalProjector")
                    : null;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE decal culling could not bind Entry.DecalProjector; the preflight scrub is unavailable",
                    ex);
                return null;
            }
        }

        private static AccessTools.FieldRef<DecalCullingManager> BindSharedManagerRef()
        {
            try
            {
                var field = AccessTools.Field(typeof(DecalCullingManager), "_shared");
                return field != null
                    ? AccessTools.StaticFieldRefAccess<DecalCullingManager>(field)
                    : null;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE decal culling could not bind the existing-manager reference",
                    ex);
                return null;
            }
        }

        private static void Prefix(DecalCullingManager __instance)
        {
            try
            {
                ScrubDestroyedEntries(__instance);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE decal culling preflight scrub failed; letting the vanilla job run", ex);
            }
        }

        /// <summary>
        /// Removes stale projectors before vanilla allocates its TempJob arrays.
        /// The no-removal path performs no managed allocation.
        /// </summary>
        internal static int ScrubDestroyedEntries(DecalCullingManager manager)
        {
            if (manager == null || DecalProjectorsRef == null || EntryProjectorRef == null)
            {
                return 0; // layout changed: fail open and leave the finalizer active.
            }

            var entries = DecalProjectorsRef(manager);
            if (entries == null)
            {
                return 0;
            }

            var removed = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var projector = entry != null ? EntryProjectorRef(entry) : null;
                if (projector != null)
                {
                    continue; // Unity fake-null covers destroyed and never-set projectors.
                }

                entries.RemoveAt(i);
                removed++;
                var scrubbed = FuseRuntimeGuardCounters.RecordDecalRegistryScrubbed();
                if (FuseGuardLog.ShouldLog(scrubbed))
                {
                    FuseLog.Warning(
                        $"FUSE pruned destroyed decal #{scrubbed} from the decal culling registry " +
                        "before the visibility job allocated temp memory (usually a car-lettering " +
                        "decal orphaned by a mod destroying the car outside the normal disable path).");
                }
            }

            return removed;
        }

        internal static bool TryGetExistingManager(out DecalCullingManager manager)
        {
            manager = null;
            if (SharedManagerRef == null)
            {
                return false;
            }

            manager = SharedManagerRef();
            return manager != null;
        }

        /// <summary>
        /// Reads the live registry without touching the public Shared getter,
        /// which would create a manager during teardown or diagnostics.
        /// </summary>
        internal static bool TryGetRegistryCount(out int count)
        {
            count = 0;
            DecalCullingManager manager;
            if (DecalProjectorsRef == null || !TryGetExistingManager(out manager))
            {
                return false;
            }

            var entries = DecalProjectorsRef(manager);
            if (entries == null)
            {
                return false;
            }

            count = entries.Count;
            return true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            var suppressed = FuseRuntimeGuardCounters.RecordDecalVisibilitySuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                FuseLog.Exception(
                    $"FUSE suppressed decal visibility job exception #{suppressed}. Suppressing lets " +
                    "the manager reset its update timer instead of re-throwing (and leaking temp job " +
                    "memory) every frame; the allocation-free preflight rechecks the registry before " +
                    "the next invocation",
                    __exception);
            }

            return null;
        }
    }

    /// <summary>
    /// Keeps third-party visibility callbacks from unwinding past vanilla's
    /// NativeArray disposal instructions. The game invokes <c>VisibleDidChange</c>
    /// after allocating five TempJob arrays and before disposing any of them; an
    /// exception from a subscriber therefore leaks the complete allocation set.
    /// Replacing that one delegate call with this non-throwing helper preserves
    /// vanilla ordering and adds no allocation to the healthy update path.
    /// </summary>
    [HarmonyPatch(typeof(DecalCullingManager), "UpdateDecalVisibilityJob")]
    internal static class FuseDecalVisibilityCallbackGuardPatch
    {
        private static readonly MethodInfo ActionBoolInvoke =
            AccessTools.Method(typeof(Action<bool>), nameof(Action<bool>.Invoke));

        private static readonly MethodInfo SafeInvoke =
            AccessTools.Method(
                typeof(FuseDecalVisibilityCallbackGuardPatch),
                nameof(InvokeVisibilityCallbackSafely));

        /// <summary>
        /// True when the most recent rewrite found the exact expected call site.
        /// Exposed to the IL canary tests so game updates cannot silently detach
        /// the leak guard.
        /// </summary>
        internal static bool RewriteInstalled { get; private set; }

        [HarmonyPriority(Priority.Last)]
        private static List<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RewriteVisibilityCallbackInvocation(instructions);
        }

        internal static List<CodeInstruction> RewriteVisibilityCallbackInvocation(
            IEnumerable<CodeInstruction> instructions)
        {
            var rewritten = instructions != null
                ? new List<CodeInstruction>(instructions)
                : new List<CodeInstruction>();

            RewriteInstalled = false;
            if (ActionBoolInvoke == null || SafeInvoke == null)
            {
                WarnRewriteFailure(
                    "FUSE decal visibility callback guard could not bind its replacement methods; " +
                    "the existing preflight and finalizer remain active.");
                return rewritten;
            }

            var matchIndex = -1;
            var matchCount = 0;
            for (var i = 0; i < rewritten.Count; i++)
            {
                var instruction = rewritten[i];
                if (instruction.opcode != OpCodes.Callvirt ||
                    !Equals(instruction.operand, ActionBoolInvoke))
                {
                    continue;
                }

                matchIndex = i;
                matchCount++;
            }

            if (matchCount != 1)
            {
                WarnRewriteFailure(
                    $"FUSE decal visibility callback guard expected one Action<bool>.Invoke call " +
                    $"but found {matchCount}; leaving the method unchanged so the other decal " +
                    "guards can still install.");
                return rewritten;
            }

            // Mutate the existing instruction so any exception blocks and labels
            // attached by Harmony or another mod remain on the call site.
            rewritten[matchIndex].opcode = OpCodes.Call;
            rewritten[matchIndex].operand = SafeInvoke;
            RewriteInstalled = true;
            return rewritten;
        }

        private static void WarnRewriteFailure(string message)
        {
            try
            {
                FuseLog.Warning(message);
            }
            catch
            {
                // A logger failure during Harmony reconstruction must not prevent
                // the independent scrub/finalizer patch from installing.
                return;
            }
        }

        internal static void InvokeVisibilityCallbackSafely(Action<bool> callback, bool visible)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback(visible);
            }
            catch (Exception ex)
            {
                // Nothing in the containment path may rethrow: reaching vanilla's
                // five Dispose calls is the reason this helper exists.
                try
                {
                    var suppressed = FuseRuntimeGuardCounters.RecordDecalVisibilitySuppressed();
                    if (!FuseGuardLog.ShouldLog(suppressed))
                    {
                        return;
                    }

                    var method = callback.Method;
                    var declaringType = method.DeclaringType;
                    var callbackName = declaringType != null
                        ? declaringType.FullName + "." + method.Name
                        : method.Name;
                    FuseLog.Exception(
                        $"FUSE suppressed decal visibility callback exception #{suppressed} from " +
                        $"'{callbackName}' while setting visible={visible}. NativeArray cleanup " +
                        "will continue",
                        ex);
                }
                catch
                {
                    // Diagnostics are best-effort and must not recreate the leak.
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Rejects a null or destroyed projector before vanilla can append a poisoned
    /// entry to the culling registry. Harmony passes argument zero as <c>object</c>
    /// so FUSE does not need a compile-time reference to the URP runtime assembly.
    /// </summary>
    [HarmonyPatch(typeof(DecalCullingManager), nameof(DecalCullingManager.RegisterDecal))]
    internal static class FuseDecalCullingRegisterGuardPatch
    {
        private static long _rejectedRegistrations;

        private static bool Prefix(object __0)
        {
            var shouldRegister = ShouldRegister(__0);
            if (!shouldRegister)
            {
                var rejected = ++_rejectedRegistrations;
                if (FuseGuardLog.ShouldLog(rejected))
                {
                    FuseLog.Warning(
                        $"FUSE rejected null/destroyed decal registration #{rejected}; adding it " +
                        "would poison the visibility job's TempJob allocations.");
                }
            }

            return shouldRegister;
        }

        internal static bool ShouldRegister(object decal)
        {
            var unityObject = decal as UnityEngine.Object;
            return unityObject != null;
        }
    }

    /// <summary>
    /// Silences the enable-time crash of <c>DecalProjectorHelper</c> (the game's
    /// car-lettering decal driver) when its prefab is enabled somewhere without a
    /// <c>Car</c> ancestor: vanilla <c>OnEnable</c> dereferences the parent car
    /// unconditionally (<c>OnCarVisibilityDidChange(_car.IsVisible)</c>). The throw
    /// already aborts the rest of the helper's setup, so suppressing it changes no
    /// behavior — it removes the exception spam (and crash-report uploads) for a
    /// state the helper ends up in either way. A finalizer rather than a
    /// pre-emptive skip so a future game version that supports car-less decals
    /// keeps working: this guard only acts on an observed throw.
    /// </summary>
    [HarmonyPatch(typeof(DecalProjectorHelper), "OnEnable")]
    internal static class FuseDecalProjectorHelperEnableGuardPatch
    {
        /// <summary>OnEnable exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalHelperEnableSuppressed;

        private static Exception Finalizer(Exception __exception, DecalProjectorHelper __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            var suppressed = FuseRuntimeGuardCounters.RecordDecalHelperEnableSuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                // Unity's overloaded null covers a destroyed instance, so .name is safe.
                var name = __instance != null ? __instance.name : "<destroyed>";
                FuseLog.Exception(
                    $"FUSE suppressed car-decal helper enable exception #{suppressed} on '{name}'. " +
                    "The helper usually has no Car ancestor (car decal machinery mounted on " +
                    "scenery); it stays inert either way",
                    __exception);
            }

            return null;
        }
    }

    /// <summary>
    /// Keeps a decal from staying registered in <c>DecalCullingManager</c> when
    /// <c>DecalProjectorHelper.OnDisable</c> throws. Vanilla ordering unsubscribes
    /// <c>_car.OnVisibleDidChange</c> FIRST (throws when <c>_car</c> is null — e.g. a
    /// helper force-registered by a lettering mod without ever running its own
    /// enable) and only calls <c>SetDecalRegistered(false)</c> LAST, so the throw
    /// skips the unregister and strands a destroyed projector in the culling
    /// registry — the exact poisoning that
    /// <see cref="FuseDecalCullingScrubPatch"/> then has to heal. The prefix now
    /// unregisters before vanilla touches <c>_car</c>; the finalizer retries as a
    /// fallback and suppresses any original exception. Both paths no-op when the
    /// manager singleton is already gone, avoiding the public <c>Shared</c> getter
    /// that would resurrect a manager GameObject during scene teardown.
    /// </summary>
    [HarmonyPatch(typeof(DecalProjectorHelper), "OnDisable")]
    internal static class FuseDecalProjectorHelperDisableGuardPatch
    {
        private delegate void SetDecalRegisteredDelegate(
            DecalProjectorHelper instance,
            bool registered);

        private static readonly SetDecalRegisteredDelegate SetDecalRegistered = BindSetDecalRegistered();

        /// <summary>OnDisable exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => FuseRuntimeGuardCounters.DecalHelperDisableSuppressed;

        private static SetDecalRegisteredDelegate BindSetDecalRegistered()
        {
            try
            {
                var method = AccessTools.Method(typeof(DecalProjectorHelper), "SetDecalRegistered");
                return method != null
                    ? AccessTools.MethodDelegate<SetDecalRegisteredDelegate>(method, virtualCall: false)
                    : null;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    "FUSE decal helper disable guard could not bind SetDecalRegistered",
                    ex);
                return null;
            }
        }

        private static void Prefix(DecalProjectorHelper __instance)
        {
            TryUnregister(__instance, "before vanilla OnDisable");
        }

        private static Exception Finalizer(Exception __exception, DecalProjectorHelper __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            TryUnregister(__instance, "from the OnDisable finalizer fallback");

            var suppressed = FuseRuntimeGuardCounters.RecordDecalHelperDisableSuppressed();
            if (FuseGuardLog.ShouldLog(suppressed))
            {
                FuseLog.Exception(
                    $"FUSE suppressed car-decal helper disable exception #{suppressed}. The helper's " +
                    "decal registration was released before vanilla teardown where possible; the " +
                    "preflight registry scrub covers anything left behind",
                    __exception);
            }

            return null;
        }

        private static bool TryUnregister(DecalProjectorHelper instance, string phase)
        {
            if (instance == null || SetDecalRegistered == null)
            {
                return false;
            }

            DecalCullingManager manager;
            if (!FuseDecalCullingScrubPatch.TryGetExistingManager(out manager))
            {
                return false;
            }

            try
            {
                // No-ops internally when the helper never registered its decal.
                // The existing-manager check above prevents Shared from creating a
                // replacement GameObject during scene teardown.
                SetDecalRegistered(instance, false);
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE decal helper disable guard could not unregister {phase}", ex);
                return false;
            }
        }
    }

}
