using System;
using Fuse.Core.Authoring;
using Fuse.Core.Geometry;
using Fuse.Core.Model;
using Xunit;

namespace Fuse.Core.Tests;

public class MeasureProfileTests
{
    [Fact]
    public void Bearing_Distance_Grade()
    {
        Assert.True(Math.Abs(Measurement.BearingDeg((0, 0), (0, 10)) - 0.0) < 1e-9);    // +Z = north = 0°
        Assert.True(Math.Abs(Measurement.BearingDeg((0, 0), (10, 0)) - 90.0) < 1e-9);   // +X = east = 90°
        Assert.True(Math.Abs(Measurement.BearingDeg((0, 0), (0, -10)) - 180.0) < 1e-9);
        Assert.True(Math.Abs(Measurement.BearingDeg((0, 0), (-10, 0)) - 270.0) < 1e-9);
        Assert.True(Math.Abs(Measurement.DistanceXz((0, 0), (3, 4)) - 5.0) < 1e-9);
        Assert.Equal(0.0, Measurement.GradePercent(5, 0));
        Assert.True(Math.Abs(Measurement.GradePercent(5, 100) - 5.0) < 1e-9);
    }

    [Fact]
    public void Profile_Computes_Station_Grade_And_CutFill()
    {
        var t = new FuseTrackDefinition();
        TrackOps.AddNode(t, "A", new FuseVector3(0, 100, 0), default);
        TrackOps.AddNode(t, "B", new FuseVector3(0, 110, 100), default); // +10 m rise
        TrackOps.ConnectSegment(t, "s1", "A", "B");

        var profile = TrackProfile.Build(t, new[] { "A", "B" }, (_, _) => 105.0); // flat terrain @105

        Assert.Equal(2, profile.Count);
        Assert.Equal(0.0, profile[0].Station);
        Assert.True(profile[1].Station >= 99 && profile[1].Station <= 102); // ~100 m of run
        Assert.Equal(100.0, profile[0].TrackElevation);
        Assert.True(profile[1].GradePercent > 9.0 && profile[1].GradePercent < 11.0); // ~10%
        Assert.Equal(-5.0, profile[0].CutFill!.Value); // track below terrain → cut
        Assert.Equal(5.0, profile[1].CutFill!.Value);  // track above terrain → fill
    }
}
