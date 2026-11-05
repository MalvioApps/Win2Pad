using Win2Pad.Core.Calibration;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class TabletCalibratorTests
{
    /// <summary>
    /// An iPad in landscape behind Safari: 1080x810 points with a 70-point bar at the top, a slow
    /// gain of 0.325 points per count and acceleration that doubles it for fast reports.
    /// </summary>
    private sealed class SimulatedTablet : ICalibrationMouse, IPointerFeed
    {
        public const int W = 1080, H = 810, Band = 70;
        private long _sequence;
        private double _x = 500, _y = 400;

        public Func<int, double> Gain { get; init; } = speed => 0.325 * (speed <= 4 ? 1 : Math.Min(2.2, 1 + (speed - 4) / 20.0));
        public double VerticalScale { get; init; } = 1;
        public bool PageVisible { get; init; } = true;

        public PageInfo? Page { get; init; } = new(810, 1080, W, H - Band);
        public PointerSample? Latest { get; private set; }

        public Task MoveAsync(int dx, int dy)
        {
            var speed = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var gain = Gain(speed);
            var nx = Math.Clamp(_x + dx * gain, 0, W - 1);
            var ny = Math.Clamp(_y + dy * gain * VerticalScale, 0, H - 1);
            var moved = nx != _x || ny != _y;
            (_x, _y) = (nx, ny);
            // The page sees the pointer only inside the viewport, and only when it moves.
            if (moved && PageVisible && _y >= Band) Latest = new PointerSample(++_sequence, _x, _y - Band);
            return Task.CompletedTask;
        }
    }

    private static Task NoDelay(int _) => Task.CompletedTask;

    [Fact]
    public void Page_info_follows_the_orientation_not_safaris_portrait_screen_size()
    {
        var landscape = new PageInfo(810, 1080, 1080, 740);
        Assert.True(landscape.Landscape);
        Assert.Equal((1080, 810, 70), (landscape.Width, landscape.Height, landscape.TopBand));

        var portrait = new PageInfo(810, 1080, 810, 1010);
        Assert.False(portrait.Landscape);
        Assert.Equal((810, 1080, 70), (portrait.Width, portrait.Height, portrait.TopBand));
    }

    [Fact]
    public async Task Measures_the_screen_in_counts_at_the_slow_gain()
    {
        var tablet = new SimulatedTablet();
        var result = await new TabletCalibrator(tablet, tablet, NoDelay).RunAsync();

        // 1080 / 0.325 = 3323, 810 / 0.325 = 2492; within a couple of counts of rounding.
        Assert.InRange(result.Width, 3300, 3345);
        Assert.InRange(result.Height, 2470, 2515);
        Assert.True(result.Landscape);
        Assert.Equal(70, result.TopBandPoints);
        Assert.Equal(TabletCalibrator.Speeds.Length, result.HorizontalCurve.Count);
    }

    [Fact]
    public async Task The_away_factor_covers_the_measured_acceleration()
    {
        var tablet = new SimulatedTablet();
        var result = await new TabletCalibrator(tablet, tablet, NoDelay).RunAsync();

        // Speed 32 multiplies the gain by 2.2 (the simulator's cap); the factor adds 15% headroom.
        var measured = result.HorizontalCurve.Max(s => s.PointsPerCount) / result.SlowGain;
        Assert.InRange(measured, 2.1, 2.3);
        Assert.Equal(Math.Round(measured * 1.15, 2), result.AwayFactor, 2);
    }

    [Fact]
    public async Task Without_acceleration_the_away_factor_is_only_the_headroom()
    {
        var tablet = new SimulatedTablet { Gain = _ => 0.4 };
        var result = await new TabletCalibrator(tablet, tablet, NoDelay).RunAsync();
        Assert.Equal(1.15, result.AwayFactor, 2);
        Assert.InRange(result.Width, 2690, 2710); // 1080 / 0.4 = 2700
    }

    [Fact]
    public async Task Vertical_gain_is_measured_separately()
    {
        var tablet = new SimulatedTablet { VerticalScale = 1.1 };
        var result = await new TabletCalibrator(tablet, tablet, NoDelay).RunAsync();
        Assert.InRange(result.Height, 2245, 2290); // 810 / (0.325 * 1.1) = 2266
    }

    [Fact]
    public async Task A_missing_page_fails_with_a_clear_message()
    {
        var tablet = new SimulatedTablet { Page = null };
        var error = await Assert.ThrowsAsync<CalibrationException>(
            () => new TabletCalibrator(tablet, tablet, NoDelay).RunAsync());
        Assert.Contains("not open", error.Message);
    }

    [Fact]
    public async Task A_page_that_never_sees_the_pointer_gives_up()
    {
        var tablet = new SimulatedTablet { PageVisible = false };
        var error = await Assert.ThrowsAsync<CalibrationException>(
            () => new TabletCalibrator(tablet, tablet, NoDelay).RunAsync());
        Assert.Contains("never saw the pointer", error.Message);
    }

    [Fact]
    public async Task Progress_is_reported_through_to_the_end()
    {
        var tablet = new SimulatedTablet();
        var calibrator = new TabletCalibrator(tablet, tablet, NoDelay);
        var fractions = new List<double>();
        calibrator.Progress += (_, fraction) => fractions.Add(fraction);
        await calibrator.RunAsync();
        Assert.Equal(1, fractions[^1]);
        Assert.Equal(fractions.OrderBy(f => f), fractions);
    }
}
