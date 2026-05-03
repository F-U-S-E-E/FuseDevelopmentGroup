using System;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseLog
    {
        private static UnityModManager.ModEntry.ModLogger _logger;

        public static void Initialize(UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
        }

        public static void Info(string message)
        {
            _logger?.Log(message ?? string.Empty);
        }

        public static void Warning(string message)
        {
            _logger?.Warning(message ?? string.Empty);
        }

        public static void Error(string message)
        {
            _logger?.Error(message ?? string.Empty);
        }

        public static void Exception(string message, Exception exception)
        {
            var text = string.IsNullOrWhiteSpace(message) ? "Exception" : message;
            _logger?.Error($"{text}: {exception}");
        }
    }
}
