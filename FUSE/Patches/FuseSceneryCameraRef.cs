using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// One shared cache of the scenery culler's distance reference (Camera.main),
    /// used by both the cull debounce and the load throttle so they always measure
    /// against the same camera. Refreshed when the cached camera is destroyed OR
    /// merely disabled — a camera-mode / scene transition leaves the old gameplay
    /// camera disabled but not destroyed, so a <c>== null</c> check alone would keep a
    /// stale reference and let the hold / drop-stale decisions misjudge distance after
    /// the player changes views or teleports.
    /// </summary>
    internal static class FuseSceneryCameraRef
    {
        private static Camera _camera;

        /// <summary>Camera.main, re-fetched when the cached one is gone or inactive.</summary>
        internal static Camera Resolve()
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
            {
                _camera = Camera.main;
            }

            return _camera;
        }

        /// <summary>Drops the cached camera (mod unload / throttle teardown).</summary>
        internal static void Reset()
        {
            _camera = null;
        }
    }
}
