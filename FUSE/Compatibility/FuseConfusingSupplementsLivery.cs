using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FUSE.Infrastructure;
using FUSE.Loading;
using Game.Messages;
using Game.State;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Railloader.Extensions;
using UI.Builder;
using UnityEngine;

namespace FUSE.Compatibility
{
    internal sealed class FuseConfusingSupplementsLiveryComponent : Model.Definition.Component
    {
        internal const string ComponentKind = "CS.LiverySwap";

        public override string Kind => ComponentKind;
    }

    internal sealed class FuseConfusingSupplementsLiveryBuilder : ComponentBuilder<FuseConfusingSupplementsLiveryComponent>
    {
        internal const string SavedPropertyKey = "cs.livery";

        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsLiveryComponent component)
        {
            var car = context.GameObject?.GetComponentInParent<Car>();
            if (car == null)
            {
                FuseLog.Warning(
                    $"FUSE could not attach legacy livery support to '{context.ObjectName ?? "<unknown car>"}' " +
                    "because its car object was unavailable.");
                return;
            }

            var controller = car.GetComponent<FuseConfusingSupplementsLiveryController>() ??
                             car.gameObject.AddComponent<FuseConfusingSupplementsLiveryController>();
            controller.Configure(car);
            context.ObserveProperty(SavedPropertyKey, controller.ApplySavedSelection);
        }
    }

    internal sealed class FuseConfusingSupplementsLiveryController : MonoBehaviour
    {
        private readonly List<MaterialSnapshot> _materials = new List<MaterialSnapshot>();
        private readonly HashSet<Material> _ownedMaterials = new HashSet<Material>();
        private Car _car;
        private string _carIdentifier;
        private bool _configured;

        internal void Configure(Car car)
        {
            if (_configured || car == null)
            {
                return;
            }

            var definitionInfo = car.DefinitionInfo;
            if (definitionInfo == null)
            {
                return;
            }

            _car = car;
            _carIdentifier = definitionInfo.Identifier ?? string.Empty;
            CaptureMaterials(car.gameObject);
            _configured = true;
        }

        internal void ApplySavedSelection(Value value)
        {
            if (!_configured)
            {
                return;
            }

            var selected = value.StringValue;
            RestoreOriginalTextures();
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            var choice = FuseConfusingSupplementsLiveryRegistry.GetChoices(_carIdentifier)
                .FirstOrDefault(candidate => string.Equals(candidate.Id, selected, StringComparison.OrdinalIgnoreCase));
            if (choice == null)
            {
                FuseLog.Warning(
                    $"FUSE could not find legacy livery '{selected}' for rolling stock '{_carIdentifier}'. " +
                    "The standard textures remain active.");
                return;
            }

            ApplyDirectory(choice.DirectoryPath);
        }

        internal string CarIdentifier => _carIdentifier ?? string.Empty;

        internal string SelectedLiveryId
        {
            get
            {
                if (!_configured || _car == null || _car.KeyValueObject == null)
                {
                    return string.Empty;
                }

                return _car.KeyValueObject[FuseConfusingSupplementsLiveryBuilder.SavedPropertyKey].StringValue ??
                       string.Empty;
            }
        }

        internal void RefreshFromSavedSelection()
        {
            if (!_configured)
            {
                return;
            }

            ApplySavedSelection(
                _car == null || _car.KeyValueObject == null
                    ? Value.Null()
                    : _car.KeyValueObject[FuseConfusingSupplementsLiveryBuilder.SavedPropertyKey]);
        }

        internal void RestoreOriginalTexturesForRefresh()
        {
            if (_configured)
            {
                RestoreOriginalTextures();
            }
        }

        internal void CaptureMaterials(GameObject carObject)
        {
            _materials.Clear();
            var propertyIds = new List<int>();
            foreach (var renderer in carObject.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials;
                try
                {
                    // Renderer.materials creates per-renderer instances. This keeps a
                    // livery chosen on one car from repainting every car that shares
                    // the asset pack's source material.
                    materials = renderer.materials;
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not isolate livery materials on '{_carIdentifier}': {ex.GetBaseException().Message}");
                    continue;
                }

                foreach (var material in materials.Where(material => material != null))
                {
                    _ownedMaterials.Add(material);
                    propertyIds.Clear();
                    material.GetTexturePropertyNameIDs(propertyIds);
                    foreach (var propertyId in propertyIds)
                    {
                        var texture = material.GetTexture(propertyId);
                        if (texture == null)
                        {
                            continue;
                        }

                        _materials.Add(new MaterialSnapshot(
                            material,
                            propertyId,
                            texture,
                            texture.name ?? string.Empty));
                    }
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var material in _ownedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }

            _ownedMaterials.Clear();
        }

        private void RestoreOriginalTextures()
        {
            foreach (var snapshot in _materials)
            {
                if (snapshot.Material != null)
                {
                    snapshot.Material.SetTexture(snapshot.PropertyId, snapshot.OriginalTexture);
                }
            }
        }

        private void ApplyDirectory(string directoryPath)
        {
            var files = FuseConfusingSupplementsLiveryRegistry.GetTextureFiles(directoryPath);
            if (files.Count == 0)
            {
                FuseLog.Warning(
                    $"FUSE legacy livery folder '{directoryPath}' has no PNG or JPEG texture files.");
                return;
            }

            var replacements = 0;
            foreach (var snapshot in _materials)
            {
                if (snapshot.Material == null || string.IsNullOrWhiteSpace(snapshot.OriginalTextureName) ||
                    !files.TryGetValue(snapshot.OriginalTextureName, out var texturePath))
                {
                    continue;
                }

                var texture = FuseConfusingSupplementsLiveryRegistry.LoadTexture(texturePath);
                if (texture == null)
                {
                    continue;
                }

                snapshot.Material.SetTexture(snapshot.PropertyId, texture);
                replacements++;
            }

            if (replacements == 0)
            {
                FuseLog.Warning(
                    $"FUSE legacy livery folder '{directoryPath}' did not match any source texture names on " +
                    $"rolling stock '{_carIdentifier}'.");
            }
        }

        private sealed class MaterialSnapshot
        {
            internal MaterialSnapshot(
                Material material,
                int propertyId,
                Texture originalTexture,
                string originalTextureName)
            {
                Material = material;
                PropertyId = propertyId;
                OriginalTexture = originalTexture;
                OriginalTextureName = originalTextureName;
            }

            internal Material Material { get; }
            internal int PropertyId { get; }
            internal Texture OriginalTexture { get; }
            internal string OriginalTextureName { get; }
        }
    }

    internal static class FuseConfusingSupplementsLiveryPolicy
    {
        internal static bool IsTextureFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class FuseConfusingSupplementsLiveryRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Texture2D> Textures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IReadOnlyList<FuseConfusingSupplementsLiveryChoice>> Choices =
            new Dictionary<string, IReadOnlyList<FuseConfusingSupplementsLiveryChoice>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> FileIndexes =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal static int CachedTextureCount
        {
            get
            {
                lock (Gate)
                {
                    return Textures.Count;
                }
            }
        }

        internal static int RefreshLiveCars()
        {
            var controllers = Resources
                .FindObjectsOfTypeAll<FuseConfusingSupplementsLiveryController>()
                .Where(IsLiveController)
                .ToArray();
            foreach (var controller in controllers)
            {
                try
                {
                    controller.RestoreOriginalTexturesForRefresh();
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not restore standard textures on '{controller.CarIdentifier}': " +
                        ex.GetBaseException().Message);
                }
            }

            ClearTextureCache();
            foreach (var controller in controllers)
            {
                try
                {
                    controller.RefreshFromSavedSelection();
                }
                catch (Exception ex)
                {
                    FuseLog.Warning(
                        $"FUSE could not refresh the legacy livery on '{controller.CarIdentifier}': " +
                        ex.GetBaseException().Message);
                }
            }

            return controllers.Length;
        }

        internal static string BuildDiagnosticReport()
        {
            var controllers = Resources
                .FindObjectsOfTypeAll<FuseConfusingSupplementsLiveryController>()
                .Where(IsLiveController)
                .OrderBy(controller => controller.CarIdentifier, StringComparer.OrdinalIgnoreCase)
                .ThenBy(controller => controller.GetInstanceID())
                .ToArray();
            var report = new StringBuilder();
            report.Append("FUSE livery diagnostics: cars=")
                .Append(controllers.Length)
                .Append(" cachedTextures=")
                .Append(CachedTextureCount);

            foreach (var controller in controllers)
            {
                var choices = GetChoices(controller.CarIdentifier);
                report.AppendLine()
                    .Append("- car=")
                    .Append(string.IsNullOrWhiteSpace(controller.CarIdentifier)
                        ? "<unknown>"
                        : controller.CarIdentifier)
                    .Append(" selected=")
                    .Append(string.IsNullOrWhiteSpace(controller.SelectedLiveryId)
                        ? "<standard>"
                        : controller.SelectedLiveryId)
                    .Append(" choices=")
                    .Append(choices.Count);
                if (choices.Count > 0)
                {
                    report.Append(" [")
                        .Append(string.Join(", ", choices.Select(choice => choice.Id)))
                        .Append(']');
                }
            }

            return report.ToString();
        }

        internal static void Shutdown()
        {
            ClearTextureCache();
        }

        private static void ClearTextureCache()
        {
            Texture2D[] textures;
            lock (Gate)
            {
                textures = Textures.Values.Where(texture => texture != null).ToArray();
                Textures.Clear();
                Choices.Clear();
                FileIndexes.Clear();
            }

            foreach (var texture in textures)
            {
                DestroyTexture(texture);
            }
        }

        internal static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static bool IsLiveController(
            FuseConfusingSupplementsLiveryController controller)
        {
            return controller != null
                   && controller.gameObject != null
                   && controller.gameObject.scene.IsValid();
        }

        internal static IReadOnlyList<FuseConfusingSupplementsLiveryChoice> GetChoices(string carIdentifier)
        {
            if (string.IsNullOrWhiteSpace(carIdentifier))
            {
                return Array.Empty<FuseConfusingSupplementsLiveryChoice>();
            }

            lock (Gate)
            {
                if (Choices.TryGetValue(carIdentifier, out var cached))
                {
                    return cached;
                }
            }

            var choices = new Dictionary<string, FuseConfusingSupplementsLiveryChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (var mixinto in FuseLegacyAssemblyHost.EnumerateMixintos("livery:" + carIdentifier))
            {
                var path = mixinto.Mixinto;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    FuseLog.Warning(
                        $"FUSE ignored legacy livery entry '{path ?? "<empty>"}' for '{carIdentifier}' " +
                        "because it is not an existing directory.");
                    continue;
                }

                var id = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (choices.ContainsKey(id))
                {
                    FuseLog.Warning(
                        $"FUSE found more than one legacy livery named '{id}' for '{carIdentifier}'. " +
                        $"The first folder wins; ignored '{path}'.");
                    continue;
                }

                choices[id] = new FuseConfusingSupplementsLiveryChoice(id, path);
            }

            var discovered = choices.Values.OrderBy(choice => choice.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            lock (Gate)
            {
                if (!Choices.TryGetValue(carIdentifier, out var cached))
                {
                    Choices[carIdentifier] = discovered;
                    return discovered;
                }

                return cached;
            }
        }

        internal static Dictionary<string, string> IndexTextureFiles(string directoryPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return result;
            }

            try
            {
                foreach (var path in Directory.EnumerateFiles(directoryPath))
                {
                    if (!FuseConfusingSupplementsLiveryPolicy.IsTextureFile(path))
                    {
                        continue;
                    }

                    var key = Path.GetFileNameWithoutExtension(path);
                    if (result.TryGetValue(key, out var existing))
                    {
                        var winner = string.Compare(path, existing, StringComparison.OrdinalIgnoreCase) < 0
                            ? path
                            : existing;
                        var ignored = string.Equals(winner, path, StringComparison.OrdinalIgnoreCase)
                            ? existing
                            : path;
                        result[key] = winner;
                        FuseLog.Warning(
                            $"FUSE found more than one legacy livery texture named '{key}' in " +
                            $"'{directoryPath}'. Alphabetical path precedence selected '{winner}' and ignored '{ignored}'.");
                        continue;
                    }

                    result[key] = path;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                LogTextureIndexFailure(directoryPath, ex);
            }
            catch (IOException ex)
            {
                LogTextureIndexFailure(directoryPath, ex);
            }

            return result;
        }

        internal static IReadOnlyDictionary<string, string> GetTextureFiles(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            lock (Gate)
            {
                if (FileIndexes.TryGetValue(directoryPath, out var cached))
                {
                    return cached;
                }
            }

            var discovered = IndexTextureFiles(directoryPath);
            lock (Gate)
            {
                if (!FileIndexes.TryGetValue(directoryPath, out var cached))
                {
                    FileIndexes[directoryPath] = discovered;
                    return discovered;
                }

                return cached;
            }
        }

        private static void LogTextureIndexFailure(string directoryPath, Exception ex)
        {
            FuseLog.Warning(
                $"FUSE could not index legacy livery textures in '{directoryPath}': " +
                ex.GetBaseException().Message);
        }

        internal static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            lock (Gate)
            {
                if (Textures.TryGetValue(path, out var cached) && cached != null)
                {
                    return cached;
                }
            }

            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                {
                    name = Path.GetFileNameWithoutExtension(path)
                };
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true))
                {
                    DestroyTexture(texture);
                    return null;
                }
            }
            catch (Exception ex)
            {
                DestroyTexture(texture);

                FuseLog.Warning(
                    $"FUSE could not load legacy livery texture '{path}': {ex.GetBaseException().Message}");
                return null;
            }

            lock (Gate)
            {
                if (Textures.TryGetValue(path, out var cached) && cached != null)
                {
                    DestroyTexture(texture);
                    return cached;
                }

                Textures[path] = texture;
                return texture;
            }
        }
    }

    internal sealed class FuseConfusingSupplementsLiveryChoice
    {
        internal FuseConfusingSupplementsLiveryChoice(string id, string directoryPath)
        {
            Id = id;
            DirectoryPath = directoryPath;
        }

        internal string Id { get; }
        internal string DirectoryPath { get; }
    }

    [HarmonyPatch(typeof(UI.CarCustomizeWindow.CarCustomizeWindow), "BuildColorTab")]
    internal static class FuseConfusingSupplementsLiveryCustomizePatch
    {
        private static readonly string[] StandardLiveryIds = { string.Empty };
        private static readonly string[] StandardLiveryNames = { "Standard" };

        private static void Postfix(UIPanelBuilder builder, Car ____car)
        {
            if (____car?.Definition?.Components == null ||
                !____car.Definition.Components.OfType<FuseConfusingSupplementsLiveryComponent>().Any())
            {
                return;
            }

            try
            {
                var choices = FuseConfusingSupplementsLiveryRegistry
                    .GetChoices(____car.DefinitionInfo.Identifier)
                    .ToArray();
                builder.AddSection("Livery", section =>
                {
                    if (choices.Length == 0)
                    {
                        section.AddLabel(
                            $"No compatible liveries found for {____car.DefinitionInfo.Identifier}.");
                        return;
                    }

                    var ids = StandardLiveryIds.Concat(choices.Select(choice => choice.Id)).ToArray();
                    var names = StandardLiveryNames.Concat(choices.Select(choice => choice.Id)).ToList();
                    var current = ____car.KeyValueObject == null
                        ? null
                        : ____car.KeyValueObject[FuseConfusingSupplementsLiveryBuilder.SavedPropertyKey].StringValue;
                    var selected = Array.FindIndex(
                        ids,
                        id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase));
                    if (selected < 0)
                    {
                        selected = 0;
                    }

                    section.AddField("Livery", section.AddDropdown(names, selected, index =>
                    {
                        if (____car.KeyValueObject == null || index < 0 || index >= ids.Length)
                        {
                            return;
                        }

                        StateManager.ApplyLocal(new PropertyChange(
                            ____car.id,
                            FuseConfusingSupplementsLiveryBuilder.SavedPropertyKey,
                            new StringPropertyValue(ids[index] ?? string.Empty)));
                    }));
                }, 30f);
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE contained a legacy livery Customize-window error; " +
                    $"the rest of the window remains usable: {ex.GetBaseException().Message}");
            }
        }
    }
}
