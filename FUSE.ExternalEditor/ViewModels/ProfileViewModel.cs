using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Logic;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Elevation/alignment profile along the track's connected path: station, track
/// elevation, grade and cut/fill (terrain via the loaded grid), plus arc-fit and
/// minimum-radius diagnostics. "Fit arc" snaps the path nodes onto the best-fit arc.
/// </summary>
public partial class ProfileViewModel : ViewModelBase
{
    private readonly TrackGraphViewModel _trackGraph;
    private readonly ViewportViewModel _viewport;
    private readonly UndoService _undo;
    private IReadOnlyList<string> _path = Array.Empty<string>();

    [ObservableProperty]
    private ObservableCollection<ProfilePoint> _points = new();

    [ObservableProperty]
    private string _summary = "No profile yet — open a mod and Refresh.";

    [ObservableProperty]
    private double _minRadius;

    [ObservableProperty]
    private double _arcRadius;

    [ObservableProperty]
    private double _arcRmsError;

    [ObservableProperty]
    private double _minRadiusThreshold = 75.0;

    public ProfileViewModel(TrackGraphViewModel trackGraph, ViewportViewModel viewport, UndoService undo)
    {
        _trackGraph = trackGraph;
        _viewport = viewport;
        _undo = undo;
        trackGraph.ProjectLoaded += Refresh;
    }

    [RelayCommand]
    public void Refresh()
    {
        var tracks = _trackGraph.Tracks;
        _path = DerivePath(tracks);

        var grid = _viewport.Grid;
        Func<double, double, double?>? sampler = grid.Count > 0
            ? (Func<double, double, double?>)((x, z) => TerrainHeightSampler.Sample(grid, x, z))
            : null;

        var points = TrackProfile.Build(tracks, _path, sampler);
        Points = new ObservableCollection<ProfilePoint>(points);

        var xz = PathXz(tracks);
        var arc = Alignment.FitArcToChain(xz);
        ArcRadius = arc?.Radius ?? 0.0;
        ArcRmsError = arc?.RmsError ?? 0.0;

        var radii = Alignment.LocalRadiusSamples(xz);
        MinRadius = radii.Count > 0 ? radii.Min(r => r.Radius) : 0.0;

        var maxGrade = points.Count > 0 ? points.Max(p => Math.Abs(p.GradePercent)) : 0.0;
        var length = points.Count > 0 ? points[^1].Station : 0.0;
        var warn = MinRadius > 0 && MinRadius < MinRadiusThreshold ? $"  ⚠ min radius {MinRadius:0.0} m < {MinRadiusThreshold:0} m" : string.Empty;
        Summary = $"{points.Count} pts · length {length:0.0} m · max grade {maxGrade:0.0}%{warn}";
    }

    [RelayCommand]
    public void FitArc()
    {
        var tracks = _trackGraph.Tracks;
        var ids = _path.Where(id => tracks.Nodes.ContainsKey(id)).ToList();
        var arc = Alignment.FitArcToChain(PathXz(tracks));
        if (arc is null || arc.Points.Count != ids.Count)
        {
            _trackGraph.ToolStatus = "Arc fit needs a clear single curve.";
            return;
        }

        var oldPos = ids.Select(id => tracks.Nodes[id].Position).ToList();
        var oldRot = ids.Select(id => tracks.Nodes[id].Rotation).ToList();
        var fitted = arc.Points;

        _undo.Execute(new UndoAction(
            "Fit arc to chain",
            () =>
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    TrackOps.MoveNode(tracks, ids[i], new FuseVector3((float)fitted[i].X, oldPos[i].y, (float)fitted[i].Z));
                    TrackOps.SetNodeRotation(tracks, ids[i], new FuseVector3(oldRot[i].x, (float)fitted[i].RotY, oldRot[i].z));
                }

                _trackGraph.Changed();
                Refresh();
            },
            () =>
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    TrackOps.MoveNode(tracks, ids[i], oldPos[i]);
                    TrackOps.SetNodeRotation(tracks, ids[i], oldRot[i]);
                }

                _trackGraph.Changed();
                Refresh();
            }));

        _trackGraph.ToolStatus = $"Fitted arc: R {arc.Radius:0.0} m, RMS {arc.RmsError:0.00} m.";
    }

    private List<(double X, double Z)> PathXz(FuseTrackDefinition tracks) =>
        _path.Where(id => tracks.Nodes.ContainsKey(id))
            .Select(id =>
            {
                var p = tracks.Nodes[id].Position;
                return ((double)p.x, (double)p.z);
            })
            .ToList();

    // The connected path to profile: between two dead-ends (valency 1) if present, else node order.
    private static IReadOnlyList<string> DerivePath(FuseTrackDefinition tracks)
    {
        if (tracks.Nodes.Count == 0)
        {
            return Array.Empty<string>();
        }

        var endpoints = tracks.Nodes.Keys.Where(id => TrackOps.NodeValency(tracks, id) == 1).ToList();
        if (endpoints.Count >= 2)
        {
            var path = Stationing.ShortestPath(tracks, endpoints[0], endpoints[1]);
            if (path is { Count: >= 2 })
            {
                return path;
            }
        }

        return tracks.Nodes.Keys.ToList();
    }
}
