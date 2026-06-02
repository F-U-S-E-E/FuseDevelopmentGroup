using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Rendering;
using Fuse.ExternalEditor.Tools;

namespace Fuse.ExternalEditor.Controls;

/// <summary>
/// 2D top-down map viewport: custom-drawn terrain tiles + track graph (bezier
/// segments and node markers) + world entities (splineys/scenery) under a pan/zoom
/// <see cref="ViewTransform"/>. Wheel = cursor zoom; drag = pan. Pointer input is
/// routed to the active <see cref="ITool"/> first (select/move/connect/place); the
/// tool may consume the gesture and draw a ghost <see cref="ToolPreview"/>.
/// </summary>
public sealed class MapViewport : Control
{
    public static readonly StyledProperty<TileGrid?> TileGridProperty =
        AvaloniaProperty.Register<MapViewport, TileGrid?>(nameof(TileGrid));

    public static readonly StyledProperty<TerrainMode> ModeProperty =
        AvaloniaProperty.Register<MapViewport, TerrainMode>(nameof(Mode));

    public static readonly StyledProperty<bool> HillshadeProperty =
        AvaloniaProperty.Register<MapViewport, bool>(nameof(Hillshade), defaultValue: true);

    public static readonly StyledProperty<FuseTrackDefinition?> TracksProperty =
        AvaloniaProperty.Register<MapViewport, FuseTrackDefinition?>(nameof(Tracks));

    public static readonly StyledProperty<FuseWorldDefinition?> WorldProperty =
        AvaloniaProperty.Register<MapViewport, FuseWorldDefinition?>(nameof(World));

