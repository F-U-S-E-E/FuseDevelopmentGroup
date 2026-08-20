using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using GalaSoft.MvvmLight.Helpers;
using HarmonyLib;

namespace FUSE.Patches
{
    /// <summary>
    /// Contains exceptions thrown by guarded messenger event listeners so one broken
    /// listener can never disturb delivery to anyone else — and so the offender is
    /// actually NAMED in the log.
    ///
    /// The game broadcasts <c>MapWillLoadEvent</c> / <c>MapDidLoadEvent</c> /
    /// <c>MapWillUnloadEvent</c> / <c>MapDidUnloadEvent</c> through MvvmLight's
    /// <c>Messenger</c>, and every mod plus a number of scene components register
    /// handlers for them — many during the load sequence itself. A handler that
    /// throws (observed in the wild: a stale third-party mod whose MapDidLoadEvent
    /// handler died in a TypeLoadException before its first statement ran) surfaces
    /// as a raw exception inside the dispatch loop. The game's dispatch logs such
    /// failures generically and moves on, but that log line does not identify which
    /// registered recipient failed — a JIT-time type-load failure never even enters
    /// the handler, so no mod frame appears — and nothing guards
    /// <see cref="WeakAction"/> invocations that happen outside that one loop. As
    /// the mod-compat layer, FUSE pins the containment to the WeakAction itself:
    /// the exception is suppressed at the listener boundary, counted, and logged
    /// (throttled) with the recipient type and handler method name, and dispatch
    /// continues so later-registered listeners still receive the event.
    ///
    /// Deliberately scoped to map lifecycle events and the legacy debug-report
    /// contribution event — this is not a blanket "never throw from Messenger"
    /// patch; every other event keeps its stock semantics.
    ///
    /// Generic-method caveat: this targets the CONSTRUCTED <c>WeakAction&lt;T&gt;</c>
    /// methods, one instantiation per event type. That is only reliable because
    /// every lifecycle event is a struct: each value-type instantiation gets its own
    /// unshared method body, so the patch lands on exactly that instantiation.
    /// Reference-type arguments share one canonical body — patching it is unreliable
    /// and can bleed into unrelated instantiations — so a class-typed event must
    /// never be added to <see cref="LifecycleEventTypeNames"/>; TargetMethods
    /// enforces this with an IsValueType guard.
    /// </summary>
    [HarmonyPatch]
    internal static class FuseMessengerIsolationPatch
    {
        // Resolved by name (rather than typeof) so a game update that renames or
        // removes one event skips that entry with a warning instead of failing the
        // whole patch class. (If EVERY entry failed to resolve, the empty target
        // set would make Harmony reject the class as a whole — FusePatchResilience
        // contains that to a logged skip of this one class.)
        private static readonly string[] GuardedEventTypeNames =
        {
            "Game.Events.MapWillLoadEvent",
            "Game.Events.MapDidLoadEvent",
            "Game.Events.MapWillUnloadEvent",
            "Game.Events.MapDidUnloadEvent",
            "Railloader.Events.WillCopyDebugInformation"
        };

        private static long _suppressed;
        private static long _diagnosticFailures;
        private static long _logged;

        // Offenders already named in the log, so a NEW broken listener is always
        // reported even after the global first-5 budget is spent (otherwise a noisy
        // mod could burn the budget and a second offender would stay anonymous until
        // the every-100th heartbeat). Bounded so the set cannot grow without limit.
        private const int MaxRememberedOffenders = 32;
        private static readonly HashSet<string> _loggedOffenders = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Exceptions suppressed since startup (diagnostics).</summary>
        internal static long SuppressedExceptions => _suppressed;

        /// <summary>Log attempts the finalizer had to abandon (diagnostics).</summary>
        internal static long DiagnosticFailures => _diagnosticFailures;

        /// <summary>Suppressed exceptions actually surfaced to the log, i.e. not throttled (diagnostics).</summary>
        internal static long LoggedExceptions => _logged;

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new HashSet<MethodBase>();
            foreach (var eventTypeName in GuardedEventTypeNames)
            {
                // WeakAction<T> and the lifecycle event structs are defined in the
                // SAME assembly: MvvmLight is compiled into the game's Assembly-CSharp
                // rather than shipped as a separate DLL, so only the namespace differs
                // (GalaSoft.MvvmLight.Helpers vs Game.Events), not the assembly. That
                // makes typeof(WeakAction<>).Assembly.GetType the path actually taken;
                // AccessTools.TypeByName is a cross-assembly fallback that only engages
                // if a future game build relocates the events.
                var eventType = typeof(WeakAction<>).Assembly.GetType(eventTypeName)
                                ?? AccessTools.TypeByName(eventTypeName);
                if (eventType == null)
                {
                    FuseLog.Warning(
                        $"FUSE messenger isolation could not resolve lifecycle event type '{eventTypeName}'; " +
                        "listeners for that event stay unguarded. Remaining lifecycle events are still patched.");
                    continue;
                }

                if (!eventType.IsValueType)
                {
                    // See the class comment: a reference-type event would patch the
                    // shared canonical WeakAction<T> body, which is unreliable and
                    // can affect unrelated instantiations. Refuse rather than risk it.
                    FuseLog.Warning(
                        $"FUSE messenger isolation skipped '{eventTypeName}' because it is not a value type; " +
                        "listeners for that event stay unguarded.");
                    continue;
                }

                Type weakActionType;
                try
                {
                    weakActionType = typeof(WeakAction<>).MakeGenericType(eventType);
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE messenger isolation could not construct WeakAction<{eventType.Name}>: " +
                        $"{ex.GetBaseException().Message}. Listeners for that event stay unguarded.");
                    continue;
                }

