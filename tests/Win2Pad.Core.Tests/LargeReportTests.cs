using Win2Pad.Core.Calibration;
using Win2Pad.Core.Devices;
using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>
/// The iPad keeps accelerating past the steady-speed curve's 32 counts: a single 60 to 120-count
/// report travels about 10.7x, not the 5.35x the curve stopped at. Real use produces such reports
/// (motion piled up during the entry pauses, a fast hand), and capping them left the estimate
/// hundreds of counts away from the real pointer until it hit an edge.
/// </summary>
public sealed class LargeReportTests
{
    [Fact]
    public void The_default_curve_covers_large_reports()
    {
        var curve = new GainCurve(TabletModels.DefaultGainCurve);
        Assert.Equal(5.35, curve.Ratio(32), 3);
        Assert.Equal(10.747, curve.Ratio(85), 3);
        Assert.Equal(10.747, curve.Ratio(157), 3); // beyond the last point: the last ratio
        Assert.InRange(curve.Ratio(50), 5.35, 10.747);
    }

    [Fact]
    public void Replays_the_entry_that_was_off_by_a_thousand_counts()
    {
        // Recorded on an iPad (7th generation), tablet below the PC: after entering at the top
        // (y = 50), the first report was dy = 157 and the real pointer landed at y = 1871.
        // The model used to stop at y = 890.
        var options = new EdgeOptions { CalibrationWidth = 3371, CalibrationHeight = 2524, GainCurve = TabletModels.DefaultGainCurve };
        var model = options.CreateModel(TabletSide.Bottom);
        model.Enter(0.42);
        model.Place(1424, 50);
        model.Move(-48, 157, 1.0);
        Assert.InRange(model.Y, 1871 * 0.9, 1871 * 1.1);
    }

    [Fact]
    public void The_entry_move_is_solved_against_the_curve()
    {
        var model = new PointerModel(new PointerCalibration(3371, 2524), TabletSide.Bottom,
            gainCurve: new GainCurve(TabletModels.DefaultGainCurve));
        var along = model.Enter(0.5)[1];   // to x = 1686, 50 down: ~1687 counts of travel
        var distance = Math.Sqrt(along.Dx * along.Dx + along.Dy * along.Dy);
        var travelled = distance * new GainCurve(TabletModels.DefaultGainCurve).Ratio(distance);
        Assert.InRange(travelled, 1687 * 0.97, 1687 * 1.03);
        Assert.InRange(distance, 140, 175); // sent as one ~157-count report
    }

    [Fact]
    public void A_curve_that_stops_at_32_gets_the_measured_large_move()
    {
        GainPoints old = [new(2, 1), new(4, 1.283), new(8, 1.852), new(16, 3.002), new(32, 5.35)];
        var profile = DeviceProfile.FromTablet("id", new TabletInfo("iPad", "Apple Inc.", "iPad7,12"))
            with { GainCurve = old, EntryRatio = 10.747 };
        var options = profile.ApplyTo(new EdgeOptions());
        Assert.Equal(10.747, new GainCurve(options.GainCurve!).Ratio(100), 3);

        // A curve that already reaches past 32 is left alone.
        GainPoints full = [new(2, 1), new(32, 5.35), new(128, 12)];
        Assert.Same(full, DeviceProfile.WithLargeMoves(full, 10.747));
    }

    [Fact]
    public void The_fine_tune_adds_large_reports_to_the_curve()
    {
        var tablet = new LargeMoveTablet();
        var result = new TabletCalibrator(tablet, tablet, _ => Task.CompletedTask).RunAsync().GetAwaiter().GetResult();
        Assert.NotNull(result.LargeMoves);
        Assert.Equal(TabletCalibrator.EntryMoves.Length, result.LargeMoves!.Count);

        var curve = DeviceProfile.FromTablet("id", null).WithMeasurement(result).GainCurve!;
        Assert.Contains(curve, p => p.CountsPerReport == 192);
        // The simulated tablet goes up to 3.5x for large reports, past its 2.2x at 32.
        Assert.InRange(new GainCurve(curve).Ratio(192), 3.3, 3.7);
    }

    /// <summary>Slow gain 0.325; 2.2x at 32 counts, rising to 3.5x for 192-count reports.</summary>
    private sealed class LargeMoveTablet : ICalibrationMouse, IPointerFeed
    {
        private const int W = 1080, H = 810, Band = 70;
        private long _sequence;
        private double _x = 500, _y = 400;

        public PageInfo? Page { get; } = new(810, 1080, W, H - Band);
        public PointerSample? Latest { get; private set; }

        public Task MoveAsync(int dx, int dy)
        {
            var speed = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var ratio = speed <= 4 ? 1 : speed <= 32 ? 1 + (speed - 4) * 1.2 / 28 : Math.Min(3.5, 2.2 + (speed - 32) * 1.3 / 160);
            var gain = 0.325 * ratio;
            var nx = Math.Clamp(_x + dx * gain, 0, W - 1);
            var ny = Math.Clamp(_y + dy * gain, 0, H - 1);
            var moved = nx != _x || ny != _y;
            (_x, _y) = (nx, ny);
            if (moved && _y >= Band) Latest = new PointerSample(++_sequence, _x, _y - Band);
            return Task.CompletedTask;
        }
    }
}
