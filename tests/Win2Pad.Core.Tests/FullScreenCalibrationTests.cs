using Win2Pad.Core.Calibration;
using Xunit;

namespace Win2Pad.Core.Tests;

/// <summary>A page may only go full screen on a click, so the calibration gives it one.</summary>
public sealed class FullScreenCalibrationTests
{
    /// <summary>An iPad in Safari whose page goes full screen (its 70-point bars gone) when clicked.</summary>
    private sealed class FullScreenTablet(bool startFull) : ICalibrationMouse, IPointerFeed
    {
        private const int W = 1080, H = 810;
        private long _sequence;
        private double _x = 500, _y = 400;
        private int _band = startFull ? 0 : 70;

        public int Clicks { get; private set; }
        public PageInfo? Page => new(810, 1080, W, H - _band);
        public PointerSample? Latest { get; private set; }

        public Task MoveAsync(int dx, int dy)
        {
            var speed = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var gain = 0.325 * (speed <= 4 ? 1 : Math.Min(2.2, 1 + (speed - 4) / 20.0));
            var nx = Math.Clamp(_x + dx * gain, 0, W - 1);
            var ny = Math.Clamp(_y + dy * gain, 0, H - 1);
            var moved = nx != _x || ny != _y;
            (_x, _y) = (nx, ny);
            if (moved && _y >= _band) Latest = new PointerSample(++_sequence, _x, _y - _band);
            return Task.CompletedTask;
        }

        public Task ClickAsync()
        {
            Clicks++;
            _band = 0; // the bars are gone: the page now covers the whole screen
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task It_clicks_once_to_go_full_screen_and_measures_the_whole_screen()
    {
        var tablet = new FullScreenTablet(startFull: false);
        var result = await new TabletCalibrator(tablet, tablet, _ => Task.CompletedTask).RunAsync();

        Assert.Equal(1, tablet.Clicks);
        Assert.Equal(0, result.TopBandPoints);
        Assert.InRange(result.Width, 3300, 3345);   // 1080 / 0.325
        Assert.InRange(result.Height, 2470, 2515);  // 810 / 0.325
    }

    [Fact]
    public async Task A_page_already_full_screen_is_not_clicked()
    {
        var tablet = new FullScreenTablet(startFull: true);
        await new TabletCalibrator(tablet, tablet, _ => Task.CompletedTask).RunAsync();
        Assert.Equal(0, tablet.Clicks);
    }
}