                // ExecuteWithObject(object) is the entry the Messenger dispatch loop
                // calls; Execute(T) sits underneath it and is also reachable directly,
                // so guard both. When Execute(T) suppresses, ExecuteWithObject simply
                // completes — the throttled log line fires once per failure.
                AddTarget(targets, weakActionType, eventType, "ExecuteWithObject", new[] { typeof(object) });
                AddTarget(targets, weakActionType, eventType, "Execute", new[] { eventType });
            }

            return targets;
        }

        private static void AddTarget(
            HashSet<MethodBase> targets,
            Type weakActionType,
            Type eventType,
            string methodName,
            Type[] parameterTypes)
        {
            var method = AccessTools.Method(weakActionType, methodName, parameterTypes);
            if (method == null)
            {
                FuseLog.Warning(
                    $"FUSE messenger isolation could not resolve WeakAction<{eventType.Name}>.{methodName}; " +
                    "that dispatch path stays unguarded.");
                return;
            }

            targets.Add(method);
        }

        internal static Exception Finalizer(Exception __exception, object __instance)
        {
            if (__exception == null)
            {
                return null;
            }

            _suppressed++;
            try
            {
                // Suppression means Unity never logs this exception, so the mod
                // health log hook cannot see it — feed the registry directly, and
                // BEFORE the throttle decision so every containment is counted
                // even when its log line below is suppressed.
                FuseModExceptionRegistry.RecordContained(
                    __exception, ResolveRecipientType(__instance), "messenger listener");

                // First few individually, every previously-unseen offender once, then
                // heartbeat only — a permanently-broken listener re-throws on every
                // map load/unload it survives to see. Logged with the full exception
                // (not just the message): suppressing here also silences the game's
                // own dispatch-loop log line, so this line must carry the stack.
                var listener = DescribeListener(__instance);
                var newOffender = _loggedOffenders.Count < MaxRememberedOffenders && _loggedOffenders.Add(listener);
                if (_suppressed <= 5 || newOffender || _suppressed % 100 == 0)
                {
                    FuseLog.Exception(
                        $"FUSE contained messenger listener exception #{_suppressed} from {listener}; " +
                        "the exception was suppressed and dispatch continued, so later-registered listeners " +
                        "still received the event", __exception);
                    _logged++;
                }
            }
            catch
            {
                // Diagnostics are best-effort; the whole point of this patch is that
                // nothing thrown here may leak back into the dispatch loop. Count the
                // abandoned log attempt so the readout can reveal a broken log path.
                _diagnosticFailures++;
            }

            return null;
        }

        // The Type the health registry attributes by: the recipient passed to
        // Messenger.Register when it is still alive, else the handler delegate's
        // declaring type (same fallback order as DescribeListener, but yielding
        // the Type itself so attribution is exact — no string parsing). May
        // return null (collected recipient AND unresolvable delegate); the
        // registry treats that as unattributed.
        private static Type ResolveRecipientType(object weakAction)
        {
            try
            {
                var recipientType = (weakAction as WeakAction)?.Target?.GetType();
                if (recipientType != null)
                {
                    return recipientType;
                }

                if (weakAction != null &&
                    AccessTools.DeclaredProperty(weakAction.GetType(), "Action")?.GetValue(weakAction, null)
                        is Delegate action)
                {
                    return action.Method?.DeclaringType;
                }
            }
            catch
            {
                // Best-effort, mirroring DescribeListener: the containment must
                // never throw over a diagnostics lookup.
                FUSE.Infrastructure.FuseModExceptionRegistry.CountSelfFault();
            }

            return null;
        }

        internal static string DescribeListener(object weakAction)
        {
            try
            {
                if (weakAction == null)
                {
                    return "<unknown listener>";
                }

                var type = weakAction.GetType();
                var eventName = type.IsGenericType ? type.GetGenericArguments()[0].Name : type.Name;

                // The recipient passed to Messenger.Register is what users recognise;
                // it can already be collected (WeakReference), so fall back to the
                // handler delegate's declaring type when it is gone.
                var recipientType = (weakAction as WeakAction)?.Target?.GetType().FullName;

                // The constructed WeakAction<T> hides the base 'Action' slot with its
                // own typed property — same name, DIFFERENT property type — so a
                // base-walking lookup (AccessTools.Property/GetProperty) is an
                // ambiguous match; only the declared-property lookup resolves it.
                // Best-effort in its own guard so the recipient/event naming above
                // still reaches the log if the delegate lookup ever breaks.
                string methodName = null;
                try
                {
                    if (AccessTools.DeclaredProperty(type, "Action")?.GetValue(weakAction, null) is Delegate action &&
                        action.Method != null)
                    {
                        methodName = action.Method.Name;
                        recipientType = recipientType ?? action.Method.DeclaringType?.FullName;
                    }
                }
                catch
                {
                    // Keep recipientType/eventName; only the handler name is lost.
                    // Counted so the readout reveals a degraded delegate lookup.
                    _diagnosticFailures++;
                }

                var handler = recipientType ?? "<collected recipient>";
                if (methodName != null)
                {
                    handler = handler + "." + methodName;
                }

                return $"'{handler}' (event {eventName})";
            }
            catch
            {
                return "<undescribable listener>";
            }
        }
    }
}
