using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using FUSE.Compatibility;
using FUSE.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railloader;
using UI.Console;
using UnityEngine;
// FUSE refers to old-loader plugin shapes with Legacy* names; the right-hand
// sides are the compat declarations that legacy mod DLLs resolve against and
// are deletable once we stop honoring the old-loader contract.
using LegacyPluginBase = Railloader.PluginBase;

namespace FUSE.Loading
{
    internal static class FuseLegacyAssemblyHost
    {
        private static readonly Dictionary<string, HostedLegacyPlugin> HostedPlugins =
            new Dictionary<string, HostedLegacyPlugin>(StringComparer.OrdinalIgnoreCase);
        // RailLoader captures its IUpdateHandler instances once after startup.
        // Keep the same shape here: UpdateHostedPlugins runs every Unity frame,
        // so enumerating Dictionary.Values.ToArray() and re-reflecting each type
        // there produces permanent GC pressure in large mod sets.
        private static HostedLegacyPlugin[] _hostedUpdatePlugins =
            Array.Empty<HostedLegacyPlugin>();

        private static readonly List<IConsoleCommand> PendingConsoleCommands = new List<IConsoleCommand>();
        private static readonly HashSet<string> ReportedLoadOrderCycles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static GameObject _startupHost;
        private static bool _consoleSurfaceUnavailableLogged;

        internal static void EnsureStartupHost()
        {
            if (_startupHost != null)
            {
                return;
            }

            _startupHost = new GameObject("FUSE Legacy Support Host");
            UnityEngine.Object.DontDestroyOnLoad(_startupHost);
            _startupHost.hideFlags = HideFlags.HideAndDontSave;
            _startupHost.AddComponent<FuseLegacyAssemblyStartup>();
            FuseLog.Info("FUSE legacy support host scheduled legacy Definition.json assembly startup.");
        }

        internal static int LoadAllAvailableAssemblies(string reason)
        {
            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                FuseLog.Warning("FUSE legacy support could not locate the Mods folder for old-loader assembly hosting.");
                return 0;
            }

            var hostedCount = 0;
            foreach (var manifest in DiscoverLegacyManifests(modsRoot).Where(manifest => manifest.Assemblies.Length > 0))
            {
                if (!ShouldInspectPackage(manifest))
                {
                    continue;
                }

                var definition = new FuseLegacyModDefinition(manifest);
                var context = new FuseLegacyModdingContext(modsRoot);
                foreach (var assemblyReference in manifest.Assemblies)
                {
                    try
                    {
                        var assemblyPath = ResolveAssemblyPath(manifest.FolderPath, assemblyReference);
                        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                        {
                            FuseLog.Warning(
                                $"FUSE legacy support skipped assembly '{assemblyReference}' for '{manifest.Id}' because the DLL was not found.");
                            continue;
                        }

                        var assembly = LoadOrFindAssembly(assemblyPath);
                        if (assembly == null)
                        {
                            continue;
                        }

                        foreach (var pluginType in SafeGetTypes(assembly).Where(IsLegacyPluginType))
                        {
                            if (HostPlugin(manifest, definition, context, pluginType))
                            {
                                hostedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FuseLog.Exception(
                            $"FUSE legacy support failed while hosting assembly '{assemblyReference}' for '{manifest.Id}'",
                            ex);
                    }
                }
            }

            if (hostedCount > 0)
            {
                FuseLog.Info($"FUSE legacy support hosted {hostedCount} old-loader plugin instance(s) for '{reason ?? "unspecified"}'.");
            }

            // The mod population may have changed even when hostedCount is 0
            // (LoadOrFindAssembly can pull new DLLs into the AppDomain before a
            // plugin fails to host), so drop the attribution cache
            // unconditionally — invalidation is a flag clear; the rebuild is
            // lazy on the next observed exception.
            FuseModAttributionMap.Invalidate();

            RetryPendingConsoleCommands();
            return hostedCount;
        }

        /// <summary>
        /// Snapshot of hosted legacy plugin metadata for callers outside this class
        /// (settings UI, diagnostics) that need to enumerate live plugin instances.
        /// </summary>
        internal readonly struct HostedPluginInfo
        {
            public HostedPluginInfo(FuseLegacyAssemblyManifest manifest, Type type, object plugin)
            {
                Manifest = manifest;
                PluginType = type;
                Plugin = plugin;
            }

            public FuseLegacyAssemblyManifest Manifest { get; }
            public Type PluginType { get; }
            public object Plugin { get; }
        }

        /// <summary>
        /// Returns every hosted legacy plugin instance, regardless of folder.
        /// </summary>
        internal static IEnumerable<HostedPluginInfo> EnumerateAllHostedPlugins()
        {
            foreach (var hosted in HostedPlugins.Values)
            {
                if (hosted == null || hosted.Plugin == null)
                {
                    continue;
                }

                yield return new HostedPluginInfo(hosted.Manifest, hosted.Type, hosted.Plugin);
            }
        }

        /// <summary>
        /// Returns hosted plugin instances that live inside the given package folder
        /// or whose manifest id matches the provided id. Either match argument can be
        /// null/empty; at least one must be supplied for a non-empty result.
        /// </summary>
        internal static IEnumerable<HostedPluginInfo> EnumerateHostedPlugins(string folderPath, string id)
        {
            var hasFolder = !string.IsNullOrWhiteSpace(folderPath);
            var hasId = !string.IsNullOrWhiteSpace(id);
            if (!hasFolder && !hasId)
            {
                yield break;
            }

            var normalizedFolder = hasFolder ? NormalizePath(folderPath) : null;
            foreach (var hosted in HostedPlugins.Values)
            {
                if (hosted == null || hosted.Plugin == null || hosted.Manifest == null)
                {
                    continue;
                }

                var folderMatch = hasFolder &&
                    string.Equals(NormalizePath(hosted.Manifest.FolderPath), normalizedFolder, StringComparison.OrdinalIgnoreCase);
                var idMatch = hasId &&
                    string.Equals(hosted.Manifest.Id, id, StringComparison.OrdinalIgnoreCase);
                if (!folderMatch && !idMatch)
                {
                    continue;
                }

                yield return new HostedPluginInfo(hosted.Manifest, hosted.Type, hosted.Plugin);
            }
        }

        internal static IEnumerable<ModMixinto> EnumerateMixintos(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                yield break;
            }

            var modsRoot = FuseDataPackageDiscovery.GetModsRoot();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                yield break;
            }

            foreach (var manifest in DiscoverLegacyManifests(modsRoot))
            {
                if (!ShouldInspectPackage(manifest))
                {
                    continue;
                }

                var mixintos = manifest.RawDefinition["mixintos"] as JObject ??
                               manifest.RawDefinition["Mixintos"] as JObject;
                if (mixintos == null)
                {
                    continue;
                }

                foreach (var property in mixintos.Properties())
                {
                    if (!string.Equals(property.Name, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var entry in EnumerateMixintoEntries(property.Value))
                    {
                        if (!IsLegacyMixintoActive(manifest, entry))
                        {
                            continue;
                        }

                        var mixintoPath = ResolvePackageFile(manifest.FolderPath, entry.Reference);
                        if (!string.IsNullOrWhiteSpace(mixintoPath))
                        {
                            yield return new ModMixinto(
                                new FuseLegacyModDefinition(manifest),
                                mixintoPath,
                                MixintoType.File,
                                entry.Requires,
                                entry.ConflictsWith);
                        }
                    }
                }
            }
        }

