using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Services;

namespace Fuse.ExternalEditor.ViewModels;

/// <summary>
/// Drives terrain generation: a Mapbox token + a world-tile region → downloaded,
/// decoded tiles via <see cref="ITerrainGenerationService"/>, with live progress.
/// </summary>
public partial class GenerationViewModel : ViewModelBase
{
    private readonly ITerrainGenerationService _generation;
    private readonly ViewportViewModel _viewport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _mapboxToken = string.Empty;

    [ObservableProperty]
    private int _originGx;

    [ObservableProperty]
    private int _originGy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private int _width = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private int _height = 1;

    [ObservableProperty]
    private bool _useNlcd = true;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private int _progressCompleted;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _isGenerating;

    [ObservableProperty]
    private string _status = "Set a Mapbox token + output folder, then Generate.";

    public GenerationViewModel(ITerrainGenerationService generation, ViewportViewModel viewport)
    {
        _generation = generation;
        _viewport = viewport;
    }

    private bool CanGenerate() => !IsGenerating && !string.IsNullOrWhiteSpace(MapboxToken) && Width > 0 && Height > 0;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync(CancellationToken ct)
    {
        var tiles = new List<(int Gx, int Gy)>();
        for (var gx = OriginGx; gx < OriginGx + Width; gx++)
        {
            for (var gy = OriginGy; gy < OriginGy + Height; gy++)
            {
                tiles.Add((gx, gy));
            }
        }

        IsGenerating = true;
        ProgressCompleted = 0;
        ProgressTotal = tiles.Count;
        // Update only the numeric progress here — Status is owned by the start/success/error
        // path below, so a late (async) progress callback can't clobber the final message.
        var progress = new Progress<TerrainGenProgress>(p =>
        {
            ProgressCompleted = p.Completed;
            ProgressTotal = p.Total;
        });

        var options = new TerrainGenOptions
        {
            UseNlcd = UseNlcd,
            OutputDir = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder,
        };

        try
        {
            var done = await _generation.GenerateRegionAsync(tiles, MapboxToken, options, progress, ct).ConfigureAwait(true);
            ProgressCompleted = done; // deterministic final (async progress callbacks may still be draining)
            Status = $"Generated {done} tile(s).";
            if (!string.IsNullOrWhiteSpace(OutputFolder))
            {
                _viewport.LoadFolder(OutputFolder);
            }
        }
        catch (MapboxAuthException)
        {
            Status = "Mapbox token rejected — re-paste it to remove hidden characters.";
        }
        catch (Exception e)
        {
            Status = "Generation failed: " + e.Message;
        }
        finally
        {
            IsGenerating = false;
        }
    }
}
