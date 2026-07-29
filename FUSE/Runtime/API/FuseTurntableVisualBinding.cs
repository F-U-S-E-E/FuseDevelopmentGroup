using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    internal sealed class FuseTurntableVisualBinding : MonoBehaviour
    {
        private float _lastAngle;
        private bool _hasLastAngle;

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

            var angle = Turntable.Angle;
            if (_hasLastAngle && angle == _lastAngle)
            {
                return;
            }

            BridgeRoot.localRotation = Quaternion.Euler(0f, angle, 0f);
            _lastAngle = angle;
            _hasLastAngle = true;
        }
    }
}
