using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using FUSE.Infrastructure;
using FUSE.Loading;
using HarmonyLib;
using Helpers;
using UI.Common;

namespace FUSE.Patches
{
    /// <summary>
    /// Bubbles permanently-failing scenery asset loads up to the user. The game's
    /// scenery loader faults the returned task when a pack's bundle does not
    /// contain an asset its catalog/definitions declare (or the bundle itself
    /// cannot load), logs to Player.log, and then retries the same load on every
    /// culling-band transition — forever, invisibly, with a hitch per retry
    /// cluster. The user never learns which pack is broken.
    ///
    /// This postfix watches every scenery load task and, on the first fault per
    /// asset identifier, records a <see cref="FuseLoadReport"/> entry (visible in
    /// FUSE Health → Issues, the main-menu status panel, and /fuse.report) and
    /// raises one toast per asset pack. Resolution of pack/owner names uses Unity
    /// APIs, so faults are queued from the task continuation (which may complete
    /// off the main thread) and drained on the main thread by
    /// <see cref="DrainPending"/>. Everything is fail-open: a failure inside this
    /// patch never affects the load path itself.
    /// </summary>
    [HarmonyPatch(typeof(SceneryAssetManager), nameof(SceneryAssetManager.LoadScenery))]
    internal static class FuseSceneryLoadFailurePatch
    {
        private static readonly ConcurrentQueue<PendingFailure> Pending = new ConcurrentQueue<PendingFailure>();

        // Identifiers already queued/recorded this map, so the game's endless
        // retries of the same broken asset don't grow the queue. Cleared by
        // FuseLifecycle at map-load start (see ResetForNewMap), in lockstep with
        // the load report registry this feeds.
        private static readonly HashSet<string> SeenIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SeenLock = new object();

        // One toast per asset pack, not per asset: a broken pack usually breaks
        // many assets at once and the report carries the full list.
        private static readonly HashSet<string> ToastedPacks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Distinct failing scenery assets recorded since startup (diagnostics).</summary>
        internal static long RecordedFailures => FuseRuntimeGuardCounters.SceneryLoadFailures;

        internal static void ResetForNewMap()
        {
            lock (SeenLock)
            {
                SeenIdentifiers.Clear();
            }

            ToastedPacks.Clear();
            while (Pending.TryDequeue(out _))
            {
            }

            // The bundle audit shares this dedupe lifecycle: its findings land in
            // the same report bucket, so both repopulate together after a reload.
            FuseAssetPackBundleAuditPatch.ResetForNewMap();
        }

        /// <summary>
        /// Entry point for the bundle audit: reports an asset a pack's
        /// Catalog.json declares but its bundle does not contain. Same dedupe,
        /// drain, report bucket, and per-pack toast as runtime load failures —
        /// an asset caught by both the audit and a later load attempt is
        /// recorded once. The pack is known at the call site, so no resolution
        /// fallback is needed.
        /// </summary>
        internal static void ReportCatalogMismatch(string identifier, string packIdentifier, string filename)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            var declaredAs = string.IsNullOrWhiteSpace(filename) ? identifier : filename;
            EnqueueFailure(
                identifier,
                $"declared in the pack's Catalog.json (filename '{declaredAs}') but not present in its bundle — " +
                "the pack needs its bundle rebuilt or the catalog entry (and content referencing it) removed",
                string.IsNullOrWhiteSpace(packIdentifier) ? null : packIdentifier);
        }

