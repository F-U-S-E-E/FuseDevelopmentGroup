using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FUSE.Infrastructure;
using Newtonsoft.Json.Linq;
using Railloader;
using UI.Console;
using UnityEngine;

namespace FUSE.Loading
{
    internal static class FuseLegacyAssemblyHost
    {
        private static readonly Dictionary<string, HostedLegacyPlugin> HostedPlugins =
            new Dictionary<string, HostedLegacyPlugin>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<IConsoleCommand> PendingConsoleCommands = new List<IConsoleCommand>();
        private static readonly HashSet<string> FuseReplacedLegacyPackages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AlinaNova21.AlinasMapMod",
                "AlinaNova21.MapEditor",
                "Railloader",
                "RailLoader",
                "Zamu.StrangeCustoms"
            };

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

            RetryPendingConsoleCommands();
            return hostedCount;
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

                    foreach (var fileReference in EnumerateMixintoReferences(property.Value))
                    {
                        var mixintoPath = ResolvePackageFile(manifest.FolderPath, fileReference);
                        if (!string.IsNullOrWhiteSpace(mixintoPath))
                        {
                            yield return new ModMixinto(mixintoPath, new FuseLegacyModDefinition(manifest));
                        }
                    }
                }
            }
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
                    hosted.Plugin.OnDisable();
                    FuseLog.Info(
                        $"FUSE legacy support disabled hosted old-loader plugin '{hosted.Type.FullName}' from '{hosted.Manifest.Id}'.");
                }
                catch (Exception ex)
                {
                    FuseLog.Exception(
                        $"FUSE legacy support failed while disabling hosted old-loader plugin '{hosted.Type.FullName}'",
                        ex);
                }
            }

            HostedPlugins.Clear();
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

            LegacyPluginBase plugin;
            try
            {
                plugin = CreatePluginInstance(pluginType, context, definition);
            }
            catch (Exception ex)
            {
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
                plugin.OnEnable();
                HostedPlugins[key] = new HostedLegacyPlugin(manifest, pluginType, plugin);
                FuseLog.Info(
                    $"FUSE legacy support enabled hosted old-loader plugin '{pluginType.FullName}' " +
                    $"from package '{manifest.Id}'. This is temporary legacy compatibility, not a native FUSE package.");
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Exception(
                    $"FUSE legacy support failed to enable old-loader plugin '{pluginType.FullName}' from '{manifest.Id}'",
                    ex);
                return false;
            }
        }

        private static LegacyPluginBase CreatePluginInstance(
            Type pluginType,
            FuseLegacyModdingContext context,
            FuseLegacyModDefinition definition)
        {
            var constructor = pluginType.GetConstructor(new[] { typeof(IModdingContext), typeof(IModDefinition) });
            object instance;
            if (constructor != null)
            {
                instance = constructor.Invoke(new object[] { context, definition });
            }
            else
            {
                instance = Activator.CreateInstance(pluginType);
            }

            return instance as LegacyPluginBase;
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
            return type != null &&
                   type.IsClass &&
                   !type.IsAbstract &&
                   typeof(LegacyPluginBase).IsAssignableFrom(type);
        }

        private static IEnumerable<FuseLegacyAssemblyManifest> DiscoverLegacyManifests(string modsRoot)
        {
            foreach (var packagePath in Directory.GetDirectories(modsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (TryReadLegacyManifest(packagePath, out var manifest))
                {
                    yield return manifest;
                }
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
                FuseLog.Warning($"FUSE legacy support ignored '{folderPath}' because Definition.json could not be parsed: {ex.Message}");
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

            if (FuseReplacedLegacyPackages.Contains(manifest.Id ?? string.Empty))
            {
                FuseLog.Info(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' " +
                    "because native FUSE compatibility replaces it.");
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

            foreach (var requirementId in manifest.Requires ?? Array.Empty<string>())
            {
                if (FuseReplacedLegacyPackages.Contains(requirementId ?? string.Empty))
                {
                    continue;
                }

                if (IsLegacyRequirementPresent(manifest, requirementId))
                {
                    continue;
                }

                FuseLog.Warning(
                    $"FUSE legacy support skipped old-loader package '{manifest.Id}' " +
                    $"because required legacy package '{requirementId}' is not installed or enabled.");
                return false;
            }

            return true;
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

        private static IEnumerable<string> EnumerateMixintoReferences(JToken token)
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
                    yield return reference;
                }

                yield break;
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    foreach (var reference in EnumerateMixintoReferences(item))
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
                        yield return reference;
                    }
                }

                foreach (var property in obj.Properties())
                {
                    foreach (var reference in EnumerateMixintoReferences(property.Value))
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
            public HostedLegacyPlugin(FuseLegacyAssemblyManifest manifest, Type type, LegacyPluginBase plugin)
            {
                Manifest = manifest;
                Type = type;
                Plugin = plugin;
            }

            public FuseLegacyAssemblyManifest Manifest { get; }
            public Type Type { get; }
            public LegacyPluginBase Plugin { get; }
        }
    }

    internal sealed class FuseLegacyAssemblyStartup : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null;
            // Run UMM injection here so the modEntries mutation lands after
            // UnityModManager._Start's foreach has released its enumerator.
            FUSE.Infrastructure.FuseUmmInjector.FlushPendingInjection();
            FuseLegacyAssemblyHost.LoadAllAvailableAssemblies("legacy support startup");
        }
    }

    internal sealed class FuseLegacyAssemblyManifest
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string FolderPath { get; set; }
        public string[] Assemblies { get; set; } = Array.Empty<string>();
        public string[] Requires { get; set; } = Array.Empty<string>();
        public JObject RawDefinition { get; set; }
    }

    internal sealed class FuseLegacyModDefinition : IModDefinition
    {
        public FuseLegacyModDefinition(FuseLegacyAssemblyManifest manifest)
        {
            Id = manifest?.Id ?? string.Empty;
            Name = manifest?.Name ?? Id;
            Version = manifest?.Version ?? string.Empty;
            Directory = manifest?.FolderPath ?? string.Empty;
        }

        public string Id { get; }
        public string Name { get; }
        public string Version { get; }
        public string Directory { get; }
    }

    internal sealed class FuseLegacyModdingContext : IModdingContext
    {
        public FuseLegacyModdingContext(string modsBaseDirectory)
        {
            ModsBaseDirectory = modsBaseDirectory ?? string.Empty;
        }

        public string ModsBaseDirectory { get; }

        public IEnumerable<ModMixinto> GetMixintos(string identifier)
        {
            return FuseLegacyAssemblyHost.EnumerateMixintos(identifier);
        }

        public void RegisterConsoleCommand(IConsoleCommand command)
        {
            FuseLegacyAssemblyHost.RegisterConsoleCommand(command);
        }
    }
}
