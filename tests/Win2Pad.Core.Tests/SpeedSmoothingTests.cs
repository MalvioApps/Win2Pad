using Win2Pad.Core.Calibration;
using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// The tablet accelerates by a smoothed speed, so a quick flick travels less than the
/// steady-speed curve says. Without modelling it, the estimate ran ahead of the real pointer and
/// a fast move up from mid-screen could return as if it had reached the top.
/// </summary>
public sealed class SpeedSmoothingTests
{
    private static readonly GainCurve Steep = new([
        new GainPoint(2, 1), new GainPoint(4, 1.283), new GainPoint(8, 1.852), new GainPoint(16, 3.002), new GainPoint(32, 5.35)
    ]);

    [Fact]
    public void The_smoother_builds_up_and_starts_again_after_a_pause()
    {
        var smoother = new SpeedSmoother(0.5);
        Assert.Equal(16, smoother.Next(32, 0.000));
        Assert.Equal(24, smoother.Next(32, 0.015));
        Assert.Equal(28, smoother.Next(32, 0.030));
        Assert.Equal(16, smoother.Next(32, 0.200)); // the hand stopped: from rest again
    }

    [Fact]
    public void Without_smoothing_each_report_stands_alone()
    {
        var smoother = new SpeedSmoother(1);
        Assert.Equal(32, smoother.Next(32, 0));
        Assert.Equal(2, smoother.Next(2, 0.015));
    }

    [Fact]
    public void A_smoothed_flick_travels_less_than_the_steady_curve_says()
    {
        int[] flick = [32, 32, 32, 32];
        var steady = Steep.PredictTravel(flick, 1.0);
        var smoothed = Steep.PredictTravel(flick, 0.3);
        Assert.Equal(4 * 32 * 5.35, steady, 3);
        Assert.True(smoothed < steady * 0.75);
    }

    [Theory]
    [InlineData(0.35)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void The_fit_recovers_the_smoothing_that_produced_the_flicks(double actual)
    {
        var flicks = TabletCalibrator.Flicks.Select(f => (f, Steep.PredictTravel(f, actual))).ToList();
        var (smoothing, error) = Steep.FitSmoothing(flicks);
        Assert.Equal(actual, smoothing, 2);
        Assert.True(error < 0.001);
    }

    [Fact]
    public void A_quick_flick_up_from_mid_screen_no_longer_reaches_the_top()
    {
        PointerModel Model(double smoothing)
        {
            var model = new PointerModel(new PointerCalibration(2000, 1000), TabletSide.Bottom,
                returnMargin: 0.03, awayFactor: 1.15, gainCurve: Steep, earlyReturn: true,
                speedSmoothing: smoothing);
            model.Enter(0.5);
            model.Place(1000, 600);
            return model;
        }

        var unsmoothed = Model(1.0);
        var smoothed = Model(0.3);
        for (var i = 0; i < 4; i++)
        {
            unsmoothed.Move(0, -32, 1.0 + i * 0.015);
            smoothed.Move(0, -32, 1.0 + i * 0.015);
        }

        Assert.Equal(0, unsmoothed.Y);   // believed at the top after 128 counts
        Assert.InRange(smoothed.Y, 100, 400); // still well down the screen, like the real pointer
    }

    [Fact]
    public void Crossing_the_top_in_one_quick_move_is_not_a_return_push()
    {
        var options = new EdgeOptions { CalibrationWidth = 2000, CalibrationHeight = 1000 };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.5);
        model.Move(0, 300, 1.0);                      // well into the tablet
        Assert.False(model.Move(0, -400, 1.1));       // over the edge by a lot, but in an instant
        Assert.False(model.Move(0, 0, 1.15));         // the hand stopped
        Assert.True(model.Overshoot > model.MarginCounts);
    }

    /// <summary>A tablet that smooths speed before accelerating, for the calibrator to measure.</summary>
    private sealed class SmoothingTablet(double smoothing) : ICalibrationMouse, IPointerFeed
    {
        private const int W = 1080, H = 810, Band = 70;
        private long _sequence;
        private double _x = 500, _y = 400, _speed;

        public PageInfo? Page { get; } = new(810, 1080, W, H - Band);
        public PointerSample? Latest { get; private set; }

