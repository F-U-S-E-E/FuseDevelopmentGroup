using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Always-on main-thread host for queue consumers that must not depend on
    /// an optional UI component existing. Idle work is limited to lock-free
    /// queue-empty checks in the consumers.
    /// </summary>
    internal static class FuseRuntimePump
    {
        private static GameObject _host;

        internal static void EnsureStarted()
        {
            if (_host != null)
            {
                return;
            }

            GameObject host = null;
            try
            {
                host = new GameObject("FUSE.RuntimePump");
                host.hideFlags = HideFlags.HideAndDontSave;
                host.AddComponent<FuseRuntimePumpRunner>();
                UnityEngine.Object.DontDestroyOnLoad(host);
                _host = host;
                FuseLog.Info("FUSE runtime pump initialized.");
            }
            catch (Exception ex)
            {
                if (host != null)
                {
                    UnityEngine.Object.Destroy(host);
                }

                FuseLog.Exception("FUSE runtime pump host creation failed", ex);
            }
        }

        internal static void Shutdown()
        {
            if (_host == null)
            {
                return;
            }

            try
            {
                UnityEngine.Object.Destroy(_host);
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE runtime pump shutdown failed", ex);
            }
            finally
            {
                _host = null;
            }
        }

        private sealed class FuseRuntimePumpRunner : MonoBehaviour
        {
            // slopwatch-ignore: SW002 Unity invokes Update as an instance message, so CA1822 suppression is required.
            [System.Diagnostics.CodeAnalysis.SuppressMessage(
                "Performance", "CA1822:Mark members as static",
                Justification = "Unity invokes Update() as an instance message; a static method is never called.")]
            private void Update()
            {
                FUSE.Patches.FuseSceneryLoadFailurePatch.DrainPending();
            }
        }
    }
}
