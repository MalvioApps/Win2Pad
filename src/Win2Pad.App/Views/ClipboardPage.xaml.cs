using System.Windows;
using System.Windows.Controls;
using Win2Pad.Core.Clipboard;

namespace Win2Pad.App.Views;

/// <summary>Clipboard sharing with the iPad through an iOS Shortcut.</summary>
public partial class ClipboardPage : Page
{
    private ClipboardServer? _watched;

    public ClipboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ClipboardServer.CurrentChanged -= OnServerChanged;
            ClipboardServer.CurrentChanged += OnServerChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            ClipboardServer.CurrentChanged -= OnServerChanged;
            Watch(null);
        };
    }

    private void OnServerChanged() => Dispatcher.Invoke(Refresh);

    private void OnEnabledChanged(object sender, RoutedEventArgs e) =>
        (ClipboardOptions.Load() with { Enabled = EnabledToggle.IsChecked == true }).Save();

    private void OnNewKey(object sender, RoutedEventArgs e) =>
        (ClipboardOptions.Load() with { Key = ClipboardOptions.NewKey() }).Save();

    private void Refresh()
    {
        var options = ClipboardOptions.Load();
        var server = ClipboardServer.Current;
        EnabledToggle.IsChecked = options.Enabled;
        Watch(server);

        SetupPanel.Visibility = server is null ? Visibility.Collapsed : Visibility.Visible;
        if (server is not null)
        {
            SetupUrl.Text = server.SetupUrl;
            QrImage.Source = CalibrationWindow.QrCode(server.SetupUrl);
            Status(Wpf.Ui.Controls.InfoBarSeverity.Success, "Ready",
                "With input on the iPad, Ctrl+V runs the Shortcut and pastes.");
        }
        else if (options.Enabled)
        {
            Status(Wpf.Ui.Controls.InfoBarSeverity.Error, "Not available",
                ClipboardServer.StartError ?? "Starting…");
        }
        else
        {
            Status(Wpf.Ui.Controls.InfoBarSeverity.Informational, "Off",
                "Ctrl+V on the iPad types the PC's text out key by key, up to 5,000 characters.");
        }
        ShowLastFetch();
    }

    private void Watch(ClipboardServer? server)
    {
        if (ReferenceEquals(_watched, server)) return;
        if (_watched is not null) _watched.Fetched -= OnFetched;
        _watched = server;
        if (server is not null) server.Fetched += OnFetched;
    }

    private void OnFetched(string what) => Dispatcher.Invoke(ShowLastFetch);

    private void ShowLastFetch()
    {
        LastFetch.Text = _watched?.LastFetch is { } last
            ? $"The iPad last fetched the clipboard at {last.At:T}: {last.What}."
            : "The iPad has not fetched the clipboard yet.";
    }

    private void Status(Wpf.Ui.Controls.InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
