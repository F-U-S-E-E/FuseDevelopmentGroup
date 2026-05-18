using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using UI.Builder;
using UI.Console;

// Legacy support surface for assemblies compiled against the previous loader API.
// New FUSE packages should use FUSE's native schema and APIs instead.
namespace Railloader
{
    public interface IModDefinition
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        string Directory { get; }
    }

    public interface IModdingContext
    {
        string ModsBaseDirectory { get; }
        IEnumerable<ModMixinto> GetMixintos(string identifier);
        void RegisterConsoleCommand(IConsoleCommand command);
    }

    public readonly struct ModMixinto
    {
        public ModMixinto(string mixinto, IModDefinition source)
        {
            Mixinto = mixinto;
            Source = source;
        }

        public string Mixinto { get; }
        public IModDefinition Source { get; }
    }

    public interface IModTabHandler
    {
        void ModTabDidOpen(UIPanelBuilder builder);
        void ModTabDidClose();
    }

    // Mirrors the original Railloader.PluginBase shape so legacy plugins compiled
    // against Railloader (which all inherit this) can be loaded against FUSE.
    public abstract class PluginBase
    {
        public bool IsEnabled { get; private set; }

        internal void Enable()
        {
            OnEnable();
            IsEnabled = true;
        }

        internal void Disable()
        {
            OnDisable();
            IsEnabled = false;
        }

        public virtual void OnEnable()
        {
        }

        public virtual void OnDisable()
        {
        }
    }

    public abstract class SingletonPluginBase<T> : PluginBase where T : SingletonPluginBase<T>
    {
        protected SingletonPluginBase()
        {
            Shared = (T)(object)this;
        }

        public static T Shared { get; private set; }
    }
}

namespace FUSE.Compatibility
{
    internal static class FuseLegacySupportAssemblyShim
    {
        private static readonly Assembly ShimAssembly = typeof(Railloader.IModDefinition).Assembly;
        private static bool _registered;

        internal static void Initialize()
        {
            if (_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveLegacyAssembly;
            _registered = true;
            FuseLog.Info("FUSE legacy support assembly shim registered for old-loader API references.");
        }

        internal static void Shutdown()
        {
            if (!_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve -= ResolveLegacyAssembly;
            _registered = false;
            FuseLog.Info("FUSE legacy support assembly shim unregistered.");
        }

        private static Assembly ResolveLegacyAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name).Name;
                if (IsLegacyLoaderAssembly(requested))
                {
                    FuseLog.Info($"FUSE legacy support resolved missing old-loader assembly '{args.Name}' to FUSE shim types.");
                    return ShimAssembly;
                }
            }
            catch (Exception ex)
            {
                FuseLog.Warning($"FUSE legacy support assembly resolution failed for '{args?.Name}': {ex.GetBaseException().Message}");
            }

            return null;
        }

        private static bool IsLegacyLoaderAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            return assemblyName.StartsWith("Railloader", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Equals("StrangeCustoms", StringComparison.OrdinalIgnoreCase);
        }
    }
}
