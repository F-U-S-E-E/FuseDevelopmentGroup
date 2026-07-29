using System;
using System.Reflection;
using FUSE.Profiler.Infrastructure;
using FUSE.Profiler.Instrumentation;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using UI.Console;
using UnityEngine;

namespace FUSE.Profiler.Interface
{
    [ConsoleCommand("/profiler", "Toggle the FUSE Profiler window.")]
    public sealed class ProfilerToggleCommand : IConsoleCommand
    {
        public string Execute(string[] components)
        {
            // The game console keeps registered commands for the map's
            // lifetime and offers no unregister — so if the mod was disabled
            // after registration, refuse instead of arming sampling with no
            // host to drive clocks or cleanup.
            if (!ProfilerHost.IsRunning)
            {
                return "FUSE Profiler is disabled (enable it in Unity Mod Manager).";
            }

            ProfilerRuntime.ToggleWindow();
            return ProfilerRuntime.WindowVisible ? "Profiler opened." : "Profiler closed.";
        }
    }

    /// <summary>
    /// Registers the console command with the game's handler. The handler is
    /// scene-bound (absent in the main menu and during early load), and its
    /// Register method is a non-public generic — so registration is
    /// reflection-driven and retried on every map load.
    /// </summary>
    internal sealed class ProfilerConsole
    {
        private static ProfilerConsole _instance;
        private bool _registered;

        internal static void Initialize()
        {
            if (_instance != null)
            {
                return;
            }

            _instance = new ProfilerConsole();
            Messenger.Default.Register<MapDidLoadEvent>(_instance, _instance.OnMapDidLoad);
            _instance.TryRegister();
        }

        internal static void Shutdown()
        {
            if (_instance == null)
            {
                return;
            }

            Messenger.Default.Unregister(_instance);
            _instance = null;
        }

        private void OnMapDidLoad(MapDidLoadEvent message)
        {
            // A fresh map brings a fresh ConsoleCommandHandler instance.
            _registered = false;
            TryRegister();
        }

        private void TryRegister()
        {
            if (_registered)
            {
                return;
            }

            try
            {
                var handler = UnityEngine.Object.FindObjectOfType<ConsoleCommandHandler>();
                if (handler == null)
                {
                    return;
                }

                var register = typeof(ConsoleCommandHandler).GetMethod(
                    "Register",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (register == null)
                {
                    ProfilerLog.Warning("FUSE.Profiler could not find ConsoleCommandHandler.Register; /profiler unavailable.");
                    return;
                }

                var command = new ProfilerToggleCommand();
                register.MakeGenericMethod(command.GetType()).Invoke(handler, new object[] { command });
                _registered = true;
                ProfilerLog.Info("FUSE.Profiler registered the /profiler console command.");
            }
            catch (Exception ex)
            {
                ProfilerLog.Exception("FUSE.Profiler console registration failed", ex);
            }
        }
    }
}
