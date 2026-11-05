using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Win2Pad.App.Services;
using Win2Pad.Core;

namespace Win2Pad.App.Views;

/// <summary>
/// A small pill that floats above every window and says where the input is. It never takes the
/// focus, stays out of the taskbar and Alt+Tab, and can be dragged anywhere; right-click hides it.
/// </summary>
public sealed class IndicatorWindow : Window
{
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _text = new() { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold };

    static IndicatorWindow()
    {
        // WPF-UI's theme manager restyles every window of the app; whatever it sets, this one keeps
        // a transparent background so only the pill is drawn (see also RemoveBackdrop).
        BackgroundProperty.OverrideMetadata(typeof(IndicatorWindow),
            new FrameworkPropertyMetadata(Brushes.Transparent, null, (_, _) => Brushes.Transparent));
    }

    public IndicatorWindow()
    {
        Style = new Style(typeof(Window));
        Title = "Win2Pad indicator";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Cursor = Cursors.SizeAll;
        ToolTip = "Win2Pad - drag to move, right-click to hide";

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_dot);
        row.Children.Add(_text);
        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x21, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 14, 6),
            Child = row
        };

        var menu = new ContextMenu();
        var hide = new MenuItem { Header = "Hide indicator" };
        hide.Click += (_, _) => AppSettings.Instance.ShowIndicator = false;
        menu.Items.Add(hide);
        ContextMenu = menu;

        MouseLeftButtonDown += (_, _) =>
        {
            DragMove();
            AppSettings.Instance.IndicatorLeft = Left;
            AppSettings.Instance.IndicatorTop = Top;
        };
        SourceInitialized += (_, _) =>
        {
            MakeToolWindow();
            RemoveBackdrop();
        };
        // The theme manager applies the system backdrop to every window again on each theme change.
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (_, _) => Dispatcher.BeginInvoke(RemoveBackdrop);
        Loaded += (_, _) => PlaceInitially();

        Update(LinkState.Off, null);
    }

    public void Update(LinkState state, string? tabletName)
    {
        var c = TrayIconService.StateColor(state);
        _dot.Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        _text.Text = state switch
        {
            LinkState.Off => "Win2Pad off",
            LinkState.Waiting => "Waiting for the tablet",
            LinkState.Local => "On this PC",
            LinkState.Remote => $"On {tabletName ?? "the tablet"}",
            _ => state.ToString()
        };
    }

    /// <summary>Where the user left it, if that is still on a screen; otherwise above the taskbar, bottom right.</summary>
    private void PlaceInitially()
    {
        var settings = AppSettings.Instance;
        var area = SystemParameters.WorkArea;
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (settings is { IndicatorLeft: { } left, IndicatorTop: { } top } && virtualScreen.Contains(new Point(left + 10, top + 10)))
        {
            Left = left;
            Top = top;
            return;
        }
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
    }

    /// <summary>
    /// WPF-UI's theme manager gives every window of the app a backdrop and sets the colour its
    /// render surface is cleared with to the theme background, so the window rectangle came out
    /// opaque round the pill. Clearing to transparent, with no system backdrop, leaves only the
    /// pill visible.
    /// </summary>
    private void RemoveBackdrop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var none = DWMSBT_NONE;
        DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
    }

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void MakeToolWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
