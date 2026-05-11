using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace FUSE.Infrastructure
{
    public static class FuseLog
    {
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
