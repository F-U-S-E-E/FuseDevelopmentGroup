using System.Linq;
using Game.Progression;
using Helpers;
using Model;
using Model.Ops;
using Track;
using UI.Map;
using UnityEngine;

namespace FUSE.Runtime.Cache
{
    public sealed class FuseNodeRuntimeIndex : FuseRuntimeIndex<FuseNodeRuntimeIndex>
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

    public sealed class FuseSegmentRuntimeIndex : FuseRuntimeIndex<FuseSegmentRuntimeIndex>
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

    public sealed class FuseSpanRuntimeIndex : FuseRuntimeIndex<FuseSpanRuntimeIndex>
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

    public sealed class FuseAreaRuntimeIndex : FuseRuntimeIndex<FuseAreaRuntimeIndex>
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

    public sealed class FuseIndustryRuntimeIndex : FuseRuntimeIndex<FuseIndustryRuntimeIndex>
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

    public sealed class FuseIndustryComponentRuntimeIndex : FuseRuntimeIndex<FuseIndustryComponentRuntimeIndex>
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
    public sealed class FuseLoaderRuntimeIndex : FuseRuntimeIndex<FuseLoaderRuntimeIndex>
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

    public sealed class FuseTurntableRuntimeIndex : FuseRuntimeIndex<FuseTurntableRuntimeIndex>
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

    public sealed class FuseLoadRuntimeIndex : FuseRuntimeIndex<FuseLoadRuntimeIndex>
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
    public sealed class FuseProgressionRuntimeIndex : FuseRuntimeIndex<FuseProgressionRuntimeIndex>
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

    public sealed class FuseSectionRuntimeIndex : FuseRuntimeIndex<FuseSectionRuntimeIndex>
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

    public sealed class FuseMapFeatureRuntimeIndex : FuseRuntimeIndex<FuseMapFeatureRuntimeIndex>
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

    public sealed class FuseStationRuntimeIndex : FuseRuntimeIndex<FuseStationRuntimeIndex>
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

    public sealed class FuseSceneryRuntimeIndex : FuseRuntimeIndex<FuseSceneryRuntimeIndex>
    {
        public override void Rebuild()
        {
            Clear();

            // Index only FUSE-owned scenery via the marker MonoBehaviour
            // that SceneryAPI.AddScenery attaches. The previous design
            // walked every SceneryAssetInstance in the scene and keyed by
            // <c>scenery.name</c> with a case-insensitive dictionary; that
            // silently collapsed multiple vanilla scenery sharing a leaf
            // name (e.g. the four vanilla "Freight House" instances in
            // Bryson / Sylva / Dillsboro / Ela) into a single index slot,
            // then served whichever survived back to any mod that asked
            // for that id — turning a legacy "scenery: {\"freight house\":
            // {...}}" add-a-new-entity declaration into a silent
            // teleport+repaint of one of the vanilla originals (the bug
            // that left the Ela freight house missing). Vanilla scenery
            // is intentionally absent from this index now: the apply
            // path's <see cref="FUSE.Runtime.API.SceneryAPI.GetScenery"/> will
            // therefore return null for ids that FUSE has never claimed,
            // which lets the FuseSceneryEntity.ApplyToRuntime fall into
            // AddScenery and create a brand-new entity — matching the
            // legacy "scenery dict is an add list, not an update list"
            // contract that authoring mods are written against.
            foreach (var marker in Object.FindObjectsOfType<FUSE.Runtime.API.SceneryAPI.FuseSceneryMarker>(true)
                         .Where(marker => marker != null && !string.IsNullOrWhiteSpace(marker.Id)))
            {
                var scenery = marker.GetComponent<SceneryAssetInstance>();
                if (scenery != null)
                {
                    Set(marker.Id, scenery);
                }
            }
        }
    }

    public sealed class FuseSplineyRuntimeIndex : FuseRuntimeIndex<FuseSplineyRuntimeIndex>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var marker in Object.FindObjectsOfType<FUSE.Runtime.API.FuseSplineyMarker>(true).Where(marker => marker != null && !string.IsNullOrWhiteSpace(marker.Id)))
            {
                Set(marker.Id, marker.gameObject);
            }
        }
    }

    public sealed class FuseWaterSurfaceRuntimeIndex : FuseRuntimeIndex<FuseWaterSurfaceRuntimeIndex>
    {
        public override void Rebuild()
        {
            Clear();
            foreach (var marker in Object.FindObjectsOfType<FUSE.Runtime.API.FuseWaterSurfaceMarker>(true)
                         .Where(marker => marker != null && !string.IsNullOrWhiteSpace(marker.Id)))
            {
                Set(marker.Id, marker.gameObject);
            }
        }
    }

    public sealed class FuseMapLabelRuntimeIndex : FuseRuntimeIndex<FuseMapLabelRuntimeIndex>
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

    public abstract class FuseRuntimeIndex<TCache> : FuseRuntimeCacheBase<TCache, object>
        where TCache : FuseRuntimeIndex<TCache>
    {
        public override void Rebuild()
        {
            Clear();
        }
    }
}
