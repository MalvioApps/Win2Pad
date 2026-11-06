using Win2Pad.Core.Edge;

namespace Win2Pad.Core.Calibration;

/// <summary>What the calibration page reports about itself, in CSS pixels (iPad points).</summary>
public sealed record PageInfo(int ScreenWidth, int ScreenHeight, int ViewportWidth, int ViewportHeight)
{
    public bool Landscape => ViewportWidth > ViewportHeight;

    // Safari reports screen.width/height in portrait whatever the orientation, so the real
    // extents follow the viewport's shape.
    public int Width => Landscape ? Math.Max(ScreenWidth, ScreenHeight) : Math.Min(ScreenWidth, ScreenHeight);
    public int Height => Landscape ? Math.Min(ScreenWidth, ScreenHeight) : Math.Max(ScreenWidth, ScreenHeight);

    /// <summary>Status bar and Safari's address bar above the page; the pointer is invisible to the page there.</summary>
    public int TopBand => Math.Max(0, Height - ViewportHeight);
}

/// <summary>A pointer position the page saw, in viewport coordinates.</summary>
public readonly record struct PointerSample(long Sequence, double X, double Y);

/// <summary>Where the tablet pointer is, as far as the calibration page can see.</summary>
public interface IPointerFeed
{
    PageInfo? Page { get; }

    /// <summary>The most recent position, or null before the pointer was first seen on the page.</summary>
    PointerSample? Latest { get; }
}

/// <summary>One relative report to the tablet being calibrated.</summary>
public interface ICalibrationMouse
{
    Task MoveAsync(int dx, int dy);

    /// <summary>A left click where the pointer is. A real click, so a web page may go full screen on it.</summary>
    Task ClickAsync() => Task.CompletedTask;
}

/// <summary>Points the tablet pointer moves per HID count, at one speed.</summary>
public readonly record struct GainSample(int CountsPerReport, double PointsPerCount);

public sealed record CalibrationResult(
    int Width,
    int Height,
    double AwayFactor,
    bool Landscape,
    int TopBandPoints,
    IReadOnlyList<GainSample> HorizontalCurve,
    double VerticalGain,
    double? SpeedSmoothing = null,
    double? FlickError = null,
    double? EntryRatio = null,
    IReadOnlyList<GainSample>? LargeMoves = null)
{
    public double SlowGain => HorizontalCurve.Min(s => s.PointsPerCount);

    /// <summary>The whole acceleration curve: steady speeds and single large reports together.</summary>
    public IEnumerable<GainSample> FullCurve =>
        HorizontalCurve.Concat((LargeMoves ?? []).Where(l => HorizontalCurve.All(h => h.CountsPerReport != l.CountsPerReport)));
}

/// <summary>
/// Measures the tablet automatically: it moves the pointer with HID reports and reads where the
/// calibration page saw it land. Each measurement starts from a pin against the left edge (a
/// known x) and moves right at a constant speed, so the points travelled per count give the gain
/// at that speed. The slowest gain gives the screen size in counts; the ratio of the fastest to
/// the slowest gives the worst-case acceleration the pointer model must assume.
/// </summary>
public sealed class TabletCalibrator(ICalibrationMouse mouse, IPointerFeed feed, Func<int, Task> delay)
{
    public const int ReportIntervalMs = 15;
    public const int SettleMs = 250;

    /// <summary>Time for Safari to animate into full screen and the page to report its new size.</summary>
    public const int FullScreenMs = 1200;
    private const int Pin = 32767;
    private const int MaxReports = 2500;

    /// <summary>Counts per report for the gain curve; 2 is about 130 counts/s, slow enough to be unaccelerated.</summary>
    public static readonly int[] Speeds = [2, 4, 8, 16, 32];

    /// <summary>Share of the viewport each measurement travels: long enough for 1 px to be under 0.5%.</summary>
    private const double Travel = 0.3;

    /// <summary>
    /// Quick gestures like a hand makes: speeding up and slowing down, a burst, a fast start.
    /// How far each really goes, against the steady-speed curve, shows how the tablet smooths
    /// speed before accelerating.
    /// </summary>
    /// <summary>
    /// Sizes of the single large reports measured. The tablet keeps accelerating well past the
    /// steady-speed curve's 32 counts (about 11x for 100 counts on an iPad 7), and real use
    /// produces such reports: motion that piles up during the entry pauses, or a fast hand.
    /// Small enough that none reaches the right edge from the left.
    /// </summary>
    public static readonly int[] EntryMoves = [48, 64, 96, 128, 192];

    public static readonly int[][] Flicks =
    [
        [2, 4, 8, 16, 32, 32, 16, 8, 4, 2],
        [32, 32, 32, 32, 32, 32],
        [4, 8, 16, 24, 32, 32],
        [16, 16, 2, 2, 16, 16],
    ];

    public event Action<string, double>? Progress;

    public async Task<CalibrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var page = feed.Page ?? throw new CalibrationException("The calibration page is not open on the tablet.");
        int vw = page.ViewportWidth, vh = page.ViewportHeight;
        var steps = Speeds.Length + 4;

        // 1. Into the page: pin top-left (inside the status bar, where the page cannot see it),
        //    then step down slowly until the page reports the pointer, and on to mid-height.
        Report("Finding the pointer", 0);
        await mouse.MoveAsync(-Pin, -Pin);
        await delay(SettleMs);
        await MoveUntilAsync(0, 4, s => s.Y >= 0.45 * vh, cancellationToken,
            "The page never saw the pointer. Keep Safari in front, the page visible and do not touch the tablet.");

