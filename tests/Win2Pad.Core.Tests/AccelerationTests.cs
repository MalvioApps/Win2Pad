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
    public void Without_a_curve_the_top_edge_errs_only_mildly_towards_returning_early()
    {
        // Tablet below the PC, acceleration unknown (factor 6 from an old fine-tune).
        var model = new PointerModel(Cal, TabletSide.Bottom, returnMargin: 0.02, awayFactor: 6, earlyReturn: true);
        Assert.Equal(1.3, model.TowardFactor);
        model.Enter(0.5);                        // y = 20
        model.Move(0, 600, 1.0);                 // away: counted once, y = 620

        // Back up at a steady pace: counted 1.3x, so the edge is reached after ~477 counts and
        // the 20-count push past it follows: a little early, never late, and never on a whim.
        var sent = 0;
        while (!model.Move(0, -10, 1.1 + sent * 0.0001)) sent += 10;
        Assert.InRange(sent, 470, 510);
    }

    [Fact]
    public void A_small_slide_up_near_the_top_does_not_return()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom); // the way back is up through the top
        model.Enter(0.5);
        model.Move(0, 100, 1.0);                 // a little below the top: y = 120

        // A quick flick upwards that stops short of the edge.
        Assert.False(model.Move(0, -40, 1.10));
        Assert.False(model.Move(0, -40, 1.12));
        Assert.True(model.Y > 0);
    }

    [Fact]
    public void A_quick_flick_that_touches_the_top_does_not_return_either()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 60, 1.0);                  // y = 80

        // Counted 1.3x without a curve: 52 + 52 from y = 80 reaches the edge and goes about 18
        // counts of real movement past it, under the 3% (30-count) push.
        Assert.False(model.Move(0, -40, 1.10));
        Assert.False(model.Move(0, -40, 1.12));
        Assert.Equal(0, model.Y);
    }

    [Fact]
    public void A_deliberate_push_past_the_top_returns()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 60, 1.0);                  // y = 80

        var returned = false;
        for (var i = 0; i < 20 && !returned; i++) returned = model.Move(0, -10, 1.1 + i * 0.015);
        Assert.True(returned);                   // 80 to the edge, then 30 past it for at least 80 ms
    }

    [Fact]
    public void A_pause_at_the_top_starts_the_push_again()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 200, 1.0);                 // into the tablet first, which arms the return
        model.Place(1000, 0);                    // then resting exactly at the top
        // The push is measured as the hand made it (the 1.3x bias and acceleration taken out).
        Assert.False(model.Move(0, -15, 1.1));   // 15 past, under 30
        Assert.False(model.Move(0, -15, 2.0));   // after a pause: counting from zero again
        Assert.True(model.Move(0, -20, 2.09));   // 15 + 20 = 35 past, sustained for 90 ms
    }

    [Fact]
    public void Right_after_entering_a_move_back_towards_the_edge_does_not_return()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom); // enters next to the top edge
        model.Enter(0.5);                                   // y = 20
        Assert.False(model.LeftEdge);
        Assert.False(model.Move(0, -200, 1.0));             // straight back up: not armed yet
        Assert.False(model.Move(0, 50, 1.1));               // y = 50: still within 8%
        Assert.False(model.Move(0, -200, 1.2));
        Assert.False(model.LeftEdge);
    }

    [Fact]
    public void Once_well_into_the_tablet_the_way_back_is_armed()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 100, 1.0);                            // y = 120: past the 80-count arm
        Assert.True(model.LeftEdge);
        Assert.False(model.Move(0, -200, 1.1));             // 120 up to the edge, then 46 real past it...
        Assert.True(model.Move(0, -10, 1.2));               // ...and still pushing 100 ms later
    }

    [Fact]
    public void A_fast_flick_past_the_edge_counts_as_the_hand_moved_not_as_the_tablet_did()
    {
        // With the measured curve a 32-count report travels 5.35x; past the edge only the hand's
        // movement counts towards the push.
        var options = new EdgeOptions
        {
            CalibrationWidth = 2000, CalibrationHeight = 1000,
            GainCurve = [new GainPoint(2, 1), new GainPoint(32, 5.35)]
        };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 100, 1.0);                            // armed, y well below the top
        model.Place(1000, 100);
        Assert.False(model.Move(0, -32, 1.1));              // 171 travelled: 100 to the edge, 71 past = 13 real
        Assert.Equal(0, model.Y);
    }

    [Fact]
    public void The_push_needed_follows_the_users_choice()
    {
        var light = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000, ReturnPushScale = 0.5 };
        var firm = light with { ReturnPushScale = 2 };
        Assert.Equal(15, light.CreateModel(TabletSide.Bottom).MarginCounts);
        Assert.Equal(60, firm.CreateModel(TabletSide.Bottom).MarginCounts);
        Assert.Equal(40, light.CreateModel(TabletSide.Right).MarginCounts); // sides: 4% x 0.5 of 2000
    }

    [Fact]
    public void Old_settings_drop_the_eager_vertical_return()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-edge-{Guid.NewGuid():N}.json");
        try
        {
            // Written before the version field existed, with the old eager values.
            File.WriteAllText(path, """
                { "Enabled": true, "Side": "Bottom", "CalibrationWidth": 3371, "CalibrationHeight": 2524,
                  "VerticalReturnLead": 0.05, "VerticalReturnMargin": 0.01, "AwayFactor": 6 }
                """);
            var loaded = EdgeOptions.Load(path);
            Assert.Equal(EdgeOptions.CurrentVersion, loaded.Version);
            Assert.Equal(0, loaded.VerticalReturnLead);
            Assert.Equal(0.03, loaded.VerticalReturnMargin);
            Assert.Equal(6, loaded.AwayFactor);  // everything else kept
            Assert.Equal(3371, loaded.CalibrationWidth);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Current_settings_keep_their_vertical_values()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-edge-{Guid.NewGuid():N}.json");
        try
        {
            var custom = new EdgeOptions { VerticalReturnLead = 0.03, VerticalReturnMargin = 0.05 };
            Assert.True(custom.TrySave(path, out _));
            var loaded = EdgeOptions.Load(path);
            Assert.Equal(0.03, loaded.VerticalReturnLead);
            Assert.Equal(0.05, loaded.VerticalReturnMargin);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Beside_the_pc_it_still_errs_towards_returning_late()
    {
        var model = new PointerModel(Cal, TabletSide.Right, returnMargin: 0.01, awayFactor: 3);
        Assert.False(model.EarlyReturn);
        model.Enter(0.5);                        // x = 40
        model.Move(100, 0, 1.0);                 // away: counted 3x, x = 340
        Assert.False(model.Move(-200, 0, 1.1));  // the real pointer may already be at the edge
    }

    [Fact]
    public void Top_and_bottom_returns_use_the_vertical_settings()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var below = options.CreateModel(TabletSide.Bottom);
        Assert.Equal(options.VerticalReturnLead, below.ReturnLead);
        Assert.Equal(options.VerticalReturnMargin, below.ReturnMargin);
        Assert.True(below.EarlyReturn);

        var beside = options.CreateModel(TabletSide.Right);
        Assert.Equal(0, beside.ReturnLead);
        Assert.Equal(options.ReturnMargin, beside.ReturnMargin);
        Assert.Null(beside.GainCurve);

        var tuned = (options with { GainCurve = new GainPoints(Steep.Points) }).CreateModel(TabletSide.Right);
        Assert.NotNull(tuned.GainCurve);
    }
}
