using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fuse.Core.Authoring;
using Fuse.Core.Model;
using Fuse.ExternalEditor.Logic;
using Fuse.ExternalEditor.Services;
using Fuse.ExternalEditor.ViewModels;
using Xunit;

namespace Fuse.ExternalEditor.UiTests;

/// <summary>Phase 8b: calculator/expression evaluator, the dialog-driven New Mod flow, NewProject reset.</summary>
public class Dialogs8bTests
{
    private sealed class FakeDialog : IDialogService
    {
        private readonly Queue<string?> _inputs;

        public FakeDialog(params string?[] inputs) => _inputs = new Queue<string?>(inputs);

        public Task<string?> PromptInputAsync(string title, string prompt, string initial = "")
            => Task.FromResult(_inputs.Count > 0 ? _inputs.Dequeue() : null);

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }

    private static MainWindowViewModel BuildVm()
    {
        var undo = new UndoService();
        var viewport = new ViewportViewModel(new TerrainTileService());
        var trackGraph = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), undo);
        var gen = new GenerationViewModel(new TerrainGenerationService(new HttpClient(), new TerrainTileService()), viewport);
        var osm = new OsmOverlayViewModel(new OsmTileService(new HttpClient()), viewport);
        var profile = new ProfileViewModel(trackGraph, viewport, undo);
        return new MainWindowViewModel(
            new ProjectService(), viewport, trackGraph,
            new TerrainEditViewModel(new TerrainTileService(), viewport, undo),
            new EntityTreeViewModel(), new LegacyImportService(), gen, osm, profile);
    }

    [Theory]
    [InlineData("2+3*4", 14.0)]
    [InlineData("(2+3)*4", 20.0)]
    [InlineData("2^3^2", 512.0)] // right-associative
    [InlineData("-5+3", -2.0)]
    [InlineData("10/4", 2.5)]
    public void Evaluator_Respects_Precedence(string expr, double expected)
    {
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(expr), 9);
    }

    [Fact]
    public void Evaluator_Rejects_Malformed()
    {
        Assert.Throws<FormatException>(() => ExpressionEvaluator.Evaluate("2++"));
        Assert.Throws<FormatException>(() => ExpressionEvaluator.Evaluate("(1+2"));
    }

    [Fact]
    public void Calculator_Evaluates_And_Reports_Errors()
    {
        var calc = new CalculatorViewModel { Expression = "(2+3)*4" };
        calc.EvaluateCommand.Execute(null);
        Assert.Equal("20", calc.Result);

        calc.Expression = "bad";
        calc.EvaluateCommand.Execute(null);
        Assert.StartsWith("Error", calc.Result, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProject_Resets_The_Track_Graph()
    {
        var tg = new TrackGraphViewModel(new ProjectService(), new LiveBridgeService(), new UndoService());
        TrackOps.AddNode(tg.Tracks, "x", new FuseVector3(0, 0, 0), default);
        Assert.Single(tg.Tracks.Nodes);

        tg.NewProject("fresh.id", "Fresh");

        Assert.Equal("fresh.id", tg.Project.Id);
        Assert.Equal("Fresh", tg.Project.Name);
        Assert.Empty(tg.Tracks.Nodes);
    }

    [Fact]
    public async Task NewMod_Prompts_And_Starts_Fresh()
    {
        var vm = BuildVm();
        await vm.NewModAsync(new FakeDialog("my.cool.route", "My Cool Route"));
        Assert.Equal("my.cool.route", vm.TrackGraph.Project.Id);
        Assert.Equal("My Cool Route", vm.TrackGraph.Project.Name);
    }

    [Fact]
    public async Task NewMod_Cancel_Leaves_Project_Unchanged()
    {
        var vm = BuildVm();
        var beforeId = vm.TrackGraph.Project.Id;
        await vm.NewModAsync(new FakeDialog((string?)null));
        Assert.Equal(beforeId, vm.TrackGraph.Project.Id);
    }
}
