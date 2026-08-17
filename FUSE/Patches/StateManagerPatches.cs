using System;
using Game.Messages;
using Game.State;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Runtime.Lifecycle;
using FUSE.Authoring.Migrations;

namespace FUSE.Patches
{
    [HarmonyPatch(typeof(StateManager), "PopulateFromRemoteSnapshot")]
    internal static class StateManagerPatches
    {
        // The snapshot is taken by value (struct) and the dictionaries inside
        // are reference types, so passing by ref lets us mutate the
        // dictionaries the game is about to read. game-migrations declared by
        // any loaded FUSE definition is applied here so renamed industries
        // and waybill destinations from older saves resolve before the game
        // tries to bind them.
        private static void Prefix(ref Snapshot snapshot, out IDisposable __state)
        {
            // The turntable restore callbacks occur inside this snapshot operation.
            // Hold their rebind requests until the world reaches the postfix so FUSE
            // builds its scene indexes once from settled state instead of four times
            // from intermediate states.
            __state = FuseRuntimeRebindService.BeginSnapshotRestore("after snapshot restore");

            try
            {
                FuseGameMigrationApplier.ApplyToSnapshot(ref snapshot, "before snapshot restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE game-migrations apply (prefix) failed.", ex);
            }

            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("before snapshot restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind (prefix) failed.", ex);
            }
        }

        private static void Postfix(IDisposable __state)
        {
            try
            {
                __state?.Dispose();
                FuseSnapshotTrackRebuildCoordinator.Flush();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind (postfix) failed.", ex);
            }
        }

        // Harmony postfixes do not run if the original method throws. Always close
        // the coalescing scope so a failed restore cannot leave every future rebind
        // permanently deferred. The scope is deliberately idempotent, so the
        // normal success path reaches this finalizer as a harmless no-op.
        private static Exception Finalizer(Exception __exception, IDisposable __state)
        {
            try
            {
                __state?.Dispose();
                if (__exception != null)
                {
                    FuseSnapshotTrackRebuildCoordinator.Cancel();
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind finalizer failed.", ex);
            }

            return __exception;
        }
    }
}
