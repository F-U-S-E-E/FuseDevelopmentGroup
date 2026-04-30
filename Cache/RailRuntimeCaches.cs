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
    public sealed class RailNodeRuntimeIndex : RailRuntimeIndex<RailNodeRuntimeIndex>
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

    public sealed class RailSegmentRuntimeIndex : RailRuntimeIndex<RailSegmentRuntimeIndex>
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

    public sealed class RailSpanRuntimeIndex : RailRuntimeIndex<RailSpanRuntimeIndex>
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

    public sealed class RailAreaRuntimeIndex : RailRuntimeIndex<RailAreaRuntimeIndex>
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

    public sealed class RailIndustryRuntimeIndex : RailRuntimeIndex<RailIndustryRuntimeIndex>
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

    public sealed class RailIndustryComponentRuntimeIndex : RailRuntimeIndex<RailIndustryComponentRuntimeIndex>
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
    public sealed class RailLoaderRuntimeIndex : RailRuntimeIndex<RailLoaderRuntimeIndex>
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

    public sealed class RailTurntableRuntimeIndex : RailRuntimeIndex<RailTurntableRuntimeIndex>
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

    public sealed class RailLoadRuntimeIndex : RailRuntimeIndex<RailLoadRuntimeIndex>
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
    public sealed class RailProgressionRuntimeIndex : RailRuntimeIndex<RailProgressionRuntimeIndex>
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

    public sealed class RailSectionRuntimeIndex : RailRuntimeIndex<RailSectionRuntimeIndex>
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

    public sealed class RailMapFeatureRuntimeIndex : RailRuntimeIndex<RailMapFeatureRuntimeIndex>
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

    public sealed class RailStationRuntimeIndex : RailRuntimeIndex<RailStationRuntimeIndex>
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

    public sealed class RailSceneryRuntimeIndex : RailRuntimeIndex<RailSceneryRuntimeIndex>
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

    public sealed class RailSplineyRuntimeIndex : RailRuntimeIndex<RailSplineyRuntimeIndex>
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

    public sealed class RailMapLabelRuntimeIndex : RailRuntimeIndex<RailMapLabelRuntimeIndex>
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

    public abstract class RailRuntimeIndex<TCache> : RailRuntimeCacheBase<TCache, object>
        where TCache : RailRuntimeIndex<TCache>
    {
        public override void Rebuild()
        {
            Clear();
        }
    }
}
