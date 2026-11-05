using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Win2Pad.App.Services;
using Win2Pad.Core.Edge;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace Win2Pad.App.Views;

public partial class StatusPage : Page
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public StatusPage()
    {
        InitializeComponent();
        DataContext = _service;
        _service.PropertyChanged += OnServiceChanged;
        Loaded += (_, _) => RefreshSteps();
        Unloaded += (_, _) =>
        {
            _service.PropertyChanged -= OnServiceChanged;
        };
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PeripheralService.IsRunning) or nameof(PeripheralService.KeyboardSubscribers)
            or nameof(PeripheralService.ActiveProfile) or nameof(PeripheralService.IsRedirecting))
            Dispatcher.BeginInvoke(RefreshSteps);
    }

    /// <summary>
    /// The setup as a checklist that follows the real state: each step ticks itself once it is
    /// true, so a first-time user always sees the one thing left to do.
    /// </summary>
    private void RefreshSteps()
    {
        var options = EdgeOptions.Load();
        var profile = _service.ActiveProfile;
        var side = options.EffectiveSide switch
        {
            TabletSide.Left => "left", TabletSide.Top => "top", TabletSide.Bottom => "bottom", _ => "right"
        };

        (bool Done, string Title, string Detail)[] steps =
        [
            (_service.IsRunning, "Start Win2Pad",
                "Press Start below. Your PC then appears to the iPad as a Bluetooth keyboard and mouse."),
            (_service.KeyboardSubscribers > 0, "Pair the iPad",
                "On the iPad, open Settings → Bluetooth and tap this PC under Other Devices. Only needed once."),
            (profile?.Active is not null, "Calibrate",
                profile is null ? "Happens by itself when the iPad connects: Win2Pad reads its model."
                : $"{profile.DisplayName} could not be recognised. Fine-tune it with Safari in Devices."),
            (options.Placement is not null, "Arrange",
                $"Optional: in Screens, place the iPad where it is on your desk. Now any free {side} edge leads to it."),
        ];

        var allDone = steps.Take(3).All(s => s.Done);
        GetStartedTitle.Text = allDone
            ? $"Ready: push the pointer against the {side} edge of your screen to move onto the iPad"
            : "Get started";

        Steps.Children.Clear();
        var next = true;
        foreach (var (done, title, detail) in steps)
        {
            var current = !done && next;
            if (current) next = false;
            Steps.Children.Add(StepRow(done, current, title, detail));
        }
    }

    private UIElement StepRow(bool done, bool current, string title, string detail)
    {
        var icon = new SymbolIcon
        {
            Symbol = done ? SymbolRegular.CheckmarkCircle24 : current ? SymbolRegular.ArrowCircleRight24 : SymbolRegular.Circle24,
            Filled = done,
            FontSize = 20,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = done ? new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x4F))
                : current ? (Brush)FindResource("SystemAccentColorPrimaryBrush")
                : (Brush)FindResource("TextFillColorTertiaryBrush")
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontTypography = FontTypography.BodyStrong, Opacity = done ? 0.7 : 1 });
        if (!done)
            text.Children.Add(new TextBlock
            {
                Text = detail, FontTypography = FontTypography.Caption, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
            });

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    private async void OnStart(object sender, RoutedEventArgs e) =>
        await _service.StartAsync();

    private async void OnStop(object sender, RoutedEventArgs e) =>
        await _service.StopAsync();
}
