using Win2Pad.Core.Devices;

namespace Win2Pad.Core.Edge;

/// <summary>
/// The geometry behind the arrangement editor: the tablet drawn next to the monitors in desktop
/// pixels, snapped against a side of one of them like Windows' display arrangement does.
/// </summary>
public static class Arrangement
{
    /// <summary>Least share of the shorter edge two screens must share, so a placement is usable.</summary>
    public const double MinOverlapShare = 0.15;

    /// <summary>
    /// The tablet's size in the arrangement, in desktop pixels. A tablet point is drawn as one
    /// pixel: close to the real size next to a typical monitor, which is what makes the picture
    /// read right.
    /// </summary>
    public static (int Width, int Height) TabletSize(PointerCalibration calibration) =>
        ((int)Math.Round(calibration.Width * TabletModels.DefaultPointsPerCount),
         (int)Math.Round(calibration.Height * TabletModels.DefaultPointsPerCount));

    /// <summary>Where a placement puts a tablet of the given size.</summary>
    public static ScreenRect RectFor(TabletPlacement placement, int width, int height)
    {
        var m = placement.Monitor;
        return placement.Side switch
        {
            TabletSide.Left => new(m.Left - width, placement.Offset, m.Left, placement.Offset + height),
            TabletSide.Right => new(m.Right, placement.Offset, m.Right + width, placement.Offset + height),
            TabletSide.Top => new(placement.Offset, m.Top - height, placement.Offset + width, m.Top),
            _ => new(placement.Offset, m.Bottom, placement.Offset + width, m.Bottom + height)
        };
    }

    /// <summary>
    /// The placement nearest to where the tablet was dropped: against any side of any monitor,
    /// sharing at least <see cref="MinOverlapShare"/> of the edge, and covering no monitor.
    /// Null only when the monitors leave no room at all.
    /// </summary>
    public static TabletPlacement? Snap(IReadOnlyList<ScreenRect> monitors, ScreenRect dropped)
    {
        int w = dropped.Width, h = dropped.Height;
        var cx = (dropped.Left + dropped.Right) / 2.0;
        var cy = (dropped.Top + dropped.Bottom) / 2.0;

        TabletPlacement? best = null;
        var bestDistance = double.MaxValue;
        foreach (var m in monitors)
        {
            foreach (var side in Enum.GetValues<TabletSide>())
            {
                var horizontal = side is TabletSide.Top or TabletSide.Bottom;
                var length = horizontal ? w : h;
                var edge = horizontal ? m.Width : m.Height;
                var min = Math.Max(1, (int)Math.Round(Math.Min(length, edge) * MinOverlapShare));
                var wanted = horizontal ? dropped.Left : dropped.Top;
                var start = horizontal ? m.Left : m.Top;
                var offset = Math.Clamp(wanted, start - length + min, start + edge - min);

                var placement = new TabletPlacement(m, side, offset, length);
                var rect = RectFor(placement, w, h);
                if (monitors.Any(other => Intersects(rect, other))) continue;

                var dx = (rect.Left + rect.Right) / 2.0 - cx;
                var dy = (rect.Top + rect.Bottom) / 2.0 - cy;
                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance) continue;
                best = placement;
                bestDistance = distance;
            }
        }
        return best is null ? null : Magnet(monitors, best, w, h);
    }

    /// <summary>How close to an aligned position, as a share of the monitor's edge, pulls the tablet onto it.</summary>
    public const double MagnetShare = 0.06;

    /// <summary>
    /// Pulls a placement onto the nearest aligned position along its edge when it was dropped
    /// close to one: flush with the start or end of its monitor, centred on it, or flush with the
    /// start or end of the run of monitors on that line. Dropped further away, it stays put.
    /// </summary>
    public static TabletPlacement Magnet(IReadOnlyList<ScreenRect> monitors, TabletPlacement placement, int width, int height)
    {
        var start = placement.MonitorStart;
        var end = placement.MonitorEnd;
        var length = placement.Length;
        var (spanStart, spanEnd) = LineSpan(monitors, placement);
        int[] targets =
        [
            start, end - length, start + (end - start - length) / 2,
            spanStart, spanEnd - length, spanStart + (spanEnd - spanStart - length) / 2
        ];

        var reach = (end - start) * MagnetShare;
        var best = placement;
        var bestDistance = reach;
        foreach (var target in targets)
        {
            var distance = Math.Abs(target - placement.Offset);
            if (distance > bestDistance) continue;
            var candidate = placement with { Offset = target };
            if (!candidate.Touches) continue;
            var rect = RectFor(candidate, width, height);
            if (monitors.Any(other => Intersects(rect, other))) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>
    /// The same placement at a new length along the edge (the tablet turned round, was
    /// recalibrated, or the user changed how much edge it shares): it stays centred where it was,
    /// still sharing part of the edge.
    /// </summary>
    public static TabletPlacement Resize(TabletPlacement placement, int newLength)
    {
        if (newLength == placement.Length || newLength <= 0) return placement;
        var centre = placement.Offset + placement.Length / 2;
        var offset = centre - newLength / 2;
        var edge = placement.MonitorEnd - placement.MonitorStart;
        var min = Math.Max(1, (int)Math.Round(Math.Min(newLength, edge) * MinOverlapShare));
        offset = Math.Clamp(offset, placement.MonitorStart - newLength + min, placement.MonitorEnd - min);
        return placement with { Offset = offset, Length = newLength };
    }

    /// <summary>The placement for a tablet whose natural length along the edge is <paramref name="naturalLength"/>, at its chosen scale.</summary>
    public static TabletPlacement Rescale(TabletPlacement placement, int naturalLength) =>
        Resize(placement, (int)Math.Round(naturalLength * placement.Scale));

    /// <summary>
    /// The longest edge the tablet can share from where it is placed: the run of monitors side by
    /// side whose edge on that side lies on the same line, without gaps. One monitor gives its own
    /// edge; two side by side give both.
    /// </summary>
    public static (int Start, int End) LineSpan(IReadOnlyList<ScreenRect> monitors, TabletPlacement placement)
    {
        var horizontal = placement.Side is TabletSide.Top or TabletSide.Bottom;
        var onLine = monitors
            .Where(m => TabletPlacement.EdgeLine(m, placement.Side) == placement.Line)
            .Select(m => horizontal ? (Start: m.Left, End: m.Right) : (Start: m.Top, End: m.Bottom))
            .ToList();
        var (start, end) = horizontal
            ? (placement.Monitor.Left, placement.Monitor.Right)
            : (placement.Monitor.Top, placement.Monitor.Bottom);
        bool grew;
        do
        {
            grew = false;
            foreach (var (s, e) in onLine)
            {
                if (s <= end && e > end) { end = e; grew = true; }
                if (e >= start && s < start) { start = s; grew = true; }
            }
        } while (grew);
        return (start, end);
    }

    private static bool Intersects(ScreenRect a, ScreenRect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
