using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class AccelerationTests
{
    private static readonly PointerCalibration Cal = new(2000, 1000);

    /// <summary>A tablet that moves the pointer 5x further for fast reports, like the one measured.</summary>
    private static readonly GainCurve Steep = new([
        new GainPoint(2, 1), new GainPoint(4, 1), new GainPoint(8, 1.8), new GainPoint(16, 3.4), new GainPoint(32, 5.2)
    ]);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(8, 1.8)]
    [InlineData(64, 5.2)]                   // beyond the fastest measured: the last ratio
    [InlineData(11.3137, 2.6)]              // half way between 8 and 16 on a log scale
    public void The_curve_interpolates_between_doublings(double speed, double ratio) =>
        Assert.Equal(ratio, Steep.Ratio(speed), 2);

    [Fact]
    public void A_curve_from_measured_gains_is_relative_to_the_slowest()
    {
        var curve = GainCurve.FromGains([(2, 0.32), (4, 0.33), (32, 1.6)]);
        Assert.Equal(1, curve.Ratio(2), 3);
        Assert.Equal(5, curve.Ratio(32), 3);
    }

    [Fact]
    public void With_the_curve_a_fast_move_away_is_followed_rather_than_feared()
    {
        // Tablet below the PC; the way back is up through its top edge.
        var model = new PointerModel(Cal, TabletSide.Bottom, returnMargin: 0.01, awayFactor: 1.0, gainCurve: Steep);
        model.Enter(0.5); // y = 20

        // Fast reports down: 20 reports of 32 counts travel 20 * 32 * 5.2 = 3328 counts, clamped.
        for (var i = 0; i < 20; i++) model.Move(0, 32, 1.0 + i * 0.015);
        Assert.Equal(1000, model.Y);

        // Slow way back up covers the real distance; the old 6x factor would have needed 6 times more.
        for (var i = 0; i < 99; i++) model.Move(0, -10, 2.0 + i * 0.015);
        Assert.InRange(model.Y, 0, 30);
    }

    [Fact]
    public void Moving_quickly_to_the_top_returns_before_reaching_it()
    {
        var model = new PointerModel(Cal, TabletSide.Bottom, returnMargin: 0.01, awayFactor: 1.0,
            returnLead: 0.05, leadMinSpeed: 4);
        model.Enter(0.5);
        model.Move(0, 500, 1.0);                 // well inside, y = 520

        Assert.False(model.Move(0, -400, 1.1));  // y = 120: outside the 50-count lead
        Assert.True(model.Move(0, -80, 1.2));    // y = 40: inside it, heading there quickly
    }

    [Fact]
    public void Approaching_slowly_still_reaches_the_top()
    {
        var model = new PointerModel(Cal, TabletSide.Bottom, returnMargin: 0.01, awayFactor: 1.0,
            returnLead: 0.05, leadMinSpeed: 4);
        model.Enter(0.5);
        model.Move(0, 100, 1.0);                 // y = 120

        for (var i = 0; i < 39; i++) Assert.False(model.Move(0, -3, 1.1 + i * 0.015));
        Assert.Equal(3, model.Y);                // in the lead zone, but slowly: no return
    }

    [Fact]
    public void The_lead_only_applies_to_the_return_edge()
    {
        var model = new PointerModel(Cal, TabletSide.Bottom, returnMargin: 0.01, awayFactor: 1.0, returnLead: 0.05);
        model.Enter(0.5);
        model.Move(0, 500, 1.0);
        Assert.False(model.Move(0, 470, 1.1));   // fast towards the far (bottom) edge
        Assert.False(model.Move(-990, 0, 1.2));  // fast towards a side edge
    }

    [Fact]
    public void Top_and_bottom_returns_use_the_vertical_settings()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var below = options.CreateModel(TabletSide.Bottom);
        Assert.Equal(options.VerticalReturnLead, below.ReturnLead);
        Assert.Equal(options.VerticalReturnMargin, below.ReturnMargin);

        var beside = options.CreateModel(TabletSide.Right);
        Assert.Equal(0, beside.ReturnLead);
        Assert.Equal(options.ReturnMargin, beside.ReturnMargin);
        Assert.Null(beside.GainCurve);

        var tuned = (options with { GainCurve = Steep.Points }).CreateModel(TabletSide.Right);
        Assert.NotNull(tuned.GainCurve);
    }
}
