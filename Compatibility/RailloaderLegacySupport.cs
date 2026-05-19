using System;
using System.Collections.Generic;
using System.Reflection;
using FUSE.Infrastructure;
using UI.Builder;
using UI.Common;
using UI.Console;
using UnityEngine;

// Legacy support surface for assemblies compiled against the previous loader API.
// This file reproduces the public surface of the legacy Railloader.Interchange API
// so the CLR JIT can validate IL from old-loader plugins that reference any of these
// types. New FUSE packages should use FUSE's native schema and APIs instead.
namespace Railloader
{
    [Obsolete(LegacyShim.Message)]
    public interface IMixintoDefinitionProvider
    {
        IEnumerable<MixintoDefinition> GetMixintoDefinitions(string mixintoIdentifier);
    }

    [Obsolete(LegacyShim.Message)]
    public interface IMod : IModDefinition
    {
        bool IsEnabled { get; }
        bool IsLoaded { get; }
        bool IsFaulted { get; }
        PluginBase[] Plugins { get; }
    }

    [Obsolete(LegacyShim.Message)]
    public interface IModDefinition
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        string Directory { get; }
        string[] LoadBefore { get; }
        ModReference[] LoadAfter { get; }
        ModReference[] Requires { get; }
        ModReference[] ConflictsWith { get; }
    }

    [Obsolete(LegacyShim.Message)]
    public interface IModdingContext
    {
        System.Version RailloaderVersion { get; }
        string ModsBaseDirectory { get; }
        IReadOnlyCollection<IMod> Mods { get; }

        void RegisterConsoleCommand(IConsoleCommand command);

        T LoadSettingsData<T>(string settingsIdentifier) where T : class;
        void SaveSettingsData<T>(string settingsIdentifier, T settings) where T : class;

        IEnumerable<ModMixinto> GetMixintos(string target);
        IEnumerable<ModMixinto> GetMixintos(string target, bool allowNonFileEntries);
        IEnumerable<ModMixinto> GetMixintos(string[] targets);
        IEnumerable<ModMixinto> GetMixintos(string[] targets, bool allowNonFileEntries);

        bool TryResolveFilePath(string baseDirectory, string value, bool allowNonFileEntries, out string result);
        bool TryResolveFilePath(string baseDirectory, string rootDirectory, string value, bool allowNonFileEntries, out string result);

        void RegisterSubTypeOverload<TBaseClass, TImplementation>(string identifier);
        void RegisterSubTypeOverload(System.Type baseClass, string identifier, System.Type implementation);

        void RegisterComponent<TComponent, TComponentBuilder>(string kind)
            where TComponent : Component
            where TComponentBuilder : IComponentBuilder;
    }

    [Obsolete(LegacyShim.Message)]
    public interface IModTabHandler
    {
        void ModTabDidOpen(UIPanelBuilder builder);
        void ModTabDidClose();
    }

    [Obsolete(LegacyShim.Message)]
    public interface IUIHelper
    {
        Window CreateWindow(int width, int height, Window.Position position);
        Window CreateWindow(string identifier, int width, int height, Window.Position position);
        Window CreateWindow<TWindow>(string identifier, int width, int height, Window.Position position, Action<TWindow> configure = null)
            where TWindow : Component, UI.IBuilderWindow;
        Window CreateWindow<TWindow>(Action<TWindow> configure = null)
            where TWindow : Component, UI.IProgrammaticWindow;
        UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure);
    }

    [Obsolete(LegacyShim.Message)]
    public interface IUpdateHandler
    {
        void Update();
    }

    [Obsolete(LegacyShim.Message)]
    public interface IComponentBuilder
    {
        System.Type ComponentType { get; }
        void Build(ComponentBuilderContext ctx, Component component);
    }

    // Shape-only stub for interop. FUSE's legacy host does not invoke
    // IComponentBuilder.Build, but the type must exist so plugin IL that
    // references it can JIT.
    [Obsolete(LegacyShim.Message)]
    public class ComponentBuilderContext
    {
    }

    [Flags]
    [Obsolete(LegacyShim.Message)]
    public enum MixintoType
    {
        Unknown = 0,
        File = 1,
        Directory = 2,
        ManagedObject = 3,
    }

    [Obsolete(LegacyShim.Message)]
    public struct MixintoDefinition
    {
        public MixintoDefinition(string mixinto)
        {
            Mixinto = mixinto;
            Type = MixintoType.Unknown;
            Requires = null;
            ConflictsWith = null;
            ManagedObject = null;
        }

        public string Mixinto { get; set; }
        public ModReference[] Requires { get; set; }
        public ModReference[] ConflictsWith { get; set; }
        public MixintoType Type { get; set; }
        internal object ManagedObject { get; set; }

        public override string ToString() => Mixinto;
    }

    // Regular struct rather than a record struct: only the property getters
    // and constructor signatures need to match for plugin IL to JIT against
    // this shim; record-struct value semantics aren't required for interop.
    [Obsolete(LegacyShim.Message)]
    public struct ModMixinto
    {
        public ModMixinto(IMod source, string mixinto, MixintoType type, ModReference[] requires = null, ModReference[] conflictsWith = null, object managedObject = null)
        {
            Source = source;
            Mixinto = mixinto;
            Type = type;
            Requires = requires;
            ConflictsWith = conflictsWith;
            ManagedObject = managedObject;
        }

        public ModMixinto(IMod source, string mixinto)
            : this(source, mixinto, MixintoType.File, null, null, null)
        {
        }

        public ModMixinto(IMod source, MixintoDefinition definition)
            : this(source, definition.Mixinto, definition.Type, definition.Requires, definition.ConflictsWith, null)
        {
        }

        public ModMixinto(IMod source, string mixinto, MixintoType type, ModReference[] requires, ModReference[] conflictsWith)
            : this(source, mixinto, type, requires, conflictsWith, null)
        {
        }

        public IMod Source { get; set; }
        public string Mixinto { get; set; }
        public MixintoType Type { get; set; }
        public ModReference[] Requires { get; set; }
        public ModReference[] ConflictsWith { get; set; }
        public object ManagedObject { get; set; }
    }

    [Obsolete(LegacyShim.Message)]
    public struct ModReference
    {
        public string Id;
        public System.Version NotBefore;
        public System.Version NotAfter;

        public static implicit operator ModReference(string id)
        {
            return new ModReference { Id = id };
        }

        public override string ToString()
        {
            if (NotBefore == null && NotAfter == null)
            {
                return Id;
            }

            var text = string.Empty;
            if (NotBefore != null)
            {
                text = $"{NotBefore}<";
            }

            text += Id;
            if (NotAfter != null)
            {
                text += $"<{NotAfter}";
            }

            return text;
        }
    }

    // Compatibility base for legacy plugins that inherit Railloader.PluginBase.
    [Obsolete(LegacyShim.Message)]
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

    [Obsolete(LegacyShim.Message)]
    public abstract class SingletonPluginBase<T> : PluginBase where T : SingletonPluginBase<T>
    {
        protected SingletonPluginBase()
        {
            Shared = (T)(object)this;
        }

        public static T Shared { get; private set; }
    }

    // Shared [Obsolete] message used across the legacy shim. Lifted to a
    // constant so every type carries the same migration hint and the message
    // can be updated in one place.
    internal static class LegacyShim
    {
        internal const string Message = "Legacy Railloader compatibility shim. Use FUSE's native API for new packages.";
    }
}

