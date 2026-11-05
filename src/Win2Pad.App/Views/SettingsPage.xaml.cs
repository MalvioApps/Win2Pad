using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Win2Pad.App.Services;

namespace Win2Pad.App.Views;

/// <summary>General preferences: startup, the floating indicator and updates.</summary>
public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = AppSettings.Instance;

        var available = StartupService.Command is not null;
        DevBar.IsOpen = !available;
        AutoStartToggle.IsEnabled = available;
        AutoStartToggle.IsChecked = available && StartupService.IsEnabled();

        Loaded += (_, _) =>
        {
            UpdateService.Instance.PropertyChanged -= OnUpdateChanged;
            UpdateService.Instance.PropertyChanged += OnUpdateChanged;
            RefreshUpdates();
        };
        Unloaded += (_, _) => UpdateService.Instance.PropertyChanged -= OnUpdateChanged;
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e) =>
        StartupService.Set(AutoStartToggle.IsChecked == true);

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e) => RefreshUpdates();

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