        // Full screen hides Safari's bars, so the measurements use the whole screen and the page
        // sees the top. A page may only go full screen on a click: give it one. The viewport
        // changes, so the page is read again; if it stays as it was, calibration goes on as is.
        if (page.TopBand > 0)
        {
            Report("Going full screen", 0.02);
            await mouse.ClickAsync();
            await delay(FullScreenMs);
            page = feed.Page ?? page;
            (vw, vh) = (page.ViewportWidth, page.ViewportHeight);
        }

        // 2. Horizontal gain at each speed.
        var curve = new List<GainSample>();
        for (var i = 0; i < Speeds.Length; i++)
        {
            var speed = Speeds[i];
            Report($"Measuring speed {i + 1} of {Speeds.Length}", (i + 1.0) / steps);
            await mouse.MoveAsync(-Pin, 0);
            await delay(SettleMs);
            var start = Current("lost the pointer while pinning it left");
            var counts = await MoveUntilAsync(speed, 0, s => s.X >= start.X + Travel * vw, cancellationToken,
                "The pointer stopped moving across the page.");
            await delay(SettleMs);
            var end = Current("lost the pointer while measuring");
            if (end.X >= vw - 2) throw new CalibrationException("The pointer reached the right edge during a measurement.");
            curve.Add(new GainSample(speed, (end.X - start.X) / counts));
        }

        // 3. Vertical gain at the slowest speed, downwards from mid-height.
        Report("Measuring vertically", (Speeds.Length + 1.0) / steps);
        var top = Current("lost the pointer");
        var vertical = await MoveUntilAsync(0, Speeds[0], s => s.Y >= top.Y + Travel * vh, cancellationToken,
            "The pointer stopped moving down the page.");
        await delay(SettleMs);
        var bottom = Current("lost the pointer while measuring vertically");
        var verticalGain = (bottom.Y - top.Y) / vertical;

        var slow = curve.Min(s => s.PointsPerCount);
        var fast = curve.Max(s => s.PointsPerCount);
        if (slow <= 0 || verticalGain <= 0) throw new CalibrationException("The measured movement was not plausible.");

        // 4. Quick gestures, to learn how the tablet smooths speed before accelerating.
        Report("Measuring quick movements", (Speeds.Length + 2.0) / steps);
        var flicks = new List<(int[] Reports, double Travelled)>();
        foreach (var flick in Flicks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await mouse.MoveAsync(-Pin, 0);
            await delay(SettleMs);
            var start = Current("lost the pointer before a quick movement");
            foreach (var counts in flick)
            {
                await mouse.MoveAsync(counts, 0);
                await delay(ReportIntervalMs);
            }
            await delay(SettleMs);
            var end = Current("lost the pointer during a quick movement");
            // A flick that hit the right edge says nothing about how far it would have gone.
            if (end.X < vw - 2) flicks.Add((flick, (end.X - start.X) / slow));
        }
        var gainCurve = GainCurve.FromGains(curve.Select(g => (g.CountsPerReport, g.PointsPerCount)));
        (double Smoothing, double Error)? fit = flicks.Count >= 2 ? gainCurve.FitSmoothing(flicks) : null;

        // 5. Single large moves after a pause: the entry places the pointer with one of these.
        Report("Measuring a single large move", (Speeds.Length + 3.0) / steps);
        var entryRatios = new List<double>();
        var largeMoves = new List<GainSample>();
        foreach (var size in EntryMoves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await mouse.MoveAsync(-Pin, 0);
            await delay(SettleMs);
            var start = Current("lost the pointer before a large move");
            await mouse.MoveAsync(size, 0);
            await delay(SettleMs);
            var end = Current("lost the pointer after a large move");
            if (end.X >= vw - 2) continue;
            entryRatios.Add((end.X - start.X) / (size * slow));
            largeMoves.Add(new GainSample(size, (end.X - start.X) / size));
        }
        double? entryRatio = entryRatios.Count > 0 ? Math.Round(entryRatios.Average(), 3) : null;

        // A little headroom over the fastest measured gain: real hand movements can be faster still.
        var away = Math.Clamp(fast / slow * 1.15, 1.0, 6.0);
        Report("Done", 1);
        return new CalibrationResult(
            (int)Math.Round(page.Width / slow),
            (int)Math.Round(page.Height / verticalGain),
            Math.Round(away, 2),
            page.Landscape,
            page.TopBand,
            curve,
            verticalGain,
            fit?.Smoothing,
            fit is { } f ? Math.Round(f.Error, 3) : null,
            entryRatio,
            largeMoves);
    }

    /// <summary>
    /// Sends reports at a steady pace until the page reports what was wanted; returns the counts
    /// sent along the move. Only positions reported after the move started count, so a stale
    /// sample from before a pin cannot end it early.
    /// </summary>
    private async Task<int> MoveUntilAsync(int dx, int dy, Func<PointerSample, bool> done,
        CancellationToken cancellationToken, string failure)
    {
        var since = feed.Latest?.Sequence ?? -1;
        var sent = 0;
        for (var reports = 0; reports < MaxReports; reports++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (feed.Latest is { } sample && sample.Sequence > since && done(sample)) return sent;
            await mouse.MoveAsync(dx, dy);
            sent += Math.Abs(dx) + Math.Abs(dy);
            await delay(ReportIntervalMs);
        }
        throw new CalibrationException(failure);
    }

    private PointerSample Current(string what) =>
        feed.Latest ?? throw new CalibrationException($"Calibration {what}.");

    private void Report(string step, double fraction) => Progress?.Invoke(step, fraction);
}

public sealed class CalibrationException(string message) : Exception(message);