        internal static IReadOnlyCollection<IMod> EnumerateInstalledMods(
            string modsRoot,
            Func<string, string, bool> enabledByActiveSet = null)
        {
            var mods = new Dictionary<string, IMod>(StringComparer.OrdinalIgnoreCase);
            var isEnabled = enabledByActiveSet ?? FuseModSetService.IsPackageEnabledByActiveSet;
            if (!string.IsNullOrWhiteSpace(modsRoot) && Directory.Exists(modsRoot))
            {
                foreach (var manifest in DiscoverLegacyManifests(modsRoot))
                {
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) ||
                        FuseUmmState.TryGetDisabledReason(manifest.FolderPath, manifest.Id, out _) ||
                        !isEnabled(manifest.Id, manifest.FolderPath))
                    {
                        continue;
                    }

                    mods[manifest.Id] = new FuseLegacyModDefinition(manifest);
                }
            }

            // Old plugins commonly use context.Mods as a capability check. A
            // FUSE-owned replacement must therefore be visible under the legacy
            // package id even though its superseded DLL is intentionally absent.
            var fuseVersion = typeof(FuseLegacyAssemblyHost).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            foreach (var replacedId in FuseReplacementCapabilityCatalog.AdvertisedPackageIds)
            {
                if (string.IsNullOrWhiteSpace(replacedId) || mods.ContainsKey(replacedId))
                {
                    continue;
                }

                mods[replacedId] = new FuseLegacyModDefinition(new FuseLegacyAssemblyManifest
                {
                    Id = replacedId,
                    Name = replacedId,
                    Version = fuseVersion,
                    FolderPath = modsRoot ?? string.Empty
                });
            }

