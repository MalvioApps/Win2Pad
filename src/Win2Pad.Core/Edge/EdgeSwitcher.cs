namespace Win2Pad.Core.Edge;

/// <summary>Switching to the tablet: which monitor, where along its edge, and the moves that place the tablet pointer there.</summary>
public sealed record EdgeEntry(ScreenRect Monitor, double Fraction, IReadOnlyList<PinMove> Moves);

/// <summary>
/// Decides when input crosses between the PC screen and the tablet. While local it watches raw
/// mouse motion for a push against the free edge on the tablet's side; while remote it tracks
/// the tablet pointer with a <see cref="PointerModel"/> and reports where the PC pointer should
/// reappear once the user pushes back out. Thread-safe: the hook thread and the report pump both call in.
/// </summary>
public sealed class EdgeSwitcher
{
    private readonly object _lock = new();
    private readonly IScreens _screens;
    private readonly Func<double> _clock;
    private readonly EdgePush _push;
    private PointerModel? _model;
    private ScreenRect _entryMonitor;
    // The tablet's edge along the monitor side, in desktop pixels: where positions map to and from.
    private int _spanStart, _spanLength;
    private double _returnedAt = double.NegativeInfinity;

    public EdgeSwitcher(EdgeOptions options, IScreens screens, Func<double> clock)
    {
        Options = options;
        _screens = screens;
        _clock = clock;
        _push = new EdgePush(options.EntryPushCounts);
    }

    public EdgeOptions Options { get; }

    /// <summary>Input is going to the tablet (through the edge or a hotkey).</summary>
    public bool IsRemote { get; private set; }

    /// <summary>The tablet pointer is being tracked, so pushing back out can return.</summary>
    public bool IsTracking => _model is not null;

    public bool CanSwitch => Options.Enabled && Options.Calibration.IsValid && !Suspended;

    private static volatile bool _suspended;

    /// <summary>
    /// Set while calibrating: the calibration drives the tablet pointer itself, and the user's
    /// own mouse must not carry input over to the tablet in the middle of it.
    /// </summary>
    public static bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    /// <summary>
    /// Raw motion while input is local. Returns an entry when the user pushed far enough against
    /// the tablet's edge; the caller then hands input to the tablet and sends the entry moves.
    /// </summary>
    public EdgeEntry? OnLocalMotion(int dx, int dy, bool buttonsHeld)
    {
        lock (_lock)
        {
            if (IsRemote || !CanSwitch) return null;

            // Never while dragging: snapping a window to the edge must not throw it at the tablet.
            if (buttonsHeld)
            {
                _push.Reset();
                return null;
            }

            var now = _clock();
            if (now - _returnedAt < Options.ReentryGraceSeconds) return null;

            var (x, y) = _screens.CursorPosition();
            if (_screens.MonitorAt(x, y) is not { } mon) return null;

            var side = Options.EffectiveSide;
            var horizontal = side is TabletSide.Top or TabletSide.Bottom;
            var along = horizontal ? x : y;
            int spanStart, spanLength, crossStart, crossEnd;
            if (Options.Placement is { } placement)
            {
                // Only the monitor the tablet was placed against, and only where they touch.
                if (mon != placement.Monitor || !placement.Touches)
                {
                    _push.Reset();
                    return null;
                }
                (spanStart, spanLength) = (placement.Offset, placement.Length);
                (crossStart, crossEnd) = placement.Overlap;
            }
            else
            {
                spanStart = horizontal ? mon.Left : mon.Top;
                spanLength = horizontal ? mon.Width : mon.Height;
                (crossStart, crossEnd) = (spanStart, spanStart + spanLength);
            }

            var (atEdge, toward) = side switch
            {
                TabletSide.Top => (y <= mon.Top && _screens.MonitorAt(x, mon.Top - 1) is null, -dy),
                TabletSide.Bottom => (y >= mon.Bottom - 1 && _screens.MonitorAt(x, mon.Bottom) is null, dy),
                TabletSide.Left => (x <= mon.Left && _screens.MonitorAt(mon.Left - 1, y) is null, -dx),
                _ => (x >= mon.Right - 1 && _screens.MonitorAt(mon.Right, y) is null, dx)
            };
            atEdge &= along >= crossStart && along < crossEnd;
            if (!_push.Feed(atEdge, toward, now)) return null;

            _push.Reset();
            var fraction = Math.Clamp((along - spanStart) / (double)Math.Max(1, spanLength - 1), 0, 1);

            _model = new PointerModel(Options.Calibration, side, Options.ReturnMargin, Options.AwayFactor);
            var moves = _model.Enter(fraction);
            // The entry moves take two settle pauses before any user motion reaches the tablet.
            var settle = moves.Sum(m => m.SettleMs) / 1000.0;
            _model.ArmAfter(now + settle + Options.ExitGraceSeconds);
            _entryMonitor = mon;
            (_spanStart, _spanLength) = (spanStart, spanLength);
            IsRemote = true;
            return new EdgeEntry(mon, fraction, moves);
        }
    }

    /// <summary>
    /// A move that was sent to the tablet. Returns where the PC pointer should reappear when the
    /// user pushed back out through the edge facing the PC; null to stay on the tablet.
    /// </summary>
    public (int X, int Y)? OnRemoteMotion(int dx, int dy)
    {
        lock (_lock)
        {
            if (!IsRemote || _model is null || (dx == 0 && dy == 0)) return null;
            var now = _clock();
            if (!_model.Move(dx, dy, now)) return null;

            var mon = _entryMonitor;
            var gap = Options.ReturnGapPixels;
            var side = _model.Side;
            var horizontal = side is TabletSide.Top or TabletSide.Bottom;
            // Back to the same place along the tablet's edge; where the tablet overhangs the
            // monitor, the nearest point the monitor still has.
            var along = _spanStart + (int)Math.Round(_model.FractionAlongEdge() * (_spanLength - 1));
            along = horizontal ? Math.Clamp(along, mon.Left, mon.Right - 1) : Math.Clamp(along, mon.Top, mon.Bottom - 1);
            (int X, int Y) point = side switch
            {
                TabletSide.Top => (along, mon.Top + gap),
                TabletSide.Bottom => (along, mon.Bottom - 1 - gap),
                TabletSide.Left => (mon.Left + gap, along),
                _ => (mon.Right - 1 - gap, along)
            };

            GoLocalLocked(now);
            return point;
        }
    }

    /// <summary>
    /// Keeps in step with switches made elsewhere (hotkeys, the UI, a host disconnecting). A
    /// switch to the tablet that did not come through the edge has no known pointer position, so
    /// it is not tracked and only a hotkey brings input back.
    /// </summary>
    public void OnTargetChanged(bool local)
    {
        lock (_lock)
        {
            if (local)
            {
                if (IsRemote) GoLocalLocked(_clock());
                return;
            }

            if (IsRemote) return; // our own entry, already tracked
            IsRemote = true;
            _model = null;
        }
    }

    private void GoLocalLocked(double now)
    {
        IsRemote = false;
        _model = null;
        _push.Reset();
        _returnedAt = now;
    }
}
