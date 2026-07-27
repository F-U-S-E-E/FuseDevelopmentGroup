using System;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Runtime.Lifecycle
{
    /// <summary>
    /// Creates texture-memory headroom on constrained graphics cards.
    /// A one-level mip limit halves texture dimensions (approximately one
    /// quarter of the top-level texel memory) and is reversible on unload.
    /// </summary>
    internal static class FuseConstrainedTextureMemoryPolicy
    {
        internal const int ConstrainedMipmapLimit = 1;
        // SystemInfo reports dedicated graphics memory in megabytes on the
        // Windows/D3D player. The tolerance above 8 GiB covers reporting variance.
        internal const int ConstrainedGraphicsMemoryThresholdMb = 9 * 1024;

        private static bool _applied;
        private static int _previousMipmapLimit;
        private static bool _previousDiscardUnusedMips;
        private static int _appliedMipmapLimit;

        internal static bool IsApplied => _applied;

        internal static int EffectiveMipmapLimit(int currentLimit)
        {
            return Math.Max(currentLimit, ConstrainedMipmapLimit);
        }

        internal static void ApplyIfNeeded()
        {
            if (_applied || !ShouldConstrainTextures(
                    SystemInfo.graphicsMemorySize,
                    FuseSettings.ForceConstrainedVramMode))
            {
                return;
            }

            var previousLimit = QualitySettings.globalTextureMipmapLimit;
            var previousDiscardUnusedMips = Texture.streamingTextureDiscardUnusedMips;
            var targetLimit = EffectiveMipmapLimit(previousLimit);

            try
            {
                _previousMipmapLimit = previousLimit;
                _previousDiscardUnusedMips = previousDiscardUnusedMips;
                _appliedMipmapLimit = targetLimit;

                QualitySettings.globalTextureMipmapLimit = targetLimit;
                Texture.streamingTextureDiscardUnusedMips = true;
                _applied = true;

                FuseLog.Info(
                    "FUSE constrained texture-memory policy enabled: " +
                    $"graphicsMemoryMB={SystemInfo.graphicsMemorySize} " +
                    $"globalTextureMipmapLimit={previousLimit}->{targetLimit} " +
                    $"discardUnusedMips={previousDiscardUnusedMips}->True. " +
                    "One mip level halves texture width and height to create VRAM headroom; " +
                    "normal scenery distance bands remain enabled.");
            }
            catch (Exception ex)
            {
                TryRestore(previousLimit, previousDiscardUnusedMips);
                _applied = false;
                FuseLog.Exception("FUSE could not enable the constrained texture-memory policy", ex);
            }
        }

        internal static void Restore()
        {
            if (!_applied)
            {
                return;
            }

            try
            {
                // Do not overwrite a later in-game/user texture-quality change.
                if (QualitySettings.globalTextureMipmapLimit == _appliedMipmapLimit)
                {
                    QualitySettings.globalTextureMipmapLimit = _previousMipmapLimit;
                }

                if (Texture.streamingTextureDiscardUnusedMips)
                {
                    Texture.streamingTextureDiscardUnusedMips = _previousDiscardUnusedMips;
                }

                FuseLog.Info(
                    "FUSE constrained texture-memory policy restored the prior texture settings.");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE failed to restore the prior texture-memory settings", ex);
            }
            finally
            {
                _applied = false;
            }
        }

        internal static bool ShouldConstrainTextures(
            int graphicsMemoryMb,
            bool forceConstrained)
        {
            return forceConstrained ||
                   IsConstrainedGraphicsMemory(graphicsMemoryMb);
        }

        internal static bool IsConstrainedGraphicsMemory(int graphicsMemoryMb)
        {
            return graphicsMemoryMb > 0 &&
                   graphicsMemoryMb <= ConstrainedGraphicsMemoryThresholdMb;
        }

        private static void TryRestore(int mipmapLimit, bool discardUnusedMips)
        {
            try
            {
                QualitySettings.globalTextureMipmapLimit = mipmapLimit;
                Texture.streamingTextureDiscardUnusedMips = discardUnusedMips;
            }
            catch (Exception restoreException)
            {
                FuseLog.Exception(
                    "FUSE texture-memory policy rollback also failed",
                    restoreException);
            }
        }
    }
}
