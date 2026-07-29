using System;
using System.Threading;

namespace FUSE.Patches
{
    /// <summary>
    /// Bounds outstanding scenery asset tasks. A generation-tagged lease makes a
    /// late task continuation harmless after a map reset or mod shutdown.
    /// </summary>
    internal sealed class FuseSceneryLoadConcurrencyGate
    {
        internal sealed class Lease : IDisposable
        {
            private FuseSceneryLoadConcurrencyGate _owner;
            private readonly int _generation;
            private int _released;

            internal Lease(FuseSceneryLoadConcurrencyGate owner, int generation)
            {
                _owner = owner;
                _generation = generation;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                {
                    return;
                }

                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Release(_generation);
            }
        }

        private readonly object _sync = new object();
        private readonly int _limit;
        private int _generation;
        private int _active;
        private int _peak;

        internal FuseSceneryLoadConcurrencyGate(int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }

            _limit = limit;
        }

        internal int Limit => _limit;

        internal int Active
        {
            get
            {
                lock (_sync)
                {
                    return _active;
                }
            }
        }

        internal int Peak
        {
            get
            {
                lock (_sync)
                {
                    return _peak;
                }
            }
        }

        internal bool HasCapacity
        {
            get
            {
                lock (_sync)
                {
                    return _active < _limit;
                }
            }
        }

        internal bool TryAcquire(out Lease lease)
        {
            lock (_sync)
            {
                if (_active >= _limit)
                {
                    lease = null;
                    return false;
                }

                _active++;
                if (_active > _peak)
                {
                    _peak = _active;
                }

                lease = new Lease(this, _generation);
                return true;
            }
        }

        internal void ResetPeak()
        {
            lock (_sync)
            {
                _peak = _active;
            }
        }

        internal void Reset()
        {
            lock (_sync)
            {
                unchecked
                {
                    _generation++;
                }

                _active = 0;
                _peak = 0;
            }
        }

        private void Release(int generation)
        {
            lock (_sync)
            {
                if (generation != _generation || _active == 0)
                {
                    return;
                }

                _active--;
            }
        }
    }
}
