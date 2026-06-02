using UnityEngine;

namespace FUSE.LiveBridge
{
    /// <summary>Creates the persistent bridge MonoBehaviour once (DontDestroyOnLoad).</summary>
    public static class FuseLiveBridgeHost
    {
        private static GameObject _host;

        public static void Ensure(string modPath)
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Live Bridge");
            Object.DontDestroyOnLoad(_host);
            var behaviour = _host.AddComponent<FuseLiveBridgeBehaviour>();
            behaviour.Configure(modPath);
        }
    }
}
