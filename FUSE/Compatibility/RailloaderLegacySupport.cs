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
        /// <summary>
        /// Preserves the legacy mixinto-discovery contract so existing plugin IL can bind to the FUSE shim.
        /// </summary>
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

        /// <summary>
        /// Preserves the legacy console-registration entry point so existing plugin IL can bind to the FUSE host.
        /// </summary>
        void RegisterConsoleCommand(IConsoleCommand command);

        /// <summary>
        /// Preserves the legacy settings-load signature so existing plugin IL can resolve its loader context calls.
        /// </summary>
        T LoadSettingsData<T>(string settingsIdentifier) where T : class;

        /// <summary>
        /// Preserves the legacy settings-save signature so existing plugin IL can resolve its loader context calls.
        /// </summary>
        void SaveSettingsData<T>(string settingsIdentifier, T settings) where T : class;

        /// <summary>
        /// Preserves the single-target legacy mixinto query used by existing plugin binaries.
        /// </summary>
        IEnumerable<ModMixinto> GetMixintos(string target);

        /// <summary>
        /// Preserves the single-target legacy mixinto query that controls non-file entries.
        /// </summary>
        IEnumerable<ModMixinto> GetMixintos(string target, bool allowNonFileEntries);

        /// <summary>
        /// Preserves the multi-target legacy mixinto query used by existing plugin binaries.
        /// </summary>
        IEnumerable<ModMixinto> GetMixintos(string[] targets);

        /// <summary>
        /// Preserves the multi-target legacy mixinto query that controls non-file entries.
        /// </summary>
        IEnumerable<ModMixinto> GetMixintos(string[] targets, bool allowNonFileEntries);

        /// <summary>
        /// Preserves the legacy file-resolution overload used by existing plugin binaries.
        /// </summary>
        bool TryResolveFilePath(string baseDirectory, string value, bool allowNonFileEntries, out string result);

        /// <summary>
        /// Preserves the rooted legacy file-resolution overload used by existing plugin binaries.
        /// </summary>
        bool TryResolveFilePath(string baseDirectory, string rootDirectory, string value, bool allowNonFileEntries, out string result);

        /// <summary>
        /// Preserves the generic subtype-registration signature required by legacy plugin IL.
        /// </summary>
        void RegisterSubTypeOverload<TBaseClass, TImplementation>(string identifier);

        /// <summary>
        /// Preserves the reflection-based subtype-registration signature required by legacy plugin IL.
        /// </summary>
        void RegisterSubTypeOverload(System.Type baseClass, string identifier, System.Type implementation);

        /// <summary>
        /// Preserves the legacy component-builder registration contract so plugin types can load under FUSE.
        /// </summary>
        void RegisterComponent<TComponent, TComponentBuilder>(string kind)
            where TComponent : Component
            where TComponentBuilder : IComponentBuilder;
    }

    [Obsolete(LegacyShim.Message)]
    public interface IModTabHandler
    {
        /// <summary>
        /// Preserves the legacy mod-tab open callback so existing plugin IL can bind to the UI contract.
        /// </summary>
        void ModTabDidOpen(UIPanelBuilder builder);

        /// <summary>
        /// Preserves the legacy mod-tab close callback so existing plugin IL can bind to the UI contract.
        /// </summary>
        void ModTabDidClose();
    }

    [Obsolete(LegacyShim.Message)]
    public interface IUIHelper
    {
        /// <summary>
        /// Preserves the legacy dimension-based window factory signature used by existing plugin binaries.
        /// </summary>
        Window CreateWindow(int width, int height, Window.Position position);

        /// <summary>
        /// Preserves the legacy identified window factory signature used by existing plugin binaries.
        /// </summary>
        Window CreateWindow(string identifier, int width, int height, Window.Position position);

        /// <summary>
        /// Preserves the legacy configurable builder-window factory signature used by existing plugin binaries.
        /// </summary>
        Window CreateWindow<TWindow>(string identifier, int width, int height, Window.Position position, Action<TWindow> configure = null)
            where TWindow : Component, UI.IBuilderWindow;

        /// <summary>
        /// Preserves the legacy programmatic-window factory signature used by existing plugin binaries.
        /// </summary>
        Window CreateWindow<TWindow>(Action<TWindow> configure = null)
            where TWindow : Component, UI.IProgrammaticWindow;

        /// <summary>
        /// Preserves the legacy window-population callback contract used by existing plugin binaries.
        /// </summary>
        UIPanel PopulateWindow(Window window, Action<UIPanelBuilder> closure);
    }

    [Obsolete(LegacyShim.Message)]
    public interface IUpdateHandler
    {
        /// <summary>
        /// Preserves the legacy per-frame callback signature so update-handler plugin types can load under FUSE.
        /// </summary>
        void Update();
    }

    [Obsolete(LegacyShim.Message)]
    public interface IComponentBuilder
    {
        System.Type ComponentType { get; }

        /// <summary>
        /// Preserves the non-generic legacy component-build entry point required by plugin IL.
        /// </summary>
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
        /// <summary>
        /// Preserves the legacy one-argument mixinto-definition constructor used by existing plugin binaries.
        /// </summary>
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

        /// <summary>
        /// Preserves the legacy string projection used when plugins log or compare mixinto definitions.
        /// </summary>
        public override string ToString() => Mixinto;
    }

    // Regular struct rather than a record struct: only the property getters
    // and constructor signatures need to match for plugin IL to JIT against
    // this shim; record-struct value semantics aren't required for interop.
    [Obsolete(LegacyShim.Message)]
    public struct ModMixinto
    {
        /// <summary>
        /// Preserves the full legacy mixinto constructor signature and compatibility data shape.
        /// </summary>
        public ModMixinto(IMod source, string mixinto, MixintoType type, ModReference[] requires = null, ModReference[] conflictsWith = null, object managedObject = null)
        {
            Source = source;
            Mixinto = mixinto;
            Type = type;
            Requires = requires;
            ConflictsWith = conflictsWith;
            ManagedObject = managedObject;
        }

        /// <summary>
        /// Preserves the legacy file-mixinto convenience constructor used by existing plugin binaries.
        /// </summary>
        public ModMixinto(IMod source, string mixinto)
            : this(source, mixinto, MixintoType.File, null, null, null)
        {
        }

        /// <summary>
        /// Preserves the legacy constructor that combines a mod with a mixinto definition.
        /// </summary>
        public ModMixinto(IMod source, MixintoDefinition definition)
            : this(source, definition.Mixinto, definition.Type, definition.Requires, definition.ConflictsWith, null)
        {
        }

        /// <summary>
        /// Preserves the legacy typed-mixinto constructor overload without managed-object state.
        /// </summary>
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

        /// <summary>
        /// Preserves the legacy implicit conversion used by plugin metadata initializers.
        /// </summary>
        public static implicit operator ModReference(string id)
        {
            return new ModReference { Id = id };
        }

        /// <summary>
        /// Preserves the legacy version-range text format used by plugin metadata and diagnostics.
        /// </summary>
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

        /// <summary>
        /// Provides the legacy host enable transition expected by plugin binaries while FUSE owns invocation.
        /// </summary>
        internal void Enable()
        {
            OnEnable();
            IsEnabled = true;
        }

        /// <summary>
        /// Provides the legacy host disable transition expected by plugin binaries while FUSE owns invocation.
        /// </summary>
        internal void Disable()
        {
            OnDisable();
            IsEnabled = false;
        }

        /// <summary>
        /// Preserves the overridable legacy enable callback implemented by existing plugins.
        /// </summary>
        public virtual void OnEnable()
        {
        }

        /// <summary>
        /// Preserves the overridable legacy disable callback implemented by existing plugins.
        /// </summary>
        public virtual void OnDisable()
        {
        }
    }

    [Obsolete(LegacyShim.Message)]
    public abstract class SingletonPluginBase<T> : PluginBase where T : SingletonPluginBase<T>
    {
        /// <summary>
        /// Preserves the legacy singleton-registration behavior expected by derived plugin constructors.
        /// </summary>
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

        /// <summary>
        /// Preserves the legacy non-generic dispatch entry point and forwards it to the typed builder contract.
        /// </summary>
        public void Build(ComponentBuilderContext ctx, Component component)
        {
            Build(ctx, (T)(object)component);
        }

        /// <summary>
        /// Preserves the typed legacy component-build override implemented by existing plugin binaries.
        /// </summary>
        protected abstract void Build(ComponentBuilderContext ctx, T component);
    }
}

