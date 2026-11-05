using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Win2Pad.App.Services;
using Win2Pad.Core.Edge;
using Wpf.Ui.Controls;

namespace Win2Pad.App.Views;

/// <summary>
/// Places the tablet next to the monitors, like Windows' display arrangement. Monitors are drawn
/// at their desktop positions and the tablet at its size in points; dropping it snaps it against
/// the nearest side and saves the placement, which a running session picks up at once. A slider
/// sets how much edge it shares, from its real size up to every monitor side by side on that line.
/// </summary>
public partial class ArrangementPage : Page
{
    private const double BoardPadding = 24;

    private IReadOnlyList<(ScreenRect Bounds, bool Primary)> _monitors = [];
    private EdgeOptions _options = new();
    private TabletPlacement? _placement;
    private ScreenRect _tablet;
    private (int Width, int Height) _natural;
    private bool _updatingSlider;
    private readonly DispatcherTimer _saveDelay;

    // Desktop -> canvas transform of the last drawing.
    private double _scale = 1, _originX, _originY;

    private Border? _tabletVisual;
    private Point _dragStart;
    private double _dragLeft, _dragTop;

    public ArrangementPage()
    {
        InitializeComponent();
        _saveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDelay.Tick += (_, _) =>
        {
            _saveDelay.Stop();
            if (_placement is not null) Save(_placement);
        };
        Loaded += (_, _) => Reload();
        Unloaded += (_, _) =>
        {
            // Leaving the page must not lose a slider change still waiting to be saved.
            if (!_saveDelay.IsEnabled) return;
            _saveDelay.Stop();
            if (_placement is not null) Save(_placement);
        };
    }

    private IReadOnlyList<ScreenRect> Bounds => _monitors.Select(m => m.Bounds).ToList();

    private void Reload()
    {
        _monitors = Win32Screens.All();
        _options = EdgeOptions.Load();
        EdgeToggle.IsChecked = _options.Enabled;

        var calibration = PeripheralService.Instance.ActiveProfile?.Active?.Pointer
                          ?? (_options.Calibration.IsValid ? _options.Calibration : new PointerCalibration(3349, 2512));
        _natural = Arrangement.TabletSize(calibration);

        _placement = _options.Placement;
        if (_placement is not null && !_monitors.Any(m => m.Bounds == _placement.Monitor))
        {
            ShowHint(InfoBarSeverity.Warning, "Your screens changed since the iPad was placed. Drag it into place again.");
            _placement = null;
        }
        else
        {
            Hint.IsOpen = PeripheralService.Instance.ActiveProfile is null;
            if (Hint.IsOpen) ShowHint(InfoBarSeverity.Informational, "Connect the iPad to see it at its real size.");
        }

        if (_placement is not null) _placement = Arrangement.Rescale(_placement, NaturalAlong(_placement.Side));
        _tablet = _placement is not null ? TabletRect(_placement) : DefaultTablet();
        Draw();
        RefreshSlider();
    }

    private int NaturalAlong(TabletSide side) => side is TabletSide.Top or TabletSide.Bottom ? _natural.Width : _natural.Height;

    /// <summary>The tablet drawn at its placement's scale, keeping its shape.</summary>
    private ScreenRect TabletRect(TabletPlacement placement) =>
        Arrangement.RectFor(placement,
            (int)Math.Round(_natural.Width * placement.Scale), (int)Math.Round(_natural.Height * placement.Scale));

    /// <summary>Before any placement: beside the primary monitor on the last side used, centred.</summary>
    private ScreenRect DefaultTablet() => BesidePrimary(_options.Side);

    /// <summary>The tablet centred against one side of the primary monitor, at its real size.</summary>
    private ScreenRect BesidePrimary(TabletSide side)
    {
        var primary = _monitors.FirstOrDefault().Bounds;
        var (w, h) = _natural;
        var cx = (primary.Left + primary.Right - w) / 2;
        var cy = (primary.Top + primary.Bottom - h) / 2;
        ScreenRect dropped = side switch
        {
            TabletSide.Left => new(primary.Left - w, cy, primary.Left, cy + h),
            TabletSide.Top => new(cx, primary.Top - h, cx + w, primary.Top),
            TabletSide.Bottom => new(cx, primary.Bottom, cx + w, primary.Bottom + h),
            _ => new(primary.Right, cy, primary.Right + w, cy + h)
        };
        var snapped = Arrangement.Snap(Bounds, dropped);
        return snapped is null ? dropped : Arrangement.RectFor(snapped, w, h);
    }

