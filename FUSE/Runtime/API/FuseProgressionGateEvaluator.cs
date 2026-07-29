using System.Collections.Generic;

namespace FUSE.Runtime.API
{
    /// <summary>
    /// Pure decision core behind
    /// <see cref="ProgressionAPI.IsGameObjectHiddenByLockedFeature(UnityEngine.GameObject)"/>:
    /// given each progression feature's lock state and the objects it gates, decides
    /// whether a target object is currently held hidden — i.e. referenced by at least one
    /// LOCKED feature. Extracted from the live <c>MapFeature</c> / <c>MapFeatureManager</c>
    /// walk so the locked-vs-unlocked filter, reference-identity match, and null-safety
    /// can be pinned by fast unit tests without a Unity scene, mirroring how
    /// <see cref="FuseSceneryDeferralClassifier"/> splits its pure type-name core from the
    /// game-resolved <c>CanDefer</c> path.
    /// </summary>
    internal static class FuseProgressionGateEvaluator
    {
        /// <summary>
        /// One feature's contribution to the decision: its unlock state and the objects
        /// it enables on unlock (the game's <c>gameObjectsEnableOnUnlock</c>). Objects are
        /// carried as <see cref="object"/> so the core is independent of UnityEngine types.
        /// </summary>
        internal readonly struct Gate
        {
            public Gate(bool unlocked, IReadOnlyList<object> gatedObjects)
            {
                Unlocked = unlocked;
                GatedObjects = gatedObjects;
            }

            public bool Unlocked { get; }

            public IReadOnlyList<object> GatedObjects { get; }
        }

        /// <summary>
        /// True when <paramref name="target"/> is referenced (by identity) in any gate
        /// whose feature is locked (<see cref="Gate.Unlocked"/> is false). Unlocked gates,
        /// a null target, a null gate set, and a null per-gate object list never hide
        /// anything, so anything not actively held by a locked feature is reported visible.
        ///
        /// The match is reference identity, never value equality: this mirrors the game's
        /// per-GameObject <c>SetActive</c> gating, where one instance being gated says
        /// nothing about a different instance that merely looks the same.
        /// </summary>
        internal static bool IsHiddenByLockedGate(object target, IEnumerable<Gate> gates)
        {
            if (target == null || gates == null)
            {
                return false;
            }

            foreach (var gate in gates)
            {
                if (gate.Unlocked)
                {
                    continue;
                }

                var gatedObjects = gate.GatedObjects;
                if (gatedObjects == null)
                {
                    continue;
                }

                for (var index = 0; index < gatedObjects.Count; index++)
                {
                    if (ReferenceEquals(gatedObjects[index], target))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
