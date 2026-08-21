using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FUSE.Infrastructure
{
    /// <summary>
    /// Optional Windows console mirror for authors who want a RailLoader-style
    /// live FUSE log on another screen. FUSE detaches only a console it allocated
    /// itself; an existing --console/parent console is never freed.
    /// </summary>
    internal static class FuseLiveConsole
    {
        private const string Kernel32 = "kernel32.dll";
        private const string User32 = "user32.dll";
        private const uint ScClose = 0xF060;
        private const uint MfByCommand = 0x00000000;
        private const int PendingLineCapacity = 2048;
        private static readonly object Gate = new object();
        private static readonly BlockingCollection<string> PendingLines =
            new BlockingCollection<string>(PendingLineCapacity);

        private static Thread _writerThread;
        private static volatile bool _enabled;
        private static bool _allocatedByFuse;
        private static long _droppedLineCount;
        private static TextWriter _previousOut;
        private static TextWriter _consoleWriter;

        internal static bool IsEnabled => _enabled;

        internal static string Enable()
        {
            lock (Gate)
            {
                if (_enabled)
                {
                    return "The FUSE live console is already open.";
                }

                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    return "The separate live console is available on Windows only. Use the in-game log viewer on this platform.";
                }

                try
                {
                    _allocatedByFuse = GetConsoleOutputCP() == 0;
                    if (_allocatedByFuse && !AllocConsole())
                    {
                        _allocatedByFuse = false;
                        return "Windows could not open a console for FUSE.";
                    }

                    if (_allocatedByFuse)
                    {
                        SetConsoleTitleW("FUSE Live Diagnostics");
                        DisableWindowCloseCommand();
                    }
                    _previousOut = System.Console.Out;
                    var stream = System.Console.OpenStandardOutput();
                    var outputEncoding = System.Console.OutputEncoding;
                    if (outputEncoding.CodePage == Encoding.UTF8.CodePage)
                    {
                        outputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    }

                    var rawWriter = new StreamWriter(
                        stream,
                        outputEncoding,
                        bufferSize: 1024,
                        leaveOpen: true) { AutoFlush = true };
                    System.Console.SetOut(TextWriter.Synchronized(rawWriter));
                    _consoleWriter = System.Console.Out;
                    EnsureWriterThreadStarted();
                    _enabled = true;
                    EnqueueLine("FUSE live diagnostics console");
                    EnqueueLine("This mirrors FUSE.log. Close it from FUSE > Tools > Live Diagnostics.");
                    foreach (var entry in FuseLiveLogBuffer.Snapshot("All", string.Empty, 120))
                    {
                        EnqueueLine(entry.FormatLine());
                    }

                    return _allocatedByFuse
                        ? "Opened the FUSE live diagnostics console."
                        : "FUSE is now mirroring to the existing console.";
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    RestoreConsoleOutput();
                    if (_allocatedByFuse)
                    {
                        FreeConsole();
                        _allocatedByFuse = false;
                    }

                    return "FUSE could not open the live console: " + ex.GetBaseException().Message;
                }
            }
        }

        internal static string Disable()
        {
            lock (Gate)
            {
                if (!_enabled)
                {
                    return "The FUSE live console is not open.";
                }

                var detached = _allocatedByFuse;
                RestoreConsoleOutput();
                if (_allocatedByFuse)
                {
                    FreeConsole();
                }

                _allocatedByFuse = false;
                return detached
                    ? "Closed the FUSE live diagnostics console."
                    : "Stopped mirroring FUSE logs to the existing console.";
            }
        }

        internal static void WriteLine(string line)
        {
            if (!IsEnabled)
            {
                return;
            }

            EnqueueLine(line);
        }

        private static void EnqueueLine(string line)
        {
            if (!PendingLines.TryAdd(line ?? string.Empty))
            {
                Interlocked.Increment(ref _droppedLineCount);
            }
        }

        private static void EnsureWriterThreadStarted()
        {
            if (_writerThread != null && _writerThread.IsAlive)
            {
                return;
            }

            _writerThread = new Thread(ProcessPendingLines)
            {
                IsBackground = true,
                Name = "FUSE.LiveConsoleWriter"
            };
            _writerThread.Start();
        }

        private static void ProcessPendingLines()
        {
            try
            {
                foreach (var line in PendingLines.GetConsumingEnumerable())
                {
                    TextWriter writer;
                    lock (Gate)
                    {
                        if (!_enabled || _consoleWriter == null)
                        {
                            continue;
                        }

                        writer = _consoleWriter;
                    }

                    try
                    {
                        var droppedLines = Interlocked.Exchange(ref _droppedLineCount, 0);
                        if (droppedLines > 0)
                        {
                            writer.WriteLine(
                                $"[FUSE Live Diagnostics] Dropped {droppedLines} line(s) because the console queue was full.");
                        }

                        writer.WriteLine(line);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "FUSE live console write failed: " + ex.Message);
                        lock (Gate)
                        {
                            if (!ReferenceEquals(writer, _consoleWriter))
                            {
                                continue;
                            }

                            var detachOwnedConsole = _allocatedByFuse;
                            RestoreConsoleOutput();
                            if (detachOwnedConsole)
                            {
                                FreeConsole();
                            }

                            _allocatedByFuse = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "FUSE live console writer stopped unexpectedly: " + ex.Message);
            }
            finally
            {
                lock (Gate)
                {
                    if (ReferenceEquals(_writerThread, Thread.CurrentThread))
                    {
                        _writerThread = null;
                    }
                }
            }
        }

        private static void DisableWindowCloseCommand()
        {
            // Closing an AllocConsole window through its title-bar X can deliver
            // CTRL_CLOSE_EVENT to the game process. Keep this optional diagnostics
            // surface non-destructive; the Tools page owns the explicit detach.
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                return;
            }

            var systemMenu = GetSystemMenu(consoleWindow, false);
            if (systemMenu == IntPtr.Zero)
            {
                return;
            }

            if (DeleteMenu(systemMenu, ScClose, MfByCommand))
            {
                DrawMenuBar(consoleWindow);
            }
        }

        private static void RestoreConsoleOutput()
        {
            _enabled = false;
            if (_previousOut != null && ReferenceEquals(System.Console.Out, _consoleWriter))
            {
                System.Console.SetOut(_previousOut);
            }

            try
            {
                _consoleWriter?.Dispose();
            }
            catch (Exception ex)
            {
                // Do not route this through FuseLog: this cleanup can itself be
                // running because the console-backed FuseLog mirror failed, and
                // logging there would recurse into the same broken stream.
                System.Diagnostics.Debug.WriteLine(
                    "FUSE could not release its optional diagnostics console: " + ex.Message);
            }

            _consoleWriter = null;
            _previousOut = null;
        }

        [DllImport(Kernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport(Kernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport(Kernel32)]
        private static extern uint GetConsoleOutputCP();

        [DllImport(Kernel32)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleTitleW([MarshalAs(UnmanagedType.LPWStr)] string title);

        [DllImport(User32, SetLastError = true)]
        private static extern IntPtr GetSystemMenu(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool revert);

        [DllImport(User32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteMenu(IntPtr menu, uint position, uint flags);

        [DllImport(User32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawMenuBar(IntPtr window);
    }
}
