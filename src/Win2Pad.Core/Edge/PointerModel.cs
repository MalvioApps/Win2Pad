// Ported from BlueShare (https://github.com/giagar5/BlueShare, commit 0a26a12, blueshare/pointer.py),
// licensed under the Apache License 2.0. Changes: translated to C#; only the return edge is modelled
// (side exits to neighbouring monitors are not ported yet); time is passed in seconds by the caller.

namespace Win2Pad.Core.Edge;

/// <summary>Where the tablet sits relative to the PC screen it is attached to.</summary>
public enum TabletSide { Left, Right, Top, Bottom }

/// <summary>The tablet screen size in HID mouse counts, measured at a slow, unaccelerated speed.</summary>
public readonly record struct PointerCalibration(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>One relative move sent to the tablet, followed by a pause.</summary>
public readonly record struct PinMove(int Dx, int Dy, int SettleMs);

/// <summary>
/// Estimates the tablet pointer position from the relative moves sent to it. The tablet clamps its
/// pointer at the screen borders, and the model mirrors that. The calibration measures the screen
/// at the minimum gain, so faster moves may travel further on the tablet than the model thinks.
/// The model is therefore pessimistic towards the return edge: moves away from it are multiplied
/// by <see cref="AwayFactor"/>, moves towards it are not. The estimate never reaches the return
/// edge before the real pointer does, so the user is never pulled back early.
/// With a measured <see cref="GainCurve"/> the model follows the tablet's acceleration instead,
/// and only a little headroom is needed. It must then be fed the reports actually sent to the
/// tablet, because the acceleration depends on the size of each report.
/// </summary>
public sealed class PointerModel
{
    /// <summary>One report this large drives the pointer into a corner whatever the acceleration.</summary>
    public const int PinCounts = 32767;

    /// <summary>Pause after a pin so the tablet's velocity tracking forgets it and the next move is not accelerated.</summary>
    public const int SettleMs = 150;

    private double _lastPush = double.NegativeInfinity;
    private double _armedAt = double.NegativeInfinity;

    /// <param name="returnLead">
    /// Share of the tablet before the return edge where moving towards it quickly already returns,
    /// so the tablet's own edge gestures (notifications, Control Center, Dock) never get the time
    /// to open. Approaching slowly still reaches the edge.
    /// </param>
    /// <param name="leadMinSpeed">Counts per report towards the edge that count as moving quickly.</param>
    public PointerModel(PointerCalibration calibration, TabletSide side, double returnMargin = 0.04,
        double awayFactor = 2.4, double overshootResetSeconds = 0.4, double entryInset = 0.02,
        GainCurve? gainCurve = null, double returnLead = 0, int leadMinSpeed = 4)
    {
        GainCurve = gainCurve;
        ReturnLead = Math.Max(0, returnLead);
        LeadMinSpeed = leadMinSpeed;
        if (!calibration.IsValid) throw new ArgumentException("Calibration must be positive.", nameof(calibration));
        Calibration = calibration;
        Side = side;
        ReturnMargin = returnMargin;
        AwayFactor = Math.Max(1.0, awayFactor);
        OvershootResetSeconds = overshootResetSeconds;
        EntryInset = entryInset;
    }

    public PointerCalibration Calibration { get; }
    public TabletSide Side { get; }
    public double ReturnMargin { get; }
    public double AwayFactor { get; }
    public double OvershootResetSeconds { get; }
    public double EntryInset { get; }
    public GainCurve? GainCurve { get; }
    public double ReturnLead { get; }
    public int LeadMinSpeed { get; }

    /// <summary>The early-return zone in counts.</summary>
    public int ReturnLeadCounts => (int)Math.Round(ReturnLead * ReturnExtent);

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Overshoot { get; private set; }

    private bool Horizontal => Side is TabletSide.Top or TabletSide.Bottom;

    /// <summary>Tablet extent across the return edge, in counts.</summary>
    private int ReturnExtent => Horizontal ? Calibration.Height : Calibration.Width;

    public int MarginCounts => Math.Max(1, (int)Math.Round(ReturnMargin * ReturnExtent));

    /// <summary>With a negative margin: how far before the edge the return already triggers.</summary>
    public int LeadCounts => Math.Max(0, (int)Math.Round(-ReturnMargin * ReturnExtent));

    /// <summary>
    /// Ignores the return until <paramref name="when"/>: right after entering, the hand's rebound
    /// from the push must not bounce the pointer straight back out.
    /// </summary>
    public void ArmAfter(double when) => _armedAt = when;

    /// <summary>Sets a known pointer position, e.g. after pinning it into a corner.</summary>
    public void Place(double x, double y)
    {
        X = x;
        Y = y;
        Overshoot = 0;
    }

    /// <summary>
    /// Places the pointer just inside the edge facing the PC, at <paramref name="fraction"/> along it.
    /// Returns the moves that do it on the tablet: pin into the corner of that edge nearest to the
    /// target, then move along the edge. Each move is followed by a pause so neither is accelerated.
    /// The nearest corner keeps the visible travel along the edge to half the screen at most.
    /// </summary>
    public IReadOnlyList<PinMove> Enter(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        int w = Calibration.Width, h = Calibration.Height;
        var fromStart = fraction <= 0.5; // pin to the top (or left) corner rather than the other one
        PinMove pin, along;
        double x, y;
        switch (Side)
        {
            case TabletSide.Top: // tablet above the PC: enter through its bottom edge
            case TabletSide.Bottom:
            {
                var bottom = Side == TabletSide.Top;
                var inset = Math.Max(1, (int)Math.Round(EntryInset * h));
                x = Math.Round(fraction * w);
                y = bottom ? h - inset : inset;
                pin = new(fromStart ? -PinCounts : PinCounts, bottom ? PinCounts : -PinCounts, SettleMs);
                along = new(fromStart ? (int)x : (int)x - w, bottom ? -inset : inset, SettleMs);
                break;
            }
            default: // tablet beside the PC: enter through its right edge (Left) or left edge (Right)
            {
                var right = Side == TabletSide.Left;
                var inset = Math.Max(1, (int)Math.Round(EntryInset * w));
                x = right ? w - inset : inset;
                y = Math.Round(fraction * h);
                pin = new(right ? PinCounts : -PinCounts, fromStart ? -PinCounts : PinCounts, SettleMs);
                along = new(right ? -inset : inset, fromStart ? (int)y : (int)y - h, SettleMs);
                break;
            }
        }

        Place(x, y);
        return [pin, along];
    }

    /// <summary>Where the pointer is along the return edge, from 0 to 1.</summary>
    public double FractionAlongEdge() => Horizontal ? X / Calibration.Width : Y / Calibration.Height;

    /// <summary>Applies one move sent to the tablet; true when the user pushed past the return edge.</summary>
    public bool Move(int dx, int dy, double now)
    {
        // Scale the report by the tablet's acceleration at its speed, so the estimate travels as
        // far as the real pointer did.
        var speed = Math.Sqrt((double)dx * dx + (double)dy * dy);
        var ratio = GainCurve?.Ratio(speed) ?? 1.0;
        var returned = ReturnMove(dx * ratio, dy * ratio, speed, now);
        if (now < _armedAt)
        {
            Overshoot = 0;
            return false;
        }
        return returned;
    }

    private bool ReturnMove(double dx, double dy, double speed, double now)
    {
        int w = Calibration.Width, h = Calibration.Height;
        double ex = dx, ey = dy;
        if (Side == TabletSide.Top && dy < 0 || Side == TabletSide.Bottom && dy > 0) ey *= AwayFactor;
        else if (Side == TabletSide.Left && dx < 0 || Side == TabletSide.Right && dx > 0) ex *= AwayFactor;

        double nx = X + ex, ny = Y + ey;
        var beyond = Side switch
        {
            TabletSide.Top => ny - h,
            TabletSide.Bottom => -ny,
            TabletSide.Left => nx - w,
            _ => -nx
        };
        var atEdge = Side switch
        {
            TabletSide.Top => Y >= h,
            TabletSide.Bottom => Y <= 0,
            TabletSide.Left => X >= w,
            _ => X <= 0
        };
        X = Math.Clamp(nx, 0, w);
        Y = Math.Clamp(ny, 0, h);

        // Heading for the edge quickly and already close to it: return now, before the tablet
        // can open what it shows at that edge.
        if (ReturnLeadCounts > 0)
        {
            var (toward, distance) = Side switch
            {
                TabletSide.Top => (dy, h - Y),
                TabletSide.Bottom => (-dy, Y),
                TabletSide.Left => (dx, w - X),
                _ => (-dx, X)
            };
            if (toward > 0 && speed >= LeadMinSpeed && distance <= ReturnLeadCounts)
            {
                Overshoot = 0;
                return true;
            }
        }

        if (LeadCounts > 0)
        {
            var (toward, distance) = Side switch
            {
                TabletSide.Top => (dy, h - Y),
                TabletSide.Bottom => (-dy, Y),
                TabletSide.Left => (dx, w - X),
                _ => (-dx, X)
            };
            return toward > 0 && distance <= LeadCounts;
        }

        if (beyond > 0)
        {
            var keep = atEdge && now - _lastPush <= OvershootResetSeconds;
            Overshoot = (keep ? Overshoot : 0) + beyond;
            _lastPush = now;
        }
        else if (beyond < 0 || !atEdge)
        {
            Overshoot = 0;
        }
        return Overshoot > MarginCounts;
    }
}

/// <summary>Detects a deliberate push against a PC screen edge, not just touching it.</summary>
public sealed class EdgePush(int thresholdCounts, double resetSeconds = 0.5)
{
    private double _last;

    public int Pushed { get; private set; }

    public void Reset() => Pushed = 0;

    /// <summary>
    /// <paramref name="towardEdge"/> is the movement component towards the edge (positive =
    /// pushing). Small moves away from the edge (jitter) do not reset the count: really moving
    /// away takes the pointer off the edge, and that does.
    /// </summary>
    public bool Feed(bool atEdge, int towardEdge, double now)
    {
        if (!atEdge || now - _last > resetSeconds) Pushed = 0;
        _last = now;
        if (atEdge && towardEdge > 0) Pushed += towardEdge;
        return Pushed >= thresholdCounts;
    }
}