namespace Railloader.Extensions
{
    [Obsolete(LegacyShim.Message)]
    public abstract class ComponentBuilder<T> : IComponentBuilder where T : Component
    {
        public System.Type ComponentType => typeof(T);

        public void Build(ComponentBuilderContext ctx, Component component)
        {
            Build(ctx, (T)(object)component);
        }

        protected abstract void Build(ComponentBuilderContext ctx, T component);
    }
}

namespace Railloader.Events
{
    [Obsolete(LegacyShim.Message)]
    public struct WillCopyDebugInformation
    {
        public readonly Action<string> AppendLine;

        public WillCopyDebugInformation(Action<string> appendLine)
        {
            AppendLine = appendLine;
        }
    }
}

namespace Railloader.Compatibility
{
    // Surface kept for interop with plugins compiled against the legacy
    // Railloader.Compatibility.UIPanelBuilderCompatibility helpers. Each
    // resolved MethodInfo is cached with its arity and dispatched through
    // Invoke at the call site; overload-arity resolution is centralized
    // in ResolveOverload.
    [Obsolete(LegacyShim.Message)]
    public static class UIPanelBuilderCompatibility
    {
        private static readonly object ResolveLock = new object();
        private static readonly Dictionary<System.Type, MethodInfo> ListDetailByValueType = new Dictionary<System.Type, MethodInfo>();
        private static (MethodInfo Method, int Arity)? _addToggle;
        private static MethodInfo _vScrollView;
        private static (MethodInfo Method, int Arity)? _addListDetail;

        public static RectTransform AddToggleCompat(this UIPanelBuilder builder, Func<bool> valueClosure, Action<bool> action, bool interactable = true)
        {
            var binding = _addToggle ??= ResolveOverload(typeof(UIPanelBuilder), "AddToggle", expectedArities: new[] { 2, 3 });
            var args = binding.Arity == 3
                ? new object[] { valueClosure, action, interactable }
                : new object[] { valueClosure, action };
            return (RectTransform)binding.Method.Invoke(builder, args);
        }

        public static void VScrollViewCompat(this UIPanelBuilder builder, Action<UIPanelBuilder> closure, RectOffset padding = null)
        {
            var method = _vScrollView ??= RequireInstanceMethod(typeof(UIPanelBuilder), "VScrollView");
            method.Invoke(builder, new object[] { closure, padding });
        }

        public static void AddListDetailCompat<TValue>(this UIPanelBuilder builder, IEnumerable<UIPanelBuilder.ListItem<TValue>> data, UIState<string> selectedItem, Action<UIPanelBuilder, TValue> builderClosure, float? listWidth = null)
            where TValue : class
        {
            var open = _addListDetail ??= ResolveOverload(typeof(UIPanelBuilder), "AddListDetail", expectedArities: new[] { 3, 4 });

            MethodInfo constructed;
            lock (ResolveLock)
            {
                if (!ListDetailByValueType.TryGetValue(typeof(TValue), out constructed))
                {
                    constructed = open.Method.MakeGenericMethod(typeof(TValue));
                    ListDetailByValueType[typeof(TValue)] = constructed;
                }
            }

            var args = open.Arity == 4
                ? new object[] { data, selectedItem, builderClosure, listWidth }
                : new object[] { data, selectedItem, builderClosure };
            constructed.Invoke(builder, args);
        }

        private static (MethodInfo Method, int Arity) ResolveOverload(System.Type declaringType, string methodName, int[] expectedArities)
        {
            var method = RequireInstanceMethod(declaringType, methodName);
            var arity = method.GetParameters().Length;
            if (Array.IndexOf(expectedArities, arity) < 0)
            {
                throw new NotSupportedException(
                    $"{declaringType.FullName}.{methodName} has unexpected arity {arity}; expected one of [{string.Join(",", expectedArities)}].");
            }

            return (method, arity);
        }

        private static MethodInfo RequireInstanceMethod(System.Type declaringType, string methodName)
        {
            return declaringType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new NotSupportedException($"{declaringType.FullName}.{methodName} is not present in this Railroader build.");
        }
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
