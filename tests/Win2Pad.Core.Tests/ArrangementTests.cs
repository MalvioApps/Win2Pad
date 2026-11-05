using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class ArrangementTests
{
    private static readonly ScreenRect Main = new(0, 0, 1920, 1080);
    private static readonly ScreenRect Second = new(1920, 0, 3840, 1080);
    private const int W = 1080, H = 810;

    private static ScreenRect Dropped(int x, int y) => new(x, y, x + W, y + H);

    [Fact]
    public void Dropped_beside_a_monitor_it_snaps_to_that_side_at_the_same_height()
    {
        var placement = Arrangement.Snap([Main], Dropped(2000, 150));
        Assert.Equal(new TabletPlacement(Main, TabletSide.Right, 150, H), placement);
    }

    [Fact]
    public void Dropped_above_it_snaps_on_top()
    {
        var placement = Arrangement.Snap([Main], Dropped(400, -900));
        Assert.Equal(TabletSide.Top, placement!.Side);
        Assert.Equal(400, placement.Offset);
        Assert.Equal(W, placement.Length);
    }

    [Fact]
    public void It_keeps_a_minimum_overlap_when_dropped_far_along()
    {
        // Mostly below the monitor's right edge, but nearer the right side than the bottom.
        var placement = Arrangement.Snap([Main], Dropped(2000, 1000))!;
        Assert.Equal(TabletSide.Right, placement.Side);
        // 15% of the 810-pixel tablet edge stays against the monitor.
        Assert.Equal(1080 - 122, placement.Offset);
        Assert.True(placement.Touches);
    }

    [Fact]
    public void It_never_covers_another_monitor()
    {
        // Between the two monitors there is no room: the shared edge is not a candidate.
        var placement = Arrangement.Snap([Main, Second], Dropped(1500, 100))!;
        Assert.False(placement.Monitor == Main && placement.Side == TabletSide.Right);
        Assert.False(placement.Monitor == Second && placement.Side == TabletSide.Left);
        var rect = Arrangement.RectFor(placement, W, H);
        Assert.False(rect.Left < Second.Right && Second.Left < rect.Right && rect.Top < Second.Bottom && Second.Top < rect.Bottom);
        Assert.False(rect.Left < Main.Right && Main.Left < rect.Right && rect.Top < Main.Bottom && Main.Top < rect.Bottom);
    }

    [Theory]
    [InlineData(TabletSide.Left, -1080, 100, 0, 910)]
    [InlineData(TabletSide.Right, 1920, 100, 3000, 910)]
    [InlineData(TabletSide.Top, 100, -810, 1180, 0)]
    [InlineData(TabletSide.Bottom, 100, 1080, 1180, 1890)]
    public void A_placement_draws_the_tablet_against_its_side(TabletSide side, int l, int t, int r, int b)
    {
        var length = side is TabletSide.Top or TabletSide.Bottom ? W : H;
        Assert.Equal(new ScreenRect(l, t, r, b), Arrangement.RectFor(new TabletPlacement(Main, side, 100, length), W, H));
    }

    [Fact]
    public void Turning_the_tablet_keeps_it_centred_along_the_edge()
    {
        var landscape = new TabletPlacement(Main, TabletSide.Right, 135, 810); // centred on 540
        var portrait = Arrangement.Resize(landscape, 1080);
        Assert.Equal(0, portrait.Offset);
        Assert.Equal(1080, portrait.Length);
        Assert.Same(landscape, Arrangement.Resize(landscape, 810));
    }

    [Fact]
    public void Tablet_size_is_its_screen_in_points()
    {
        Assert.Equal((1080, 810), Arrangement.TabletSize(new PointerCalibration(3349, 2512)));
    }
}
