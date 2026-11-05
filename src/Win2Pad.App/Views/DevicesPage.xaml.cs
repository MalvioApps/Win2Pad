using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Win2Pad.App.Services;
using Win2Pad.Core.Devices;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Win2Pad.App.Views;

/// <summary>The remembered tablets: rename, turn, fine-tune or forget each one.</summary>
public partial class DevicesPage : Page
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _service.Profiles.Changed += OnChanged;
            _service.Hosts.CollectionChanged += OnHostsChanged;
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _service.Profiles.Changed -= OnChanged;
            _service.Hosts.CollectionChanged -= OnHostsChanged;
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Rebuild);
    private void OnHostsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        List.Children.Clear();
        var profiles = _service.Profiles.All;
        Empty.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var profile in profiles) List.Children.Add(CardFor(profile));
    }

    private bool IsConnected(string deviceId) =>
        _service.Hosts.Any(h => string.Equals(h.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    private UIElement CardFor(DeviceProfile profile)
    {
        var connected = IsConnected(profile.DeviceId);

        var name = new TextBox
        {
            Text = profile.DisplayName, MinWidth = 220, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Rename this tablet", ClearButtonEnabled = false
        };
        name.LostFocus += (_, _) => Rename(profile.DeviceId, name.Text);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };

        var source = profile.Source switch
        {
            CalibrationSource.Safari => "fine-tuned with Safari",
            CalibrationSource.Model => "calibrated from its model",
            CalibrationSource.Manual => "calibrated by hand",
            _ => "not calibrated"
        };
        var size = profile.Active is { } c ? $" · {c.Width}×{c.Height} counts" : "";
        var detail = new TextBlock
        {
            FontTypography = FontTypography.Caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            Text = $"{profile.ModelIdentifier ?? "model unknown"} · {source}{size} · " +
                   (connected ? "connected now" : $"last seen {profile.LastSeen.LocalDateTime:g}")
        };

        var status = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = connected ? new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x4F)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            ToolTip = connected ? "Connected" : "Not connected"
        };

        var orientation = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 8, 0), ToolTip = "How you hold this tablet" };
        orientation.Items.Add("Landscape");
        orientation.Items.Add("Portrait");
        orientation.SelectedIndex = profile.Landscape ? 0 : 1;
        orientation.SelectionChanged += (_, _) => SetOrientation(profile.DeviceId, orientation.SelectedIndex == 0);

        var fineTune = new Wpf.Ui.Controls.Button
        {
            Content = "Fine-tune…", Margin = new Thickness(0, 0, 8, 0), IsEnabled = connected,
            ToolTip = connected ? "Measure this tablet with Safari" : "Connect this tablet to fine-tune it"
        };
        fineTune.Click += (_, _) => new CalibrationWindow(profile.DeviceId) { Owner = Window.GetWindow(this) }.ShowDialog();

        var forget = new Wpf.Ui.Controls.Button { Content = "Forget…", Appearance = ControlAppearance.Danger };
        forget.Click += async (_, _) => await ForgetAsync(profile);

        var left = new StackPanel();
        left.Children.Add(name);
        left.Children.Add(detail);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(orientation);
        actions.Children.Add(fineTune);
        actions.Children.Add(forget);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(left, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(status);
        grid.Children.Add(left);
        grid.Children.Add(actions);

        return new Card { Content = grid, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(16) };
    }

    private void Rename(string deviceId, string text)
    {
        if (_service.Profiles.Get(deviceId) is not { } profile) return;
        var custom = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        // Typing the default name back is not a custom name.
        if (custom == (profile.ModelName ?? profile.Name)) custom = null;
        if (custom == profile.CustomName) return;
        _service.Profiles.Save(profile with { CustomName = custom });
    }

    private void SetOrientation(string deviceId, bool landscape)
    {
        if (_service.Profiles.Get(deviceId) is not { } profile || profile.Landscape == landscape) return;
        _service.Profiles.Save(profile with { Landscape = landscape });
        _service.ApplyActiveProfile();
    }

    private async Task ForgetAsync(DeviceProfile profile)
    {
        var box = new MessageBox
        {
            Title = $"Forget {profile.DisplayName}?",
            Content = "Win2Pad forgets its calibration and settings. Unpairing also removes the Bluetooth pairing " +
                      "from Windows; to pair again later, also choose Forget This Device for this PC on the iPad " +
                      "(Settings → Bluetooth).",
            PrimaryButtonText = "Forget and unpair",
            SecondaryButtonText = "Forget only",
            CloseButtonText = "Cancel"
        };
        var answer = await box.ShowDialogAsync();
        if (answer is not (MessageBoxResult.Primary or MessageBoxResult.Secondary)) return;

        var outcome = await _service.ForgetDeviceAsync(profile.DeviceId, unpair: answer == MessageBoxResult.Primary);
        Notice.Severity = outcome.Contains("failed") || outcome.Contains("refused") ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        Notice.Title = profile.DisplayName;
        Notice.Message = outcome;
        Notice.IsOpen = true;
    }
}
