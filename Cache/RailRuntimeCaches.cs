using System.Linq;
using Game.Progression;
using Helpers;
using Model;
using Model.Ops;
using Track;
using UI.Map;
using UnityEngine;

namespace RAIL.Cache
{
    public sealed class TrackNodeCache : RuntimeObjectCache<TrackNodeCache>
    {
        public override void Rebuild()
        {
            Clear();
            var graph = Graph.Shared;
            if (graph == null)
            {
                return;
            }

            foreach (var node in graph.Nodes)
            {
                Set(node.id, node);
            }
        }
    }

    public sealed class TrackSegmentCache : RuntimeObjectCache<TrackSegmentCache>
    {
        public override void Rebuild()
        {
            Clear();
            var graph = Graph.Shared;
            if (graph == null)
            {
                return;
            }

            foreach (var segment in graph.Segments)
            {
                Set(segment.id, segment);
            }
        }
    }

    public sealed class TrackSpanCache : RuntimeObjectCache<TrackSpanCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var span in Object.FindObjectsOfType<TrackSpan>(true).Where(span => span != null && !string.IsNullOrWhiteSpace(span.id)))
            {
                Set(span.id, span);
            }
        }
    }

    public sealed class AreaCache : RuntimeObjectCache<AreaCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var area in Object.FindObjectsOfType<Area>(true).Where(area => area != null && !string.IsNullOrWhiteSpace(area.identifier)))
            {
                Set(area.identifier, area);
            }
        }
    }

    public sealed class IndustryCache : RuntimeObjectCache<IndustryCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var industry in Object.FindObjectsOfType<Model.Ops.Industry>(true).Where(industry => industry != null && !string.IsNullOrWhiteSpace(industry.identifier)))
            {
                Set(industry.identifier, industry);
            }
        }
    }

    public sealed class IndustryComponentCache : RuntimeObjectCache<IndustryComponentCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var component in Object.FindObjectsOfType<Model.Ops.IndustryComponent>(true).Where(component => component != null && !string.IsNullOrWhiteSpace(component.Identifier)))
            {
                Set(component.Identifier, component);
            }
        }
    }
    public sealed class LoaderCache : RuntimeObjectCache<LoaderCache>
    {
        public override void Rebuild()
        {
            Clear();
            var root = GameObject.Find("World/Loaders") ?? GameObject.Find("Loaders");
            if (root == null)
            {
                return;
            }

            foreach (Transform child in root.transform)
            {
                if (child != null && !string.IsNullOrWhiteSpace(child.name))
                {
                    var component = child.GetComponent<Model.Ops.IndustryComponent>();
                    var key = component != null && !string.IsNullOrWhiteSpace(component.Identifier)
                        ? component.Identifier
                        : child.name;
                    Set(key, child.gameObject);
                }
            }
        }
    }

    public sealed class TurntableCache : RuntimeObjectCache<TurntableCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var turntable in Object.FindObjectsOfType<Turntable>(true).Where(turntable => turntable != null && !string.IsNullOrWhiteSpace(turntable.id)))
            {
                Set(turntable.id, turntable);
            }
        }
    }

    public sealed class LoadCache : RuntimeObjectCache<LoadCache>
    {
        public override void Rebuild()
        {
            Clear();
            var library = Model.CarPrototypeLibrary.instance;
            if (library?.opsLoads == null)
            {
                return;
            }

            foreach (var load in library.opsLoads.Where(load => load != null && !string.IsNullOrWhiteSpace(load.id)))
            {
                Set(load.id, load);
            }
        }
    }
    public sealed class ProgressionCache : RuntimeObjectCache<ProgressionCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var progression in Object.FindObjectsOfType<Progression>().Where(progression => progression != null && !string.IsNullOrWhiteSpace(progression.identifier)))
            {
                Set(progression.identifier, progression);
            }
        }
    }

    public sealed class SectionCache : RuntimeObjectCache<SectionCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var section in Object.FindObjectsOfType<Section>().Where(section => section != null && !string.IsNullOrWhiteSpace(section.identifier)))
            {
                Set(section.identifier, section);
            }
        }
    }

    public sealed class MapFeatureCache : RuntimeObjectCache<MapFeatureCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var feature in Object.FindObjectsOfType<MapFeature>().Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.identifier)))
            {
                Set(feature.identifier, feature);
            }
        }
    }

    public sealed class StationAgentCache : RuntimeObjectCache<StationAgentCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var agent in Object.FindObjectsOfType<StationAgent>().Where(agent => agent != null && !string.IsNullOrWhiteSpace(agent.name)))
            {
                Set(agent.name, agent);
            }
        }
    }

    public sealed class SceneryCache : RuntimeObjectCache<SceneryCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var scenery in Object.FindObjectsOfType<SceneryAssetInstance>(true).Where(scenery => scenery != null && !string.IsNullOrWhiteSpace(scenery.name)))
            {
                Set(scenery.name, scenery);
            }
        }
    }

    public sealed class SplineyCache : RuntimeObjectCache<SplineyCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var marker in Object.FindObjectsOfType<RAIL.API.RailSplineyMarker>(true).Where(marker => marker != null && !string.IsNullOrWhiteSpace(marker.Id)))
            {
                Set(marker.Id, marker.gameObject);
            }
        }
    }

    public sealed class MapLabelCache : RuntimeObjectCache<MapLabelCache>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var label in Object.FindObjectsOfType<MapLabel>().Where(label => label != null && !string.IsNullOrWhiteSpace(label.name)))
            {
                Set(label.name, label);
            }
        }
    }

    public abstract class RuntimeObjectCache<TCache> : BaseCache<TCache, object>
        where TCache : RuntimeObjectCache<TCache>
    {
        public override void Rebuild()
        {
            Clear();
        }
    }
}
