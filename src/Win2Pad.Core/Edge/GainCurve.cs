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
