using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Win2Pad.Core.Edge;

/// <summary>A monitor in physical screen pixels; Right and Bottom are exclusive.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore]
    public int Width => Right - Left;
    [JsonIgnore]
    public int Height => Bottom - Top;
}

/// <summary>The monitor queries edge switching needs, so the logic can be tested without real screens.</summary>
public interface IScreens
{
    /// <summary>The monitor containing the point, or null when the point is on no monitor.</summary>
    ScreenRect? MonitorAt(int x, int y);

    (int X, int Y) CursorPosition();
}

/// <summary>Real monitors. Coordinates are physical pixels, which needs a per-monitor DPI-aware process.</summary>
public sealed class Win32Screens : IScreens
{
    private const uint MONITOR_DEFAULTTONULL = 0;

    public ScreenRect? MonitorAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { x = x, y = y }, MONITOR_DEFAULTTONULL);
        if (monitor == IntPtr.Zero) return null;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        return new ScreenRect(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
    }

    public (int X, int Y) CursorPosition() => GetCursorPos(out var p) ? (p.x, p.y) : (0, 0);

    public static void SetCursor(int x, int y) => SetCursorPos(x, y);

    /// <summary>Every monitor, primary first, in physical pixels.</summary>
    public static IReadOnlyList<(ScreenRect Bounds, bool Primary)> All()
    {
        var monitors = new List<(ScreenRect, bool)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add((new ScreenRect(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors.OrderByDescending(m => m.Item2).ThenBy(m => m.Item1.Left).ThenBy(m => m.Item1.Top).ToList();
    }

    private const uint MONITORINFOF_PRIMARY = 1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