    private void Draw()
    {
        Board.Children.Clear();
        if (_monitors.Count == 0 || BoardFrame.ActualWidth < 10) return;

        // Fit every monitor and the tablet, with room to drag it round.
        var all = Bounds.Append(_tablet).ToList();
        double left = all.Min(r => r.Left), top = all.Min(r => r.Top);
        double right = all.Max(r => r.Right), bottom = all.Max(r => r.Bottom);
        var padX = (right - left) * 0.15;
        var padY = (bottom - top) * 0.15;
        left -= padX; right += padX; top -= padY; bottom += padY;

        var width = BoardFrame.ActualWidth - 2 * BoardPadding;
        var height = BoardFrame.ActualHeight - 2 * BoardPadding;
        _scale = Math.Min(width / (right - left), height / (bottom - top));
        _originX = BoardPadding + (width - (right - left) * _scale) / 2 - left * _scale;
        _originY = BoardPadding + (height - (bottom - top) * _scale) / 2 - top * _scale;

        for (var i = 0; i < _monitors.Count; i++)
        {
            var (bounds, primary) = _monitors[i];
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = (i + 1).ToString(), FontSize = 26, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            label.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{bounds.Width}×{bounds.Height}{(primary ? " · main" : "")}", FontSize = 11, Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            Board.Children.Add(Box(bounds, "ControlFillColorDefaultBrush", "ControlStrokeColorDefaultBrush", label));
        }

        // Where they touch: the stretches the pointer crosses, on every monitor on that line.
        if (_placement is { } placement)
        {
            var horizontal = placement.Side is TabletSide.Top or TabletSide.Bottom;
            foreach (var m in Bounds.Where(m => TabletPlacement.EdgeLine(m, placement.Side) == placement.Line))
            {
                var start = Math.Max(placement.Offset, horizontal ? m.Left : m.Top);
                var end = Math.Min(placement.Offset + placement.Length, horizontal ? m.Right : m.Bottom);
                if (end <= start) continue;
                var line = placement.Line;
                Board.Children.Add(new Line
                {
                    X1 = X(horizontal ? start : line), Y1 = Y(horizontal ? line : start),
                    X2 = X(horizontal ? end : line), Y2 = Y(horizontal ? line : end),
                    Stroke = (Brush)FindResource("SystemAccentColorPrimaryBrush"), StrokeThickness = 4,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                });
            }
        }

        var name = PeripheralService.Instance.ActiveProfile?.DisplayName ?? "iPad";
        var tabletLabel = new System.Windows.Controls.TextBlock
        {
            Text = name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _tabletVisual = Box(_tablet, "SystemAccentColorPrimaryBrush", "SystemAccentColorSecondaryBrush", tabletLabel);
        _tabletVisual.Cursor = Cursors.SizeAll;
        _tabletVisual.Opacity = _placement is null ? 0.75 : 1;
        _tabletVisual.MouseLeftButtonDown += OnTabletDown;
        _tabletVisual.MouseMove += OnTabletMove;
        _tabletVisual.MouseLeftButtonUp += OnTabletUp;
        Board.Children.Add(_tabletVisual);

        Summary.Text = _placement is null
            ? $"Not placed yet: drag the iPad or use a quick placement. Until then, any free edge on the {_options.Side.ToString().ToLowerInvariant()} leads to it."
            : Describe(_placement);
    }

    private string Describe(TabletPlacement placement)
    {
        var index = _monitors.ToList().FindIndex(m => m.Bounds == placement.Monitor) + 1;
        var side = placement.Side switch
        {
            TabletSide.Left => "left of", TabletSide.Right => "right of",
            TabletSide.Top => "above", _ => "below"
        };
        return $"The iPad is {side} screen {index}. Drag it to move it; use the slider to set how much edge it shares.";
    }

    private int AnchorEdge(TabletPlacement p) => p.MonitorEnd - p.MonitorStart;

    /// <summary>
    /// The shared-edge slider, as a share of the screen it was placed against: from the iPad's
    /// real size up to every monitor side by side on that line (100% for one, 200% for two...).
    /// </summary>
    private void RefreshSlider()
    {
        _updatingSlider = true;
        try
        {
            if (_placement is not { } p)
            {
                SharePanel.Visibility = Visibility.Collapsed;
                return;
            }

            var edge = AnchorEdge(p);
            var (spanStart, spanEnd) = Arrangement.LineSpan(Bounds, p);
            var min = NaturalAlong(p.Side) * 100.0 / edge;
            var max = Math.Max(min, (spanEnd - spanStart) * 100.0 / edge);
            SharePanel.Visibility = Visibility.Visible;
            ShareSlider.Minimum = Math.Round(min);
            ShareSlider.Maximum = Math.Round(max);
            ShareSlider.IsEnabled = max - min >= 1;
            ShareSlider.Value = Math.Clamp(Math.Round(p.Length * 100.0 / edge), ShareSlider.Minimum, ShareSlider.Maximum);
            ShareValue.Text = $"{ShareSlider.Value:0}%";
        }
        finally
        {
            _updatingSlider = false;
        }
    }

    private void OnShareChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _placement is not { } p) return;
        ShareValue.Text = $"{e.NewValue:0}%";

        var edge = AnchorEdge(p);
        var length = (int)Math.Round(e.NewValue / 100 * edge);
        var resized = Arrangement.Resize(p, length) with { Scale = (double)length / NaturalAlong(p.Side) };

