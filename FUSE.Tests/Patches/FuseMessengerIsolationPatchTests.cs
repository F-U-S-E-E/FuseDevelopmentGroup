using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FUSE.Patches;
using GalaSoft.MvvmLight.Helpers;
using Game.Events;
using HarmonyLib;
using Railloader.Events;
using Xunit;

namespace FUSE.Tests.Patches
{
    /// <summary>
    /// Targeting and direct-invocation tests for
    /// <see cref="FuseMessengerIsolationPatch"/>.
    ///
    /// The patch closes <c>WeakAction&lt;T&gt;</c> over each map lifecycle
    /// event struct and finalizes both dispatch entries
    /// (<c>ExecuteWithObject(object)</c> and <c>Execute(T)</c>) so a
    /// throwing listener is contained at its own boundary instead of
    /// surfacing into the Messenger dispatch loop. Because the targets
    /// are CONSTRUCTED generic methods, the highest-value regression
    /// net here is target resolution against the real game DLLs: a
    /// game update that renames an event struct, turns it into a
    /// class, or reshapes WeakAction would silently detach the patch
    /// at runtime — these tests fail instead.
    ///
    /// FUSE.Tests never applies Harmony patches (no Harmony instance,
    /// no .Patch() calls — see FusePatchTargetingTests), so there is
    /// deliberately no "apply + Send + assert listener B still ran"
    /// integration test; the finalizer is exercised the same way
    /// FusePrefabStoreMaterialDefinitionsPatchTests exercises its
    /// patch bodies — as a plain static method.
    ///
    /// Invoking the finalizer records the contained exception into the
    /// static session-cumulative <c>FuseModExceptionRegistry</c>, so this
    /// class shares the registry's xUnit collection — xUnit runs different
    /// collections in parallel, and stray records would otherwise race the
    /// registry/report assertions.
    /// </summary>
    [Collection(FUSE.Tests.Infrastructure.FuseModExceptionRegistryTestCollection.Name)]
    public class FuseMessengerIsolationPatchTests
    {
        // ---- target resolution against the real game assemblies ----

        [Fact]
        public void TargetMethods_ResolvesMapDidLoadEventInstantiation()
        {
            // The minimum guarantee: the event observed breaking in the
            // wild stays guarded. typeof(MapDidLoadEvent) is a
            // compile-time reference, so a game rename fails this test
            // (and the build) rather than silently detaching the patch.
            var targets = FuseMessengerIsolationPatch.TargetMethods().ToList();

            var executeWithObject = AccessTools.Method(
                typeof(WeakAction<MapDidLoadEvent>), "ExecuteWithObject", new[] { typeof(object) });
            var execute = AccessTools.Method(
                typeof(WeakAction<MapDidLoadEvent>), "Execute", new[] { typeof(MapDidLoadEvent) });

            Assert.NotNull(executeWithObject);
            Assert.NotNull(execute);
            Assert.Contains(executeWithObject, targets);
            Assert.Contains(execute, targets);
        }

        [Fact]
        public void TargetMethods_CoverLifecycleAndLegacyDebugEvents_WithBothDispatchEntries()
        {
            // Full expected surface: four lifecycle structs plus the legacy
            // debug-report contribution event, two dispatch
            // entries each. Compile-time typeofs again so any single
            // event going missing or changing shape raises an alarm here
            // instead of a runtime "stays unguarded" warning nobody reads.
            var eventTypes = new[]
            {
                typeof(MapWillLoadEvent),
                typeof(MapDidLoadEvent),
                typeof(MapWillUnloadEvent),
                typeof(MapDidUnloadEvent),
                typeof(WillCopyDebugInformation)
            };

            var expected = new HashSet<MethodBase>();
            foreach (var eventType in eventTypes)
            {
                var weakActionType = typeof(WeakAction<>).MakeGenericType(eventType);
                expected.Add(AccessTools.Method(weakActionType, "ExecuteWithObject", new[] { typeof(object) }));
                expected.Add(AccessTools.Method(weakActionType, "Execute", new[] { eventType }));
            }

            var targets = new HashSet<MethodBase>(FuseMessengerIsolationPatch.TargetMethods());

            Assert.DoesNotContain((MethodBase)null, expected);
            Assert.Equal(expected.Count, targets.Count);
            Assert.Subset(expected, targets);
        }

        [Fact]
        public void TargetMethods_OnlyTargetValueTypeInstantiations()
        {
            // Pins the generic-method caveat the patch documents: the
            // constructed-method approach is only sound for value-type
            // arguments (unshared bodies). If a lifecycle event ever
            // becomes a class, the patch must drop it — and this test
            // makes that conversation happen at test time.
            foreach (var target in FuseMessengerIsolationPatch.TargetMethods())
            {
                var declaringType = target.DeclaringType;
                Assert.NotNull(declaringType);
                Assert.True(declaringType.IsGenericType, $"{target} is not on a constructed generic type");
                Assert.Equal(typeof(WeakAction<>), declaringType.GetGenericTypeDefinition());
                Assert.True(
                    declaringType.GetGenericArguments()[0].IsValueType,
                    $"{target} closes WeakAction<T> over a reference type — shared canonical bodies make that patch unreliable");
            }
        }

