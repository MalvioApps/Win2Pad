using System.Collections.Specialized;
using System.Windows.Controls;
using Win2Pad.App.Services;

namespace Win2Pad.App.Views;

public partial class StatusPage : Page
{
    private readonly PeripheralService _service = PeripheralService.Instance;

    public StatusPage()
    {
        InitializeComponent();
        DataContext = _service;
        _service.Log.CollectionChanged += OnLogChanged;
        Unloaded += (_, _) => _service.Log.CollectionChanged -= OnLogChanged;
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) LogScroller.ScrollToEnd();
    }

    private async void OnStart(object sender, System.Windows.RoutedEventArgs e) =>
        await _service.StartAsync();

    private async void OnStop(object sender, System.Windows.RoutedEventArgs e) =>
        await _service.StopAsync();
}
