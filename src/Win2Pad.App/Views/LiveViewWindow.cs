using System.Windows;
using System.Windows.Controls;
using Win2Pad.Core;
using Win2Pad.Core.Calibration;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Win2Pad.Core.Edge;

namespace Win2Pad.App.Views;

/// <summary>
/// Shows, live, where Win2Pad believes the iPad pointer is: the iPad screen, the estimate and its
/// recent trail, the band near the way back where returning is not armed yet, and how far the
/// current push past the edge has got. Put it beside the iPad to see whether the two agree.
/// </summary>
public sealed class LiveViewWindow : Window
{
    private const int TrailLength = 90;

    private readonly Canvas _board = new() { ClipToBounds = true };
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 8, 12, 4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _numbers = new()
    {
        Margin = new Thickness(12, 0, 12, 10), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Opacity = 0.8
    };
    private readonly Queue<Point> _trail = new();
    private readonly DispatcherTimer _timer;
    private bool _wasRemote;

    // Comparison with the real pointer, seen by the Safari page.
    private readonly Button _compareButton = new() { Content = "Compare with the iPad", Padding = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock _compareText = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Image _qr = new() { Width = 150, Height = 150, Margin = new Thickness(12, 6, 12, 0), Visibility = Visibility.Collapsed };
    private CalibrationServer? _server;
    private AccuracyRecorder? _recorder;
    private DateTime _lastReal;

    public LiveViewWindow()
    {
        Title = "Win2Pad live view";
        Width = 560;
        Height = 700;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        Foreground = Brushes.White;

        var compareRow = new DockPanel { Margin = new Thickness(12, 10, 12, 0) };
        DockPanel.SetDock(_compareButton, Dock.Left);
        compareRow.Children.Add(_compareButton);
        compareRow.Children.Add(_compareText);
        _compareButton.Click += (_, _) => ToggleCompare();
        _compareText.Text = "Shows where the pointer really is, from a page in Safari, and records both for analysis.";

        var layout = new DockPanel();
        DockPanel.SetDock(compareRow, Dock.Top);
        DockPanel.SetDock(_qr, Dock.Top);
        layout.Children.Add(compareRow);
        layout.Children.Add(_qr);
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(_numbers, Dock.Bottom);
        layout.Children.Add(_status);
        layout.Children.Add(_numbers);
        layout.Children.Add(new Border { Margin = new Thickness(12, 4, 12, 8), Child = _board });
        Content = layout;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Render, (_, _) => Draw(), Dispatcher);
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            StopCompare();
        };
    }

    private void ToggleCompare()
    {
        if (_recorder is not null)
        {
            StopCompare();
            return;
        }

        try
        {
            _server = CalibrationServer.Start();
        }
        catch (Exception ex)
        {
            _compareText.Text = $"Cannot start the comparison page: {ex.Message}";
            return;
        }

        var path = AppPaths.InLogs($"compare-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var server = _server;
        _recorder = new AccuracyRecorder(path, () => server.Page,
            () => EdgeSwitcher.Current?.Options.Calibration ?? new PointerCalibration(1, 1));
        var recorder = _recorder;
        server.Sampled += sample =>
        {
            recorder.OnSample(sample, EdgeSwitcher.Current?.Snapshot());
            _lastReal = DateTime.UtcNow;
        };
        EdgeSwitcher.Tracked += recorder.OnReport;

        var url = server.UrlFor("track");
        _qr.Source = CalibrationWindow.QrCode(url);
        _qr.Visibility = Visibility.Visible;
        _compareButton.Content = "Stop comparing";
        _compareText.Text = $"Scan with the iPad, keep Safari in front and use the mouse over the page. Recording to {path}";
    }

    private void StopCompare()
    {
        if (_recorder is { } recorder)
        {
            EdgeSwitcher.Tracked -= recorder.OnReport;
            recorder.Dispose();
            _compareText.Text = $"Saved {recorder.Samples} real positions to {recorder.Path}";
        }
        _recorder = null;
        _ = _server?.DisposeAsync().AsTask();
        _server = null;
        _qr.Visibility = Visibility.Collapsed;
        _compareButton.Content = "Compare with the iPad";
    }

    private void Draw()
    {
        _board.Children.Clear();
        var switcher = EdgeSwitcher.Current;
        if (switcher is null)
        {
            _status.Text = "Edge switching is not running. Start Win2Pad and turn it on in Screens.";
            _numbers.Text = "";
            return;
        }

        var live = switcher.Snapshot();
        if (live.Width <= 0 || live.Height <= 0 || _board.ActualWidth < 20) return;

        // The iPad screen, fitted to the window with its shape kept.
        var scale = Math.Min(_board.ActualWidth / live.Width, _board.ActualHeight / live.Height);
        double w = live.Width * scale, h = live.Height * scale;
        double left = (_board.ActualWidth - w) / 2, top = (_board.ActualHeight - h) / 2;
        Add(new Rectangle
        {
            Width = w, Height = h, RadiusX = 10, RadiusY = 10,
            Fill = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), StrokeThickness = 1
        }, left, top);

        // The way back: the edge facing the PC, and the band beside it where returning is not
        // armed until the pointer has been further in.
        var vertical = live.Side is TabletSide.Top or TabletSide.Bottom;
        var arm = live.ArmCounts * scale;
        var (edgeX, edgeY, edgeW, edgeH) = live.Side switch
        {
            TabletSide.Bottom => (left, top, w, 4.0),          // tablet below the PC: back through its top
            TabletSide.Top => (left, top + h - 4, w, 4.0),
            TabletSide.Right => (left, top, 4.0, h),           // tablet right of the PC: back through its left
            _ => (left + w - 4, top, 4.0, h)
        };
        if (arm > 0)
        {
            var (bandX, bandY, bandW, bandH) = live.Side switch
            {
                TabletSide.Bottom => (left, top, w, arm),
                TabletSide.Top => (left, top + h - arm, w, arm),
                TabletSide.Right => (left, top, arm, h),
                _ => (left + w - arm, top, arm, h)
            };
            Add(new Rectangle
            {
                Width = bandW, Height = bandH,
                Fill = new SolidColorBrush(Color.FromArgb(live.LeftEdge ? (byte)0x18 : (byte)0x40, 0xE0, 0x8E, 0x00))
            }, bandX, bandY);
        }

        // How far the push past the edge has got, as the edge filling up.
        var push = live.MarginCounts > 0 ? Math.Clamp(live.Overshoot / live.MarginCounts, 0, 1) : 0;
        Add(new Rectangle { Width = edgeW, Height = edgeH, Fill = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) }, edgeX, edgeY);
        Add(new Rectangle
        {
            Width = vertical ? edgeW * push : edgeW, Height = vertical ? edgeH : edgeH * push,
            Fill = new SolidColorBrush(push >= 1 ? Color.FromRgb(0x1E, 0x9E, 0x4F) : Color.FromRgb(0x1F, 0x6F, 0xEB))
        }, edgeX, edgeY);

        // The estimate and where it has been.
        if (live.IsTracking)
        {
            var p = new Point(left + live.X * scale, top + live.Y * scale);
            if (!_wasRemote) _trail.Clear();
            _trail.Enqueue(p);
            while (_trail.Count > TrailLength) _trail.Dequeue();
            var i = 0;
            foreach (var t in _trail)
            {
                var alpha = (byte)(30 + 150 * ++i / _trail.Count);
                Add(new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromArgb(alpha, 0x1F, 0x6F, 0xEB)) }, t.X - 2, t.Y - 2);
            }
            Add(new Ellipse
            {
                Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB))
            }, p.X - 8, p.Y - 8);
        }

        // The real pointer, while the Safari page is reporting it.
        if (_recorder?.Real is { } real && DateTime.UtcNow - _lastReal < TimeSpan.FromSeconds(1))
        {
            var r = new Point(left + real.X * scale, top + real.Y * scale);
            Add(new Ellipse
            {
                Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x4F))
            }, r.X - 7, r.Y - 7);
        }
        _wasRemote = live.IsRemote;

        _status.Text = !live.IsRemote ? "On this PC. Move onto the iPad to see the estimate."
            : !live.IsTracking ? "On the iPad through a shortcut: there is no estimate to show."
            : !live.LeftEdge ? "On the iPad. Move further in before the way back is armed (amber band)."
            : _recorder is null ? "On the iPad. Compare the blue dot with the real pointer on the iPad."
            : "On the iPad. Blue: Win2Pad's estimate. Green: the real pointer, seen by the page.";
        var gap = _recorder?.Real is { } g && live.IsTracking
            ? $"  off by x {(live.X - g.X) / live.Width:P0} y {(live.Y - g.Y) / live.Height:P0}"
            : "";
        _numbers.Text = live.IsTracking
            ? $"x {live.X / live.Width:P0}  y {live.Y / live.Height:P0}  push {live.Overshoot:0}/{live.MarginCounts}  " +
              $"last report {live.LastSpeed:0} counts x{live.LastRatio:0.00}{gap}"
            : "";
    }

    private void Add(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _board.Children.Add(element);
    }
}
