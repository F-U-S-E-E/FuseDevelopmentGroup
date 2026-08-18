namespace Fuse.Core.Versioning
{
    /// <summary>
    /// A monotonic "operation generation" used to discard results from a
    /// superseded asynchronous check. Each <see cref="Begin"/> starts a new
    /// generation and supersedes the prior one; a result tagged with a generation
    /// is only valid while <see cref="IsCurrent"/> holds for it. An older response
    /// that completes out of order — after a newer check has already started — is
    /// therefore rejected instead of overwriting the newer result.
    ///
    /// Not thread-safe by design: FUSE drives it from the Unity main thread only
    /// (the update-check coroutine and its callers all run there), so no lock is
    /// needed and none is taken. Callers that touch it from other threads must
    /// serialize access themselves.
    /// </summary>
    public sealed class FuseGenerationGate
    {
        private int _current;

        /// <summary>The newest generation started, or 0 before the first <see cref="Begin"/>.</summary>
        public int Current => _current;

        /// <summary>Starts a new generation, supersedes any prior one, and returns its token.</summary>
        public int Begin() => ++_current;

        /// <summary>True while <paramref name="generation"/> is still the newest generation started.</summary>
        public bool IsCurrent(int generation) => generation == _current;

        /// <summary>Resets to the initial state (no generation started).</summary>
        public void Reset() => _current = 0;
    }
}