            return mods.Values.ToArray();
        }

        internal static void UpdateHostedPlugins()
        {
            var updatePlugins = _hostedUpdatePlugins;
            for (var index = 0; index < updatePlugins.Length; index++)
            {
                var hosted = updatePlugins[index];
                if (hosted == null || hosted.Plugin == null || hosted.UpdateFaulted)
                {
                    continue;
                }

                try
                {
                    hosted.InvokeUpdate();
                }
                catch (Exception ex)
                {
                    hosted.UpdateFaulted = true;
                    FuseModExceptionRegistry.RecordContained(ex, hosted.Manifest.Id, "legacy host update");
                    FuseLog.Exception(
                        $"FUSE legacy support disabled the per-frame update callback for '{hosted.Type.FullName}' from '{hosted.Manifest.Id}' after it threw",
                        ex);
                }
            }
        }

        private static bool ImplementsLegacyUpdateHandler(Type pluginType)
        {
            if (pluginType == null)
            {
                return false;
            }

            return typeof(IUpdateHandler).IsAssignableFrom(pluginType) ||
                   pluginType.GetInterfaces().Any(candidate =>
                       string.Equals(candidate.FullName, "Railloader.IUpdateHandler", StringComparison.Ordinal));
        }

        internal static void RegisterConsoleCommand(IConsoleCommand command)
        {
            if (command == null)
            {
                return;
            }

            if (TryRegisterConsoleCommand(command))
            {
                return;
            }

            if (!PendingConsoleCommands.Contains(command))
            {
                PendingConsoleCommands.Add(command);
                FuseLog.Info($"FUSE legacy support queued console command '{command.GetType().FullName}' until the console surface is available.");
            }
        }

        internal static void RetryPendingConsoleCommands()
        {
            if (PendingConsoleCommands.Count == 0)
            {
                return;
            }

            for (var index = PendingConsoleCommands.Count - 1; index >= 0; index--)
            {
                var command = PendingConsoleCommands[index];
                if (TryRegisterConsoleCommand(command))
                {
                    PendingConsoleCommands.RemoveAt(index);
                }
            }
        }

        internal static void Shutdown()
        {
            foreach (var hosted in HostedPlugins.Values.Reverse().ToArray())
            {
                try
                {
                    InvokePluginLifecycleMethod(hosted.Plugin, nameof(LegacyPluginBase.OnDisable));
                    FuseLog.Info(
                        $"FUSE legacy support disabled hosted old-loader plugin '{hosted.Type.FullName}' from '{hosted.Manifest.Id}'.");
                }
                catch (Exception ex)
                {
                    FuseModExceptionRegistry.RecordContained(ex, hosted.Manifest.Id, "legacy host plugin disable");
                    FuseLog.Exception(
                        $"FUSE legacy support failed while disabling hosted old-loader plugin '{hosted.Type.FullName}'",
                        ex);
                }
            }

            HostedPlugins.Clear();
            _hostedUpdatePlugins = Array.Empty<HostedLegacyPlugin>();
            PendingConsoleCommands.Clear();
            if (_startupHost != null)
            {
                UnityEngine.Object.Destroy(_startupHost);
                _startupHost = null;
            }
        }

        private static bool HostPlugin(
            FuseLegacyAssemblyManifest manifest,
            FuseLegacyModDefinition definition,
            FuseLegacyModdingContext context,
            Type pluginType)
        {
            var key = NormalizePath(manifest.FolderPath) + "|" + pluginType.FullName;
            if (HostedPlugins.ContainsKey(key))
            {
                return false;
            }

            object plugin;
            try
            {
                plugin = CreatePluginInstance(pluginType, context, definition);
            }
            catch (Exception ex)
            {
                // Contained here, so Unity never logs it — feed the mod health
                // registry directly (attribution is the manifest id itself).
                FuseModExceptionRegistry.RecordContained(ex, manifest.Id, "legacy host plugin instantiate");
                FuseLog.Exception(
                    $"FUSE legacy support failed to instantiate old-loader plugin '{pluginType.FullName}' from '{manifest.Id}'",
                    ex);
                return false;
            }

            if (plugin == null)
            {
                return false;
            }

            try
            {
                InvokePluginLifecycleMethod(plugin, nameof(LegacyPluginBase.OnEnable));
                var hosted = new HostedLegacyPlugin(manifest, pluginType, plugin);
                HostedPlugins[key] = hosted;
                if (hosted.HasUpdateHandler)
                {
                    var existing = _hostedUpdatePlugins;
                    var replacement = new HostedLegacyPlugin[existing.Length + 1];
                    Array.Copy(existing, replacement, existing.Length);
                    replacement[replacement.Length - 1] = hosted;
                    _hostedUpdatePlugins = replacement;
                }
                FuseLog.Info(
                    $"FUSE legacy support enabled hosted old-loader plugin '{pluginType.FullName}' " +
                    $"from package '{manifest.Id}'. This is temporary legacy compatibility, not a native FUSE package.");
                return true;
            }
            catch (Exception ex)
            {
                FuseModExceptionRegistry.RecordContained(ex, manifest.Id, "legacy host plugin enable");
                FuseLog.Exception(
                    $"FUSE legacy support failed to enable old-loader plugin '{pluginType.FullName}' from '{manifest.Id}'",
                    ex);
                return false;
            }
        }

        private static object CreatePluginInstance(
            Type pluginType,
            FuseLegacyModdingContext context,
            FuseLegacyModDefinition definition)
        {
            var constructor = pluginType.GetConstructor(new[] { typeof(IModdingContext), typeof(IModDefinition) });
            if (constructor != null)
            {
                return constructor.Invoke(new object[] { context, definition });
            }

            foreach (var candidate in pluginType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
            {
                if (TryBuildLegacyConstructorArguments(candidate, context, definition, out var arguments))
                {
                    return candidate.Invoke(arguments);
                }
            }

            return Activator.CreateInstance(pluginType);
        }

        private static Assembly LoadOrFindAssembly(string assemblyPath)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .Where(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(assembly => SamePath(SafeAssemblyLocation(assembly), assemblyPath))
                .ThenByDescending(assembly => SameDirectory(SafeAssemblyLocation(assembly), assemblyPath))
                .FirstOrDefault();
            if (loaded != null)
            {
                return loaded;
            }

            FuseLog.Info($"FUSE legacy support loading old-loader assembly '{assemblyPath}'.");
            return Assembly.LoadFrom(assemblyPath);
        }

        private static bool IsLegacyPluginType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            try
            {
                return type.IsClass &&
                       !type.IsAbstract &&
                       (typeof(LegacyPluginBase).IsAssignableFrom(type) ||
                        InheritsFromFullName(type, "Railloader.PluginBase"));
            }
            catch (Exception ex) when (
                ex is TypeLoadException ||
                ex is FileLoadException ||
                ex is FileNotFoundException ||
                ex is BadImageFormatException)
            {
                // A UMM package can already have a stale, partially bound copy
                // of its DLL in the AppDomain before recovery runs. Reflection
                // over compiler-generated nested types may throw here even
                // though FuseLegacyUmmRecovery subsequently activates the real
                // entry. Treat that type as unhostable; the package-level host
                // must not turn a recoverable probe into a mod error.
                return false;
            }
        }

        private static bool TryBuildLegacyConstructorArguments(
            ConstructorInfo constructor,
            FuseLegacyModdingContext context,
            FuseLegacyModDefinition definition,
            out object[] arguments)
        {
            arguments = null;
            if (constructor == null)
            {
                return false;
            }

            var parameters = constructor.GetParameters();
            arguments = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                if (parameterType.IsInstanceOfType(context))
                {
                    arguments[i] = context;
                    continue;
                }

                if (parameterType.IsInstanceOfType(definition))
                {
                    arguments[i] = definition;
                    continue;
                }

                if (parameterType.IsInstanceOfType(FuseLegacyUIHelper.Shared))
                {
                    arguments[i] = FuseLegacyUIHelper.Shared;
                    continue;
                }

                if (string.Equals(parameterType.FullName, "Railloader.IModdingContext", StringComparison.Ordinal))
                {
                    arguments[i] = CreateRealModdingContextProxy(parameterType, context);
                    continue;
                }

                if (string.Equals(parameterType.FullName, "Railloader.IModDefinition", StringComparison.Ordinal) ||
                    string.Equals(parameterType.FullName, "Railloader.IMod", StringComparison.Ordinal))
                {
                    arguments[i] = CreateRealModDefinitionProxy(parameterType, definition);
                    continue;
                }

                if (string.Equals(parameterType.FullName, "Railloader.IUIHelper", StringComparison.Ordinal))
                {
                    arguments[i] = CreateRealUIHelperProxy(parameterType);
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void InvokePluginLifecycleMethod(object plugin, string methodName)
        {
            if (plugin == null)
            {
                return;
            }

            if (plugin is LegacyPluginBase shimPlugin)
            {
                if (string.Equals(methodName, nameof(LegacyPluginBase.OnEnable), StringComparison.Ordinal))
                {
                    shimPlugin.OnEnable();
                }
                else if (string.Equals(methodName, nameof(LegacyPluginBase.OnDisable), StringComparison.Ordinal))
                {
                    shimPlugin.OnDisable();
                }

                return;
            }

            var method = plugin.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            method?.Invoke(plugin, Array.Empty<object>());
        }

        private static bool InheritsFromFullName(Type type, string fullName)
        {
            try
            {
                for (var current = type?.BaseType; current != null; current = current.BaseType)
                {
                    if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static object CreateRealModDefinitionProxy(Type interfaceType, FuseLegacyModDefinition definition)
        {
            return new LegacyInterfaceProxy(interfaceType, (method, args) =>
                InvokeRealModDefinition(definition, method)).GetTransparentProxy();
        }

        private static object CreateRealModdingContextProxy(Type interfaceType, FuseLegacyModdingContext context)
        {
            return new LegacyInterfaceProxy(interfaceType, (method, args) =>
                InvokeRealModdingContext(context, method, args)).GetTransparentProxy();
        }

        private static object CreateRealUIHelperProxy(Type interfaceType)
        {
            return new LegacyInterfaceProxy(interfaceType, InvokeRealUIHelper).GetTransparentProxy();
        }

        private static object InvokeRealUIHelper(MethodInfo method, object[] args)
        {
            if (method == null)
            {
                return null;
            }

            if (string.Equals(method.Name, "PopulateWindow", StringComparison.Ordinal))
            {
                return FuseLegacyUIHelper.Shared.PopulateWindow(
                    args != null && args.Length > 0 ? args[0] as UI.Common.Window : null,
                    args != null && args.Length > 1 ? args[1] as Action<UI.Builder.UIPanelBuilder> : null);
            }

            if (!string.Equals(method.Name, "CreateWindow", StringComparison.Ordinal))
            {
                return DefaultValue(method.ReturnType);
            }

            if (!method.IsGenericMethod)
            {
                if (args != null && args.Length == 3)
                {
                    return FuseLegacyUIHelper.Shared.CreateWindow(
                        (int)args[0],
                        (int)args[1],
                        (UI.Common.Window.Position)args[2]);
                }

                if (args != null && args.Length == 4)
                {
                    return FuseLegacyUIHelper.Shared.CreateWindow(
                        args[0] as string,
                        (int)args[1],
                        (int)args[2],
                        (UI.Common.Window.Position)args[3]);
                }
            }

            var genericArguments = method.GetGenericArguments();
            var target = typeof(FuseLegacyUIHelper)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(candidate => candidate.Name == "CreateWindow" && candidate.IsGenericMethodDefinition)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == (args?.Length ?? 0));
            return target?.MakeGenericMethod(genericArguments).Invoke(
                FuseLegacyUIHelper.Shared,
                args ?? Array.Empty<object>());
        }

        private static object InvokeRealModDefinition(FuseLegacyModDefinition definition, MethodInfo method)
        {
            switch (method?.Name)
            {
                case "get_Id":
                    return definition.Id;
                case "get_Name":
                    return definition.Name;
                case "get_Version":
                    return definition.Version;
                case "get_Directory":
                    return definition.Directory;
                case "get_LoadBefore":
                    return definition.LoadBefore;
                case "get_LoadAfter":
                    return ConvertModReferences(definition.LoadAfter, method.ReturnType);
                case "get_Requires":
                    return ConvertModReferences(definition.Requires, method.ReturnType);
                case "get_ConflictsWith":
                    return ConvertModReferences(definition.ConflictsWith, method.ReturnType);
                case "get_Plugins":
                    return CreateEmptyArrayForReturnType(method.ReturnType);
                case "get_IsEnabled":
                case "get_IsLoaded":
                    return true;
                case "get_IsFaulted":
                    return false;
                case "ToString":
                    return definition.Id;
                default:
                    return DefaultValue(method?.ReturnType);
            }
        }

        private static object InvokeRealModdingContext(FuseLegacyModdingContext context, MethodInfo method, object[] args)
        {
            switch (method?.Name)
            {
                case "get_RailloaderVersion":
                    return context.RailloaderVersion;
                case "get_ModsBaseDirectory":
                    return context.ModsBaseDirectory;
                case "get_Mods":
                    return CreateRealModCollectionForReturnType(context.Mods, method.ReturnType);
                case "RegisterConsoleCommand":
                    if (args != null && args.Length > 0 && args[0] is IConsoleCommand command)
                    {
                        RegisterConsoleCommand(command);
                    }
                    return null;
                case "LoadSettingsData":
                    return context.LoadSettingsData(
                        method.ReturnType,
                        args != null && args.Length > 0 ? args[0] as string : null);
                case "SaveSettingsData":
                    context.SaveSettingsDataObject(
                        args != null && args.Length > 0 ? args[0] as string : null,
                        args != null && args.Length > 1 ? args[1] : null);
                    return null;
                case "GetMixintos":
                    return CreateRealMixintoCollectionForReturnType(context, method, args);
                case "TryResolveFilePath":
                    return TryResolveFilePathFromProxy(context, method, args);
                case "RegisterSubTypeOverload":
                    RegisterLegacySubtypeFromProxy(method, args);
                    return null;
                case "RegisterComponent":
                    RegisterLegacyComponentFromProxy(method, args);
                    return null;
                case "ToString":
                    return "FUSE legacy Railloader context bridge";
                default:
                    return DefaultValue(method?.ReturnType);
            }
        }

        private static void RegisterLegacySubtypeFromProxy(MethodInfo method, object[] args)
        {
            var genericArguments = method?.GetGenericArguments() ?? Type.EmptyTypes;
            if (genericArguments.Length == 2)
            {
                FuseLegacyTypeRegistry.RegisterSubType(
                    genericArguments[0],
                    args != null && args.Length > 0 ? args[0] as string : null,
                    genericArguments[1]);
                return;
            }

            if (args != null && args.Length >= 3)
            {
                FuseLegacyTypeRegistry.RegisterSubType(
                    args[0] as Type,
                    args[1] as string,
                    args[2] as Type);
                return;
            }

            throw new MissingMethodException("Unsupported legacy RegisterSubTypeOverload signature.");
        }

        private static void RegisterLegacyComponentFromProxy(MethodInfo method, object[] args)
        {
            var genericArguments = method?.GetGenericArguments() ?? Type.EmptyTypes;
            if (genericArguments.Length != 2)
            {
                throw new MissingMethodException("Unsupported legacy RegisterComponent signature.");
            }

            FuseLegacyTypeRegistry.RegisterComponent(
                genericArguments[0],
                genericArguments[1],
                args != null && args.Length > 0 ? args[0] as string : null);
        }

        private static object CreateRealModCollectionForReturnType(IReadOnlyCollection<IMod> mods, Type returnType)
        {
            var elementType = returnType?.IsArray == true
                ? returnType.GetElementType()
                : returnType?.GetGenericArguments().FirstOrDefault();
            if (elementType == null)
            {
                return CreateEmptyArrayForReturnType(returnType);
            }

            var definitions = (mods ?? Array.Empty<IMod>()).OfType<FuseLegacyModDefinition>().ToArray();
            var result = Array.CreateInstance(elementType, definitions.Length);
            for (var index = 0; index < definitions.Length; index++)
            {
                result.SetValue(CreateRealModDefinitionProxy(elementType, definitions[index]), index);
            }

            return result;
        }

        private static object CreateRealMixintoCollectionForReturnType(
            FuseLegacyModdingContext context,
            MethodInfo method,
            object[] args)
        {
            var targets = args != null && args.Length > 0 && args[0] is string[] many
                ? many
                : new[] { args != null && args.Length > 0 ? args[0] as string : null };
            var mixintos = context.GetMixintos(targets).ToArray();
            var elementType = method.ReturnType?.IsArray == true
                ? method.ReturnType.GetElementType()
                : method.ReturnType?.GetGenericArguments().FirstOrDefault();
            if (elementType == null)
            {
                return CreateEmptyArrayForReturnType(method.ReturnType);
            }

            var result = Array.CreateInstance(elementType, mixintos.Length);
            for (var index = 0; index < mixintos.Length; index++)
            {
                result.SetValue(ConvertMixintoForForeignContract(mixintos[index], elementType), index);
            }

            return result;
        }

        private static object ConvertMixintoForForeignContract(ModMixinto mixinto, Type targetType)
        {
            var converted = Activator.CreateInstance(targetType);
            SetMemberValue(converted, targetType, "Mixinto", mixinto.Mixinto);
            SetMemberValue(converted, targetType, "Type", ConvertEnumValue(mixinto.Type, GetMemberType(targetType, "Type")));

            var sourceType = GetMemberType(targetType, "Source");
            if (sourceType != null && mixinto.Source is FuseLegacyModDefinition source)
            {
                SetMemberValue(converted, targetType, "Source", CreateRealModDefinitionProxy(sourceType, source));
            }

            SetMemberValue(converted, targetType, "Requires", ConvertModReferences(mixinto.Requires, GetMemberType(targetType, "Requires")));
            SetMemberValue(converted, targetType, "ConflictsWith", ConvertModReferences(mixinto.ConflictsWith, GetMemberType(targetType, "ConflictsWith")));
            SetMemberValue(converted, targetType, "ManagedObject", mixinto.ManagedObject);
            return converted;
        }

        private static object ConvertModReferences(ModReference[] references, Type targetType)
        {
            if (targetType?.IsArray != true)
            {
                return null;
            }

            var values = references ?? Array.Empty<ModReference>();
            var elementType = targetType.GetElementType();
            var result = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                var converted = Activator.CreateInstance(elementType);
                SetMemberValue(converted, elementType, "Id", values[index].Id);
                SetMemberValue(converted, elementType, "NotBefore", values[index].NotBefore);
                SetMemberValue(converted, elementType, "NotAfter", values[index].NotAfter);
                result.SetValue(converted, index);
            }

            return result;
        }

        private static object ConvertEnumValue(object value, Type targetType)
        {
            return targetType?.IsEnum == true
                ? Enum.ToObject(targetType, Convert.ToInt32(value))
                : value;
        }

        private static Type GetMemberType(Type declaringType, string name)
        {
            return declaringType?.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.PropertyType ??
                   declaringType?.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;
        }

        private static void SetMemberValue(object target, Type declaringType, string name, object value)
        {
            var property = declaringType?.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite == true && (value == null || property.PropertyType.IsInstanceOfType(value)))
            {
                property.SetValue(target, value, null);
                return;
            }

            var field = declaringType?.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
            {
                field.SetValue(target, value);
            }
        }

        private static bool TryResolveFilePathFromProxy(FuseLegacyModdingContext context, MethodInfo method, object[] args)
        {
            if (args == null)
            {
                return false;
            }

            var parameters = method.GetParameters();
            var outIndex = Array.FindIndex(parameters, p => p.ParameterType.IsByRef);
            string resolved;
            bool result;
            if (parameters.Length == 4)
            {
                result = context.TryResolveFilePath(
                    args[0] as string,
                    args[1] as string,
                    args.Length > 2 && args[2] is bool allow && allow,
                    out resolved);
            }
            else if (parameters.Length == 5)
            {
                result = context.TryResolveFilePath(
                    args[0] as string,
                    args[1] as string,
                    args[2] as string,
                    args.Length > 3 && args[3] is bool allow && allow,
                    out resolved);
            }
            else
            {
                resolved = null;
                result = false;
            }

            if (outIndex >= 0 && outIndex < args.Length)
            {
                args[outIndex] = resolved;
            }

            return result;
        }

        private static Array CreateEmptyArrayForReturnType(Type returnType)
        {
            var elementType = returnType?.IsArray == true
                ? returnType.GetElementType()
                : returnType?.GetGenericArguments().FirstOrDefault();
            return Array.CreateInstance(elementType ?? typeof(object), 0);
        }

        private static object DefaultValue(Type type)
        {
            if (type == null || type == typeof(void) || !type.IsValueType)
            {
                return null;
            }

            return Activator.CreateInstance(type);
        }

        private static IEnumerable<FuseLegacyAssemblyManifest> DiscoverLegacyManifests(string modsRoot)
        {
            var manifests = new List<FuseLegacyAssemblyManifest>();
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (TryReadLegacyManifest(packagePath, out var manifest))
                {
                    manifests.Add(manifest);
                }
            }

            return OrderLegacyManifestsForHosting(manifests);
        }

        internal static IReadOnlyList<FuseLegacyAssemblyManifest> OrderLegacyManifestsForHosting(
            IEnumerable<FuseLegacyAssemblyManifest> manifests)
        {
            var baseline = (manifests ?? Enumerable.Empty<FuseLegacyAssemblyManifest>())
                .Where(manifest => manifest != null)
                .OrderBy(manifest => manifest.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(manifest => manifest.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (baseline.Length <= 1)
            {
                return baseline;
            }

            var baselineIndex = baseline
                .Select((manifest, index) => new { manifest, index })
                .ToDictionary(item => item.manifest, item => item.index);
            var outgoing = baseline.ToDictionary(
                manifest => manifest,
                _ => new HashSet<FuseLegacyAssemblyManifest>());
            var incoming = baseline.ToDictionary(manifest => manifest, _ => 0);

            foreach (var manifest in baseline)
            {
                foreach (var reference in (manifest.RequiredReferences ?? Array.Empty<ModReference>())
                             .Concat(manifest.LoadAfter ?? Array.Empty<ModReference>()))
                {
                    if (TryResolveLegacyManifest(baseline, reference.Id, out var dependency))
                    {
                        AddLegacyLoadOrderEdge(dependency, manifest, outgoing, incoming);
                    }
                }

                foreach (var targetId in manifest.LoadBefore ?? Array.Empty<string>())
                {
                    if (TryResolveLegacyManifest(baseline, targetId, out var target))
                    {
                        AddLegacyLoadOrderEdge(manifest, target, outgoing, incoming);
                    }
                }
            }

            var ready = baseline
                .Where(manifest => incoming[manifest] == 0)
                .OrderBy(manifest => baselineIndex[manifest])
                .ToList();
            var ordered = new List<FuseLegacyAssemblyManifest>(baseline.Length);
            while (ready.Count > 0)
            {
                var next = ready[0];
                ready.RemoveAt(0);
                ordered.Add(next);
                foreach (var after in outgoing[next].OrderBy(manifest => baselineIndex[manifest]))
                {
                    incoming[after]--;
                    if (incoming[after] == 0)
                    {
                        ready.Add(after);
                        ready.Sort((left, right) => baselineIndex[left].CompareTo(baselineIndex[right]));
                    }
                }
            }

            if (ordered.Count == baseline.Length)
            {
                return ordered;
            }

            var cycleMembers = baseline.Where(manifest => !ordered.Contains(manifest)).ToArray();
            var signature = string.Join(",", cycleMembers.Select(manifest => manifest.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            if (ReportedLoadOrderCycles.Add(signature))
            {
                FuseLog.Warning(
                    $"FUSE legacy support found a loadAfter/loadBefore cycle among '{signature}'. " +
                    "A deterministic folder order will be used for the cycle; correct the declarations to guarantee plugin initialization order.");
            }

            ordered.AddRange(cycleMembers);
            return ordered;
        }

        private static bool TryResolveLegacyManifest(
            IEnumerable<FuseLegacyAssemblyManifest> manifests,
            string id,
            out FuseLegacyAssemblyManifest match)
        {
            match = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            match = manifests.FirstOrDefault(candidate =>
                candidate != null && FuseDeclaredPackageRelationship.SamePackageId(candidate.Id, id));
            return match != null;
        }

        private static void AddLegacyLoadOrderEdge(
            FuseLegacyAssemblyManifest before,
            FuseLegacyAssemblyManifest after,
            IDictionary<FuseLegacyAssemblyManifest, HashSet<FuseLegacyAssemblyManifest>> outgoing,
            IDictionary<FuseLegacyAssemblyManifest, int> incoming)
        {
            if (before == null || after == null || ReferenceEquals(before, after))
            {
                return;
            }

            if (outgoing[before].Add(after))
            {
                incoming[after]++;
            }
        }

        private static bool TryReadLegacyManifest(string folderPath, out FuseLegacyAssemblyManifest manifest)
        {
            manifest = null;
            var definitionPath = Path.Combine(folderPath, "Definition.json");
            if (!File.Exists(definitionPath))
            {
                return false;
            }

            JObject definition;
            try
            {
                definition = FuseLegacyDataConverter.ReadLegacyObject(definitionPath);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE legacy support ignored '{folderPath}' because Definition.json could not be parsed", ex);
                return false;
            }

            var id = ReadString(definition, "id", "Id") ?? Path.GetFileName(folderPath);
            manifest = new FuseLegacyAssemblyManifest
            {
                Id = id,
                Name = ReadString(definition, "name", "Name") ?? id,
                Version = ReadString(definition, "version", "Version") ?? string.Empty,
                FolderPath = folderPath,
                Assemblies = ReadStringArray(definition["assemblies"] ?? definition["Assemblies"]).ToArray(),
                Requires = ReadLegacyRequirementIds(definition["requires"] ?? definition["Requires"]).ToArray(),
                RequiredReferences = ReadLegacyModReferences(definition["requires"] ?? definition["Requires"]).ToArray(),
                LoadAfter = ReadLegacyModReferences(definition["loadAfter"] ?? definition["LoadAfter"]).ToArray(),
                LoadBefore = ReadLegacyRequirementIds(definition["loadBefore"] ?? definition["LoadBefore"]).ToArray(),
                ConflictsWith = ReadLegacyModReferences(definition["conflictsWith"] ?? definition["ConflictsWith"]).ToArray(),
                RawDefinition = definition
            };
            return true;
        }

        private static bool ShouldInspectPackage(FuseLegacyAssemblyManifest manifest)
        {
            if (manifest == null)
            {
                return false;
            }

            if (string.Equals(manifest.Id, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (FuseReplacementCapabilityCatalog.IsProvided(manifest.Id))
            {
                FuseLog.Info(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' " +
                    "because native FUSE compatibility replaces it.");
                return false;
            }

            if (FuseLegacyUmmRecovery.WasRecovered(manifest.FolderPath, manifest.Id))
            {
                FuseLog.Info(
                    $"FUSE legacy support skipped old-loader plugin hosting for '{manifest.Id}' " +
                    "because FUSE already recovered and activated its UMM entry.");
                return false;
            }

            if (FuseUmmState.TryGetDisabledReason(manifest.FolderPath, manifest.Id, out var disabledReason))
            {
                FuseLog.Info($"FUSE legacy support skipped UMM-disabled old-loader package '{manifest.Id}' reason='{disabledReason}'.");
                return false;
            }

            if (!FuseModSetService.IsPackageEnabledByActiveSet(manifest.Id, manifest.FolderPath))
            {
                FuseLog.Info(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' " +
                    $"reason='{FuseModSetService.GetPackageDisabledReason(manifest.Id, manifest.FolderPath)}'.");
                return false;
            }

            foreach (var requirement in manifest.RequiredReferences ?? Array.Empty<ModReference>())
            {
                if (FuseReplacementCapabilityCatalog.IsProvided(requirement.Id))
                {
                    continue;
                }

                if (IsLegacyReferencePresent(manifest, requirement))
                {
                    continue;
                }

                FuseLog.Warning(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' " +
                    $"because required legacy package '{requirement}' is not installed, enabled, or version-compatible.");
                return false;
            }

            foreach (var conflict in manifest.ConflictsWith ?? Array.Empty<ModReference>())
            {
                if (!IsLegacyReferencePresent(manifest, conflict))
                {
                    continue;
                }

                FuseLog.Warning(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' because its conflictsWith " +
                    $"reference '{conflict}' matches an enabled package. Disable one of the declared incompatible packages.");
                return false;
            }

            return true;
        }

        private static bool IsLegacyReferencePresent(FuseLegacyAssemblyManifest manifest, ModReference reference)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(reference.Id))
            {
                return false;
            }

            if (FuseReplacementCapabilityCatalog.IsProvided(reference.Id))
            {
                return true;
            }

            var modsRoot = Directory.GetParent(manifest.FolderPath)?.FullName;
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return false;
            }

            foreach (var packagePath in Directory.GetDirectories(modsRoot))
            {
                if (!TryReadLegacyManifest(packagePath, out var candidate) ||
                    !FuseDeclaredPackageRelationship.SamePackageId(candidate.Id, reference.Id) ||
                    FuseUmmState.TryGetDisabledReason(candidate.FolderPath, candidate.Id, out _) ||
                    !FuseModSetService.IsPackageEnabledByActiveSet(candidate.Id, candidate.FolderPath))
                {
                    continue;
                }

                if (!FuseModRequirementResolver.TryParseVersion(candidate.Version, out var installedVersion))
                {
                    return true;
                }

                return (reference.NotBefore == null || installedVersion.CompareTo(reference.NotBefore) >= 0) &&
                       (reference.NotAfter == null || installedVersion.CompareTo(reference.NotAfter) <= 0);
            }

            return false;
        }

        private static bool IsLegacyRequirementPresent(FuseLegacyAssemblyManifest manifest, string requirementId)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(requirementId))
            {
                return true;
            }

            var modsRoot = Directory.GetParent(manifest.FolderPath)?.FullName;
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                return false;
            }

            foreach (var packagePath in Directory.GetDirectories(modsRoot))
            {
                if (!TryReadLegacyManifest(packagePath, out var candidate))
                {
                    continue;
                }

                if (!string.Equals(candidate.Id, requirementId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (FuseUmmState.TryGetDisabledReason(candidate.FolderPath, candidate.Id, out _))
                {
                    return false;
                }

                return FuseModSetService.IsPackageEnabledByActiveSet(candidate.Id, candidate.FolderPath);
            }

            return false;
        }

        private static bool TryRegisterConsoleCommand(IConsoleCommand command)
        {
            try
            {
                var consoleCommandType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes)
                    .FirstOrDefault(t => t != null && t.FullName == "UI.Console.ConsoleCommandHandler");
                if (consoleCommandType == null)
                {
                    LogConsoleSurfaceUnavailable("UI.Console.ConsoleCommandHandler type not present.");
                    return false;
                }

                var handlerInstance = UnityEngine.Object.FindObjectOfType(consoleCommandType);
                if (handlerInstance == null)
                {
                    return false;
                }

                var registerMethod = consoleCommandType.GetMethod(
                    "Register", BindingFlags.Instance | BindingFlags.NonPublic);
                if (registerMethod == null || !registerMethod.IsGenericMethodDefinition)
                {
                    LogConsoleSurfaceUnavailable("ConsoleCommandHandler.Register<T> not found via reflection.");
                    return false;
                }

                registerMethod.MakeGenericMethod(command.GetType()).Invoke(handlerInstance, new object[] { command });
                FuseLog.Info($"FUSE legacy support registered console command '{command.GetType().FullName}'.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE legacy support failed to register console command '{command.GetType().FullName}': " +
                    ex.GetBaseException().Message);
                return false;
            }
        }

        private static void LogConsoleSurfaceUnavailable(string detail)
        {
            if (_consoleSurfaceUnavailableLogged)
            {
                return;
            }

            _consoleSurfaceUnavailableLogged = true;
            FuseLog.Warning($"FUSE legacy support console command registration is unavailable: {detail}");
        }

        private static IEnumerable<FuseLegacyMixintoEntry> EnumerateMixintoEntries(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token.Type == JTokenType.String)
            {
                var reference = ExtractFileReference(token.Value<string>());
                if (!string.IsNullOrWhiteSpace(reference))
                {
                    yield return new FuseLegacyMixintoEntry { Reference = reference };
                }

                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var reference in EnumerateMixintoEntries(item))
                    {
                        yield return reference;
                    }
                }

                yield break;
            }

            if (token is JObject obj)
            {
                var direct = ReadString(obj, "mixinto", "Mixinto");
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    var reference = ExtractFileReference(direct);
                    if (!string.IsNullOrWhiteSpace(reference))
                    {
                        yield return new FuseLegacyMixintoEntry
                        {
                            Reference = reference,
                            Requires = ReadLegacyModReferences(obj["requires"] ?? obj["Requires"]).ToArray(),
                            ConflictsWith = ReadLegacyModReferences(obj["conflictsWith"] ?? obj["ConflictsWith"]).ToArray()
                        };
                    }

                    yield break;
                }

                foreach (var property in obj.Properties())
                {
                    foreach (var reference in EnumerateMixintoEntries(property.Value))
                    {
                        yield return reference;
                    }
                }
            }
        }

        private static string ExtractFileReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                text = text.Substring(open + 1, close - open - 1);
            }

            return text.Trim().Trim('"', '\'');
        }

        private static string ResolvePackageFile(string folderPath, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return string.Empty;
            }

            var relative = reference.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(folderPath, relative));
        }

        private static string ResolveAssemblyPath(string folderPath, string assemblyReference)
        {
            if (string.IsNullOrWhiteSpace(assemblyReference))
            {
                return string.Empty;
            }

            var reference = assemblyReference.Trim().Trim('"', '\'')
                .Replace('/', Path.DirectorySeparatorChar);
            if (!reference.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                reference += ".dll";
            }

            return Path.GetFullPath(Path.Combine(folderPath, reference));
        }

        private static IEnumerable<string> ReadStringArray(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var value in ReadStringArray(item))
                    {
                        yield return value;
                    }
                }

                yield break;
            }

            var text = token.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }

        private static IEnumerable<string> ReadLegacyRequirementIds(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    var value = ReadLegacyRequirementId(item);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }

                yield break;
            }

            var single = ReadLegacyRequirementId(token);
            if (!string.IsNullOrWhiteSpace(single))
            {
                yield return single;
            }
        }

        private static string ReadLegacyRequirementId(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.ToString().Trim();
            }

            return ReadString(token as JObject, "id", "Id");
        }

        private static string ReadString(JObject obj, params string[] names)
        {
            if (obj == null || names == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var property = obj.Properties()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                if (property == null || property.Value.Type == JTokenType.Null)
                {
                    continue;
                }

                var value = property.Value.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null).ToArray();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        private static string SafeAssemblyLocation(Assembly assembly)
        {
            try
            {
                return assembly?.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameDirectory(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                NormalizePath(Path.GetDirectoryName(left) ?? string.Empty),
                NormalizePath(Path.GetDirectoryName(right) ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return (path ?? string.Empty).Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private sealed class HostedLegacyPlugin
        {
            private readonly IUpdateHandler _typedUpdateHandler;
            private readonly MethodInfo _reflectedUpdateMethod;

            public HostedLegacyPlugin(FuseLegacyAssemblyManifest manifest, Type type, object plugin)
            {
                Manifest = manifest;
                Type = type;
                Plugin = plugin;
                _typedUpdateHandler = plugin as IUpdateHandler;
                if (_typedUpdateHandler == null && ImplementsLegacyUpdateHandler(type))
                {
                    _reflectedUpdateMethod = type.GetMethod(
                        nameof(IUpdateHandler.Update),
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                }
            }

            public FuseLegacyAssemblyManifest Manifest { get; }
            public Type Type { get; }
            public object Plugin { get; }
            public bool UpdateFaulted { get; set; }
            public bool HasUpdateHandler => _typedUpdateHandler != null || _reflectedUpdateMethod != null;

            public void InvokeUpdate()
            {
                if (_typedUpdateHandler != null)
                {
                    _typedUpdateHandler.Update();
                    return;
                }

                _reflectedUpdateMethod?.Invoke(Plugin, Array.Empty<object>());
            }
        }

        private static bool IsLegacyMixintoActive(
            FuseLegacyAssemblyManifest manifest,
            FuseLegacyMixintoEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            foreach (var requirement in entry.Requires ?? Array.Empty<ModReference>())
            {
                if (!IsLegacyReferencePresent(manifest, requirement))
                {
                    return false;
                }
            }

            foreach (var conflict in entry.ConflictsWith ?? Array.Empty<ModReference>())
            {
                if (IsLegacyReferencePresent(manifest, conflict))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class FuseLegacyMixintoEntry
        {
            public string Reference { get; set; }
            public ModReference[] Requires { get; set; } = Array.Empty<ModReference>();
            public ModReference[] ConflictsWith { get; set; } = Array.Empty<ModReference>();
        }

        private static IEnumerable<ModReference> ReadLegacyModReferences(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var reference in ReadLegacyModReferences(item))
                    {
                        yield return reference;
                    }
                }

                yield break;
            }

            var id = ReadLegacyRequirementId(token);
            if (string.IsNullOrWhiteSpace(id))
            {
                yield break;
            }

            var obj = token as JObject;
            var notBeforeText = ReadString(obj, "notBefore", "NotBefore");
            var notAfterText = ReadString(obj, "notAfter", "NotAfter");
            var notBefore = FuseModRequirementResolver.TryParseVersion(notBeforeText, out var parsedNotBefore)
                ? parsedNotBefore
                : null;
            var notAfter = FuseModRequirementResolver.TryParseVersion(notAfterText, out var parsedNotAfter)
                ? parsedNotAfter
                : null;
            yield return new ModReference
            {
                Id = id,
                NotBefore = notBefore,
                NotAfter = notAfter
            };
        }

        private sealed class LegacyInterfaceProxy : RealProxy
        {
            private readonly Func<MethodInfo, object[], object> _invoke;

            public LegacyInterfaceProxy(Type interfaceType, Func<MethodInfo, object[], object> invoke)
                : base(interfaceType)
            {
                _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            }

            public override IMessage Invoke(IMessage msg)
            {
                var call = (IMethodCallMessage)msg;
                var args = call.Args != null
                    ? (object[])call.Args.Clone()
                    : Array.Empty<object>();

                try
                {
                    var method = call.MethodBase as MethodInfo;
                    var returnValue = _invoke(method, args);
                    var outArgs = GetOutArguments(method, args);
                    return new ReturnMessage(returnValue, outArgs, outArgs.Length, call.LogicalCallContext, call);
                }
                catch (Exception ex)
                {
                    return new ReturnMessage(ex.GetBaseException(), call);
                }
            }

            private static object[] GetOutArguments(MethodInfo method, object[] args)
            {
                var parameters = method?.GetParameters() ?? Array.Empty<ParameterInfo>();
                var values = new List<object>();
                foreach (var parameter in parameters)
                {
                    if (parameter.ParameterType.IsByRef && parameter.Position >= 0 && parameter.Position < args.Length)
                    {
                        values.Add(args[parameter.Position]);
                    }
                }

                return values.ToArray();
            }
        }
    }

    internal sealed class FuseLegacyAssemblyStartup : MonoBehaviour
    {
        // Unity invokes Start by reflection on the MonoBehaviour instance, so
        // this must remain an instance method.
#pragma warning disable CA1822 // Mark members as static
        private IEnumerator Start()
        {
            yield return null;
            // Run UMM injection here so the modEntries mutation lands after
            // UnityModManager._Start's foreach has released its enumerator.
            FUSE.Infrastructure.FuseUmmInjector.FlushPendingInjection();
            FuseLegacyAssemblyHost.LoadAllAvailableAssemblies("legacy support startup");
            // Dual-format packages need their RailLoader plugin instance first:
            // its singleton/context is part of the UMM patch side's runtime
            // contract. Recover the failed UMM entry only after hosting succeeds.
            FUSE.Compatibility.FuseLegacyUmmRecovery.RecoverFailedEntries();
        }
#pragma warning restore CA1822
    }

    internal sealed class FuseLegacyAssemblyManifest
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string FolderPath { get; set; }
        public string[] Assemblies { get; set; } = Array.Empty<string>();
        public string[] Requires { get; set; } = Array.Empty<string>();
        public ModReference[] RequiredReferences { get; set; } = Array.Empty<ModReference>();
        public ModReference[] LoadAfter { get; set; } = Array.Empty<ModReference>();
        public string[] LoadBefore { get; set; } = Array.Empty<string>();
        public ModReference[] ConflictsWith { get; set; } = Array.Empty<ModReference>();
        public JObject RawDefinition { get; set; }
    }

    internal sealed class FuseLegacyModDefinition : IMod
    {
        public FuseLegacyModDefinition(FuseLegacyAssemblyManifest manifest)
        {
            Id = manifest?.Id ?? string.Empty;
            Name = manifest?.Name ?? Id;
            Version = manifest?.Version ?? string.Empty;
            Directory = manifest?.FolderPath ?? string.Empty;
            Requires = manifest?.RequiredReferences ?? Array.Empty<ModReference>();
            LoadAfter = manifest?.LoadAfter ?? Array.Empty<ModReference>();
            LoadBefore = manifest?.LoadBefore ?? Array.Empty<string>();
            ConflictsWith = manifest?.ConflictsWith ?? Array.Empty<ModReference>();
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public string Directory { get; }

        public string[] LoadBefore { get; }
        public ModReference[] LoadAfter { get; }
        public ModReference[] Requires { get; }
        public ModReference[] ConflictsWith { get; }

        // We host the assembly so by definition it loaded and enabled. We do not
        // track per-plugin fault state from the IMod shim; callers that need it
        // can inspect FUSE's loader logs.
        public bool IsEnabled => true;
        public bool IsLoaded => true;
        public bool IsFaulted => false;
        public PluginBase[] Plugins => Array.Empty<PluginBase>();
    }

    internal sealed class FuseLegacyModdingContext : IModdingContext
    {
        // Reported as IModdingContext.RailloaderVersion to legacy plugins. The
        // value is held high enough to satisfy version-min compatibility gates
        // that plugins may check against the legacy loader's contract.
        private static readonly System.Version LegacyRailloaderVersion = new System.Version(1, 11, 1, 2);

        // Railloader uses JsonConvert's default serializer, whose default
        // TypeNameHandling is None. Pin that security-sensitive behavior here
        // instead of inheriting a process-wide JsonConvert.DefaultSettings
        // override installed by another mod.
        private static readonly JsonSerializerSettings LegacySettingsJson = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            TypeNameHandling = TypeNameHandling.None
        };

        private static readonly HashSet<char> InvalidSettingsFileNameChars = CreateInvalidSettingsFileNameChars();
        private static readonly object LegacySettingsCommitGate = new object();

        public FuseLegacyModdingContext(string modsBaseDirectory)
        {
            ModsBaseDirectory = modsBaseDirectory ?? string.Empty;
        }

        public System.Version RailloaderVersion => LegacyRailloaderVersion;
        public string ModsBaseDirectory { get; }
        public IReadOnlyCollection<IMod> Mods => FuseLegacyAssemblyHost.EnumerateInstalledMods(ModsBaseDirectory);

        public void RegisterConsoleCommand(IConsoleCommand command)
        {
            FuseLegacyAssemblyHost.RegisterConsoleCommand(command);
        }

        public T LoadSettingsData<T>(string settingsIdentifier) where T : class
        {
            return (T)LoadSettingsData(typeof(T), settingsIdentifier);
        }

        public void SaveSettingsData<T>(string settingsIdentifier, T settings) where T : class
        {
            SaveSettingsDataObject(settingsIdentifier, settings);
        }

        internal object LoadSettingsData(Type settingsType, string settingsIdentifier)
        {
            if (settingsType == null)
            {
                throw new ArgumentNullException(nameof(settingsType));
            }

            // Keep path validation outside the recovery block, matching
            // Railloader: invalid API arguments fail fast, while malformed or
            // unreadable settings files fail softly and let the mod use its
            // defaults.
            var path = GetSettingsFilePath(settingsIdentifier);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject(
                    File.ReadAllText(path),
                    settingsType,
                    LegacySettingsJson);
            }
            catch (Exception ex)
            {
                // The settings identifier is the legacy mod's own id by the old
                // loader's convention — the closest attribution available here
                // (the shared context does not know which manifest is calling).
                FuseModExceptionRegistry.RecordContained(ex, settingsIdentifier, "legacy host settings load");
                FuseLog.Exception(
                    $"FUSE legacy support could not load configuration of type '{settingsType.FullName}' from '{path}'",
                    ex);
                return null;
            }
        }

        internal void SaveSettingsDataObject(string settingsIdentifier, object settings)
        {
            var path = GetSettingsFilePath(settingsIdentifier);
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(settings, LegacySettingsJson);
            var temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                // The temporary file lives beside the destination, so Move for
                // a first save and Replace for an update are same-volume atomic
                // operations. Readers therefore see either complete old JSON or
                // complete new JSON, never a truncated settings file.
                File.WriteAllText(temporaryPath, json);
                // The API is synchronous and multiple legacy plugins can save
                // during the same lifecycle transition. Serialize only the
                // existence check and atomic commit so concurrent first saves
                // cannot both select File.Move for the same destination.
                lock (LegacySettingsCommitGate)
                {
                    if (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, path);
                    }
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception cleanupException)
                {
                    // Best-effort cleanup must not hide the original save error,
                    // but retain enough evidence to diagnose a stranded temp file.
                    FuseLog.Warning(
                        $"FUSE legacy support could not clean up temporary settings file " +
                        $"'{temporaryPath}': {cleanupException.GetBaseException().Message}");
                }
            }
        }

        internal string GetSettingsFilePath(string settingsIdentifier)
        {
            if (settingsIdentifier == null)
            {
                throw new ArgumentNullException(nameof(settingsIdentifier));
            }

            var sanitizedIdentifier = SanitizeSettingsIdentifier(settingsIdentifier);
            var settingsDirectory = Path.GetFullPath(Path.Combine(
                ModsBaseDirectory,
                "Railloader",
                "ModSettings"));
            var candidate = Path.GetFullPath(Path.Combine(settingsDirectory, sanitizedIdentifier + ".json"));
            var directoryPrefix = settingsDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Settings identifier resolves outside the Railloader settings directory.", nameof(settingsIdentifier));
            }

            return candidate;
        }

        private static string SanitizeSettingsIdentifier(string settingsIdentifier)
        {
            var sanitized = settingsIdentifier.ToCharArray();
            for (var index = 0; index < sanitized.Length; index++)
            {
                if (InvalidSettingsFileNameChars.Contains(sanitized[index]))
                {
                    sanitized[index] = '_';
                }
            }

            return new string(sanitized);
        }

        private static HashSet<char> CreateInvalidSettingsFileNameChars()
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            invalid.UnionWith(Path.GetInvalidPathChars());

            // These are already invalid filename characters on Windows. Keep
            // them explicit so the containment guarantee survives a different
            // runtime's narrower invalid-character table.
            invalid.Add(Path.DirectorySeparatorChar);
            invalid.Add(Path.AltDirectorySeparatorChar);
            invalid.Add(Path.VolumeSeparatorChar);
            return invalid;
        }

        public IEnumerable<ModMixinto> GetMixintos(string target)
        {
            return FuseLegacyAssemblyHost.EnumerateMixintos(target);
        }

        public IEnumerable<ModMixinto> GetMixintos(string target, bool allowNonFileEntries)
        {
            return GetMixintos(target);
        }

        public IEnumerable<ModMixinto> GetMixintos(string[] targets)
        {
            if (targets == null)
            {
                yield break;
            }

            foreach (var target in targets)
            {
                foreach (var mixinto in GetMixintos(target))
                {
                    yield return mixinto;
                }
            }
        }

        public IEnumerable<ModMixinto> GetMixintos(string[] targets, bool allowNonFileEntries)
        {
            return GetMixintos(targets);
        }

        public bool TryResolveFilePath(string baseDirectory, string value, bool allowNonFileEntries, out string result)
        {
            return TryResolveFilePath(baseDirectory, baseDirectory, value, allowNonFileEntries, out result);
        }

        public bool TryResolveFilePath(string baseDirectory, string rootDirectory, string value, bool allowNonFileEntries, out string result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Mirror the minimum behavior plugins depend on: resolve a relative
            // path against baseDirectory; if missing, fall through to rootDirectory.
            try
            {
                if (System.IO.Path.IsPathRooted(value) && System.IO.File.Exists(value))
                {
                    result = value;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(baseDirectory))
                {
                    var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, value));
                    if (System.IO.File.Exists(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(rootDirectory) && !string.Equals(rootDirectory, baseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootDirectory, value));
                    if (System.IO.File.Exists(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE legacy IModdingContext.TryResolveFilePath threw resolving '{value}'", ex);
            }

            return false;
        }

        public void RegisterSubTypeOverload<TBaseClass, TImplementation>(string identifier)
        {
            FuseLegacyTypeRegistry.RegisterSubType(typeof(TBaseClass), identifier, typeof(TImplementation));
        }

        public void RegisterSubTypeOverload(System.Type baseClass, string identifier, System.Type implementation)
        {
            FuseLegacyTypeRegistry.RegisterSubType(baseClass, identifier, implementation);
        }

        public void RegisterComponent<TComponent, TComponentBuilder>(string kind)
            where TComponent : Model.Definition.Component
            where TComponentBuilder : Model.IComponentBuilder
        {
            FuseLegacyTypeRegistry.RegisterComponent(typeof(TComponent), typeof(TComponentBuilder), kind);
        }
    }
}
