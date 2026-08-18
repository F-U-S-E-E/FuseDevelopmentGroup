using Fuse.Core.Versioning;
using Xunit;

namespace Fuse.Core.Tests;

public class FuseGenerationGateTests
{
    [Fact]
    public void Starts_at_zero()
    {
        var gate = new FuseGenerationGate();
        Assert.Equal(0, gate.Current);
        // Real checks always use a token from Begin() (>= 1); 0 is just the
        // pre-start sentinel, trivially equal to the initial Current.
        Assert.True(gate.IsCurrent(0));
        Assert.False(gate.IsCurrent(1));
    }

    [Fact]
    public void Begin_supersedes_the_prior_generation()
    {
        var gate = new FuseGenerationGate();

        var g1 = gate.Begin();
        Assert.Equal(1, g1);
        Assert.True(gate.IsCurrent(g1));

        // A newer check starts: the older generation is no longer current, so an
        // older response completing out of order would be discarded.
        var g2 = gate.Begin();
        Assert.Equal(2, g2);
        Assert.False(gate.IsCurrent(g1));
        Assert.True(gate.IsCurrent(g2));
    }

    [Fact]
    public void Out_of_order_completion_only_commits_the_latest_generation()
    {
        // Models three overlapping checks. Whatever order their responses land,
        // only the newest generation's result may commit.
        var gate = new FuseGenerationGate();
        var g1 = gate.Begin();
        var g2 = gate.Begin();
        var g3 = gate.Begin();

        // Responses arrive out of order: g1, then g3, then g2.
        Assert.False(gate.IsCurrent(g1)); // stale — discard
        Assert.True(gate.IsCurrent(g3));  // newest — commit
        Assert.False(gate.IsCurrent(g2)); // stale — discard
    }

    [Fact]
    public void Reset_returns_to_the_initial_state()
    {
        var gate = new FuseGenerationGate();
        gate.Begin();
        gate.Begin();

        gate.Reset();
        Assert.Equal(0, gate.Current);

        var g = gate.Begin();
        Assert.Equal(1, g);
        Assert.True(gate.IsCurrent(g));
    }
}
