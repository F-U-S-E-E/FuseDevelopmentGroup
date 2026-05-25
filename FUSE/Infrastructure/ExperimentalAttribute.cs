using System;
using System.Collections.Generic;

namespace FUSE.Infrastructure
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property)]
    internal sealed class ExperimentalAttribute : Attribute
    {
        public ExperimentalAttribute(string note = null)
        {
            Note = note ?? string.Empty;
        }

        public string Note { get; }
    }

    /// <summary>
    /// Once-per-session warning helper for experimental APIs. Pair with
    /// <see cref="ExperimentalAttribute"/> on the surface itself; call
    /// <see cref="WarnFirstUse"/> from the implementation so the user sees
    /// the notice exactly once even if the API is invoked many times.
    /// </summary>
    public static class FuseExperimentalLog
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<string> WarnedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static void WarnFirstUse(string key, string note = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            lock (Sync)
            {
                if (!WarnedKeys.Add(key))
                {
                    return;
                }
            }

            var detail = string.IsNullOrWhiteSpace(note) ? string.Empty : $" note='{note}'";
            FuseLog.Warning(
                $"FUSE experimental surface in use: '{key}'.{detail} " +
                "This API may change or be removed without notice.");
        }

        public static void Reset()
        {
            lock (Sync)
            {
                WarnedKeys.Clear();
            }
        }
    }
}
