using UnityEngine;

namespace FUSE.TestBridge
{
    /// <summary>Creates the persistent test bridge MonoBehaviour once (DontDestroyOnLoad).</summary>
    public static class FuseTestBridgeHost
    {
        private static GameObject _host;

        public static void Ensure(string modPath)
        {
            if (_host != null)
            {
                return;
            }

            _host = new GameObject("FUSE Test Bridge");
            Object.DontDestroyOnLoad(_host);
            var behaviour = _host.AddComponent<FuseTestBridgeBehaviour>();
            behaviour.Configure(modPath);
        }
    }
}