        internal static void Postfix(string identifier, Task __result)
        {
            if (__result == null || string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            try
            {
                __result.ContinueWith(
                    task => Enqueue(identifier, task.Exception),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                // Liveness counter (guards line): proves from a pasted report
                // whether this postfix is seeing scenery loads at all, since a
                // third-party loader replacing the load path is otherwise
                // indistinguishable from "no failures happened".
                FuseRuntimeGuardCounters.RecordSceneryLoadWatchAttached();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure watch could not attach", ex);
            }
        }

        private static void Enqueue(string identifier, AggregateException exception)
        {
            var message = exception != null
                ? exception.GetBaseException().Message
                : "asset load failed";
            EnqueueFailure(identifier, message, packIdentifier: null);
        }

        private static void EnqueueFailure(string identifier, string message, string packIdentifier)
        {
            try
            {
                lock (SeenLock)
                {
                    if (!SeenIdentifiers.Add(identifier))
                    {
                        return; // the game retries broken assets forever; report once.
                    }
                }

                Pending.Enqueue(new PendingFailure(identifier, message, packIdentifier));
            }
            catch (Exception ex)
            {
                // Never let reporting interfere with the load continuation chain.
                FuseLog.Exception("FUSE scenery load-failure watch could not queue a fault", ex);
            }
        }

        // ---- Game-log fallback net ----------------------------------------
        //
        // Both the vanilla scenery loader and load-replacing mods (observed in
        // the field with SceneryLoadRaceFix, whose SetLoaded replacement drives
        // the loads and swallows the faulted task after logging) emit the same
        // "Error loading scenery <identifier>" line when an asset load fails.
        // Hooking the Unity log stream and matching that prefix reports the
        // failure no matter which mod owns the load path or what it does with
        // the task. Feeds the same per-map dedupe as the task watch, so an
        // asset caught by both nets is still recorded once.

        private const string GameLogErrorPrefix = "Error loading scenery ";

        private static bool _logHookInstalled;

        internal static void EnsureGameLogHook()
        {
            if (_logHookInstalled)
            {
                return;
            }

            try
            {
                // The threaded variant sees messages from every thread; our
                // queue + dedupe are already thread-safe and the handler cost
                // is one prefix check per log message.
                UnityEngine.Application.logMessageReceivedThreaded += OnGameLogMessage;
                _logHookInstalled = true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure log hook could not install", ex);
            }
        }

        internal static void Shutdown()
        {
            if (!_logHookInstalled)
            {
                return;
            }

            try
            {
                UnityEngine.Application.logMessageReceivedThreaded -= OnGameLogMessage;
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure log hook could not uninstall", ex);
            }

            _logHookInstalled = false;
        }

        private static void OnGameLogMessage(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception)
            {
                return;
            }

            if (!TryParseSceneryLoadErrorLine(condition, out var identifier))
            {
                return;
            }

            EnqueueFailure(
                identifier,
                "the game logged 'Error loading scenery' for this asset (see Player.log for the exception; " +
                "usually the pack's bundle does not contain an asset its catalog declares)",
                packIdentifier: null);
        }

        /// <summary>
        /// Matches the loader's "Error loading scenery &lt;identifier&gt;" line
        /// (pure; unit-tested).
        /// </summary>
        internal static bool TryParseSceneryLoadErrorLine(string condition, out string identifier)
        {
            identifier = null;
            if (condition == null ||
                !condition.StartsWith(GameLogErrorPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var value = condition.Substring(GameLogErrorPrefix.Length).Trim();
            if (value.Length == 0)
            {
                return false;
            }

            identifier = value;
            return true;
        }

        /// <summary>Queued-but-undrained failure count (test hook).</summary>
        internal static int PendingCountForTests => Pending.Count;

        /// <summary>
        /// Resolves and records queued failures. Main thread only (touches Unity
        /// object queries and the toast UI); driven by FuseHealthUi.Update.
        /// </summary>
        internal static void DrainPending()
        {
            while (Pending.TryDequeue(out var failure))
            {
                try
                {
                    Record(failure);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE could not record scenery load failure for '{failure.Identifier}'", ex);
                }
            }
        }

        private static void Record(PendingFailure failure)
        {
            // Audit-sourced failures carry their pack; runtime faults resolve it.
            var pack = failure.PackIdentifier ?? ResolvePackIdentifier(failure.Identifier);
            var owner = ResolveOwnerPackage(failure.Identifier);

            if (!FuseLoadReport.RecordSceneryLoadFailure(failure.Identifier, pack, owner, failure.Message))
            {
                return;
            }

            FuseRuntimeGuardCounters.RecordSceneryLoadFailure();
            FuseLog.Error(
                $"FUSE scenery asset '{failure.Identifier}' is failing to load and will keep failing on " +
                $"every retry: pack='{pack}' package='{owner}' reason='{failure.Message}'. The pack's " +
                "bundle likely does not contain an asset its catalog declares.");

            if (ToastedPacks.Add(pack))
            {
                try
                {
                    Toast.Present(
                        $"FUSE: assets in pack '{pack}' are failing to load - first: '{failure.Identifier}'. " +
                        "See FUSE Health > Issues.",
                        ToastPosition.Middle);
                }
                catch (Exception ex)
                {
                    FuseLog.Exception("FUSE could not display scenery load-failure toast", ex);
                }
            }
        }

        private static string ResolvePackIdentifier(string identifier)
        {
            try
            {
                var prefabStore = TrainController.Shared?.PrefabStore;
                var pack = prefabStore?.AssetPackIdentifierContainingDefinition(identifier);
                return string.IsNullOrWhiteSpace(pack) ? "<unknown>" : pack;
            }
            catch
            {
                return "<unknown>"; // no mounted store claims the identifier.
            }
        }

        private static string ResolveOwnerPackage(string identifier)
        {
            try
            {
                // Only FUSE-created scenery carries a marker; vanilla/asset-pack
                // scenery legitimately resolves to no owner.
                foreach (var marker in UnityEngine.Object.FindObjectsOfType<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>(true))
                {
                    if (marker == null)
                    {
                        continue;
                    }

                    var scenery = marker.GetComponent<SceneryAssetInstance>();
                    if (scenery == null ||
                        !string.Equals(scenery.identifier, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var owner = FUSE.Runtime.Registry.FuseRegistry.GetExclusiveOwner(
                        FUSE.Runtime.Registry.FuseClaimKind.Scenery, marker.Id);
                    if (!string.IsNullOrWhiteSpace(owner))
                    {
                        return owner;
                    }
                }
            }
            catch (Exception ex)
            {
                // Attribution is best-effort; the asset identifier alone is actionable.
                FuseLog.Exception(
                    $"FUSE could not attribute failing scenery asset '{identifier}' to a package", ex);
            }

            return "<unknown>";
        }

        private readonly struct PendingFailure
        {
            internal PendingFailure(string identifier, string message, string packIdentifier)
            {
                Identifier = identifier;
                Message = message;
                PackIdentifier = packIdentifier;
            }

            internal string Identifier { get; }

            internal string Message { get; }

            /// <summary>Known owning pack, or null to resolve at record time.</summary>
            internal string PackIdentifier { get; }
        }
    }
}
