using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Win2Pad.App.Services;
using Win2Pad.Core.Edge;
using Wpf.Ui.Controls;

namespace Win2Pad.App.Views;

/// <summary>
/// Places the tablet next to the monitors, like Windows' display arrangement. Monitors are drawn
/// at their desktop positions and the tablet at its size in points; dropping it snaps it against
/// the nearest side and saves the placement, which a running session picks up at once.
/// </summary>
public partial class ArrangementPage : Page
{
    private const double BoardPadding = 24;

    private IReadOnlyList<(ScreenRect Bounds, bool Primary)> _monitors = [];
    private EdgeOptions _options = new();
    private ScreenRect _tablet;
    private bool _placed;
    private (int Width, int Height) _tabletSize;

    // Desktop -> canvas transform of the last drawing.
    private double _scale = 1, _originX, _originY;

    private Border? _tabletVisual;
    private Point _dragStart;
    private double _dragLeft, _dragTop;

    public ArrangementPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _monitors = Win32Screens.All();
        _options = EdgeOptions.Load();

        var calibration = PeripheralService.Instance.ActiveProfile?.Active?.Pointer
                          ?? (_options.Calibration.IsValid ? _options.Calibration : new PointerCalibration(3349, 2512));
        _tabletSize = Arrangement.TabletSize(calibration);

        var placement = _options.Placement;
        if (placement is not null && !_monitors.Any(m => m.Bounds == placement.Monitor))
        {
            ShowHint(InfoBarSeverity.Warning, "Your screens changed since the iPad was placed. Drag it into place again.");
            placement = null;
        }
        else
        {
            Hint.IsOpen = PeripheralService.Instance.ActiveProfile is null;
            if (Hint.IsOpen) ShowHint(InfoBarSeverity.Informational, "Connect the iPad to see it at its real size.");
        }

        _placed = placement is not null;
        _tablet = placement is not null
            ? Arrangement.RectFor(Arrangement.Resize(placement, Along(placement.Side)), _tabletSize.Width, _tabletSize.Height)
            : DefaultTablet();
        Draw();
    }

    /// <summary>Before any placement: beside the primary monitor on the side chosen in Settings, centred.</summary>
    private ScreenRect DefaultTablet()
    {
        var primary = _monitors.FirstOrDefault().Bounds;
        var (w, h) = _tabletSize;
        var cx = (primary.Left + primary.Right - w) / 2;
        var cy = (primary.Top + primary.Bottom - h) / 2;
        ScreenRect dropped = _options.Side switch
        {
            TabletSide.Left => new(primary.Left - w, cy, primary.Left, cy + h),
            TabletSide.Top => new(cx, primary.Top - h, cx + w, primary.Top),
            TabletSide.Bottom => new(cx, primary.Bottom, cx + w, primary.Bottom + h),
            _ => new(primary.Right, cy, primary.Right + w, cy + h)
        };
        var snapped = Arrangement.Snap(_monitors.Select(m => m.Bounds).ToList(), dropped);
        return snapped is null ? dropped : Arrangement.RectFor(snapped, w, h);
    }

    private int Along(TabletSide side) => side is TabletSide.Top or TabletSide.Bottom ? _tabletSize.Width : _tabletSize.Height;

    private void Draw()
    {
        Board.Children.Clear();
        if (_monitors.Count == 0 || BoardFrame.ActualWidth < 10) return;

        // Fit every monitor and the tablet, with room to drag it round.
        var all = _monitors.Select(m => m.Bounds).Append(_tablet).ToList();
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

        // Where the two touch: the stretch the pointer crosses.
        var placement = Arrangement.Snap(_monitors.Select(m => m.Bounds).ToList(), _tablet);
        if (_placed && placement is { Touches: true })
        {
            var (start, end) = placement.Overlap;
            var m = placement.Monitor;
            var horizontal = placement.Side is TabletSide.Top or TabletSide.Bottom;
            var line = horizontal
                ? (X1: start, Y1: placement.Side == TabletSide.Top ? m.Top : m.Bottom, X2: end, Y2: placement.Side == TabletSide.Top ? m.Top : m.Bottom)
                : (X1: placement.Side == TabletSide.Left ? m.Left : m.Right, Y1: start, X2: placement.Side == TabletSide.Left ? m.Left : m.Right, Y2: end);
            Board.Children.Add(new Line
            {
                X1 = X(line.X1), Y1 = Y(line.Y1), X2 = X(line.X2), Y2 = Y(line.Y2),
                Stroke = (Brush)FindResource("SystemAccentColorPrimaryBrush"), StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
        }

        var name = PeripheralService.Instance.ActiveProfile?.ModelName ?? "iPad";
        var tabletLabel = new System.Windows.Controls.TextBlock
        {
            Text = name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _tabletVisual = Box(_tablet, "SystemAccentColorPrimaryBrush", "SystemAccentColorSecondaryBrush", tabletLabel);
        _tabletVisual.Cursor = Cursors.SizeAll;
        _tabletVisual.Opacity = _placed ? 1 : 0.75;
        _tabletVisual.MouseLeftButtonDown += OnTabletDown;
        _tabletVisual.MouseMove += OnTabletMove;
        _tabletVisual.MouseLeftButtonUp += OnTabletUp;
        Board.Children.Add(_tabletVisual);

        Summary.Text = !_placed
            ? $"Not placed yet: drag the iPad to set exactly where the pointer crosses. Until then, any free edge on the {_options.Side.ToString().ToLowerInvariant()} leads to it."
            : placement is null ? "" : Describe(placement);
    }

    private string Describe(TabletPlacement placement)
    {
        var index = _monitors.ToList().FindIndex(m => m.Bounds == placement.Monitor) + 1;
        var (start, end) = placement.Overlap;
        var edge = placement.MonitorEnd - placement.MonitorStart;
        var share = (end - start) * 100 / Math.Max(1, edge);
        var side = placement.Side switch
        {
            TabletSide.Left => "left of", TabletSide.Right => "right of",
            TabletSide.Top => "above", _ => "below"
        };
        return $"The iPad is {side} screen {index}, sharing {share}% of its edge.";
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
        var dropped = new ScreenRect(left, top, left + _tabletSize.Width, top + _tabletSize.Height);
        var placement = Arrangement.Snap(_monitors.Select(m => m.Bounds).ToList(), dropped);
        if (placement is null)
        {
            Draw();
            return;
        }

        var current = EdgeOptions.Load();
        var updated = current with { Placement = placement, Side = placement.Side };
        if (!updated.TrySave(null, out var error))
        {
            ShowHint(InfoBarSeverity.Error, $"Could not save the arrangement: {error}");
            Draw();
            return;
        }

        _options = updated;
        _placed = true;
        _tablet = Arrangement.RectFor(placement, _tabletSize.Width, _tabletSize.Height);
        Draw();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        // Back to "any free edge on the side chosen in Settings".
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
