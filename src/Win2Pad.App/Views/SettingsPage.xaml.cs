using System.Windows;
using System.Windows.Controls;
using Win2Pad.App.Services;
using Win2Pad.Core;
using Win2Pad.Core.Edge;

namespace Win2Pad.App.Views;

public partial class SettingsPage : Page
{
    private SwitchGestureWatcher? _gestureWatcher;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = AppSettings.Instance;

        var available = StartupService.Command is not null;
        DevBar.IsOpen = !available;
        AutoStartToggle.IsEnabled = available;
        AutoStartToggle.IsChecked = available && StartupService.IsEnabled();

        SwitchGestureToggle.IsChecked =
            SwitchGestureOptions.Load(AppPaths.SwitchGestureConfig).Enabled;
        LoadEdgeOptions();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_gestureWatcher is not null) return;
        _gestureWatcher = new SwitchGestureWatcher(AppPaths.SwitchGestureConfig);
        _gestureWatcher.Changed += OnGestureSettingsChanged;
        RefreshGestureToggle();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var watcher = _gestureWatcher;
        _gestureWatcher = null;
        if (watcher is null) return;
        watcher.Changed -= OnGestureSettingsChanged;
        watcher.Dispose();
    }

    private void OnGestureSettingsChanged(SwitchGestureOptions options) =>
        Dispatcher.BeginInvoke(RefreshGestureToggle);

    private void RefreshGestureToggle()
    {
        if (_gestureWatcher is { } watcher)
            SwitchGestureToggle.IsChecked = watcher.Current.Enabled;
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e) =>
        StartupService.Set(AutoStartToggle.IsChecked == true);

    private void OnSwitchGestureChanged(object sender, RoutedEventArgs e)
    {
        var enabled = SwitchGestureToggle.IsChecked == true;

        // Reload before writing so tuning values edited by hand are not flattened by a toggle,
        // and so a change made from the CLI meanwhile is not silently reverted.
        var updated = SwitchGestureOptions.Load(AppPaths.SwitchGestureConfig).WithEnabled(enabled);
        if (updated.TrySave(AppPaths.SwitchGestureConfig, out _)) return;

        // The setting did not persist, so the switch must not claim that it did.
        SwitchGestureToggle.IsChecked = !enabled;
    }

    private bool _loadingEdge;

    private void LoadEdgeOptions()
    {
        _loadingEdge = true;
        var options = EdgeOptions.Load();
        EdgeToggle.IsChecked = options.Enabled;
        SideCombo.SelectedItem = SideCombo.Items.OfType<ComboBoxItem>()
            .First(item => (string)item.Tag == options.Side.ToString());
        CalibrationWidthBox.Text = options.CalibrationWidth > 0 ? options.CalibrationWidth.ToString() : "";
        CalibrationHeightBox.Text = options.CalibrationHeight > 0 ? options.CalibrationHeight.ToString() : "";
        _loadingEdge = false;
    }

    /// <summary>Saves every edge setting together; a running capture session picks the change up at once.</summary>
    private void OnEdgeChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingEdge || !IsInitialized) return;

        // Reload first so tuning values edited by hand in the JSON file are kept.
        var current = EdgeOptions.Load();
        var side = SideCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<TabletSide>(tag, out var parsed)
            ? parsed
            : current.Side;
        var updated = current with
        {
            Enabled = EdgeToggle.IsChecked == true,
            Side = side,
            CalibrationWidth = ParseCounts(CalibrationWidthBox.Text, current.CalibrationWidth),
            CalibrationHeight = ParseCounts(CalibrationHeightBox.Text, current.CalibrationHeight)
        };
        if (updated == current) return;

        if (!updated.TrySave(null, out var error))
        {
            App.Current.Tray?.ShowMessage($"Could not save the edge settings: {error}");
            LoadEdgeOptions();
            return;
        }

        // Turning it on needs the capture running even if the user never pressed Capture.
        if (updated.Enabled && PeripheralService.Instance.IsRunning)
            _ = PeripheralService.Instance.StartCaptureAsync(armEarly: true);
    }

    private static int ParseCounts(string text, int fallback) =>
        string.IsNullOrWhiteSpace(text) ? 0
        : int.TryParse(text.Trim(), out var value) && value is > 0 and <= 100_000 ? value
        : fallback;

    private void OnExit(object sender, RoutedEventArgs e) => App.Current.ExitApplication();
}
