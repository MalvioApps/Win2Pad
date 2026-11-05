// Ported from BlueShare's tests/test_pointer.py (Apache License 2.0); side-exit cases are not ported.
using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class PointerModelTests
{
    private static readonly PointerCalibration Cal = new(2000, 1000);
    private const int Pin = PointerModel.PinCounts;
    private const int Settle = PointerModel.SettleMs;

    [Fact]
    public void Entering_from_a_pc_below_pins_bottom_left_then_moves_inside()
    {
        var model = new PointerModel(Cal, TabletSide.Top);
        Assert.Equal([new PinMove(-Pin, Pin, Settle), new PinMove(500, -20, Settle)], model.Enter(0.25));
        Assert.Equal((500d, 980d), (model.X, model.Y));
    }

    [Fact]
    public void Return_needs_a_push_beyond_the_margin()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0.1);
        model.Enter(0.5);
        Assert.False(model.Move(0, 20, 1.0));  // reaches the bottom edge
        Assert.False(model.Move(0, 60, 1.1));  // 60 past: within the 100 margin
        Assert.True(model.Move(0, 50, 1.2));   // 110 past
    }

    [Fact]
    public void Overshoot_resets_after_a_pause()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0.1, overshootResetSeconds: 0.4);
        model.Enter(0.5);
        model.Move(0, 20, 1.0);
        Assert.False(model.Move(0, 90, 1.1));
        Assert.False(model.Move(0, 90, 2.0));  // pause > 0.4 s: counting starts again
        Assert.True(model.Move(0, 20, 2.1));
    }

    [Fact]
    public void Moving_back_clears_the_overshoot()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0.1, awayFactor: 3.0);
        model.Enter(0.5);
        model.Move(0, 89, 1.0);                // 69 past the edge
        model.Move(0, -5, 1.1);                // counted 3x: may have been accelerated
        Assert.Equal(0, model.Overshoot);
        Assert.Equal(985, model.Y);
        Assert.False(model.Move(0, 60, 1.2));
    }

    [Fact]
    public void Moves_away_from_the_return_edge_are_pessimistic()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0.1, awayFactor: 3.0);
        model.Enter(0.5);                      // y = 980
        model.Move(0, -100, 1.0);              // estimated 300 up
        Assert.Equal(680, model.Y);
        Assert.False(model.Move(0, 100 + 20 + 101, 1.1));
        Assert.False(model.Move(0, 199, 1.2)); // at the edge, 100 beyond: not past the margin
        Assert.True(model.Move(0, 1, 1.3));
    }

    [Fact]
    public void Without_margin_or_pessimism_it_returns_right_at_the_edge()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0, awayFactor: 1);
        model.Enter(0.5);
        model.Move(0, -100, 1.0);
        Assert.Equal(880, model.Y);
        Assert.False(model.Move(0, 121, 1.1)); // 1 count past: the minimum margin
        Assert.True(model.Move(0, 1, 1.2));
    }

    [Fact]
    public void A_negative_margin_returns_before_the_edge_only_when_heading_there()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: -0.03, awayFactor: 1); // lead 30
        model.Enter(0.5);                      // y = 980
        Assert.False(model.Move(-50, 0, 1.0));
        Assert.False(model.Move(0, -100, 1.1));
        Assert.False(model.Move(0, 60, 1.2));  // 60 from the edge
        Assert.True(model.Move(0, 31, 1.3));   // within 30 and moving down
    }

    [Fact]
    public void Moves_along_the_edge_are_not_scaled()
    {
        var model = new PointerModel(Cal, TabletSide.Top, awayFactor: 3);
        model.Enter(0.5);
        model.Move(-100, 0, 1.0);
        Assert.Equal(900, model.X);
    }

    [Fact]
    public void The_pointer_is_clamped_on_the_other_edges()
    {
        var model = new PointerModel(Cal, TabletSide.Top, returnMargin: 0.1);
        model.Enter(0.5);
        model.Move(-5000, -5000, 1.0);
        Assert.Equal((0d, 0d), (model.X, model.Y));
        Assert.False(model.Move(0, 1050, 1.1));
        Assert.Equal(0, model.FractionAlongEdge());
    }

    [Theory]
    [InlineData(TabletSide.Bottom, -Pin, -Pin, 1000, 20, 1000, 20)]
    [InlineData(TabletSide.Left, Pin, -Pin, -40, 500, 1960, 500)]
    [InlineData(TabletSide.Right, -Pin, -Pin, 40, 500, 40, 500)]
    public void Entry_for_the_other_sides(TabletSide side, int pinX, int pinY, int alongX, int alongY, int x, int y)
    {
        var model = new PointerModel(Cal, side);
        Assert.Equal([new PinMove(pinX, pinY, Settle), new PinMove(alongX, alongY, Settle)], model.Enter(0.5));
        Assert.Equal(((double)x, (double)y), (model.X, model.Y));
    }

    [Theory]
    [InlineData(TabletSide.Right, -Pin, Pin, 40, -250, 40, 750)]    // lower half: from the bottom corner
    [InlineData(TabletSide.Left, Pin, Pin, -40, -250, 1960, 750)]
    [InlineData(TabletSide.Top, Pin, Pin, -500, -20, 1500, 980)]    // right half: from the right corner
    [InlineData(TabletSide.Bottom, Pin, -Pin, -500, 20, 1500, 20)]
    public void Entry_past_the_middle_pins_the_nearer_corner(TabletSide side, int pinX, int pinY,
        int alongX, int alongY, int x, int y)
    {
        var model = new PointerModel(Cal, side);
        Assert.Equal([new PinMove(pinX, pinY, Settle), new PinMove(alongX, alongY, Settle)], model.Enter(0.75));
        Assert.Equal(((double)x, (double)y), (model.X, model.Y));
    }

    [Fact]
    public void Return_for_a_tablet_on_the_left()
    {
        var model = new PointerModel(Cal, TabletSide.Left, returnMargin: 0.1);
        model.Enter(0.5);
        Assert.False(model.Move(40, 0, 1.0));
        Assert.True(model.Move(201, 0, 1.1));
    }

    [Fact]
    public void No_return_before_the_model_is_armed()
    {
        var model = new PointerModel(Cal, TabletSide.Right, returnMargin: 0.01);
        model.Enter(0.5);
        model.ArmAfter(2.0);
        Assert.False(model.Move(-5000, 0, 1.0));
        Assert.True(model.Move(-5000, 0, 2.1));
    }

    [Fact]
    public void Edge_push_requires_a_continuous_push()
    {
        var push = new EdgePush(100, 0.3);
        Assert.False(push.Feed(true, 60, 1.0));
        Assert.False(push.Feed(true, 0, 1.05)); // sliding along the edge keeps the count
        Assert.True(push.Feed(true, 40, 1.1));
        push.Reset();
        Assert.False(push.Feed(true, 60, 2.0));
        Assert.False(push.Feed(false, 60, 2.1)); // left the edge
        Assert.False(push.Feed(true, 60, 2.2));
        Assert.False(push.Feed(true, 60, 3.0));  // too slow: restarted
    }

    [Fact]
    public void Edge_push_tolerates_jitter_but_not_leaving_the_edge()
    {
        var push = new EdgePush(100);
        push.Feed(true, 90, 1.0);
        Assert.False(push.Feed(true, -1, 1.05));
        Assert.True(push.Feed(true, 10, 1.1));
        push.Reset();
        push.Feed(true, 90, 2.0);
        Assert.False(push.Feed(false, -5, 2.05));
        Assert.False(push.Feed(true, 20, 2.1));
    }
}
