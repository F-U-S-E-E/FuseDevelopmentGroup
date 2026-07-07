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
        // FuseLoadReport.ResetMapLoad alongside the report registry it feeds.
        private static readonly HashSet<string> SeenIdentifiers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SeenLock = new object();

        // One toast per asset pack, not per asset: a broken pack usually breaks
        // many assets at once and the report carries the full list.
        private static readonly HashSet<string> ToastedPacks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static long _recorded;

        /// <summary>Distinct failing scenery assets recorded since startup (diagnostics).</summary>
        internal static long RecordedFailures => _recorded;

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
        }

        private static void Postfix(string identifier, Task __result)
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
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery load-failure watch could not attach", ex);
            }
        }

        private static void Enqueue(string identifier, AggregateException exception)
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

                var message = exception != null
                    ? exception.GetBaseException().Message
                    : "asset load failed";
                Pending.Enqueue(new PendingFailure(identifier, message));
            }
            catch (Exception ex)
            {
                // Never let reporting interfere with the load continuation chain.
                FuseLog.Exception("FUSE scenery load-failure watch could not queue a fault", ex);
            }
        }

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
            var pack = ResolvePackIdentifier(failure.Identifier);
            var owner = ResolveOwnerPackage(failure.Identifier);

            if (!FuseLoadReport.RecordSceneryLoadFailure(failure.Identifier, pack, owner, failure.Message))
            {
                return;
            }

            _recorded++;
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
            internal PendingFailure(string identifier, string message)
            {
                Identifier = identifier;
                Message = message;
            }

            internal string Identifier { get; }

            internal string Message { get; }
        }
    }
}
