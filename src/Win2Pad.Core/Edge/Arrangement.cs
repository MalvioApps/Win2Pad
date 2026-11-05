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
        return best;
    }

    /// <summary>
    /// The same placement for a tablet that changed size (turned round, recalibrated): it stays
    /// centred where it was along the edge, still sharing part of it.
    /// </summary>
    public static TabletPlacement Resize(TabletPlacement placement, int newLength)
    {
        if (newLength == placement.Length) return placement;
        var centre = placement.Offset + placement.Length / 2;
        var offset = centre - newLength / 2;
        var edge = placement.MonitorEnd - placement.MonitorStart;
        var min = Math.Max(1, (int)Math.Round(Math.Min(newLength, edge) * MinOverlapShare));
        offset = Math.Clamp(offset, placement.MonitorStart - newLength + min, placement.MonitorEnd - min);
        return placement with { Offset = offset, Length = newLength };
    }

    private static bool Intersects(ScreenRect a, ScreenRect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
