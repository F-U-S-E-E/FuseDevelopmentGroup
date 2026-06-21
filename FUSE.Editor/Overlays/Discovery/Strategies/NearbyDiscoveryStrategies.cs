using FUSE.Infrastructure;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FUSE.Editor.Overlays.Discovery.Strategies
{
    /// <summary>
    /// Discovery strategy for finding nearby GameObjects using physics overlap.
    /// Useful for selecting/editing nearby props, structures, or other game objects.
    /// </summary>
    public class NearbyGameObjectDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        private readonly Func<Vector3> _getFocalPoint;
        private readonly LayerMask _layerMask;
        private readonly float _searchRadius;

        public string StrategyName => "NearbyGameObjects";
        public int ExecutionOrder => 10;

        /// <summary>
        /// Creates a new discovery strategy for nearby game objects.
        /// </summary>
        /// <param name="getFocalPoint">Delegate to get the focal point (e.g., camera position)</param>
        /// <param name="searchRadius">Search radius in world units</param>
        /// <param name="layerMask">Which layers to search. Use -1 for all layers.</param>
        public NearbyGameObjectDiscoveryStrategy(
            Func<Vector3> getFocalPoint,
            float searchRadius = 500f,
            LayerMask layerMask = default)
        {
            _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
            _searchRadius = searchRadius;
            _layerMask = layerMask == default ? ~0 : layerMask;
        }

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = _getFocalPoint();
            var colliders = Physics.OverlapSphere(focalPoint, _searchRadius, _layerMask);

            foreach (var collider in colliders)
            {
                var go = collider.gameObject;
                var distance = Vector3.Distance(go.transform.position, focalPoint);

                yield return new DiscoveredOverlayObject
                {
                    Entity = go,
                    HasPendingEdits = false,
                    ObjectId = GetGameObjectId(go),
                    Priority = 1.0f - (distance / _searchRadius), // Higher priority = closer
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        private string GetGameObjectId(GameObject go)
        {
            return $"GO_{go.GetInstanceID()}";
        }

        public void OnDisable() { }
        public void OnEnable() { }
    }

    /// <summary>
    /// Discovery strategy for finding nearby components of a specific type.
    /// Generic and reusable for any Component subclass.
    /// </summary>
    public class NearbyComponentDiscoveryStrategy<T> : IOverlayDiscoveryStrategy where T : Component
    {
        private readonly Func<Vector3> _getFocalPoint;
        private readonly LayerMask _layerMask;
        private readonly float _searchRadius;

        public string StrategyName => $"NearbyComponents_{typeof(T).Name}";
        public int ExecutionOrder => 11;

        public NearbyComponentDiscoveryStrategy(
            Func<Vector3> getFocalPoint,
            float searchRadius = 500f,
            LayerMask layerMask = default)
        {
            _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
            _searchRadius = searchRadius;
            _layerMask = layerMask == default ? ~0 : layerMask;
        }

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = _getFocalPoint();
            var colliders = Physics.OverlapSphere(focalPoint, _searchRadius, _layerMask);

            foreach (var collider in colliders)
            {
                var component = collider.GetComponent<T>();
                if (component == null) continue;

                var distance = Vector3.Distance(component.gameObject.transform.position, focalPoint);

                yield return new DiscoveredOverlayObject
                {
                    Entity = component,
                    HasPendingEdits = false,
                    ObjectId = GetComponentId(component),
                    Priority = 1.0f - (distance / _searchRadius),
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        private string GetComponentId(T component)
        {
            return $"COMP_{typeof(T).Name}_{component.gameObject.GetInstanceID()}_{component.GetInstanceID()}";
        }

        public void OnDisable() { }
        public void OnEnable() { }
    }

    /// <summary>
    /// Discovery strategy for finding specific objects by tag.
    /// Useful for editor-specific tags like "EditorSelectable".
    /// </summary>
    public class TagBasedDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        private readonly string _tag;
        private readonly Func<Vector3> _getFocalPoint;
        private readonly float _maxDistance;

        public string StrategyName => $"TagBased_{_tag}";
        public int ExecutionOrder => 12;

        public TagBasedDiscoveryStrategy(
            string tag,
            Func<Vector3> getFocalPoint,
            float maxDistance = 500f)
        {
            _tag = tag;
            _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
            _maxDistance = maxDistance;
        }

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = _getFocalPoint();
            var taggedObjects = GameObject.FindGameObjectsWithTag(_tag);

            foreach (var go in taggedObjects)
            {
                var distance = Vector3.Distance(go.transform.position, focalPoint);
                if (distance > _maxDistance) continue;

                yield return new DiscoveredOverlayObject
                {
                    Entity = go,
                    HasPendingEdits = false,
                    ObjectId = GetGameObjectId(go),
                    Priority = 1.0f - (distance / _maxDistance),
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        private string GetGameObjectId(GameObject go)
        {
            return $"TAG_{_tag}_{go.GetInstanceID()}";
        }

        public void OnDisable() { }
        public void OnEnable() { }
    }

    /// <summary>
    /// Discovery strategy for finding objects in specific layers.
    /// Combines layer-based filtering with distance culling.
    /// </summary>
    public class LayerBasedDiscoveryStrategy : IOverlayDiscoveryStrategy
    {
        private readonly LayerMask _layerMask;
        private readonly Func<Vector3> _getFocalPoint;
        private readonly float _searchRadius;
        private readonly string _strategyName;

        public string StrategyName => _strategyName;
        public int ExecutionOrder => 13;

        public LayerBasedDiscoveryStrategy(
            LayerMask layerMask,
            Func<Vector3> getFocalPoint,
            float searchRadius = 500f,
            string customName = null)
        {
            _layerMask = layerMask;
            _getFocalPoint = getFocalPoint ?? (() => Vector3.zero);
            _searchRadius = searchRadius;

            string layerName = "Unknown";
            for (int i = 0; i < 32; i++)
            {
                if (((1 << i) & layerMask) != 0)
                {
                    layerName = LayerMask.LayerToName(i);
                    break;
                }
            }

            _strategyName = customName ?? $"LayerBased_{layerName}";
        }

        public IEnumerable<DiscoveredOverlayObject> DiscoverObjects()
        {
            var focalPoint = _getFocalPoint();
            var colliders = Physics.OverlapSphere(focalPoint, _searchRadius, _layerMask);

            foreach (var collider in colliders)
            {
                var go = collider.gameObject;
                var distance = Vector3.Distance(go.transform.position, focalPoint);

                yield return new DiscoveredOverlayObject
                {
                    Entity = go,
                    HasPendingEdits = false,
                    ObjectId = GetGameObjectId(go),
                    Priority = 1.0f - (distance / _searchRadius),
                    Distance = distance,
                    SourceStrategy = StrategyName
                };
            }
        }

        private string GetGameObjectId(GameObject go)
        {
            return $"LAYER_{go.layer}_{go.GetInstanceID()}";
        }

        public void OnDisable() { }
        public void OnEnable() { }
    }
}
