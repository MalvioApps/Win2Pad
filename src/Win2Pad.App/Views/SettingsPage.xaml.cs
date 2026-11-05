using System.Windows;
using System.Windows.Controls;
using Win2Pad.App.Services;
using Win2Pad.Core;
using Win2Pad.Core.Devices;
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
        UpdateService.Instance.PropertyChanged -= OnUpdateChanged;
        UpdateService.Instance.PropertyChanged += OnUpdateChanged;
        RefreshUpdates();
        PeripheralService.Instance.PropertyChanged -= OnServiceChanged;
        PeripheralService.Instance.PropertyChanged += OnServiceChanged;
        RefreshDevice();
        if (_gestureWatcher is not null) return;
        _gestureWatcher = new SwitchGestureWatcher(AppPaths.SwitchGestureConfig);
        _gestureWatcher.Changed += OnGestureSettingsChanged;
        RefreshGestureToggle();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UpdateService.Instance.PropertyChanged -= OnUpdateChanged;
        PeripheralService.Instance.PropertyChanged -= OnServiceChanged;
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
            .First(item => (string)item.Tag == options.EffectiveSide.ToString());
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
        var updated = current with { Enabled = EdgeToggle.IsChecked == true, Side = side };
        // Choosing a side here replaces a placement made in the arrangement editor.
        if (side != current.EffectiveSide) updated = updated with { Placement = null };
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

    private void OnServiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PeripheralService.ActiveProfile)) Dispatcher.BeginInvoke(RefreshDevice);
    }

    /// <summary>Shows which tablet edge switching goes to and how it was calibrated.</summary>
    private void RefreshDevice()
    {
        var profile = PeripheralService.Instance.ActiveProfile;
        _loadingEdge = true;
        OrientationCombo.IsEnabled = profile is not null;
        OrientationCombo.SelectedIndex = profile is { Landscape: false } ? 1 : 0;
        _loadingEdge = false;

        if (profile is null)
        {
            DeviceTitle.Text = "No tablet connected";
            DeviceDetail.Text = "Connect the iPad over Bluetooth: Win2Pad reads its model and calibrates itself.";
            return;
        }

        DeviceTitle.Text = profile.ModelName ?? profile.Name ?? "Unknown tablet";
        var source = profile.Source switch
        {
            CalibrationSource.Safari => "fine-tuned with Safari",
            CalibrationSource.Model => "calibrated from its model",
            CalibrationSource.Manual => "calibrated by hand",
            _ => "model not recognised: fine-tune it with Safari to enable edge switching"
        };
        var size = profile.Active is { } c ? $" · {c.Width}×{c.Height} counts" : "";
        DeviceDetail.Text = $"{profile.ModelIdentifier ?? "no model reported"} · {source}{size}";
    }

    /// <summary>Bluetooth cannot tell how the iPad is held, so the user says; both calibrations are kept.</summary>
    private void OnOrientationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEdge || !IsInitialized) return;
        var service = PeripheralService.Instance;
        if (service.ActiveProfile is not { } profile) return;
        var landscape = OrientationCombo.SelectedIndex == 0;
        if (profile.Landscape == landscape) return;
        service.Profiles.Save(profile with { Landscape = landscape });
        service.ApplyActiveProfile();
    }

    private void OnFineTune(object sender, RoutedEventArgs e) =>
        new CalibrationWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void OnUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshUpdates();

    private void RefreshUpdates()
    {
        var updates = UpdateService.Instance;
        VersionText.Text = $"Win2Pad {updates.CurrentVersion}";
        UpdateStatus.Text = updates.Status is { Length: > 0 } status ? status
            : updates.IsInstalled ? "Up to date as far as we know." : "A development build: install Win2Pad to get updates.";
        CheckButton.IsEnabled = !updates.IsBusy;
        RestartButton.Visibility = updates.IsReady ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await UpdateService.Instance.CheckAsync();

    private void OnRestartToUpdate(object sender, RoutedEventArgs e) => UpdateService.Instance.RestartToUpdate();

    private void OnExit(object sender, RoutedEventArgs e) => App.Current.ExitApplication();
}
