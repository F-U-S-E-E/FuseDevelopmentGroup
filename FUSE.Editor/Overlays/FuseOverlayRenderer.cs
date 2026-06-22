using FUSE.Editor.Track.Overlays;
using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Overlays
{
    /// <summary>
    /// Renders preview overlays for objects with uncommitted edits.
    /// Displays ghost/wireframe versions at preview positions without modifying the original objects.
    /// </summary>
    public class FuseOverlayRenderer : IDisposable
    {
        private readonly Dictionary<string, OverlayPreviewData> _activePreviews = new();
        private readonly OverlayHandlerRegistry _handlerRegistry = new();
        private readonly OverlaySelectionSystem _selectionSystem;
        private Material _wireframeMaterial;
        private Material _ghostMaterial;
        private readonly LayerMask _overlayLayerMask;
        private bool _disposed;

        public Material GhostMaterial => _ghostMaterial;
        public Material WireframeMaterial => _wireframeMaterial;

        /// <summary>
        /// Layer used exclusively for overlay rendering (should not collide with gameplay layers).
        /// </summary>
        private const int OverlayLayer = 30; // Use a high layer number to avoid conflicts

        /// <summary>
        /// Called when a preview is added.
        /// </summary>
        public event Action<string> OnPreviewAdded;

        /// <summary>
        /// Called when a preview is removed.
        /// </summary>
        public event Action<string> OnPreviewRemoved;

        /// <summary>
        /// Called when a preview is updated.
        /// </summary>
        public event Action<string> OnPreviewUpdated;

        public FuseOverlayRenderer()
        {
            try
            {
                _overlayLayerMask = LayerMask.GetMask("Default"); // Safe fallback; can be overridden
                _selectionSystem = new OverlaySelectionSystem(_activePreviews);
                InitializeMaterials();

                _handlerRegistry.RegisterHandler(new TrackNodeOverlayHandler());
                _handlerRegistry.RegisterHandler(new TrackSegmentOverlayHandler());
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"FUSE overlay renderer: Error during initialization: {ex.Message}\n{ex.StackTrace}");
                _wireframeMaterial = null;
                _ghostMaterial = null;
            }
        }

        /// <summary>
        /// Initializes the wireframe and ghost materials used for previews.
        /// Uses URP-compatible shaders (Universal Render Pipeline/Unlit).
        /// </summary>
        private void InitializeMaterials()
        {
            Shader wireframeShader = null;

            // Try URP Unlit shader first (recommended for overlays in URP)
            wireframeShader = Shader.Find("Unlit/Color");

            if (wireframeShader == null)
            {
                // Fallback to URP Lit (works for overlays too)
                wireframeShader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (wireframeShader == null)
            {
                // Fallback to standard unlit
                wireframeShader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (wireframeShader == null)
            {
                // Last resort: use default material shader
                wireframeShader = Shader.Find("Standard");
            }

            if (wireframeShader == null)
            {
                FuseLog.Error("FUSE overlay renderer: No suitable shader found for wireframe material. Please ensure URP is properly installed. Attempting to use a null material as fallback (this will likely crash).");
                // If we can't find ANY shader, materials will be null but we proceed to prevent initialization loop
                _wireframeMaterial = null;
                _ghostMaterial = null;
                return;
            }

            // Wireframe material: white, opaque (for visibility)
            _wireframeMaterial = new Material(wireframeShader)
            {
                name = "OverlayWireframe"
            };

            // Set color (works for both URP and standard shaders)
            if (_wireframeMaterial.HasProperty("_BaseColor"))
            {
                _wireframeMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
            }
            else if (_wireframeMaterial.HasProperty("_Color"))
            {
                _wireframeMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 1f));
            }

            // Configure transparency (for URP shaders)
            /*
            if (_wireframeMaterial.HasProperty("_SrcBlend"))
            {
                _wireframeMaterial.SetInt("_SrcBlend", 5); // SrcAlpha
            }
            if (_wireframeMaterial.HasProperty("_DstBlend"))
            {
                _wireframeMaterial.SetInt("_DstBlend", 10); // OneMinusSrcAlpha
            }
            
            if (_wireframeMaterial.HasProperty("_ZWrite"))
            {
                _wireframeMaterial.SetInt("_ZWrite", 0);
            }
            if (_wireframeMaterial.HasProperty("_Surface"))
            {
                _wireframeMaterial.SetFloat("_Surface", 1); // Transparent
            }
            if (_wireframeMaterial.HasProperty("_Blend"))
            {
                _wireframeMaterial.SetFloat("_Blend", 0);
            }
            if (_wireframeMaterial.HasProperty("_Cull"))
            {
                _wireframeMaterial.SetFloat("_Cull", 0); // No culling
            }
            */
            // Set depth test to Always so overlay renders over everything
            /*
            if (_wireframeMaterial.HasProperty("_ZTest"))
            {
                _wireframeMaterial.SetInt("_ZTest", 8); // Always
            }
            */
            _wireframeMaterial.renderQueue = 4000; // Render last (on top)
            /*
            // Try to disable shadow receiving keyword if it exists
            if (_wireframeMaterial.HasProperty("_RECEIVE_SHADOWS_OFF"))
            {
                _wireframeMaterial.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            */
            // Ghost material: semi-transparent white
            _ghostMaterial = new Material(wireframeShader)
            {
                name = "OverlayGhost"
            };

            if (_ghostMaterial.HasProperty("_BaseColor"))
            {
                _ghostMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.3f));
            }
            else if (_ghostMaterial.HasProperty("_Color"))
            {
                _ghostMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.3f));
            }
            /*
            if (_ghostMaterial.HasProperty("_SrcBlend"))
            {
                _ghostMaterial.SetInt("_SrcBlend", 5); // SrcAlpha
            }
            if (_ghostMaterial.HasProperty("_DstBlend"))
            {
                _ghostMaterial.SetInt("_DstBlend", 10); // OneMinusSrcAlpha
            }
            if (_ghostMaterial.HasProperty("_ZWrite"))
            {
                _ghostMaterial.SetInt("_ZWrite", 0);
            }
            if (_ghostMaterial.HasProperty("_Surface"))
            {
                _ghostMaterial.SetFloat("_Surface", 1); // Transparent
            }
            if (_ghostMaterial.HasProperty("_Blend"))
            {
                _ghostMaterial.SetFloat("_Blend", 0);
            }
            if (_ghostMaterial.HasProperty("_Cull"))
            {
                _ghostMaterial.SetFloat("_Cull", 0); // No culling
            }
            /*
            // Set depth test to Always so overlay renders over everything
            if (_ghostMaterial.HasProperty("_ZTest"))
            {
                _ghostMaterial.SetInt("_ZTest", 8); // Always
            }
            */
            _ghostMaterial.renderQueue = 4000; // Render last (on top)
            /*
            if (_ghostMaterial.HasProperty("_RECEIVE_SHADOWS_OFF"))
            {
                _ghostMaterial.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            }
            */
        }

        /// <summary>
        /// Gets the handler registry for registering entity-specific overlay handlers.
        /// </summary>
        public OverlayHandlerRegistry HandlerRegistry => _handlerRegistry;

        /// <summary>
        /// Gets the selection system for handling overlay clicks and selection.
        /// </summary>
        public OverlaySelectionSystem SelectionSystem => _selectionSystem;

        /// <summary>
        /// Sets the camera used for selection raycasting.
        /// </summary>
        public void SetSelectionCamera(Camera camera)
        {
            _selectionSystem.SetCamera(camera);
        }

        /// <summary>
        /// Applies a preview for an entity using its registered handler with preview data.
        /// This is the primary generic API for creating overlays with the dual-type handler model.
        /// </summary>
        /// <typeparam name="TEntity">The entity type (e.g., TrackNode).</typeparam>
        /// <typeparam name="TPreviewData">The preview data type (e.g., FuseNode).</typeparam>
        /// <param name="entity">The entity to create a preview for.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <returns>The preview data, or null if failed.</returns>
        public OverlayPreviewData ApplyPreview<TEntity, TPreviewData>(TEntity entity, TPreviewData previewData)
        {
            var previewDataObj = _handlerRegistry.ApplyPreview(entity, previewData, out var previewId);
            if (previewDataObj == null)
            {
                return null;
            }

            return RegisterPreview(
                previewDataObj.PreviewId,
                previewDataObj.OriginalObject,
                previewDataObj.FuseData,
                previewDataObj.Renderable);
        }

        /// <summary>
        /// Applies a preview for an entity and returns its preview ID.
        /// Convenience overload that returns the ID for further reference.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="entity">The entity to create a preview for.</param>
        /// <param name="previewData">The preview/pending-edit data.</param>
        /// <param name="previewId">Output: the ID of the created preview.</param>
        /// <returns>The preview data, or null if failed.</returns>
        public OverlayPreviewData ApplyPreview<TEntity, TPreviewData>(TEntity entity, TPreviewData previewData, out string previewId)
        {
            var previewDataObj = _handlerRegistry.ApplyPreview(entity, previewData, out previewId);
            if (previewDataObj == null)
            {
                return null;
            }

            return RegisterPreview(
                previewDataObj.PreviewId,
                previewDataObj.OriginalObject,
                previewDataObj.FuseData,
                previewDataObj.Renderable);
        }

        /// <summary>
        /// Updates an existing preview from entity and preview data using its handler.
        /// </summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <typeparam name="TPreviewData">The preview data type.</typeparam>
        /// <param name="objectId">The ID of the preview to update.</param>
        /// <param name="entity">The entity with updated values.</param>
        /// <param name="previewData">The updated preview/pending-edit data.</param>
        public void UpdatePreviewFromEntity<TEntity, TPreviewData>(string objectId, TEntity entity, TPreviewData previewData)
        {
            var handler = _handlerRegistry.GetHandler<TEntity, TPreviewData>();
            if (handler == null)
            {
                FuseLog.Error($"Overlay renderer: No handler registered for type '{typeof(TEntity).Name}'.");
                return;
            }

            if (!_activePreviews.TryGetValue(objectId, out var preview))
            {
                FuseLog.Warning($"Overlay renderer: No preview registered for '{objectId}'.");
                return;
            }

            handler.ExtractPreviewTransform(entity, previewData, out var position, out var rotation, out var scale);
            UpdatePreview(objectId, position, rotation, scale);
        }

        /// <param name="objectId">Unique identifier for the object.</param>
        /// <param name="originalObject">The original game object (not modified).</param>
        /// <param name="fuseData">The preview/pending-edit data (e.g., FuseNode).</param>
        /// <param name="renderable">Optional IOverlayRenderable for custom rendering. If null, uses original object's mesh.</param>
        /// <returns>The preview data object.</returns>
        public OverlayPreviewData RegisterPreview(
            string objectId,
            GameObject originalObject,
            object fuseData,
            IOverlayRenderable renderable = null)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                FuseLog.Error("Overlay renderer: Cannot register preview with null or empty ID.");
                return null;
            }

            if (originalObject == null)
            {
                FuseLog.Error($"Overlay renderer: Cannot register preview '{objectId}' with null object.");
                return null;
            }

            // Replace existing preview if it exists
            if (_activePreviews.ContainsKey(objectId))
            {
                UnregisterPreview(objectId);
            }

            var preview = new OverlayPreviewData(
                originalObject,
                fuseData,
                objectId)
            {
                Renderable = renderable
            };

            _activePreviews[objectId] = preview;
            OnPreviewAdded?.Invoke(objectId);

            return preview;
        }

        public void RegisterPreview(OverlayPreviewData previewData)
        {
            _activePreviews[previewData.PreviewId] = previewData;
        }

        /// <summary>
        /// Updates an existing preview's transform values.
        /// </summary>
        /// <param name="objectId">The object ID.</param>
        /// <param name="position">New preview position.</param>
        /// <param name="rotation">New preview rotation.</param>
        /// <param name="scale">New preview scale.</param>
        public void UpdatePreview(
            string objectId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale)
        {
            if (!_activePreviews.TryGetValue(objectId, out var preview))
            {
                FuseLog.Warning($"Overlay renderer: No preview registered for '{objectId}'.");
                return;
            }

            preview.UpdatePreviewTransform(position, rotation, scale);
            OnPreviewUpdated?.Invoke(objectId);
        }

        /// <summary>
        /// Gets a registered preview by ID.
        /// </summary>
        public OverlayPreviewData GetPreview(string objectId)
        {
            _activePreviews.TryGetValue(objectId, out var preview);
            return preview;
        }

        /// <summary>
        /// Checks whether a preview is registered.
        /// </summary>
        public bool HasPreview(string objectId)
        {
            return _activePreviews.ContainsKey(objectId);
        }

        /// <summary>
        /// Unregisters and stops rendering a preview.
        /// </summary>
        public void UnregisterPreview(string objectId)
        {
            if (_activePreviews.Remove(objectId))
            {
                OnPreviewRemoved?.Invoke(objectId);
            }
        }

        /// <summary>
        /// Clears all registered previews.
        /// </summary>
        public void ClearAllPreviews()
        {
            var ids = new List<string>(_activePreviews.Keys);
            foreach (var id in ids)
            {
                UnregisterPreview(id);
            }
        }

        /// <summary>
        /// Renders all active previews. Call this from an editor update or OnPostRender hook.
        /// </summary>
        public void RenderPreviews()
        {
            if (_disposed)
            {
                return;
            }

            foreach (var preview in _activePreviews.Values)
            {
                if (!preview.IsVisible)
                {
                    continue;
                }

                RenderPreview(preview);
            }
        }

        /// <summary>
        /// Renders a single preview with dual-pass rendering.
        /// First pass: Render with darkened, more opaque color to mark occlusion.
        /// Second pass: Render with normal color on top to ensure visibility.
        /// Uses MaterialPropertyBlock for per-instance color overrides without modifying the material.
        /// </summary>
        private void RenderPreview(OverlayPreviewData preview)
        {
            try
            {
                //FuseLog.Info($"Rendering preview '{preview.PreviewId}' for object '{preview.OriginalObject.name}'");
                var mesh = GetMeshForPreview(preview);
                var material = GetMaterialForPreview(preview);

                if (mesh == null || material == null)
                {
                    FuseLog.Info($"Mesh and/or Material are null: {mesh}, {material}'");
                    return;
                }

                var matrix = preview.GetPreviewMatrix();
                Color renderColor = Color.white;

                // Determine render color
                if (preview.Tint.HasValue)
                {
                    renderColor = preview.Tint.Value;
                }
                else
                {
                    // Get original material color as fallback
                    if (material.HasProperty("_BaseColor"))
                    {
                        renderColor = material.GetColor("_BaseColor");
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        renderColor = material.GetColor("_Color");
                    }
                }
                /*
                // PASS 1: Render darkened occluded layer with full opacity
                // This darkens anything hidden behind the object
                Color occludedColor = new Color(
                    renderColor.r * 0.4f,  // Darken by 60%
                    renderColor.g * 0.4f,
                    renderColor.b * 0.4f,
                    0.1f                   // Full opacity - marks depth and visibility
                );

                MaterialPropertyBlock mpbOcclusion = new MaterialPropertyBlock();
                if (material.HasProperty("_BaseColor"))
                {
                    mpbOcclusion.SetColor("_BaseColor", occludedColor);
                }
                else if (material.HasProperty("_Color"))
                {
                    mpbOcclusion.SetColor("_Color", occludedColor);
                }

                RenderParams rpOcclusion = new RenderParams(material)
                {
                    matProps = mpbOcclusion
                };
                Graphics.RenderMesh(rpOcclusion, mesh, 0, matrix);
                */
                // PASS 2: Render bright overlay on top with original color/tint
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                if (material.HasProperty("_BaseColor"))
                {
                    mpb.SetColor("_BaseColor", renderColor);
                }
                else if (material.HasProperty("_Color"))
                {
                    mpb.SetColor("_Color", renderColor);
                }

                RenderParams rp = new RenderParams(material)
                {
                    matProps = mpb,
                    //shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    //receiveShadows = false,
                    //lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
                    camera = Camera.main
                };
                Graphics.RenderMesh(rp, mesh, 0, matrix);

                //FuseLog.Info($"Rendered preview '{preview.PreviewId}' at {matrix.GetPosition()}'");
            }
            catch (System.Exception ex)
            {
                FuseLog.Error($"FUSE overlay renderer: Error rendering preview: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the mesh to render for a preview.
        /// Uses IOverlayRenderable if available, otherwise falls back to the original object's mesh.
        /// </summary>
        private Mesh GetMeshForPreview(OverlayPreviewData preview)
        {
            // Try custom renderable first
            if (preview.Renderable != null)
            {
                var mesh = preview.Renderable.GetOverlayMesh(preview.Entity, preview.FuseData);
                if (mesh != null)
                {
                    return mesh;
                }
            }

            // Fall back to original object's mesh
            var filter = preview.OriginalObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                return filter.sharedMesh;
            }

            // No mesh found
            return null;
        }

        /// <summary>
        /// Gets the material to use for rendering a preview.
        /// Uses IOverlayRenderable if available, otherwise uses default wireframe.
        /// </summary>
        private Material GetMaterialForPreview(OverlayPreviewData preview)
        {
            // Try custom renderable first
            if (preview.Renderable != null)
            {
                var material = preview.Renderable.GetOverlayMaterial(preview.Entity, preview.FuseData);
                if (material != null)
                {
                    return material;
                }
            }

            // Fall back to wireframe material
            if (_wireframeMaterial == null)
            {
                FuseLog.Error("FUSE overlay renderer: Wireframe material is null. Overlays cannot be rendered.");
                return null;
            }

            return _wireframeMaterial;
        }

        /// <summary>
        /// Gets the wireframe material used for standard previews.
        /// </summary>
        public Material GetWireframeMaterial()
        {
            if (_wireframeMaterial == null)
            {
                FuseLog.Error("FUSE overlay renderer: Wireframe material is null. Check material initialization.");
            }
            return _wireframeMaterial;
        }

        /// <summary>
        /// Gets the ghost material used for semi-transparent previews.
        /// </summary>
        public Material GetGhostMaterial()
        {
            if (_ghostMaterial == null)
            {
                FuseLog.Error("FUSE overlay renderer: Ghost material is null. Check material initialization.");
            }
            return _ghostMaterial;
        }

        /// <summary>
        /// Gets the count of active previews.
        /// </summary>
        public int GetActivePreviewCount() => _activePreviews.Count;

        /// <summary>
        /// Gets all active preview IDs.
        /// </summary>
        public IEnumerable<string> GetActivePreviewIds() => _activePreviews.Keys;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _activePreviews.Clear();

            if (_wireframeMaterial != null)
            {
                UnityEngine.Object.Destroy(_wireframeMaterial);
                _wireframeMaterial = null;
            }

            if (_ghostMaterial != null)
            {
                UnityEngine.Object.Destroy(_ghostMaterial);
                _ghostMaterial = null;
            }

            _disposed = true;
        }
    }
}