        // Keep it over the screens it can share: growing it to span them all lines it up with them.
        var (spanStart, spanEnd) = Arrangement.LineSpan(Bounds, resized);
        if (resized.Length <= spanEnd - spanStart)
            resized = resized with { Offset = Math.Clamp(resized.Offset, spanStart, spanEnd - resized.Length) };

        _placement = resized;
        _tablet = TabletRect(resized);
        Draw();
        _saveDelay.Stop();
        _saveDelay.Start();
    }

    private Border Box(ScreenRect r, string fill, string stroke, UIElement content)
    {
        var box = new Border
        {
            Width = Math.Max(4, r.Width * _scale - 2),
            Height = Math.Max(4, r.Height * _scale - 2),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource(fill),
            BorderBrush = (Brush)FindResource(stroke),
            BorderThickness = new Thickness(1),
            Child = content
        };
        Canvas.SetLeft(box, X(r.Left) + 1);
        Canvas.SetTop(box, Y(r.Top) + 1);
        return box;
    }

    private double X(double desktop) => _originX + desktop * _scale;
    private double Y(double desktop) => _originY + desktop * _scale;

    private void OnTabletDown(object sender, MouseButtonEventArgs e)
    {
        if (_tabletVisual is null) return;
        _dragStart = e.GetPosition(Board);
        _dragLeft = Canvas.GetLeft(_tabletVisual);
        _dragTop = Canvas.GetTop(_tabletVisual);
        _tabletVisual.CaptureMouse();
        e.Handled = true;
    }

    private void OnTabletMove(object sender, MouseEventArgs e)
    {
        if (_tabletVisual is not { IsMouseCaptured: true }) return;
        var p = e.GetPosition(Board);
        Canvas.SetLeft(_tabletVisual, _dragLeft + p.X - _dragStart.X);
        Canvas.SetTop(_tabletVisual, _dragTop + p.Y - _dragStart.Y);
    }

    private void OnTabletUp(object sender, MouseButtonEventArgs e)
    {
        if (_tabletVisual is not { IsMouseCaptured: true }) return;
        _tabletVisual.ReleaseMouseCapture();

        var left = (int)Math.Round((Canvas.GetLeft(_tabletVisual) - 1 - _originX) / _scale);
        var top = (int)Math.Round((Canvas.GetTop(_tabletVisual) - 1 - _originY) / _scale);
        var dropped = new ScreenRect(left, top, left + _tablet.Width, top + _tablet.Height);
        var snapped = Arrangement.Snap(Bounds, dropped);
        if (snapped is null)
        {
            Draw();
            return;
        }

        // The dropped size along the edge is the tablet's current scale on its new side.
        var placement = snapped with { Scale = (double)snapped.Length / NaturalAlong(snapped.Side) };
        if (!Save(placement))
        {
            Draw();
            return;
        }
        _placement = placement;
        _tablet = TabletRect(placement);
        Draw();
        RefreshSlider();
    }

    private bool Save(TabletPlacement placement)
    {
        var updated = EdgeOptions.Load() with { Placement = placement, Side = placement.Side };
        if (updated.TrySave(null, out var error)) return true;
        ShowHint(InfoBarSeverity.Error, $"Could not save the arrangement: {error}");
        return false;
    }

    /// <summary>One click: the iPad centred against a side of the main screen, at its real size.</summary>
    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<TabletSide>(tag, out var side)) return;
        _saveDelay.Stop();
        var rect = BesidePrimary(side);
        if (Arrangement.Snap(Bounds, rect) is not { } snapped) return;
        var placement = snapped with { Scale = (double)snapped.Length / NaturalAlong(snapped.Side) };
        if (!Save(placement)) return;
        _options = EdgeOptions.Load();
        _placement = placement;
        _tablet = TabletRect(placement);
        Draw();
        RefreshSlider();
    }

    private void OnEdgeToggle(object sender, RoutedEventArgs e)
    {
        var updated = EdgeOptions.Load() with { Enabled = EdgeToggle.IsChecked == true };
        if (!updated.TrySave(null, out var error))
        {
            ShowHint(InfoBarSeverity.Error, $"Could not save: {error}");
            EdgeToggle.IsChecked = !EdgeToggle.IsChecked;
            return;
        }
        // Edge switching watches the pointer all the time, so capture must be running.
        if (updated.Enabled && PeripheralService.Instance.IsRunning)
            _ = PeripheralService.Instance.StartCaptureAsync(armEarly: true);
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        // Back to "any free edge on the side chosen in Settings".
        _saveDelay.Stop();
        var current = EdgeOptions.Load();
        if (current.Placement is not null) (current with { Placement = null }).TrySave(null, out _);
        Reload();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void OnBoardSizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private void ShowHint(InfoBarSeverity severity, string message)
    {
        Hint.Severity = severity;
        Hint.Message = message;
        Hint.IsOpen = true;
    }
}
