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
            // Tracks the writer until ownership transfers to the session so the catch
            // can dispose it (and its file handle) if WriteLine/Flush throws after
            // creation — otherwise a failed init leaks the handle and blocks a clean
            // retry. Kept DISTINCT from the lambda-captured 'writer' local below,
            // which must NOT be nulled (the worker thread closes over it).
            StreamWriter pendingWriter = null;
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
                // readable by the in-game Logs tab. The worker flushes when the
                // queue drains and at least every FlushEveryLines lines (not per
                // line), so a heavy burst amortizes I/O and the consumer can't fall
                // behind the producer; an idle logger still persists promptly.
                var stream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(stream) { AutoFlush = false };
                pendingWriter = writer;
                writer.WriteLine($"FUSE log started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
                writer.Flush();

                var queue = new BlockingCollection<string>();
                _writer = writer;
                _queue = queue;
                // Bind the worker to the instances created here (passed as arguments)
                // rather than the mutable statics, so a later re-init that swaps
                // _queue/_writer can never make this worker read or write a different
                // session's queue/writer.
                _worker = new Thread(() => ProcessQueue(queue, writer))
                {
                    IsBackground = true,
                    Name = "FUSE.LogWriter"
                };
                _worker.Start();

                // Ownership transferred to the session (statics + the worker, which
                // closes over 'writer'); stop tracking it for failure disposal so the
                // catch below leaves the live writer alone.
                pendingWriter = null;

                _fileLoggingAvailable = true;
                // Idempotent: Shutdown resets _fileLoggingAvailable, so a later
                // re-init would otherwise stack a second handler on Application.quitting.
                Application.quitting -= Shutdown;
                Application.quitting += Shutdown;
                _logger?.Log($"FUSE file log: {_logFilePath}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Dispose the half-initialized writer (and its file handle) so a
                // failed init doesn't leak the handle and block a later retry.
                try
                {
                    pendingWriter?.Dispose();
                }
                catch
                {
                    // Best effort.
                }

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
            var timestamp = DateTime.Now;
            var normalizedMessage = message ?? string.Empty;
            var line = $"[{timestamp:HH:mm:ss.fff}] [{level}] {normalizedMessage}";
            FuseLiveLogBuffer.Append(timestamp, level, normalizedMessage);
            FuseLiveConsole.WriteLine(line);

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
                queue.Add(line);
            }
            catch (InvalidOperationException)
            {
                // The queue was completed (shutdown) between the check above and
                // the Add; drop the line rather than throw on the caller.
            }
        }

        // Flush at least this often during a sustained burst so the crash-loss
        // window stays bounded even when the queue never momentarily drains.
        private const int FlushEveryLines = 64;

        // Drains the queue to disk on a dedicated background thread. A single
        // consumer keeps lines in FIFO order. The writer is not AutoFlush: we flush
        // whenever the queue drains (prompt persistence when idle) and every
        // FlushEveryLines lines (bounded loss under a sustained burst), so a per-line
        // flush can't let the consumer fall behind the producer. The queue/writer are
        // passed in (bound at thread creation) so this worker is never affected by a
        // later re-init swapping the statics.
        private static void ProcessQueue(BlockingCollection<string> queue, StreamWriter writer)
        {
            var sinceFlush = 0;
            try
            {
                foreach (var line in queue.GetConsumingEnumerable())
                {
                    try
                    {
                        writer.WriteLine(line);
                        sinceFlush++;
                        if (queue.Count == 0 || sinceFlush >= FlushEveryLines)
                        {
                            writer.Flush();
                            sinceFlush = 0;
                        }
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
            finally
            {
                // Final flush so the tail written since the last periodic flush
                // reaches disk when the queue completes on shutdown.
                try
                {
                    writer.Flush();
                }
                catch
                {
                    // Best effort on the way out.
                }
            }
        }

        // Flushes any queued lines on a clean application quit. Not guaranteed to
        // run on a hard crash, which is why the worker also flushes as it drains —
        // at worst a crash loses the lines written since the last periodic flush.
        private static void Shutdown()
        {
            // Capture the instances this session owns so we complete/join/dispose the
            // same queue and writer the worker holds, never a re-init's swapped ones.
            var queue = _queue;
            var worker = _worker;
            var writer = _writer;
            try
            {
                _fileLoggingAvailable = false;
                queue?.CompleteAdding();
                worker?.Join(TimeSpan.FromSeconds(2));
                writer?.Flush();
                writer?.Dispose();
            }
            catch
            {
                // Best-effort flush on quit; never throw out of a shutdown hook.
            }
            finally
            {
                // Only clear a static if it still points at the instance this
                // shutdown owns, so a re-init that swapped it isn't clobbered.
                // (Init/shutdown are sequential on the main thread today, so this is
                // defensive — but it keeps the ownership contract explicit.)
                if (ReferenceEquals(_writer, writer))
                {
                    _writer = null;
                }

                if (ReferenceEquals(_queue, queue))
                {
                    _queue = null;
                }

                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                }
            }
        }
    }
}
