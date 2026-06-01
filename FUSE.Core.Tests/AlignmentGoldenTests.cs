using System;
using System.IO;
using System.Linq;
using Fuse.Core.Geometry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Fuse.Core.Tests;

/// <summary>
/// Golden test: <see cref="Alignment"/> must match the Python reference
/// (edit_tiles/alignment.py) — circle/arc fit (incl. arc-fit RMS), polyline length,
/// projection/deviation, signed turn, circumradius, local radius. Fixture:
/// Fixtures/alignment-golden.json (regenerate with tmp_align_golden.py).
/// </summary>
public class AlignmentGoldenTests
{
    private static JObject G() =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "alignment-golden.json")));

    private static (double X, double Z)[] Pts(JArray a) => a.Select(p => ((double)p[0]!, (double)p[1]!)).ToArray();

    [Fact]
    public void FitCircle_Matches_Python()
    {
        var g = G();
        var fc = Alignment.FitCircle(Pts((JArray)g["arcPoints"]!));
        Assert.NotNull(fc);
        var e = (JObject)g["fitCircle"]!;
        Assert.True(Math.Abs(fc!.Center.X - (double)e["center"]![0]!) < 1e-6);
        Assert.True(Math.Abs(fc.Center.Z - (double)e["center"]![1]!) < 1e-6);
        Assert.True(Math.Abs(fc.Radius - (double)e["radius"]!) < 1e-6);
        Assert.True(Math.Abs(fc.RmsError - (double)e["rms"]!) < 1e-6);
        Assert.Equal((int)e["turn"]!, fc.TurnSign);
    }

    [Fact]
    public void FitArcToChain_Matches_Python()
    {
        var g = G();
        var fa = Alignment.FitArcToChain(Pts((JArray)g["arcPoints"]!));
        Assert.NotNull(fa);
        var e = (JObject)g["fitArc"]!;
        Assert.True(Math.Abs(fa!.Radius - (double)e["radius"]!) < 1e-6);
        Assert.True(Math.Abs(fa.RmsError - (double)e["rms"]!) < 1e-6); // arc-fit RMS exit gate
        Assert.Equal((int)e["turnSign"]!, fa.TurnSign);
        Assert.True(Math.Abs(fa.DeltaAngleDeg - (double)e["deltaDeg"]!) < 1e-6);
        Assert.True(Math.Abs(fa.ArcLength - (double)e["arcLength"]!) < 1e-6);
        Assert.True(Math.Abs(fa.ChordLength - (double)e["chord"]!) < 1e-6);

        var ep = (JArray)e["points"]!;
        Assert.Equal(ep.Count, fa.Points.Count);
        for (var i = 0; i < ep.Count; i++)
        {
            Assert.True(Math.Abs(fa.Points[i].X - (double)ep[i][0]!) < 1e-6);
            Assert.True(Math.Abs(fa.Points[i].Z - (double)ep[i][1]!) < 1e-6);
            Assert.True(Math.Abs(fa.Points[i].RotY - (double)ep[i][2]!) < 1e-6);
        }
    }

    [Fact]
    public void Polyline_Project_Deviation_Match_Python()
    {
        var g = G();
        var poly = Pts((JArray)((JObject)g["polyline"]!)["points"]!);
        var pl = (JObject)g["polyline"]!;
        Assert.True(Math.Abs(Alignment.PolylineLength(poly) - (double)pl["length"]!) < 1e-9);

        var cum = Alignment.CumulativeLengths(poly);
        var ecum = (JArray)pl["cumulative"]!;
        for (var i = 0; i < ecum.Count; i++)
        {
            Assert.True(Math.Abs(cum[i] - (double)ecum[i]!) < 1e-9);
        }

        var pj = (JObject)g["project"]!;
        var hit = Alignment.ProjectPointToPolyline(((double)pj["point"]![0]!, (double)pj["point"]![1]!), poly);
        Assert.NotNull(hit);
        Assert.True(Math.Abs(hit!.Distance - (double)pj["distance"]!) < 1e-9);
        Assert.Equal((int)pj["segIndex"]!, hit.SegmentIndex);
        Assert.True(Math.Abs(hit.T - (double)pj["t"]!) < 1e-9);

        var dv = (JObject)g["deviation"]!;
        var dr = Alignment.DeviationSamples(new (double, double)[] { (1.0, 1.0), (3.5, 6.0), (0.0, 11.0) }, poly);
        Assert.True(Math.Abs(dr.MaxDistance!.Value - (double)dv["max"]!) < 1e-9);
        Assert.True(Math.Abs(dr.RmsDistance!.Value - (double)dv["rms"]!) < 1e-9);
    }

    [Fact]
    public void SignedTurn_Circumradius_LocalRadius_Match_Python()
    {
        var g = G();
        var arc = Pts((JArray)g["arcPoints"]!);
        Assert.Equal((int)g["signedTurn"]!, Alignment.SignedTurn(arc));

        var cr = Alignment.Circumradius(arc[0], arc[3], arc[6]);
        Assert.NotNull(cr);
        Assert.True(Math.Abs(cr!.Value - (double)g["circumradius"]!) < 1e-6);

        var lr = Alignment.LocalRadiusSamples(arc);
        var elr = (JArray)g["localRadius"]!;
        Assert.Equal(elr.Count, lr.Count);
        for (var i = 0; i < elr.Count; i++)
        {
            Assert.Equal((int)elr[i]["index"]!, lr[i].Index);
            Assert.True(Math.Abs(lr[i].Radius - (double)elr[i]["radius"]!) < 1e-6);
        }
    }
}
