using System;
using UnityEngine;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Coordinates map-mask refresh and terrain re-bake during a bulk apply that the
    /// caller guarantees is followed by exactly one terrain rebuild
    /// (<c>FuseRuntimeReloadService.ReloadTerrain</c> at the end of
    /// <c>FuseLifecycle.OnMapDidLoad</c>).
    ///
    /// Two optimizations hang off an active scope:
    ///  1. <b>Mask-refresh deferral (#3).</b> Per-object
    ///     <c>MapAPI.RefreshAttachedMapMasks</c> calls made during the apply are
    ///     skipped — the single trailing terrain rebuild re-evaluates every live mask
    ///     component at once, so the per-object <c>GetComponentsInChildren</c> +
    ///     modifier churn is redundant. (Outside a scope — e.g. a runtime
    ///     single-object API call with no trailing rebuild — the per-object refresh
    ///     still runs, so behavior there is unchanged.)
    ///  2. <b>Bounds accumulation (#4, opt-in).</b> The world-space footprints touched
    ///     during apply are unioned so the trailing rebuild can optionally be narrowed
    ///     to just those tiles instead of tearing down and re-streaming the whole map.
    ///
    /// The scope is a simple main-thread depth counter (apply is single-threaded);
    /// dispose the token returned by <see cref="Begin"/> in a finally to close it.
    /// </summary>
    internal static class FuseTerrainRefreshScope
    {
        private static int _depth;
        private static int _deferredRefreshCalls;
        private static int _deferredMaskComponents;
        private static bool _haveBounds;
        private static bool _boundsIncomplete;
        private static Bounds _bounds;

        /// <summary>True while a bulk-apply scope is open (mask refresh is deferred).</summary>
        internal static bool IsDeferringMaskRefresh => _depth > 0;

        /// <summary>RefreshAttachedMapMasks calls deferred during the current/last scope.</summary>
        internal static int DeferredRefreshCalls => _deferredRefreshCalls;

        /// <summary>Mask components deferred during the current/last scope.</summary>
        internal static int DeferredMaskComponents => _deferredMaskComponents;

        /// <summary>
        /// True only when an accurate footprint was captured for <em>every</em> deferred
        /// object — i.e. some bounds were recorded and nothing was flagged unbounded.
        /// Targeted terrain invalidation may run only when this holds; otherwise the
        /// trailing reload must do a full rebuild, since narrowing to a footprint that
        /// doesn't actually cover a mask would leave dark, uncut terrain.
        /// </summary>
        internal static bool BoundsComplete => _haveBounds && !_boundsIncomplete;

        /// <summary>
        /// Opens a deferral scope. The outermost <see cref="Begin"/> resets the
        /// deferral counters and accumulated bounds; nested calls just increase depth.
        /// Always dispose the returned token (use a <c>using</c> / finally).
        /// </summary>
        internal static IDisposable Begin()
        {
            if (_depth == 0)
            {
                _deferredRefreshCalls = 0;
                _deferredMaskComponents = 0;
                _haveBounds = false;
                _boundsIncomplete = false;
                _bounds = default(Bounds);
            }

            _depth++;
            return new Token();
        }

        /// <summary>Records that a per-object mask refresh of <paramref name="maskComponentCount"/> components was deferred.</summary>
        internal static void NoteDeferredRefresh(int maskComponentCount)
        {
            _deferredRefreshCalls++;
            if (maskComponentCount > 0)
            {
                _deferredMaskComponents += maskComponentCount;
            }
        }

        /// <summary>Unions <paramref name="worldBounds"/> into the accumulated footprint (#4).</summary>
        internal static void RecordBounds(Bounds worldBounds)
        {
            if (_haveBounds)
            {
                _bounds.Encapsulate(worldBounds);
            }
            else
            {
                _bounds = worldBounds;
                _haveBounds = true;
            }
        }

        /// <summary>
        /// Flags that a deferred object's footprint could not be measured accurately
        /// (e.g. its model/masks haven't streamed in yet). Forces the trailing reload
        /// back to a full rebuild — see <see cref="BoundsComplete"/>.
        /// </summary>
        internal static void MarkBoundsIncomplete()
        {
            _boundsIncomplete = true;
        }

        /// <summary>Returns the accumulated world-space footprint touched during apply, if any (#4).</summary>
        internal static bool TryGetAccumulatedBounds(out Bounds worldBounds)
        {
            worldBounds = _bounds;
            return _haveBounds;
        }

        private static void End()
        {
            if (_depth > 0)
            {
                _depth--;
            }
        }

        private sealed class Token : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                End();
            }
        }
    }
}
