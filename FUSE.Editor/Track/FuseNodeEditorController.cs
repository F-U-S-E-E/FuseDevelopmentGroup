using System;
using System.Collections.Generic;
using FUSE.Authoring.Data;
using FUSE.Authoring.Entities;
using FUSE.Infrastructure;
using FUSE.Loading;
using Helpers;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track
{
    /// <summary>
    /// Coordinates per-mod node markers, raycast-based node creation, and
    /// persistence back into the active FuseModDefinition. Stateless across
    /// map loads — call <see cref="ShowMarkersForActiveMod"/> when the panel
    /// is opened and <see cref="ClearMarkers"/> when it closes.
    /// </summary>
    internal static class FuseNodeEditorController
    {
        private static readonly List<FuseNodeMarker> ActiveMarkers = new List<FuseNodeMarker>();
        private static FuseNodeMarker _selected;
        private static Material _markerMaterial;

        // Distance threshold for marker culling (in world units)
        private const float CullingDistance = 150f;
        private const float CullingDistanceSq = CullingDistance * CullingDistance;

        public static FuseNodeMarker Selected => _selected;

        public static void ShowMarkersForActiveMod()
        {
            ClearMarkers();

            var mod = FuseEditor.Instance != null ? FuseEditor.Instance.ActiveMod : null;
            if (mod == null)
            {
                FuseLog.Info("FUSE node editor: no active mod selected.");
                return;
            }

            // Create markers for ALL nodes in the graph, not just the active mod's nodes
            var allNodes = FUSE.Runtime.API.TrackAPI.GetAllNodes();
            if (allNodes == null)
            {
                FuseLog.Info("FUSE node editor: no track nodes available in the graph.");
                return;
            }

            int markerCount = 0;
            foreach (var trackNode in allNodes)
            {
                if (trackNode == null)
                {
                    continue;
                }

                // Check which mod owns this node (if any)
                string owningModId = DetermineOwningMod(trackNode.id, mod.Definition.Id);
                AttachMarker(trackNode, owningModId);
                markerCount++;
            }

            FuseLog.Info($"FUSE node editor: attached {markerCount} marker(s) for all nodes in the graph (active mod: '{mod.Definition.Id}').");
        }

        public static void ClearMarkers()
        {
            if (_selected != null)
            {
                _selected.Deselect();
                _selected = null;
            }

            for (int i = 0; i < ActiveMarkers.Count; i++)
            {
                var marker = ActiveMarkers[i];
                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }

            ActiveMarkers.Clear();
        }

        public static void SelectMarker(FuseNodeMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            if (_selected != null && !ReferenceEquals(_selected, marker))
            {
                _selected.Deselect();
                _selected.SetSelected(false);
            }

            _selected = marker;
            _selected.SetSelected(true);

            // Update the editor's entity selection to match the marker selection
            var editor = FuseEditor.Instance;
            if (editor?.EntitySelection != null && marker.Node != null)
            {
                editor.EntitySelection.SetSelectedEntity(FUSE.Runtime.API.TrackAPI.GetDefinition(marker.Node), marker.Node.id);
            }
        }

        /// <summary>
        /// Looks up the marker currently rendering for <paramref name="nodeId"/>,
        /// or returns <c>false</c> if no marker exists (e.g. no marker-
        /// spawning tool is active or the node's runtime TrackNode was
        /// never materialised). Used by the entity tree to bridge a
        /// row-click to a selection + gizmo engage when possible.
        /// </summary>
        public static bool TryFindMarker(string nodeId, out FuseNodeMarker marker)
        {
            marker = null;
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            for (int i = 0; i < ActiveMarkers.Count; i++)
            {
                var candidate = ActiveMarkers[i];
                if (candidate == null || candidate.Node == null)
                {
                    continue;
                }

                if (string.Equals(candidate.Node.id, nodeId, StringComparison.Ordinal))
                {
                    marker = candidate;
                    return true;
                }
            }

            return false;
        }

        public static void DeselectCurrent()
        {
            if (_selected == null)
            {
                return;
            }

            _selected.Deselect();
            _selected.SetSelected(false);
            _selected = null;
        }

        /// <summary>
        /// Determines which mod owns a given node. Checks the active mod first,
        /// then falls back to checking all loaded mods.
        /// </summary>
        private static string DetermineOwningMod(string nodeId, string activeModId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            // First check the active mod
            if (!string.IsNullOrEmpty(activeModId))
            {
                if (FuseModLoader.TryGetLoadedMod(activeModId, out var activeMod) &&
                    activeMod?.Definition?.Tracks?.Nodes != null &&
                    activeMod.Definition.Tracks.Nodes.ContainsKey(nodeId))
                {
                    return activeModId;
                }
            }

            // Check all other loaded mods
            var loadedMods = FuseModLoader.GetLoadedModsInOrder();
            foreach (var mod in loadedMods)
            {
                if (mod?.Definition?.Tracks?.Nodes != null &&
                    mod.Definition.Tracks.Nodes.ContainsKey(nodeId))
                {
                    return mod.Definition.Id;
                }
            }

            return null; // Node doesn't belong to any known mod
        }

        /// <summary>
        /// Updates visibility of all markers based on camera distance.
        /// Should be called every frame while markers are active.
        /// </summary>
        public static void UpdateMarkerVisibility()
        {
            if (Camera.main == null)
            {
                return;
            }

            Vector3 camPos = Camera.main.transform.position;

            for (int i = 0; i < ActiveMarkers.Count; i++)
            {
                var marker = ActiveMarkers[i];
                if (marker == null || marker.Node == null)
                {
                    continue;
                }

                float distanceSq = (marker.Node.transform.position - camPos).sqrMagnitude;
                bool shouldBeVisible = distanceSq <= CullingDistanceSq;
                marker.SetVisibility(shouldBeVisible);
            }
        }

        public static bool TryCreateNodeAtCameraRaycast(string newNodeId, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(newNodeId))
            {
                error = "Node id is required.";
                return false;
            }

            if (Camera.main == null)
            {
                error = "No main camera available.";
                return false;
            }

            var mod = FuseEditor.Instance != null ? FuseEditor.Instance.ActiveMod : null;
            if (mod == null)
            {
                error = "Select a mod first.";
                return false;
            }

            if (global::Track.Graph.Shared.GetNode(newNodeId) != null)
            {
                error = $"A node with id '{newNodeId}' already exists in the live graph.";
                return false;
            }

            if (!Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out var hit, 250f))
            {
                error = "Camera ray did not hit any geometry within 250m.";
                return false;
            }

            var gamePosition = WorldTransformer.WorldToGame(hit.point);
            var fuseNode = new FuseNode
            {
                Position = gamePosition,
                Rotation = Vector3.zero,
            };

            if (!PersistFuseNode(mod, newNodeId, fuseNode, "node created via FUSE editor"))
            {
                error = "Failed to write node to mod definition (see logs).";
                return false;
            }

            var trackNode = SpawnRuntimeTrackNode(newNodeId, fuseNode);
            if (trackNode == null)
            {
                error = "Persisted node, but failed to spawn the runtime TrackNode.";
                return false;
            }

            AttachMarker(trackNode, mod.Definition.Id);
            return true;
        }

        public static void PersistNode(string modId, TrackNode trackNode)
        {
            if (trackNode == null || string.IsNullOrEmpty(modId))
            {
                return;
            }

            if (!FuseModLoader.TryGetLoadedMod(modId, out var mod) || mod?.Definition == null)
            {
                FuseLog.Warning($"FUSE node editor cannot persist node '{trackNode.id}'; mod '{modId}' is not loaded.");
                return;
            }

            var nodes = mod.Definition.Tracks?.Nodes;
            FuseNode existing = null;
            nodes?.TryGetValue(trackNode.id, out existing);

            var updated = existing ?? new FuseNode();
            updated.Position = trackNode.transform.localPosition;
            updated.Rotation = trackNode.transform.rotation.eulerAngles;

            PersistFuseNode(mod, trackNode.id, updated, "node moved/rotated via FUSE editor");
        }

        private static bool PersistFuseNode(FuseLoadedMod mod, string nodeId, FuseNode node, string reason)
        {
            try
            {
                return FuseAuthoringPersistenceService.SaveDefinitionObject(
                    packageId: mod.Definition.Id,
                    kind: "node",
                    objectId: nodeId,
                    definition: node,
                    reason: reason);
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE node editor failed to persist node '{nodeId}' in mod '{mod.Definition.Id}'", ex);
                return false;
            }
        }

        private static TrackNode SpawnRuntimeTrackNode(string id, FuseNode node)
        {
            try
            {
                var go = new GameObject($"Node-{id}");
                var trackNode = go.AddComponent<TrackNode>();
                trackNode.transform.SetParent(global::Track.Graph.Shared.transform, worldPositionStays: false);
                trackNode.transform.localPosition = node.Position;
                trackNode.transform.rotation = Quaternion.Euler(node.Rotation);
                trackNode.id = id;

                global::Track.Graph.Shared.AddNode(trackNode);
                global::Track.Graph.Shared.RebuildCollections();
                return trackNode;
            }
            catch (Exception ex)
            {
                FuseLog.Exception($"FUSE node editor failed to spawn runtime TrackNode for '{id}'", ex);
                return null;
            }
        }

        private static void AttachMarker(TrackNode trackNode, string modId)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"FuseNodeMarker-{trackNode.id}";
            sphere.transform.SetParent(trackNode.transform, worldPositionStays: false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * 0.75f;

            var renderer = sphere.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = GetMarkerMaterial();
                // Disable shadow casting/receiving for editor markers
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var marker = sphere.AddComponent<FuseNodeMarker>();
            marker.Node = trackNode;
            marker.ModId = modId;
            ActiveMarkers.Add(marker);
        }

        private static Material GetMarkerMaterial()
        {
            if (_markerMaterial != null)
            {
                return _markerMaterial;
            }

            // Try URP shaders first (for Universal Render Pipeline)
            // Fall back to built-in shaders if URP is not available
            var shader = Shader.Find("Universal Render Pipeline/Unlit") 
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Standard");

            if (shader == null)
            {
                FuseLog.Warning("FUSE node editor: Could not find a suitable shader for node markers. Using default.");
                _markerMaterial = new Material(Shader.Find("Standard"));
            }
            else
            {
                _markerMaterial = new Material(shader);
            }

            // Set color (with alpha for transparency)
            _markerMaterial.color = new Color(1f, 0.4f, 0.2f, 0.85f);

            // Enable transparency for URP Unlit shader
            if (_markerMaterial.HasProperty("_Surface"))
            {
                _markerMaterial.SetFloat("_Surface", 1); // 1 = Transparent
            }
            if (_markerMaterial.HasProperty("_Blend"))
            {
                _markerMaterial.SetFloat("_Blend", 0); // 0 = Alpha blending
            }
            if (_markerMaterial.HasProperty("_AlphaClip"))
            {
                _markerMaterial.SetFloat("_AlphaClip", 0); // Disable alpha clipping
            }
            if (_markerMaterial.HasProperty("_SrcBlend"))
            {
                _markerMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (_markerMaterial.HasProperty("_DstBlend"))
            {
                _markerMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (_markerMaterial.HasProperty("_ZWrite"))
            {
                _markerMaterial.SetFloat("_ZWrite", 0); // Disable depth writing for transparency
            }

            // Set render queue for transparency
            _markerMaterial.renderQueue = 3000; // Transparent queue

            return _markerMaterial;
        }
    }
}