namespace Railloader.Events
{
    [Obsolete(LegacyShim.Message)]
    public struct WillCopyDebugInformation
    {
        public readonly Action<string> AppendLine;

        /// <summary>
        /// Preserves the legacy debug-information event payload constructor used by plugin subscribers.
        /// </summary>
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

        /// <summary>
        /// Keeps the legacy toggle extension callable while adapting to the available game overload at runtime.
        /// </summary>
        public static RectTransform AddToggleCompat(this UIPanelBuilder builder, Func<bool> valueClosure, Action<bool> action, bool interactable = true)
        {
            var binding = _addToggle ??= ResolveOverload(typeof(UIPanelBuilder), "AddToggle", expectedArities: new[] { 2, 3 });
            var args = binding.Arity == 3
                ? new object[] { valueClosure, action, interactable }
                : new object[] { valueClosure, action };
            return (RectTransform)binding.Method.Invoke(builder, args);
        }

        /// <summary>
        /// Keeps the legacy scroll-view extension callable through the current runtime method surface.
        /// </summary>
        public static void VScrollViewCompat(this UIPanelBuilder builder, Action<UIPanelBuilder> closure, RectOffset padding = null)
        {
            var method = _vScrollView ??= RequireInstanceMethod(typeof(UIPanelBuilder), "VScrollView");
            method.Invoke(builder, new object[] { closure, padding });
        }

        /// <summary>
        /// Keeps the legacy list-detail extension callable while adapting to the available generic overload.
        /// </summary>
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

        /// <summary>
        /// Centralizes overload validation so compatibility entry points fail clearly when the game API changes.
        /// </summary>
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

        /// <summary>
        /// Resolves the runtime method required to bridge a legacy UI call without embedding another implementation.
        /// </summary>
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

        /// <summary>
        /// Registers assembly redirection so legacy loader and Strange Customs references resolve to FUSE shim types.
        /// </summary>
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

        /// <summary>
        /// Removes the legacy assembly redirection when FUSE shuts down or reloads.
        /// </summary>
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

        /// <summary>
        /// Redirects recognized legacy assembly requests to the independently implemented FUSE shim assembly.
        /// </summary>
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

        /// <summary>
        /// Limits assembly redirection to the legacy loader contracts intentionally hosted by FUSE.
        /// </summary>
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
