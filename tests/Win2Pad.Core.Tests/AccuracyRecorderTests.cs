using Win2Pad.Core.Calibration;
using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class AccuracyRecorderTests
{
    // iPad (7th generation) in landscape behind Safari: 1080x810 points, the page 70 points down.
    private static readonly PageInfo Page = new(810, 1080, 1080, 740);
    private static readonly PointerCalibration Cal = new(3371, 2524);

    [Fact]
    public void A_page_position_becomes_screen_counts_below_safaris_bars()
    {
        var (x, y) = AccuracyRecorder.ToCounts(new PointerSample(1, 540, 0), Page, Cal);
        Assert.Equal(3371 / 2.0, x, 1);
        Assert.Equal(70 / 810.0 * 2524, y, 1); // the top of the page is 70 points down the screen

        var (_, bottom) = AccuracyRecorder.ToCounts(new PointerSample(2, 0, 740), Page, Cal);
        Assert.Equal(2524, bottom, 1);
    }

    [Fact]
    public void Reports_and_real_positions_are_recorded_with_the_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-compare-{Guid.NewGuid():N}.csv");
        try
        {
            using (var recorder = new AccuracyRecorder(path, () => Page, () => Cal))
            {
                recorder.OnReport(new TrackedReport(0, -32, 1685, 900, 32, 5.35, 0, false));
                var estimate = new LiveEstimate(true, true, TabletSide.Bottom, 3371, 2524, 1685.5, 900, 0, 75, true, 202, 32, 5.35);
                recorder.OnSample(new PointerSample(1, 540, 230), estimate); // really at y = (230+70)/810*2524 = 934.8
                Assert.Equal(1, recorder.Samples);
                Assert.NotNull(recorder.Real);
            }

            var lines = File.ReadAllLines(path);
            Assert.StartsWith("ms,kind,dx,dy,est_x,est_y", lines[0]);
            Assert.Contains(lines, l => l.Split(',')[1] == "R" && l.Split(',')[3] == "-32");
            var p = lines.Single(l => l.Split(',')[1] == "P").Split(',');
            Assert.Equal(-34.8, double.Parse(p[13], System.Globalization.CultureInfo.InvariantCulture), 1); // estimate 35 counts too high
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Positions_before_the_page_reports_its_size_are_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-compare-{Guid.NewGuid():N}.csv");
        try
        {
            using var recorder = new AccuracyRecorder(path, () => null, () => Cal);
            recorder.OnSample(new PointerSample(1, 10, 10), null);
            Assert.Equal(0, recorder.Samples);
            Assert.Null(recorder.Real);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
