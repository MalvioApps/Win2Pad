using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class EdgeSwitcherTests
{
    private sealed class FakeScreens(params ScreenRect[] monitors) : IScreens
    {
        public (int X, int Y) Cursor { get; set; }

        public ScreenRect? MonitorAt(int x, int y)
        {
            foreach (var m in monitors)
                if (x >= m.Left && x < m.Right && y >= m.Top && y < m.Bottom) return m;
            return null;
        }

        public (int X, int Y) CursorPosition() => Cursor;
    }

    private static readonly ScreenRect Main = new(0, 0, 1920, 1080);
    private static readonly ScreenRect LeftMonitor = new(-1920, 0, 0, 1080);

    private double _now = 10;

    private EdgeSwitcher Create(FakeScreens screens, TabletSide side = TabletSide.Right) =>
        new(new EdgeOptions
        {
            Side = side,
            CalibrationWidth = 2000,
            CalibrationHeight = 1500,
            EntryPushCounts = 15,
            ReturnMargin = 0.04
        }, screens, () => _now);

    [Fact]
    public void Pushing_the_free_edge_enters_at_the_same_height()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 270) };
        var switcher = Create(screens);

        Assert.Null(switcher.OnLocalMotion(10, 0, false));
        var entry = switcher.OnLocalMotion(10, 0, false);

        Assert.NotNull(entry);
        Assert.Equal(270 / 1079.0, entry.Fraction, 6);
        Assert.Equal(-PointerModel.PinCounts, entry.Moves[0].Dx); // pinned top-left first
        Assert.True(switcher.IsRemote);
        Assert.True(switcher.IsTracking);
    }

    [Fact]
    public void Touching_the_edge_without_pushing_does_not_enter()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 500) };
        var switcher = Create(screens);
        for (var i = 0; i < 10; i++) Assert.Null(switcher.OnLocalMotion(0, 5, false)); // sliding along it
    }

    [Fact]
    public void An_edge_shared_with_another_monitor_is_not_an_exit()
    {
        var screens = new FakeScreens(Main, LeftMonitor) { Cursor = (0, 500) };
        var switcher = Create(screens, TabletSide.Left);
        Assert.Null(switcher.OnLocalMotion(-50, 0, false));

        // The left monitor's own left edge is free.
        screens.Cursor = (-1920, 500);
        Assert.NotNull(switcher.OnLocalMotion(-50, 0, false));
    }

    [Fact]
    public void Dragging_against_the_edge_does_not_enter()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 500) };
        var switcher = Create(screens);
        Assert.Null(switcher.OnLocalMotion(50, 0, buttonsHeld: true));
        Assert.False(switcher.IsRemote);
    }

    [Fact]
    public void Nothing_happens_without_a_calibration()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 500) };
        var switcher = new EdgeSwitcher(new EdgeOptions(), screens, () => _now);
        Assert.False(switcher.CanSwitch);
        Assert.Null(switcher.OnLocalMotion(500, 0, false));
    }

    [Fact]
    public void Pushing_back_out_returns_at_the_matching_height()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 540) };
        var switcher = Create(screens);
        switcher.OnLocalMotion(20, 0, false);

        _now += 2; // past the settle pauses and the exit grace
        Assert.Null(switcher.OnRemoteMotion(0, -375)); // up a quarter of the tablet
        var back = switcher.OnRemoteMotion(-3000, 0);  // well past the left edge

        Assert.NotNull(back);
        Assert.Equal(1919 - 8, back.Value.X);
        Assert.InRange(back.Value.Y, 260, 280);        // ~0.25 of the screen height
        Assert.False(switcher.IsRemote);
    }

    [Fact]
    public void No_return_during_the_exit_grace()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 540) };
        var switcher = Create(screens);
        switcher.OnLocalMotion(20, 0, false);
        Assert.Null(switcher.OnRemoteMotion(-5000, 0)); // the hand's rebound right after entering
        Assert.True(switcher.IsRemote);
    }

    [Fact]
    public void Re_entry_waits_for_the_grace_after_returning()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 540) };
        var switcher = Create(screens);
        switcher.OnLocalMotion(20, 0, false);
        _now += 2;
        switcher.OnRemoteMotion(-5000, 0);

        Assert.Null(switcher.OnLocalMotion(50, 0, false));
        _now += 1;
        Assert.NotNull(switcher.OnLocalMotion(50, 0, false));
    }

    [Fact]
    public void A_hotkey_switch_is_followed_but_not_tracked()
    {
        var screens = new FakeScreens(Main) { Cursor = (500, 500) };
        var switcher = Create(screens);

        switcher.OnTargetChanged(local: false);
        Assert.True(switcher.IsRemote);
        Assert.False(switcher.IsTracking);
        _now += 5;
        Assert.Null(switcher.OnRemoteMotion(-5000, 0));

        switcher.OnTargetChanged(local: true);
        Assert.False(switcher.IsRemote);
    }

    [Fact]
    public void Its_own_entry_keeps_tracking_when_the_target_change_arrives()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 540) };
        var switcher = Create(screens);
        switcher.OnLocalMotion(20, 0, false);
        switcher.OnTargetChanged(local: false);
        Assert.True(switcher.IsTracking);
    }

    // ---- placement from the arrangement editor ------------------------------------------------

    /// <summary>A 1000-pixel-tall tablet on the right of Main, starting 400 px down: it overhangs the bottom by 320 px.</summary>
    private static readonly TabletPlacement RightLow = new(Main, TabletSide.Right, 400, 1000);

    private EdgeSwitcher CreatePlaced(FakeScreens screens, TabletPlacement placement) =>
        new(new EdgeOptions
        {
            Side = TabletSide.Left, // ignored: the placement decides
            Placement = placement,
            CalibrationWidth = 2000,
            CalibrationHeight = 1500
        }, screens, () => _now);

    [Fact]
    public void A_placed_tablet_is_entered_only_where_it_touches_the_monitor()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 200) }; // above the tablet
        var switcher = CreatePlaced(screens, RightLow);
        Assert.Null(switcher.OnLocalMotion(50, 0, false));

        screens.Cursor = (1919, 900);
        var entry = switcher.OnLocalMotion(50, 0, false);
        Assert.NotNull(entry);
        Assert.Equal((900 - 400) / 999.0, entry.Fraction, 6); // half way down the tablet
    }

    [Fact]
    public void A_placed_tablet_ignores_the_other_monitors()
    {
        var screens = new FakeScreens(Main, LeftMonitor) { Cursor = (-1920, 500) };
        var switcher = CreatePlaced(screens, new TabletPlacement(Main, TabletSide.Right, 0, 1080));
        Assert.Null(switcher.OnLocalMotion(-50, 0, false));
        Assert.Null(switcher.OnLocalMotion(50, 0, false));
    }

    [Fact]
    public void Returning_maps_along_the_tablet_and_clamps_to_the_monitor()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 900) };
        var switcher = CreatePlaced(screens, RightLow);
        switcher.OnLocalMotion(50, 0, false);
        _now += 2;

        // Down to the bottom of the tablet, which is below the bottom of the monitor.
        switcher.OnRemoteMotion(0, 5000);
        var back = switcher.OnRemoteMotion(-5000, 0);
        Assert.Equal((1919 - 8, 1079), back);
    }

    [Fact]
    public void A_placement_that_does_not_touch_its_monitor_never_enters()
    {
        var screens = new FakeScreens(Main) { Cursor = (1919, 500) };
        var switcher = CreatePlaced(screens, new TabletPlacement(Main, TabletSide.Right, 2000, 800));
        Assert.Null(switcher.OnLocalMotion(500, 0, false));
    }

    [Fact]
    public void Placement_overlap_is_the_shared_stretch()
    {
        Assert.Equal((400, 1080), RightLow.Overlap);
        Assert.True(RightLow.Touches);
        var above = new TabletPlacement(Main, TabletSide.Top, -300, 1080);
        Assert.Equal((0, 780), above.Overlap);
    }

    [Fact]
    public void Options_round_trip_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-edge-{Guid.NewGuid():N}.json");
        try
        {
            var options = new EdgeOptions
            {
                Side = TabletSide.Top,
                CalibrationWidth = 3318,
                CalibrationHeight = 2536,
                Placement = new TabletPlacement(new ScreenRect(-1920, 0, 0, 1080), TabletSide.Left, 120, 810)
            };
            Assert.True(options.TrySave(path, out _));
            Assert.Equal(options, EdgeOptions.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unreadable_options_fall_back_to_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-edge-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            Assert.Equal(new EdgeOptions(), EdgeOptions.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
