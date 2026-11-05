using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using Win2Pad.App.Services;
using Win2Pad.Core;
using Win2Pad.Core.Calibration;
using Win2Pad.Core.Devices;
using Win2Pad.Core.Edge;
using Wpf.Ui.Controls;

namespace Win2Pad.App.Views;

/// <summary>
/// Optional precise calibration: serves a page to Safari on the tablet, drives the tablet pointer
/// and saves what the page measured into the tablet's profile.
/// </summary>
public partial class CalibrationWindow : FluentWindow
{
    private CalibrationServer? _server;
    private CancellationTokenSource? _run;

    public CalibrationWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _server = CalibrationServer.Start();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Cannot start the calibration page", ex.Message);
            return;
        }

        UrlBox.Text = _server.Url;
        QrImage.Source = QrCode(_server.Url);
        _server.PageChanged += () => Dispatcher.BeginInvoke(RefreshPageState);
    }

    private void RefreshPageState()
    {
        if (_server is null || _run is not null) return;
        if (_server.PageConnected && _server.Page is { } page)
        {
            ShowStatus(InfoBarSeverity.Success, "The iPad is ready",
                $"{page.Width}×{page.Height} points, {(page.Landscape ? "landscape" : "portrait")}. Press Start.");
            StartButton.IsEnabled = true;
        }
        else
        {
            ShowStatus(InfoBarSeverity.Informational, "Waiting for the iPad", "Open the page on the iPad.");
            StartButton.IsEnabled = false;
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_server is null) return;
        var service = PeripheralService.Instance;
        var peripheral = service.Peripheral;
        var host = service.Hosts.FirstOrDefault();
        if (peripheral is null || host is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "No iPad connected", "Start the peripheral and connect the iPad over Bluetooth first.");
            return;
        }

        StartButton.IsEnabled = false;
        _run = new CancellationTokenSource();
        EdgeSwitcher.Suspended = true;
        var calibrator = new TabletCalibrator(new HostMouse(peripheral, host.DeviceId), _server, ms => Task.Delay(ms));
        calibrator.Progress += (step, fraction) => Dispatcher.BeginInvoke(() =>
        {
            Progress.Value = fraction;
            ShowStatus(InfoBarSeverity.Informational, "Calibrating…", step);
            _ = _server.SendStatusAsync(step, fraction);
        });

        try
        {
            var result = await Task.Run(() => calibrator.RunAsync(_run.Token));
            var profile = service.Profiles.Get(host.DeviceId)
                          ?? DeviceProfile.FromTablet(host.DeviceId, null) with { Name = host.Name };
            service.Profiles.Save(profile.WithMeasurement(result) with { LastSeen = DateTimeOffset.Now });
            service.ApplyActiveProfile();

            var acceleration = result.HorizontalCurve.Max(s => s.PointsPerCount) / result.SlowGain;
            ShowStatus(InfoBarSeverity.Success, "Calibrated",
                $"{result.Width}×{result.Height} counts ({(result.Landscape ? "landscape" : "portrait")}), " +
                $"pointer acceleration up to {acceleration:0.0}×. Saved for this iPad.");
            await _server.SendStatusAsync("Done - you can close this page", 1, "done");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Progress.Value = 0;
            ShowStatus(InfoBarSeverity.Error, "Calibration failed", ex.Message);
            await _server.SendStatusAsync("Calibration failed - see your PC", Progress.Value, "failed");
        }
        finally
        {
            EdgeSwitcher.Suspended = false;
            _run = null;
            StartButton.IsEnabled = _server.PageConnected;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnClosed(object? sender, EventArgs e)
    {
        _run?.Cancel();
        EdgeSwitcher.Suspended = false;
        if (_server is not null) await _server.DisposeAsync();
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
    }

    private static BitmapImage QrCode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Sends calibration moves to one tablet only, whatever the user's input is doing.</summary>
    private sealed class HostMouse(HidPeripheral peripheral, string deviceId) : ICalibrationMouse
    {
        public async Task MoveAsync(int dx, int dy)
        {
            if (!await peripheral.SendMouseToHostAsync(deviceId, dx, dy))
                throw new CalibrationException("The iPad disconnected from Bluetooth.");
        }
    }
}
