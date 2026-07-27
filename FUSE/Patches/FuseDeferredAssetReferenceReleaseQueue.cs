using System;
using System.Collections.Generic;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Patches
{
    /// <summary>
    /// Releases scenery asset references one frame after their instantiated model
    /// is scheduled for destruction. Unity defers <see cref="UnityEngine.Object.Destroy"/>
    /// until the end of the frame; disposing the reference immediately can unload
    /// assets while the old model is still alive.
    /// </summary>
    internal static class FuseDeferredAssetReferenceReleaseQueue
    {
        private sealed class PendingRelease
        {
            internal int EarliestFrame;
            internal IDisposable Reference;
        }

        private static readonly List<PendingRelease> Pending = new List<PendingRelease>();

        internal static int Count => Pending.Count;

        internal static void ReleaseAfterCurrentFrame(IDisposable reference)
        {
            if (reference == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                DisposeSafely(reference);
                return;
            }

            Pending.Add(new PendingRelease
            {
                EarliestFrame = Time.frameCount + 1,
                Reference = reference,
            });
        }

        internal static void Update()
        {
            if (Pending.Count == 0)
            {
                return;
            }

            var frame = Time.frameCount;
            for (var index = Pending.Count - 1; index >= 0; index--)
            {
                var release = Pending[index];
                if (frame < release.EarliestFrame)
                {
                    continue;
                }

                Pending.RemoveAt(index);
                DisposeSafely(release.Reference);
            }
        }

        internal static void DisposeSafely(IDisposable reference)
        {
            if (reference == null)
            {
                return;
            }

            try
            {
                reference.Dispose();
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE scenery asset-reference cleanup failed", ex);
            }
        }

        internal static void Shutdown()
        {
            for (var index = Pending.Count - 1; index >= 0; index--)
            {
                DisposeSafely(Pending[index].Reference);
            }

            Pending.Clear();
        }
    }
}
