using FUSE.Data;
using Track;
using UnityEngine;

namespace FUSE.API
{
    public interface IFuseTurntableController
    {
        void Configure(Turntable turntable, Transform bridgeRoot, FuseTurntable definition);
    }
}
