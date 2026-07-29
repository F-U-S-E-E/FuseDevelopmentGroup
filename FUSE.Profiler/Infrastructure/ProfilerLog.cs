using System;

namespace FUSE.Profiler.Infrastructure
{
    /// <summary>
    /// Thin logging seam: routes through the UnityModManager mod logger when
    /// bound (so lines land in the UMM log with the mod prefix), and falls
    /// back to the Unity player log otherwise (including in unit tests,
    /// where UnityEngine.Debug is unavailable and the write is swallowed).
    /// </summary>
    internal static class ProfilerLog
    {
        private static Action<string> _info;
        private static Action<string> _warning;
        private static Action<string> _error;

        internal static void Bind(Action<string> info, Action<string> warning, Action<string> error)
        {
            _info = info;
            _warning = warning;
            _error = error;
        }

        internal static void Info(string message)
        {
            Write(_info, message, isError: false);
        }

        internal static void Warning(string message)
        {
            Write(_warning, message, isError: false);
        }

        internal static void Error(string message)
        {
            Write(_error, message, isError: true);
        }

        internal static void Exception(string context, Exception ex)
        {
            Write(_error, context + ": " + ex, isError: true);
        }

        /// <summary>
        /// The most recent sink failure, kept observable because the logger
        /// has nowhere left to report its own errors (and under the
        /// unit-test runner the UnityEngine fallback is unavailable by
        /// design). A logger must never take its caller down.
        /// </summary>
        internal static Exception LastLoggingFailure;

        private static void Write(Action<string> sink, string message, bool isError)
        {
            try
            {
                if (sink != null)
                {
                    sink(message);
                    return;
                }

                if (isError)
                {
                    UnityEngine.Debug.LogError("[FUSE.Profiler] " + message);
                }
                else
                {
                    UnityEngine.Debug.Log("[FUSE.Profiler] " + message);
                }
            }
            catch (Exception ex)
            {
                LastLoggingFailure = ex;
            }
        }
    }
}
