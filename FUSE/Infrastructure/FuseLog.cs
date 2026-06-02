using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
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

        private static UnityModManager.ModEntry.ModLogger _logger;
        private static string _logFilePath;
        private static volatile bool _fileLoggingAvailable;
        // The log file is opened once and written only by the background worker
        // thread below. Callers never touch the file: they format a line, hand it
        // to _queue, and return immediately, so logging adds no file I/O to
        // Unity's main thread during a map load. See InitializeFileLog.
        private static StreamWriter _writer;
        private static BlockingCollection<string> _queue;
        private static Thread _worker;

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

                if (_fileLoggingAvailable)
                {
                    // Already initialized; don't open a second handle or start a
                    // second worker thread.
                    return;
                }

                Directory.CreateDirectory(directory);
                _logFilePath = Path.Combine(directory, "FUSE.log");
                RotateExistingLogs(directory);

                // Open the session log once and write it only from a dedicated
                // background thread. The previous implementation wrote on the
                // calling (main) thread; a heavy map load emits tens of thousands
                // of lines, and each file write — plus the antivirus scan it can
                // trigger — blocked the loading screen. Now callers just enqueue a
                // formatted string and the worker drains _queue to disk off the
                // main thread. FileShare.ReadWrite keeps the file tailable and
                // readable by the in-game Logs tab; AutoFlush flushes each line as
                // the worker writes it, so a crash loses at most the handful of
                // lines still sitting in the queue.
                var stream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                _writer.WriteLine($"FUSE log started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");

                _queue = new BlockingCollection<string>();
                _worker = new Thread(ProcessQueue)
                {
                    IsBackground = true,
                    Name = "FUSE.LogWriter"
                };
                _worker.Start();

                _fileLoggingAvailable = true;
                Application.quitting += Shutdown;
                _logger?.Log($"FUSE file log: {_logFilePath}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.Warning($"FUSE log rotation skipped: {ex.Message}");
            }
        }

        private static void WriteFile(string level, string message)
        {
            if (!_fileLoggingAvailable)
            {
                return;
            }

            var queue = _queue;
            if (queue == null || queue.IsAddingCompleted)
            {
                return;
            }

            // Format on the calling thread so the timestamp reflects when the
            // event happened, then hand the line to the worker. Add() on an
            // unbounded BlockingCollection is a short, lock-protected enqueue —
            // no file I/O runs on the caller.
            try
            {
                queue.Add($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message ?? string.Empty}");
            }
            catch (InvalidOperationException)
            {
                // The queue was completed (shutdown) between the check above and
                // the Add; drop the line rather than throw on the caller.
            }
        }

        // Drains the queue to disk on a dedicated background thread. A single
        // consumer keeps lines in FIFO order; AutoFlush on the writer makes each
        // line durable as soon as it is written.
        private static void ProcessQueue()
        {
            try
            {
                foreach (var line in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        _writer.WriteLine(line);
                    }
                    catch
                    {
                        // Swallow a single failed write so one bad line doesn't
                        // tear down logging for the rest of the session.
                    }
                }
            }
            catch
            {
                // GetConsumingEnumerable only ends via CompleteAdding; guard
                // against unexpected enumeration failures so the worker thread
                // exits cleanly instead of crashing.
            }
        }

        // Flushes any queued lines on a clean application quit. Not guaranteed to
        // run on a hard crash, which is why the worker flushes every line as it
        // goes — at worst a crash loses the few lines still in the queue.
        private static void Shutdown()
        {
            try
            {
                _fileLoggingAvailable = false;
                _queue?.CompleteAdding();
                _worker?.Join(TimeSpan.FromSeconds(2));
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // Best-effort flush on quit; never throw out of a shutdown hook.
            }
            finally
            {
                _writer = null;
            }
        }
    }
}
