using System;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using UnityEngine;

namespace FUSE.Interface.Console
{
    /// <summary>
    /// Registers FUSE's console commands against Railroader's UI.Console.ConsoleCommandHandler.
    /// The handler exposes a non-public Register&lt;T&gt;(IConsoleCommand) method, so we invoke
    /// it via reflection. If the host's console surface is missing or has a different shape,
    /// we log once and skip — FUSE keeps loading.
    /// </summary>
    internal static class FuseConsoleRegistrar
    {
        private static bool _registered;
        private static bool _surfaceUnavailableLogged;

        public static bool IsRegistered => _registered;

        public static void TryRegisterAll()
        {
            if (_registered)
            {
                return;
            }

            try
            {
                var consoleCommandType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes)
                    .FirstOrDefault(t => t != null && t.FullName == "UI.Console.ConsoleCommandHandler");
                if (consoleCommandType == null)
                {
                    LogSurfaceUnavailable("UI.Console.ConsoleCommandHandler type not present.");
                    return;
                }

                var handlerInstance = UnityEngine.Object.FindObjectOfType(consoleCommandType);
                if (handlerInstance == null)
                {
                    // The handler may not be alive yet during early load. We will
                    // be retried by the lifecycle on map-did-load.
                    return;
                }

                var registerMethod = consoleCommandType.GetMethod(
                    "Register", BindingFlags.Instance | BindingFlags.NonPublic);
                if (registerMethod == null || !registerMethod.IsGenericMethodDefinition)
                {
                    LogSurfaceUnavailable("ConsoleCommandHandler.Register<T> not found via reflection.");
                    return;
                }

                var commands = FuseConsoleCommands.CreateAll();
                var registered = 0;
                foreach (var command in commands)
                {
                    try
                    {
                        var generic = registerMethod.MakeGenericMethod(command.GetType());
                        generic.Invoke(handlerInstance, new object[] { command });
                        registered++;
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Warning(
                            $"FUSE console command '{command.GetType().Name}' failed to register: {ex.Message}");
                    }
                }

                _registered = true;
                FuseLog.Info($"FUSE registered {registered} console command(s).");
            }
            catch (Exception ex)
            {
                FuseLog.Exception("FUSE console registration failed.", ex);
            }
        }

        private static void LogSurfaceUnavailable(string detail)
        {
            if (_surfaceUnavailableLogged)
            {
                return;
            }

            _surfaceUnavailableLogged = true;
            FuseLog.Warning(
                $"FUSE console commands not registered: {detail} " +
                "FUSE will continue to load without console commands.");
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
