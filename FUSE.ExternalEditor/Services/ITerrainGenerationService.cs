using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuse.ExternalEditor.Logic.Generation;
using Fuse.ExternalEditor.Models.Terrain;

namespace Fuse.ExternalEditor.Services;

/// <summary>Options for a terrain-generation run.</summary>
public sealed class TerrainGenOptions
{
    public bool UseNlcd { get; set; } = true;
    public double NlcdBlur { get; set; } = GenerationConstants.NlcdBlur;
    public int? VegOverride { get; set; }
    public int MaxConcurrency { get; set; } = 4;
    public string? OutputDir { get; set; }
}

public readonly record struct TerrainGenProgress(int Completed, int Total, string Message);

/// <summary>Downloads + builds terrain tiles from Mapbox elevation (+ NLCD land cover).</summary>
public interface ITerrainGenerationService
{
    Task<TerrainTile> GenerateTileAsync(int gx, int gy, string token, TerrainGenOptions options, CancellationToken ct = default);

    Task<int> GenerateRegionAsync(
        IReadOnlyList<(int Gx, int Gy)> tiles, string token, TerrainGenOptions options,
        IProgress<TerrainGenProgress>? progress = null, CancellationToken ct = default);
}