        public Task MoveAsync(int dx, int dy)
        {
            var speed = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (speed >= 30000) _speed = 0; // a pin, and a pause after it: the average restarts
            else _speed = smoothing * speed + (1 - smoothing) * _speed;
            var gain = 0.325 * Steep.Ratio(Math.Min(_speed, 32));
            var nx = Math.Clamp(_x + dx * gain, 0, W - 1);
            var ny = Math.Clamp(_y + dy * gain, 0, H - 1);
            var moved = nx != _x || ny != _y;
            (_x, _y) = (nx, ny);
            if (moved && _y >= Band) Latest = new PointerSample(++_sequence, _x, _y - Band);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(1.0)]
    public async Task The_fine_tune_measures_the_smoothing(double actual)
    {
        var tablet = new SmoothingTablet(actual);
        var result = await new TabletCalibrator(tablet, tablet, _ => Task.CompletedTask).RunAsync();
        Assert.NotNull(result.SpeedSmoothing);
        Assert.InRange(result.SpeedSmoothing!.Value, actual - 0.15, actual + 0.15);
        Assert.True(result.FlickError < 0.1, $"flick error {result.FlickError}");
    }
}

public sealed class EntryRatioTests
{
    [Fact]
    public void The_entry_move_is_shortened_by_the_measured_ratio()
    {
        var cal = new PointerCalibration(2000, 1000);
        var plain = new PointerModel(cal, TabletSide.Bottom);
        var corrected = new PointerModel(cal, TabletSide.Bottom, entryRatio: 5);

        var along = plain.Enter(0.25)[1];
        var shortened = corrected.Enter(0.25)[1];

        Assert.Equal((500, 20), (along.Dx, along.Dy));
        Assert.Equal((100, 4), (shortened.Dx, shortened.Dy));
        // The model still places the pointer where it is meant to land.
        Assert.Equal((plain.X, plain.Y), (corrected.X, corrected.Y));
        // The pin is untouched: it drives into the corner whatever the acceleration.
        Assert.Equal(plain.Enter(0.25)[0], corrected.Enter(0.25)[0]);
    }

    [Fact]
    public async Task The_fine_tune_measures_how_much_a_single_large_move_is_accelerated()
    {
        // The simulated tablet multiplies every large report by its 2.2 cap.
        var tablet = new SimulatedTabletForEntry();
        var result = await new TabletCalibrator(tablet, tablet, _ => Task.CompletedTask).RunAsync();
        Assert.NotNull(result.EntryRatio);
        Assert.InRange(result.EntryRatio!.Value, 2.1, 2.3);
    }

    [Fact]
    public void A_measured_entry_ratio_reaches_the_pointer_model()
    {
        var profile = Win2Pad.Core.Devices.DeviceProfile.FromTablet("id", new Win2Pad.Core.Devices.TabletInfo("iPad", "Apple Inc.", "iPad7,12"));
        var result = new CalibrationResult(3300, 2500, 1.15, true, 70, [new GainSample(2, 0.33)], 0.33, EntryRatio: 4.8);
        var options = profile.WithMeasurement(result).ApplyTo(new EdgeOptions());
        Assert.Equal(4.8, options.EntryRatio);
        Assert.Equal(4.8, options.CreateModel(TabletSide.Right).EntryRatio);
    }

    /// <summary>Like the calibrator tests' simulated iPad: slow gain 0.325, up to 2.2x for large reports.</summary>
    private sealed class SimulatedTabletForEntry : ICalibrationMouse, IPointerFeed
    {
        private const int W = 1080, H = 810, Band = 70;
        private long _sequence;
        private double _x = 500, _y = 400;

        public PageInfo? Page { get; } = new(810, 1080, W, H - Band);
        public PointerSample? Latest { get; private set; }

        public Task MoveAsync(int dx, int dy)
        {
            var speed = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var gain = 0.325 * (speed <= 4 ? 1 : Math.Min(2.2, 1 + (speed - 4) / 20.0));
            var nx = Math.Clamp(_x + dx * gain, 0, W - 1);
            var ny = Math.Clamp(_y + dy * gain, 0, H - 1);
            var moved = nx != _x || ny != _y;
            (_x, _y) = (nx, ny);
            if (moved && _y >= Band) Latest = new PointerSample(++_sequence, _x, _y - Band);
            return Task.CompletedTask;
        }
    }
}
