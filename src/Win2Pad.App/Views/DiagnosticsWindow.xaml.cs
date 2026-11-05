using System.Collections.Specialized;
using System.Windows;
using Win2Pad.App.Services;
using Wpf.Ui.Controls;

namespace Win2Pad.App.Views;

/// <summary>
/// The technical side, out of the way of everyday use: encryption, how many hosts subscribed
/// to each report, and the activity log, which can be copied into a bug report.
/// </summary>
public partial class DiagnosticsWindow : FluentWindow
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public DiagnosticsWindow()
    {
        InitializeComponent();
        DataContext = _service;
        _service.Log.CollectionChanged += OnLogChanged;
        Loaded += (_, _) => LogScroller.ScrollToEnd();
        Closed += (_, _) => _service.Log.CollectionChanged -= OnLogChanged;
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) LogScroller.ScrollToEnd();
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, _service.Log);
        if (text.Length > 0) Clipboard.SetText($"Win2Pad {UpdateService.Instance.CurrentVersion}{Environment.NewLine}{text}");
    }
}
