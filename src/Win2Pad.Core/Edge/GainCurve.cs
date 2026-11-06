namespace Win2Pad.Core.Edge;

/// <summary>How much further the tablet moves the pointer at one speed than at the slowest, measured.</summary>
public sealed record GainPoint(int CountsPerReport, double Ratio);

/// <summary>
/// The tablet's pointer acceleration, measured by the Safari calibration: for a report of a given
/// size, how many times further the pointer travels than at the slow, unaccelerated speed the
/// screen size was measured at. Lets the pointer model follow fast moves instead of assuming the
/// worst case for all of them.
/// </summary>
public sealed class GainCurve
{
    private readonly GainPoint[] _points;

    public GainCurve(IEnumerable<GainPoint> points)
    {
        _points = points.Where(p => p.CountsPerReport > 0 && p.Ratio > 0)
            .OrderBy(p => p.CountsPerReport).ToArray();
        if (_points.Length == 0) throw new ArgumentException("A gain curve needs at least one point.", nameof(points));
    }

    public IReadOnlyList<GainPoint> Points => _points;

    /// <summary>
    /// The ratio for a report of <paramref name="countsPerReport"/>, interpolated on a log scale
    /// (the speeds are measured in doublings). Below the slowest point the first ratio holds;
    /// above the fastest the last one does, since nothing faster was measured.
    /// </summary>
    public double Ratio(double countsPerReport)
    {
        if (countsPerReport <= _points[0].CountsPerReport) return _points[0].Ratio;
        if (countsPerReport >= _points[^1].CountsPerReport) return _points[^1].Ratio;

        for (var i = 1; i < _points.Length; i++)
        {
            var hi = _points[i];
            if (countsPerReport > hi.CountsPerReport) continue;
            var lo = _points[i - 1];
            var t = (Math.Log2(countsPerReport) - Math.Log2(lo.CountsPerReport))
                    / (Math.Log2(hi.CountsPerReport) - Math.Log2(lo.CountsPerReport));
            return lo.Ratio + t * (hi.Ratio - lo.Ratio);
        }
        return _points[^1].Ratio;
    }

    /// <summary>
    /// How far a run of reports, sent one per <see cref="SpeedSmoother.ReportSeconds"/>, travels in
    /// slow-speed counts, when the tablet accelerates by a speed smoothed with
    /// <paramref name="smoothing"/>.
    /// </summary>
    public double PredictTravel(IEnumerable<int> reports, double smoothing)
    {
        var smoother = new SpeedSmoother(smoothing);
        var travel = 0.0;
        var now = 0.0;
        foreach (var counts in reports)
        {
            travel += counts * Ratio(smoother.Next(Math.Abs(counts), now));
            now += SpeedSmoother.ReportSeconds;
        }
        return travel;
    }

    /// <summary>
    /// The smoothing that best explains measured flicks: each is a run of reports and how far it
    /// really travelled, in slow-speed counts. Picks the value with the least relative error, from
    /// 0.1 (heavily smoothed: acceleration builds up slowly) to 1 (none: every report on its own),
    /// and reports that error as a share.
    /// </summary>
    public (double Smoothing, double Error) FitSmoothing(IReadOnlyList<(int[] Reports, double Travelled)> flicks)
    {
        var best = (Smoothing: 1.0, Error: double.MaxValue);
        for (var step = 2; step <= 20; step++)
        {
            var smoothing = step / 20.0;
            var error = flicks.Average(f => Math.Abs(PredictTravel(f.Reports, smoothing) - f.Travelled) / Math.Max(1, f.Travelled));
            if (error < best.Error - 1e-9) best = (smoothing, error);
        }
        return best;
    }

    /// <summary>A curve from measured gains, normalised so the slowest is 1.</summary>
    public static GainCurve FromGains(IEnumerable<(int CountsPerReport, double PointsPerCount)> gains)
    {
        var list = gains.ToList();
        var slow = list.Min(g => g.PointsPerCount);
        return new GainCurve(list.Select(g => new GainPoint(g.CountsPerReport, Math.Round(g.PointsPerCount / slow, 3))));
    }
}

/// <summary>
/// The measured points as settings store them. A list that compares by content: records holding
/// it (profiles, edge settings) then compare equal when nothing changed, instead of by reference.
/// </summary>
public sealed class GainPoints : List<GainPoint>
{
    public GainPoints() { }

    public GainPoints(IEnumerable<GainPoint> points) : base(points) { }

    public override bool Equals(object? obj) => obj is GainPoints other && this.SequenceEqual(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in this) hash.Add(point);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The speed a tablet accelerates by: not each report's own size but a running average, as
/// iPadOS (like macOS) smooths pointer velocity. At the start of a quick flick the average is
/// still low, so the tablet accelerates less than a steady move at the same speed would.
/// </summary>
public sealed class SpeedSmoother(double smoothing)
{
    /// <summary>The report pacing the curve was measured at.</summary>
    public const double ReportSeconds = 0.015;

    /// <summary>A pause longer than this means the hand stopped; the average starts again from rest.</summary>
    public const double IdleSeconds = 0.06;

    private double _speed;
    private double _last = double.NegativeInfinity;

    /// <summary>1 means no smoothing; smaller values weigh the past more.</summary>
    public double Smoothing { get; } = Math.Clamp(smoothing, 0.05, 1.0);

    public double Next(double speed, double now)
    {
        if (now - _last > IdleSeconds) _speed = 0;
        _last = now;
        _speed = Smoothing * speed + (1 - Smoothing) * _speed;
        return _speed;
    }
}