    public static readonly StyledProperty<string?> SelectedNodeIdProperty =
        AvaloniaProperty.Register<MapViewport, string?>(nameof(SelectedNodeId), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ITool?> ActiveToolProperty =
        AvaloniaProperty.Register<MapViewport, ITool?>(nameof(ActiveTool));

    public static readonly StyledProperty<IToolContext?> ToolContextProperty =
        AvaloniaProperty.Register<MapViewport, IToolContext?>(nameof(ToolContext));

    public static readonly StyledProperty<ITerrainEditor?> TerrainEditorProperty =
        AvaloniaProperty.Register<MapViewport, ITerrainEditor?>(nameof(TerrainEditor));

    public static readonly StyledProperty<OsmOverlay?> OsmOverlayProperty =
        AvaloniaProperty.Register<MapViewport, OsmOverlay?>(nameof(OsmOverlay));

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(8, 11, 16));
    private static readonly IPen SegmentPen = new Pen(new SolidColorBrush(Color.FromRgb(90, 140, 190)), 2);
    private static readonly IPen NodeOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(12, 20, 30)), 1);
    private static readonly IBrush NodeBrush = new SolidColorBrush(Color.FromRgb(120, 180, 255));
    private static readonly IBrush SelectedNodeBrush = new SolidColorBrush(Color.FromRgb(255, 180, 60));
    private static readonly IPen SplineyPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 120, 80)), 2);
    private static readonly IBrush SceneryBrush = new SolidColorBrush(Color.FromRgb(120, 200, 120));
    private static readonly IPen PreviewPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 230, 120)), 2) { DashStyle = DashStyle.Dash };
    private static readonly IBrush PreviewMarkerBrush = new SolidColorBrush(Color.FromArgb(220, 255, 230, 120));
    private static readonly IPen BrushRingPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.5);

    private readonly ViewTransform _view = new();
    private readonly TileBitmapCache _cache = new();
    private bool _panning;
    private bool _pressed;
    private bool _toolGesture;
    private bool _brushing;
    private bool _movedDuringPress;
    private Point _last;
    private Point _pressOrigin;
    private Point _brushCursor;
    private bool _centered;
    private WriteableBitmap? _osmBitmap;
    private OsmOverlay? _osmSource;

    static MapViewport()
    {
        AffectsRender<MapViewport>(TileGridProperty, ModeProperty, HillshadeProperty, TracksProperty, WorldProperty, SelectedNodeIdProperty, ActiveToolProperty, TerrainEditorProperty, OsmOverlayProperty);
    }

    public TileGrid? TileGrid { get => GetValue(TileGridProperty); set => SetValue(TileGridProperty, value); }

    public TerrainMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    public bool Hillshade { get => GetValue(HillshadeProperty); set => SetValue(HillshadeProperty, value); }

    public FuseTrackDefinition? Tracks { get => GetValue(TracksProperty); set => SetValue(TracksProperty, value); }

    public FuseWorldDefinition? World { get => GetValue(WorldProperty); set => SetValue(WorldProperty, value); }

    public string? SelectedNodeId { get => GetValue(SelectedNodeIdProperty); set => SetValue(SelectedNodeIdProperty, value); }

    public ITool? ActiveTool { get => GetValue(ActiveToolProperty); set => SetValue(ActiveToolProperty, value); }

    public IToolContext? ToolContext { get => GetValue(ToolContextProperty); set => SetValue(ToolContextProperty, value); }

    public ITerrainEditor? TerrainEditor { get => GetValue(TerrainEditorProperty); set => SetValue(TerrainEditorProperty, value); }

    public OsmOverlay? OsmOverlay { get => GetValue(OsmOverlayProperty); set => SetValue(OsmOverlayProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TileGridProperty || change.Property == TracksProperty)
        {
            if (change.Property == TileGridProperty)
            {
                _cache.Clear();
            }

            _centered = false;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(Bounds.Size));

        var grid = TileGrid;
        var tracks = Tracks;
        var world = World;
        var hasTerrain = grid is not null && grid.Count > 0;
        var hasTracks = tracks is not null && tracks.Nodes.Count > 0;
        var hasWorld = world is not null && (world.Splineys.Count > 0 || world.Scenery.Count > 0);
        var preview = ActiveTool?.Preview;
        if (!hasTerrain && !hasTracks && !hasWorld && preview is null && OsmOverlay is null)
        {
            return;
        }

        if (hasTerrain)
        {
            _view.MinX = grid!.MinX;
            _view.MaxY = grid.MaxY;
        }

        if (!_centered && Bounds.Width > 0 && Bounds.Height > 0)
        {
            if (hasTerrain)
            {
                CenterOnGrid(grid!);
            }
            else if (hasTracks)
            {
                CenterOnTracks(tracks!);
            }

            _centered = true;
        }

        if (hasTerrain)
        {
            DrawTerrain(context, grid!);
        }

        DrawOsmOverlay(context);

        if (hasWorld)
        {
            DrawWorld(context, world!);
        }

        if (hasTracks)
        {
            DrawTracks(context, tracks!);
        }

        if (preview is not null)
        {
            DrawPreview(context, preview);
        }

        if (TerrainEditor is { Active: true } editor)
        {
            context.DrawEllipse(null, BrushRingPen, _brushCursor, editor.BrushScreenRadius, editor.BrushScreenRadius);
        }
    }

    private void DrawTerrain(DrawingContext context, TileGrid grid)
    {
        var ts = _view.TileScreenSize;
        foreach (var tile in grid.Tiles.Values)
        {
            var (sx, sy) = _view.TileTopLeft(tile.X, tile.Y);
            if (sx > Bounds.Width || sy > Bounds.Height || sx + ts < 0 || sy + ts < 0)
            {
                continue;
            }

            var bitmap = _cache.GetOrCreate(tile, Mode, Hillshade);
            context.DrawImage(bitmap, new Rect(0, 0, tile.Res, tile.Res), new Rect(sx, sy, ts, ts));
        }
    }

    private void DrawOsmOverlay(DrawingContext context)
    {
        var overlay = OsmOverlay;
        if (overlay is null || overlay.Width <= 0 || overlay.Height <= 0)
        {
            return;
        }

        if (!ReferenceEquals(_osmSource, overlay))
        {
            _osmBitmap?.Dispose();
            _osmBitmap = BuildOverlayBitmap(overlay);
            _osmSource = overlay;
        }

        var (tlx, tly) = _view.WorldToScreen(overlay.WorldMinX, overlay.WorldMaxZ);
        var (brx, bry) = _view.WorldToScreen(overlay.WorldMaxX, overlay.WorldMinZ);
        var dest = new Rect(new Point(tlx, tly), new Point(brx, bry));
        using (context.PushOpacity(0.55))
        {
            context.DrawImage(_osmBitmap!, new Rect(0, 0, overlay.Width, overlay.Height), dest);
        }
    }

    private static WriteableBitmap BuildOverlayBitmap(OsmOverlay overlay)
    {
        var bitmap = new WriteableBitmap(new PixelSize(overlay.Width, overlay.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bitmap.Lock();
        var rowLen = overlay.Width * 4;
        var row = new byte[rowLen];
        for (var y = 0; y < overlay.Height; y++)
        {
            var srcBase = y * rowLen;
            for (var x = 0; x < overlay.Width; x++)
            {
                var s = srcBase + (x * 4);
                var d = x * 4;
                row[d] = overlay.Rgba[s + 2];     // B
                row[d + 1] = overlay.Rgba[s + 1]; // G
                row[d + 2] = overlay.Rgba[s];     // R
                row[d + 3] = overlay.Rgba[s + 3]; // A
            }

            Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), rowLen);
        }

        return bitmap;
    }

    private void DrawTracks(DrawingContext context, FuseTrackDefinition tracks)
    {
        foreach (var seg in tracks.Segments.Values)
        {
            if (seg is null
                || !tracks.Nodes.TryGetValue(seg.StartNodeId ?? string.Empty, out var a) || a is null
                || !tracks.Nodes.TryGetValue(seg.EndNodeId ?? string.Empty, out var b) || b is null)
            {
                continue;
            }

            var poly = BezierMath.SegmentPolylineXz(a, b);
            if (poly.Count < 2)
            {
                continue;
            }

            var prev = _view.WorldToScreen(poly[0].X, poly[0].Z);
            for (var i = 1; i < poly.Count; i++)
            {
                var cur = _view.WorldToScreen(poly[i].X, poly[i].Z);
                context.DrawLine(SegmentPen, new Point(prev.X, prev.Y), new Point(cur.X, cur.Y));
                prev = cur;
            }
        }

        var r = Math.Clamp(_view.TileScreenSize * 0.06, 3.0, 9.0);
        foreach (var (id, node) in tracks.Nodes)
        {
            if (node is null)
            {
                continue;
            }

            var (px, py) = _view.WorldToScreen(node.Position.x, node.Position.z);
            if (px < -20 || py < -20 || px > Bounds.Width + 20 || py > Bounds.Height + 20)
            {
                continue;
            }

            var selected = id == SelectedNodeId;
            var radius = selected ? r + 2 : r;
            context.DrawEllipse(selected ? SelectedNodeBrush : NodeBrush, NodeOutlinePen, new Point(px, py), radius, radius);
        }
    }

    private void DrawWorld(DrawingContext context, FuseWorldDefinition world)
    {
        foreach (var spliney in world.Splineys.Values)
        {
            var pts = spliney?.Points;
            if (pts is null || pts.Length < 2)
            {
                continue;
            }

            var prev = _view.WorldToScreen(pts[0].Position.x, pts[0].Position.z);
            for (var i = 1; i < pts.Length; i++)
            {
                var cur = _view.WorldToScreen(pts[i].Position.x, pts[i].Position.z);
                context.DrawLine(SplineyPen, new Point(prev.X, prev.Y), new Point(cur.X, cur.Y));
                prev = cur;
            }
        }

        foreach (var scenery in world.Scenery.Values)
        {
            if (scenery is null)
            {
                continue;
            }

            var (sx, sy) = _view.WorldToScreen(scenery.Position.x, scenery.Position.z);
            if (sx < -20 || sy < -20 || sx > Bounds.Width + 20 || sy > Bounds.Height + 20)
            {
                continue;
            }

            DrawDiamond(context, SceneryBrush, sx, sy, 5);
        }
    }

    private void DrawPreview(DrawingContext context, ToolPreview preview)
    {
        foreach (var (a, b) in preview.Lines)
        {
            var pa = _view.WorldToScreen(a.X, a.Z);
            var pb = _view.WorldToScreen(b.X, b.Z);
            context.DrawLine(PreviewPen, new Point(pa.X, pa.Y), new Point(pb.X, pb.Y));
        }

        foreach (var (x, z) in preview.Markers)
        {
            var (px, py) = _view.WorldToScreen(x, z);
            context.DrawEllipse(PreviewMarkerBrush, null, new Point(px, py), 4, 4);
        }
    }

    private static void DrawDiamond(DrawingContext context, IBrush brush, double cx, double cy, double r)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - r), true);
            ctx.LineTo(new Point(cx + r, cy));
            ctx.LineTo(new Point(cx, cy + r));
            ctx.LineTo(new Point(cx - r, cy));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private void CenterOnGrid(TileGrid grid)
    {
        double cols = grid.MaxX - grid.MinX + 1;
        double rows = grid.MaxY - grid.MinY + 1;
        var worldW = cols * TerrainConstants.TileScreenBase;
        var worldH = rows * TerrainConstants.TileScreenBase;
        var zoom = Math.Min(Bounds.Width / worldW, Bounds.Height / worldH);
        _view.Zoom = Math.Clamp(zoom, ViewTransform.MinZoom, ViewTransform.MaxZoom);

        var ts = _view.TileScreenSize;
        _view.PanX = (Bounds.Width - (cols * ts)) / 2.0;
        _view.PanY = (Bounds.Height - (rows * ts)) / 2.0;
    }

    private void CenterOnTracks(FuseTrackDefinition tracks)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var node in tracks.Nodes.Values)
        {
            if (node is null)
            {
                continue;
            }

            var p = node.Position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        if (minX > maxX)
        {
            return;
        }

        _view.MinX = 0;
        _view.MaxY = 0;
        var cx = (minX + maxX) / 2.0;
        var cz = (minZ + maxZ) / 2.0;
        const double pad = 40.0;
        var spanX = Math.Max(maxX - minX, 1.0) / TerrainConstants.UnityTileMeters;
        var spanZ = Math.Max(maxZ - minZ, 1.0) / TerrainConstants.UnityTileMeters;
        var zoomX = (Bounds.Width - (2 * pad)) / Math.Max(spanX * TerrainConstants.TileScreenBase, 1e-6);
        var zoomY = (Bounds.Height - (2 * pad)) / Math.Max(spanZ * TerrainConstants.TileScreenBase, 1e-6);
        _view.Zoom = Math.Clamp(Math.Min(zoomX, zoomY), ViewTransform.MinZoom, ViewTransform.MaxZoom);

        var ts = _view.TileScreenSize;
        _view.PanX = (Bounds.Width / 2.0) - (cx / TerrainConstants.UnityTileMeters * ts);
        _view.PanY = (Bounds.Height / 2.0) - ((1.0 - (cz / TerrainConstants.UnityTileMeters)) * ts);
    }

    // Stamp the brush onto every tile under the cursor's brush footprint, converting
    // the screen-pixel radius to each tile's pixel space (matches _paint_at).
    private void DabAt(ITerrainEditor editor, Point pos)
    {
        var grid = TileGrid;
        var ts = _view.TileScreenSize;
        if (grid is null || grid.Count == 0 || ts <= 0)
        {
            return;
        }

        var r = editor.BrushScreenRadius;
        Span<(double X, double Y)> corners = stackalloc (double, double)[]
        {
            (pos.X - r, pos.Y - r), (pos.X + r, pos.Y - r),
            (pos.X - r, pos.Y + r), (pos.X + r, pos.Y + r),
            (pos.X, pos.Y),
        };

        var seen = new HashSet<(int, int)>();
        foreach (var (cx, cy) in corners)
        {
            var (tx, ty) = _view.ScreenToTile(cx, cy);
            if (!seen.Add((tx, ty)) || !grid.TryGet(tx, ty, out var tile))
            {
                continue;
            }

            var res = tile.Res;
            var screenPxPerTilePx = ts / res;
            var radiusTilePx = Math.Min(Math.Max(1, (int)Math.Round(r / screenPxPerTilePx)), res / 2);
            var (tileX, tileY) = _view.TileTopLeft(tile.X, tile.Y);
            var centreCol = (pos.X - tileX) / ts * res;
            var centreRow = (pos.Y - tileY) / ts * res;
            editor.Dab(tile, centreRow, centreCol, radiusTilePx);
            _cache.Invalidate(tile);
        }
    }

    private ToolPointer MakePointer(Point pos)
    {
        var (wx, wz) = _view.ScreenToWorld(pos.X, pos.Y);
        var under = Tracks is { } tracks ? TrackHitTest.NearestNode(_view, tracks, pos.X, pos.Y, 9.0) : null;
        return new ToolPointer(wx, wz, pos.X, pos.Y, under);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        _view.ZoomAt(p.X, p.Y, e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1);
        _centered = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);
        _brushCursor = pos;
        if (TerrainEditor is { Active: true } brush)
        {
            _brushing = true;
            brush.BeginStroke();
            DabAt(brush, pos);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        _last = pos;
        _pressOrigin = pos;
        _movedDuringPress = false;
        _pressed = true;
        e.Pointer.Capture(this);

        _toolGesture = ActiveTool is { } tool && ToolContext is { } ctx && tool.PointerPressed(ctx, MakePointer(pos));
        _panning = !_toolGesture; // pan only if the tool didn't claim the gesture
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _brushCursor = pos;
        if (TerrainEditor is { Active: true } brush)
        {
            if (_brushing)
            {
                DabAt(brush, pos);
            }

            InvalidateVisual(); // HUD ring follows the cursor (and shows dabs)
            return;
        }

        if (_pressed && (Math.Abs(pos.X - _pressOrigin.X) > 3 || Math.Abs(pos.Y - _pressOrigin.Y) > 3))
        {
            _movedDuringPress = true;
        }

        var hasPreview = false;
        if (ActiveTool is { } tool && ToolContext is { } ctx)
        {
            tool.PointerMoved(ctx, MakePointer(pos), _pressed);
            hasPreview = tool.Preview is not null;
        }

        if (_panning)
        {
            _view.PanBy(pos.X - _last.X, pos.Y - _last.Y);
            _last = pos;
            _centered = true;
        }

        if (_panning || _toolGesture || hasPreview)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);
        if (_brushing)
        {
            if (TerrainEditor is { Active: true } brush)
            {
                DabAt(brush, pos);
                brush.EndStroke();
            }

            _brushing = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            return;
        }

        var wasDrag = _movedDuringPress;

        var consumed = ActiveTool is { } tool && ToolContext is { } ctx && tool.PointerReleased(ctx, MakePointer(pos), wasDrag);

        _panning = false;
        _pressed = false;
        _toolGesture = false;
        e.Pointer.Capture(null);

        // Fallback for isolation tests with no tool wired: a click selects the nearest node.
        if (!consumed && !wasDrag && ActiveTool is null && Tracks is { } tracks)
        {
            SelectedNodeId = TrackHitTest.NearestNode(_view, tracks, pos.X, pos.Y, 9.0);
        }

        InvalidateVisual();
    }
}
