using FUSE.API;
using System.Collections.Generic;
using System.Linq;
using UI.Builder;
using UnityEngine;
using static FUSE.Interface.InterfaceUtils;

namespace FUSE.Interface.MenuWindow
{
    internal struct ToolsPanelBuilder
    {
        private enum PageId
        {
            Inspector,
            DependencyGraph,
            Assets,
            Audits,
            Stats
        }

        private class Page
        {
            public PageId Id { get; }

            public Page(PageId id)
            {
                Id = id;
            }
        }

        public static void Build(UIPanelBuilder builder, UIState<string> selectedItem)
        {
            if (selectedItem.Value == null)
            {
                selectedItem.Value = "inspector";
            }

            List<UIPanelBuilder.ListItem<Page>> list = [];
            list.Add(new UIPanelBuilder.ListItem<Page>("inspector", new Page(PageId.Inspector), "Tools", "Object Inspector"));
            list.Add(new UIPanelBuilder.ListItem<Page>("dependencyGraph", new Page(PageId.DependencyGraph), "Tools", "Dependency Graph"));
            list.Add(new UIPanelBuilder.ListItem<Page>("assets", new Page(PageId.Assets), "Tools", "Assets Report"));
            list.Add(new UIPanelBuilder.ListItem<Page>("audits", new Page(PageId.Audits), "Tools", "Diagnostics Report"));
            list.Add(new UIPanelBuilder.ListItem<Page>("stats", new Page(PageId.Stats), "Tools", "World Stats"));

            builder.AddListDetail(list, selectedItem, delegate (UIPanelBuilder builder, Page page)
            {
                if (page == null)
                {
                    builder.AddExpandingVerticalSpacer();
                    builder.AddLabelEmptyState("Select a page");
                    builder.AddExpandingVerticalSpacer();
                }
                else
                {
                    builder.VScrollView(delegate (UIPanelBuilder builder)
                    {
                        switch (page.Id)
                        {
                            case PageId.Inspector:
                                InspectorToolPage.Build(builder);
                                break;
                            case PageId.DependencyGraph:
                                DependencyGraphPage.Build(builder);
                                break;
                            case PageId.Assets:
                                AssetsToolPage.Build(builder);
                                break;
                            case PageId.Audits:
                                AuditsToolPage.Build(builder);
                                break;
                            case PageId.Stats:
                                BuildStatsPage(builder);
                                break;
                            default:
                                builder.AddLabel("Unknown page.");
                                break;
                        }
                    }, new RectOffset(0, 4, 0, 0));
                }
            });
        }

        private static void BuildStatsPage(UIPanelBuilder builder)
        {
            builder.AddTitle("World Stats", "");
            builder.AddSection("Runtime Objects");

            builder.AddField("Track Nodes", SafeCount(() => TrackAPI.GetAllNodes().Count()).ToString());
            builder.AddField("Track Segments", SafeCount(() => TrackAPI.GetAllSegments().Count()).ToString());
            builder.AddField("Track Spans", SafeCount(() => TrackAPI.GetAllSpans().Count()).ToString());
            builder.AddField("Areas", SafeCount(() => TrackAPI.GetAllAreas().Count()).ToString());
            builder.AddField("Loads", SafeCount(() => LoadAPI.GetAllLoads().Count()).ToString());
            builder.AddField("Industries", SafeCount(() => IndustryAPI.GetAllIndustries().Count()).ToString());
            builder.AddField("Loaders", SafeCount(() => LoaderAPI.GetAllLoaders().Count()).ToString());
            builder.AddField("Stations", SafeCount(() => StationAPI.GetAllStationAgents().Count()).ToString());
            builder.AddField("Passenger Stops", SafeCount(() => StationAPI.GetAllPassengerStops().Count()).ToString());
            builder.AddField("Turntables", SafeCount(() => TurntableAPI.GetAllTurntables().Count()).ToString());
            builder.AddField("Scenery", SafeCount(() => SceneryAPI.GetAllScenery().Count()).ToString());
            builder.AddField("Scene Clones", SafeCount(() => SceneCloneAPI.GetAllSceneClones().Count()).ToString());
            builder.AddField("Splineys", SafeCount(() => SplineyAPI.GetAllSplineys().Count()).ToString());
            builder.AddField("Map Labels", SafeCount(() => MapAPI.GetAllMapLabels().Count()).ToString());
            builder.AddField("Map Masks", SafeCount(() => MapAPI.GetAllMapMasks().Count()).ToString());
            builder.AddField("Progressions", SafeCount(() => ProgressionAPI.GetAllProgressions().Count()).ToString());
            builder.AddField("Map Features", SafeCount(() => ProgressionAPI.GetAllMapFeatures().Count()).ToString());
            builder.Spacer(6f);

            builder.AddSection("Registry");
            builder.AddField("Exclusive Claims", FUSE.Registry.FuseRegistry.ExclusiveClaimCount.ToString());
            builder.AddField("Shared Claims", FUSE.Registry.FuseRegistry.SharedClaimCount.ToString());
            builder.AddField("Conflicts", FUSE.Registry.FuseRegistry.Conflicts.Count.ToString());
        }
    }
}
