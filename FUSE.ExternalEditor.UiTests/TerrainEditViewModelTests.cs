using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Fuse.Core.Authoring;
using Fuse.ExternalEditor.Controls;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Models.Terrain;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

public class TerrainEditViewModelTests
{
    [Fact]
    public void Stroke_Edits_Tile_And_Pushes_One_Undo_Step()
    {
        var svc = new TerrainTileService();
        var undo = new UndoService();
        var vm = new TerrainEditViewModel(svc, new ViewportViewModel(svc), undo)
        {
            BrushKind = TerrainBrushKind.Raise,
            BrushStrength = 0.1f,
        };
        var tile = TerrainBrushTests.Flat(64, 20000);

        vm.BeginStroke();
        vm.Dab(tile, 32, 32, 6);
        vm.Dab(tile, 33, 33, 6); // same stroke, overlapping dab
        vm.EndStroke();

        Assert.True(undo.CanUndo);
        Assert.Equal(1, undo.UndoDepth); // the whole stroke is one undo step
        var raised = tile.Height16((32 * 64) + 32);
        Assert.True(raised > 20000);

        undo.Undo();
        Assert.Equal(20000, tile.Height16((32 * 64) + 32)); // exact restore
        undo.Redo();
        Assert.Equal(raised, tile.Height16((32 * 64) + 32));
    }

    [Fact]
    public void SetBrush_And_SetEditMode_Parse_Enums()
    {
        var svc = new TerrainTileService();
        var vm = new TerrainEditViewModel(svc, new ViewportViewModel(svc), new UndoService());

        vm.SetBrushCommand.Execute("ErodeThermal");
        Assert.Equal(TerrainBrushKind.ErodeThermal, vm.BrushKind);

        vm.SetEditModeCommand.Execute("Water");
        Assert.Equal(TerrainEditMode.Water, vm.EditMode);
    }

    [Fact]
    public void SaveTerrain_Persists_Edited_Tiles()
    {
        var svc = new TerrainTileService();
        var viewport = new ViewportViewModel(svc);
        var vm = new TerrainEditViewModel(svc, viewport, new UndoService()) { BrushStrength = 0.1f };

        var dir = Path.Combine(Path.GetTempPath(), "fuse-terrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tile_0_0.data");
            svc.SaveTile(TerrainBrushTests.Flat(16, 10000), path);
            viewport.LoadFolder(dir);
            Assert.Equal(1, viewport.Grid.Count);
            Assert.True(viewport.Grid.TryGet(0, 0, out var tile));

            vm.BeginStroke();
            vm.Dab(tile, 8, 8, 4);
            vm.EndStroke();
            Assert.True(tile.Dirty);

            vm.SaveTerrainCommand.Execute(null);
            Assert.False(tile.Dirty);

            var reloaded = svc.LoadTile(path);
            Assert.NotNull(reloaded);
            Assert.Equal(tile.Height16((8 * 16) + 8), reloaded!.Height16((8 * 16) + 8));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void MapViewport_Renders_Brush_Ring_When_Editing()
    {
        var svc = new TerrainTileService();
        var editor = new TerrainEditViewModel(svc, new ViewportViewModel(svc), new UndoService()) { EditingEnabled = true };
        var grid = new TileGrid();
        grid.Add(TerrainBrushTests.Flat(64, 20000)); // tile (0,0)

        var viewport = new MapViewport { TileGrid = grid, TerrainEditor = editor };
        var window = new Window { Width = 400, Height = 300, Content = viewport };
        window.Show();

        Assert.True(viewport.Bounds.Width > 0); // drew terrain + brush ring without throwing
    }
}