        // ---- finalizer contract (direct invocation, no Harmony apply) ----

        [Fact]
        public void Finalizer_NullException_ReturnsNull_AndDoesNotCount()
        {
            var before = FuseMessengerIsolationPatch.SuppressedExceptions;
            var weakAction = new WeakAction<MapDidLoadEvent>(this, _ => { });

            var result = FuseMessengerIsolationPatch.Finalizer(null, weakAction);

            Assert.Null(result);
            Assert.Equal(before, FuseMessengerIsolationPatch.SuppressedExceptions);
        }

        [Fact]
        public void Finalizer_SuppressesListenerException_AndCountsIt()
        {
            var before = FuseMessengerIsolationPatch.SuppressedExceptions;
            var weakAction = new WeakAction<MapDidLoadEvent>(this, OnMapDidLoadThatThrows);

            var result = FuseMessengerIsolationPatch.Finalizer(
                new InvalidOperationException("listener exploded"), weakAction);

            // Returning null is what tells Harmony to swallow the
            // exception so the dispatch loop never sees it.
            Assert.Null(result);
            Assert.Equal(before + 1, FuseMessengerIsolationPatch.SuppressedExceptions);
        }

        [Fact]
        public void Finalizer_SurvivesNullInstance()
        {
            // __instance should never be null for an instance-method
            // finalizer, but the diagnostics must hold up anyway — a
            // throw from the finalizer would be strictly worse than the
            // listener exception it replaces.
            var result = FuseMessengerIsolationPatch.Finalizer(
                new InvalidOperationException("listener exploded"), null);

            Assert.Null(result);
        }

        [Fact]
        public void DescribeListener_NamesRecipientTypeAndHandlerMethod()
        {
            // Pins the offender-naming promise against the REAL game type shape:
            // WeakAction<T> hides the base 'Action' property with a same-named
            // property of a different type, so a base-walking property lookup is
            // an ambiguous match and would collapse every description to the
            // fallback — the log would contain the suppression but never name
            // who threw, which is the patch's entire reason to exist.
            var weakAction = new WeakAction<MapDidLoadEvent>(this, OnMapDidLoadThatThrows);

            var description = FuseMessengerIsolationPatch.DescribeListener(weakAction);

            Assert.Contains(nameof(FuseMessengerIsolationPatchTests), description);
            Assert.Contains(nameof(OnMapDidLoadThatThrows), description);
            Assert.Contains(nameof(MapDidLoadEvent), description);
            Assert.DoesNotContain("undescribable", description);
        }

        [Fact]
        public void Finalizer_SurvivesForeignInstance()
        {
            // Defensive: DescribeListener reflects over the instance's
            // actual type, so hand it something that is not a WeakAction
            // at all and make sure suppression still happens cleanly.
            var result = FuseMessengerIsolationPatch.Finalizer(
                new InvalidOperationException("listener exploded"), new object());

            Assert.Null(result);
        }

        [Fact]
        public void Finalizer_LogsNewOffender_ThenThrottlesTheRepeat()
        {
            // The throttle promise: a previously-unseen offender is always surfaced
            // (even after the global first-5 budget is spent), but the SAME offender
            // on a later, non-heartbeat suppression is counted yet not logged.
            // SuppressedExceptions is a process-global counter the other tests also
            // advance, so drive it to a known residue first: residue 50 puts the two
            // calls below at xx51/xx52, clear of both the "<= 5" and "% 100 == 0"
            // branches no matter what order xUnit ran the suite in.
            while (FuseMessengerIsolationPatch.SuppressedExceptions % 100 != 50)
            {
                FuseMessengerIsolationPatch.Finalizer(new InvalidOperationException("warmup"), new object());
            }

            // A recipient type unique to this test, so its description is guaranteed
            // absent from the remembered-offender set (i.e. genuinely "new").
            var probe = new ThrottleProbe();
            var weakAction = new WeakAction<MapDidLoadEvent>(probe, probe.Handler);

            var loggedBeforeNew = FuseMessengerIsolationPatch.LoggedExceptions;
            FuseMessengerIsolationPatch.Finalizer(new InvalidOperationException("first sighting"), weakAction);
            var loggedAfterNew = FuseMessengerIsolationPatch.LoggedExceptions;

            FuseMessengerIsolationPatch.Finalizer(new InvalidOperationException("repeat"), weakAction);
            var loggedAfterRepeat = FuseMessengerIsolationPatch.LoggedExceptions;

            Assert.Equal(loggedBeforeNew + 1, loggedAfterNew);   // new offender surfaced
            Assert.Equal(loggedAfterNew, loggedAfterRepeat);     // repeat throttled away
        }

        private void OnMapDidLoadThatThrows(MapDidLoadEvent message)
        {
            throw new InvalidOperationException("listener exploded");
        }

        // Distinct recipient type so its DescribeListener output is unique to this
        // test and never collides with another test's remembered offender.
        private sealed class ThrottleProbe
        {
            public void Handler(MapDidLoadEvent message)
            {
            }
        }
    }
}
