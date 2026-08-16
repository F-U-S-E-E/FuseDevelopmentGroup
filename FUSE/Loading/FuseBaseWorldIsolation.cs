using System;
using System.Collections.Generic;
using System.Linq;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using FUSE.Infrastructure;
using FUSE.Runtime.API;
using UnityEngine;

namespace FUSE.Loading
{
    /// <summary>
    /// Turns the stock Bushnell/Whittier scene into a runtime host for a
    /// complete FUSE map. Railroader still needs the scene's manager objects,
    /// but its authored track, operations, labels, scenery, signs, setups,
    /// progression, and CTC content must not leak into the replacement world.
    /// </summary>
    internal static class FuseBaseWorldIsolation
    {
        private static readonly HashSet<string> PreservedWorldChildren =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Track",
                "Prefab Instancer",
                "Light Probe Group",
                "Environment Effects",
                "SceneryEditorSentinel",
            };

        private static readonly string[] ContentRoots =
        {
            "Ops",
            "MapFeatures",
            "Progressions",
            "Setups",
            "CTC",
            "CTC Auxiliary",
        };

        private static string _isolatedMapId = string.Empty;

        internal static bool ShouldSuppress(FuseRegisteredMap map)
        {
            return map != null && map.IsValid && map.SuppressBaseWorld;
        }

        internal static bool ApplyForActiveMap(string reason)
        {
            var mapId = FuseMapSession.ActiveMapId;
            if (string.IsNullOrWhiteSpace(mapId) ||
                !FuseMapPackageRegistry.TryGetMap(mapId, out var map) ||
                !ShouldSuppress(map))
            {
                return false;
            }

            if (string.Equals(_isolatedMapId, map.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var deactivatedRoots = DeactivateStockSceneContent();
            var removedTracks = RemoveStockTrackGraph();
            NotifyIndustriesChanged();
            _isolatedMapId = map.MapId;

            FuseLog.Info(
                $"FUSE isolated base world for map='{map.MapId}' reason='{reason ?? "unspecified"}' " +
                $"deactivatedContentRoots={deactivatedRoots} removedSpans={removedTracks.Spans} " +
                $"removedSegments={removedTracks.Segments} removedNodes={removedTracks.Nodes}.");
            return true;
        }

        internal static void Reset()
        {
            _isolatedMapId = string.Empty;
        }

        private static int DeactivateStockSceneContent()
        {
            var deactivated = 0;
            var world = GameObject.Find("World");
            if (world != null)
            {
                deactivated += DeactivateChildren(world.transform, PreservedWorldChildren);
            }

            foreach (var rootName in ContentRoots)
            {
                var root = GameObject.Find(rootName);
                if (root != null)
                {
                    deactivated += DeactivateChildren(
                        root.transform,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
            }

            return deactivated;
        }

        private static int DeactivateChildren(
            Transform root,
            HashSet<string> preservedNames)
        {
            if (root == null)
            {
                return 0;
            }

            var children = new List<GameObject>();
            for (var index = 0; index < root.childCount; index++)
            {
                var child = root.GetChild(index);
                if (child != null && child.gameObject != null)
                {
                    children.Add(child.gameObject);
                }
            }

            var deactivated = 0;
            foreach (var child in children)
            {
                if (child == null ||
                    preservedNames.Contains(child.name) ||
                    !child.activeSelf)
                {
                    continue;
                }

                child.SetActive(false);
                deactivated++;
            }

            return deactivated;
        }

        private static RemovedTrackCounts RemoveStockTrackGraph()
        {
            var counts = new RemovedTrackCounts();
            TrackAPI.BeginBatch();
            try
            {
                foreach (var spanId in TrackAPI.GetAllSpans()
                             .Where(span => span != null && !string.IsNullOrWhiteSpace(span.id))
                             .Select(span => span.id)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToArray())
                {
                    if (TryRemove("span", spanId, TrackAPI.RemoveSpan))
                    {
                        counts.Spans++;
                    }
                }

                foreach (var segmentId in TrackAPI.GetAllSegments()
                             .Where(segment => segment != null && !string.IsNullOrWhiteSpace(segment.id))
                             .Select(segment => segment.id)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToArray())
                {
                    if (TryRemove("segment", segmentId, TrackAPI.RemoveSegment))
                    {
                        counts.Segments++;
                    }
                }

                foreach (var nodeId in TrackAPI.GetAllNodes()
                             .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
                             .Select(node => node.id)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToArray())
                {
                    if (TryRemove("node", nodeId, TrackAPI.RemoveNode))
                    {
                        counts.Nodes++;
                    }
                }
            }
            finally
            {
                // The normal map-load apply/cleanup pipeline owns the single
                // graph rebuild after the replacement package has been added.
                TrackAPI.EndBatch(false);
            }

            return counts;
        }

        private static bool TryRemove(string kind, string id, Action<string> remove)
        {
            try
            {
                remove(id);
                return true;
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    $"FUSE base-world isolation could not remove stock track {kind} " +
                    $"id='{id}': {ex.Message}");
                return false;
            }
        }

        private static void NotifyIndustriesChanged()
        {
            try
            {
                Messenger.Default.Send(default(IndustriesDidChange));
            }
            catch (Exception ex)
            {
                FuseLog.Warning(
                    "FUSE base-world isolation could not refresh operations collections: " +
                    ex.Message);
            }
        }

        private sealed class RemovedTrackCounts
        {
            internal int Spans { get; set; }
            internal int Segments { get; set; }
            internal int Nodes { get; set; }
        }
    }
}
