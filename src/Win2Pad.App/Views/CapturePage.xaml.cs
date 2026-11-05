using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Win2Pad.App.Services;
using Win2Pad.Core;

namespace Win2Pad.App.Views;

public partial class CapturePage : Page
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public CapturePage()
    {
        InitializeComponent();
        DataContext = _service;
        _service.Hosts.CollectionChanged += OnHostsChanged;
        Unloaded += (_, _) => _service.Hosts.CollectionChanged -= OnHostsChanged;
        UpdateEmptyState();
        Loaded += (_, _) => SwitchGestureToggle.IsChecked = SwitchGestureOptions.Load(AppPaths.SwitchGestureConfig).Enabled;
    }

    private void OnSwitchGestureChanged(object sender, RoutedEventArgs e)
    {
        var enabled = SwitchGestureToggle.IsChecked == true;

        // Reload before writing so tuning values edited by hand are not flattened by a toggle.
        // A running capture session watches this file and picks the change up.
        var updated = SwitchGestureOptions.Load(AppPaths.SwitchGestureConfig).WithEnabled(enabled);
        if (updated.TrySave(AppPaths.SwitchGestureConfig, out _)) return;

        // The setting did not persist, so the switch must not claim that it did.
        SwitchGestureToggle.IsChecked = !enabled;
    }

    private void OnHostsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void UpdateEmptyState() => NoHostsBar.IsOpen = _service.Hosts.Count == 0;

    private void OnTargetChecked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TargetOption option) _service.Select(option);
    }

    private async void OnToggleCapture(object sender, RoutedEventArgs e)
    {
        if (_service.IsCapturing) await _service.StopCaptureAsync();
        else await _service.StartCaptureAsync();

        // Assigning IsChecked would replace the one-way binding and leave the switch deaf to
        // Ctrl+Alt+Q from then on; SetCurrentValue snaps it back without detaching.
        CaptureToggle.SetCurrentValue(Wpf.Ui.Controls.ToggleSwitch.IsCheckedProperty, _service.IsCapturing);
    }
}
