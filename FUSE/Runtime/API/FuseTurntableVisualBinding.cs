using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    internal sealed class FuseTurntableVisualBinding : MonoBehaviour
    {
        public Turntable Turntable { get; set; }
        public Transform BridgeRoot { get; set; }

        private void LateUpdate()
        {
            Sync();
        }

        public void Sync()
        {
            if (Turntable == null || BridgeRoot == null)
            {
                return;
            }

            BridgeRoot.localRotation = Quaternion.Euler(0f, Turntable.Angle, 0f);
        }
    }
}
