using FUSE.Authoring.Data;
using Track;
using UnityEngine;

namespace FUSE.Runtime.API
{
    public interface IFuseTurntableController
    {
        void Configure(Turntable turntable, Transform bridgeRoot, FuseTurntable definition);
    }
}
