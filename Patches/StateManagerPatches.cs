using System;
using Game.Messages;
using Game.State;
using HarmonyLib;
using FUSE.Infrastructure;
using FUSE.Lifecycle;
using FUSE.Migrations;

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
        private static void Prefix(ref Snapshot snapshot)
        {
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

        private static void Postfix()
        {
            try
            {
                FuseRuntimeRebindService.RebindAfterSnapshot("after snapshot restore");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE snapshot rebind (postfix) failed.", ex);
            }
        }
    }
}
