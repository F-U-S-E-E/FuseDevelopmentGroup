using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseLog
    {
        // We keep the previous five sessions' logs as numbered archives so a
        // verbose-mode capture isn't destroyed the next time the game launches.
        // FUSE.log is the current session, FUSE-1.log .. FUSE-5.log are
        // archives (FUSE-1 most recent, FUSE-5 oldest), and anything past
        // FUSE-5 is dropped on rotation.
        private const int LogArchiveCount = 5;

        private static readonly object FileLock = new object();
        private static UnityModManager.ModEntry.ModLogger _logger;
        private static string _logFilePath;
        private static bool _fileLoggingAvailable;

        public static bool MirrorInfoToPlayerLog { get; set; }

        public static string LogFilePath => _logFilePath;

        public static void Initialize(UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
            InitializeFileLog();
        }

        public static void Info(string message)
        {
            WriteFile("INFO", message);
            if (MirrorInfoToPlayerLog)
            {
                _logger?.Log(message ?? string.Empty);
            }
        }

        public static void Warning(string message)
        {
            WriteFile("WARN", message);
            _logger?.Warning(message ?? string.Empty);
        }

        public static void Error(string message)
        {
            WriteFile("ERROR", message);
            _logger?.Error(message ?? string.Empty);
        }

        public static void Exception(string message, Exception exception)
        {
            var text = string.IsNullOrWhiteSpace(message) ? "Exception" : message;
            Error($"{text}: {exception}");
        }

        private static void InitializeFileLog()
        {
            try
            {
                var directory = Application.persistentDataPath;
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = AppDomain.CurrentDomain.BaseDirectory;
                }

                Directory.CreateDirectory(directory);
                _logFilePath = Path.Combine(directory, "FUSE.log");
                RotateExistingLogs(directory);
                File.WriteAllText(
                    _logFilePath,
                    $"FUSE log started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}{Environment.NewLine}");
                _fileLoggingAvailable = true;
                _logger?.Log($"FUSE file log: {_logFilePath}");
            }
            catch (Exception ex)
            {
                _fileLoggingAvailable = false;
                _logger?.Warning($"FUSE could not initialize FUSE.log: {ex.Message}");
            }
        }

        /// <summary>
        /// Shifts the previous session's FUSE.log into FUSE-1.log and bumps the
        /// existing numbered archives down by one (FUSE-1 → FUSE-2, etc.),
        /// dropping anything past FUSE-<see cref="LogArchiveCount"/>.log. Run once at
        /// log init so verbose captures from prior runs survive into the next
        /// session instead of being overwritten the moment the game restarts.
        /// All failures are swallowed — file logging must still come up even if
        /// a rename is blocked (e.g. by a tail open on the file in another
        /// process).
        /// </summary>
        private static void RotateExistingLogs(string directory)
        {
            try
            {
                // Drop the oldest archive (if any) so the shift below has
                // somewhere to put the second-oldest.
                var oldest = Path.Combine(directory, $"FUSE-{LogArchiveCount}.log");
                if (File.Exists(oldest))
                {
                    File.Delete(oldest);
                }

                // Walk the archive slots from second-oldest up to the most
                // recent and bump each one down by one number. Iterating in
                // descending order avoids clobbering a destination that we
                // haven't yet vacated.
                for (var slot = LogArchiveCount - 1; slot >= 1; slot--)
                {
                    var current = Path.Combine(directory, $"FUSE-{slot}.log");
                    var next = Path.Combine(directory, $"FUSE-{slot + 1}.log");
                    if (File.Exists(current))
                    {
                        // File.Move on .NET Framework throws if the destination
                        // exists; the descending loop above guarantees it does
                        // not, but be defensive in case a stray file is sitting
                        // in the slot.
                        if (File.Exists(next))
                        {
                            File.Delete(next);
                        }
                        File.Move(current, next);
                    }
                }

                // Finally, the most-recent previous session: FUSE.log → FUSE-1.log.
                if (File.Exists(_logFilePath))
                {
                    var firstArchive = Path.Combine(directory, "FUSE-1.log");
                    if (File.Exists(firstArchive))
                    {
                        File.Delete(firstArchive);
                    }
                    File.Move(_logFilePath, firstArchive);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"FUSE log rotation skipped: {ex.Message}");
            }
        }

        private static void WriteFile(string level, string message)
        {
            if (!_fileLoggingAvailable || string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(
                        _logFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message ?? string.Empty}{Environment.NewLine}");
                }
            }
            catch
            {
                _fileLoggingAvailable = false;
            }
        }
    }
}
