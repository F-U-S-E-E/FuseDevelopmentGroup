using FUSE.Infrastructure;
using HarmonyLib;
using Model.Database;

namespace FUSE.Patches
{
    /// <summary>
    /// Removes PrefabStore's startup-only diagnostic validation pass.
    ///
    /// The game has already opened every store before this private method runs.
    /// DefinitionChecker only builds warning/error strings and writes them to
    /// Player.log; ordinary validation failures do not reject a definition.
    /// The stock method removes a store only if enumerating its already-open
    /// container throws. FUSE's store loading and per-asset error paths remain
    /// active, so malformed assets still fail at their actual point of use.
    /// </summary>
    [HarmonyPatch(typeof(PrefabStore), "CheckDefinitions")]
    internal static class FusePrefabStoreDefinitionCheckPerformancePatch
    {
        private static bool _logged;

        private static bool Prefix()
        {
            if (!_logged)
            {
                _logged = true;
                FuseLog.Info(
                    "FUSE skipped PrefabStore's startup-only definition diagnostic scan; " +
                    "asset-store discovery and runtime load validation remain enabled.");
            }

            return false;
        }
    }
}
