using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetPack.Runtime;
using Effects.Decals;
using FUSE.Infrastructure;
using Helpers;
using Helpers.Culling;
using Model;
using Model.Definition.Data;
using Railloader.Extensions;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FUSE.Compatibility
{
    internal sealed class FuseConfusingSupplementsDestinationSignComponent : Model.Definition.Component
    {
        internal const string ComponentKind = "ConfusingSupplements.DestinationSign";

        public override string Kind => ComponentKind;

        public AssetReference Model { get; set; } = new AssetReference();

        public Vector3 Size { get; set; } = Vector3.one;

        public List<string> Destinations { get; set; } = new List<string>();
    }

    internal sealed class FuseConfusingSupplementsDestinationSignBuilder : ComponentBuilder<FuseConfusingSupplementsDestinationSignComponent>
    {
        protected override void Build(
            ComponentBuilderContext context,
            FuseConfusingSupplementsDestinationSignComponent component)
        {
            if (context.GameObject == null || component == null)
            {
                return;
            }

            var controller = context.GameObject.AddComponent<FuseConfusingSupplementsDestinationSignController>();
            controller.Configure(component);
        }
    }

    internal sealed class FuseConfusingSupplementsDestinationSignController : MonoBehaviour,
        IPickable,
        CullingManager.ICullingEventHandler
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private LoadedAssetReference<GameObject> _loadedModel;
        private CullingManager.Token _cullingToken;
        private DecalProjectorHelper _decal;
        private GameObject _modelInstance;
        private GameObject _sign;
        private string[] _destinations = Array.Empty<string>();
        private int _selectedIndex = -1;

        public float MaxPickDistance => _sign == null ? 0f : 5f;

        public int Priority => 0;

        public TooltipInfo TooltipInfo => new TooltipInfo("Change destination sign", "Primary: next; secondary: previous");

        public PickableActivationFilter ActivationFilter => PickableActivationFilter.Any;

        internal async void Configure(FuseConfusingSupplementsDestinationSignComponent component)
        {
            await ConfigureAsync(component);
        }

        private async Task ConfigureAsync(FuseConfusingSupplementsDestinationSignComponent component)
        {
            var cancellationToken = _cancellation.Token;
            try
            {
                if (component?.Model == null || component.Model.IsEmpty)
                {
                    throw new ArgumentException("Destination-sign model reference is empty.");
                }

                _destinations = (component.Destinations ?? new List<string>())
                    .Where(destination => !string.IsNullOrWhiteSpace(destination))
                    .ToArray();
                _loadedModel = await TrainController.Shared.PrefabStore.LoadAssetAsync<GameObject>(
                    component.Model.AssetPackIdentifier,
                    component.Model.AssetIdentifier,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (_loadedModel?.Asset == null)
                {
                    throw new InvalidOperationException($"Destination-sign model '{component.Model.AssetIdentifier}' did not load.");
                }

                _modelInstance = UnityEngine.Object.Instantiate(_loadedModel.Asset, transform, false);
                _modelInstance.name = "FUSE Destination Sign";
                _modelInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                _sign = _modelInstance.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => string.Equals(child.name, "Sign", StringComparison.OrdinalIgnoreCase))
                    ?.gameObject;
                if (_sign == null)
                {
                    throw new InvalidOperationException(
                        $"Destination-sign model '{component.Model.AssetIdentifier}' has no child named 'Sign'.");
                }

                var projectorObject = new GameObject("FUSE Destination Text");
                projectorObject.SetActive(false);
                projectorObject.transform.SetParent(_sign.transform, false);
                projectorObject.transform.SetLocalPositionAndRotation(
                    Vector3.forward * 0.1f,
                    Quaternion.Euler(0f, 180f, 0f));

                var projector = projectorObject.AddComponent<DecalProjector>();
                projector.size = component.Size;
                projector.pivot = Vector3.zero;
                projector.drawDistance = Mathf.Max(
                    600f,
                    Mathf.Max(component.Size.x, component.Size.y) * 100f);

                _decal = projectorObject.AddComponent<DecalProjectorHelper>();
                _decal.decalRenderer = CanvasDecalRenderer.Shared;
                _decal.templateName = "Tender";
                _decal.text = string.Empty;
                _decal.ForceColor(Color.black);
                _decal.RenderDecal();
                projectorObject.SetActive(true);

                _cullingToken = CullingManager.Scenery?.AddSphere(transform, 10f, this);
                _cullingToken?.RegisterFixedUpdate(transform);
                RefreshSign();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CleanupRuntimeObjects();
                return;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE could not build a legacy destination sign: " +
                    $"{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                CleanupRuntimeObjects();
            }
        }

        public void Activate(PickableActivateEvent evt)
        {
            _selectedIndex = NextIndex(
                _selectedIndex,
                _destinations.Length,
                evt.Activation == PickableActivation.Secondary);
            RefreshSign();
        }

        public void Deactivate()
        {
        }

        internal static int NextIndex(int current, int count, bool previous)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (previous)
            {
                return current <= -1 ? count - 1 : current - 1;
            }

            return current >= count - 1 ? -1 : current + 1;
        }

        private void RefreshSign()
        {
            if (_sign == null)
            {
                return;
            }

            if (_selectedIndex < 0 || _selectedIndex >= _destinations.Length || _decal == null)
            {
                _sign.SetActive(false);
                return;
            }

            _decal.text = _destinations[_selectedIndex];
            _decal.RenderDecal();
            _sign.SetActive(true);
        }

        public void CullingSphereStateChanged(bool isVisible, int distanceBand)
        {
            if (_modelInstance != null)
            {
                _modelInstance.SetActive(isVisible && distanceBand < 1);
            }
        }

        public void RequestUpdateCullingPosition()
        {
            _cullingToken?.UpdatePosition(transform);
        }

        private void OnDestroy()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            CleanupRuntimeObjects();
        }

        private void CleanupRuntimeObjects()
        {
            _cullingToken?.Dispose();
            _cullingToken = null;
            _loadedModel?.Dispose();
            _loadedModel = null;
            if (_modelInstance != null)
            {
                UnityEngine.Object.Destroy(_modelInstance);
                _modelInstance = null;
            }

            _sign = null;
            _decal = null;
        }
    }
}
