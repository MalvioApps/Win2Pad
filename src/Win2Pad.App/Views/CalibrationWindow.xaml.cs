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
    private CancellationTokenSource? _countdown;
    private bool _autoStarted;

    private readonly string? _deviceId;

    /// <param name="deviceId">The tablet to calibrate; the first connected one when null.</param>
    public CalibrationWindow(string? deviceId = null)
    {
        _deviceId = deviceId;
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
                $"{page.Width}×{page.Height} points, {(page.Landscape ? "landscape" : "portrait")}. Starting by itself…");
            StartButton.IsEnabled = true;
            _ = StartAfterCountdownAsync();
        }
        else
        {
            _countdown?.Cancel();
            ShowStatus(InfoBarSeverity.Informational, "Waiting for the iPad", "Open the page on the iPad.");
            StartButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Scanning the code is the user's go-ahead: once the page is open the measurement starts by
    /// itself, after a short countdown on the iPad so it can be put down.
    /// </summary>
    private async Task StartAfterCountdownAsync()
    {
        // Once per window: the page reports again on every resize, and a finished or failed run
        // must not restart by itself. Start is there to measure again.
        if (_server is null || _run is not null || _countdown is not null || _autoStarted) return;
        _autoStarted = true;
        _countdown = new CancellationTokenSource();
        var token = _countdown.Token;
        try
        {
            for (var left = 3; left > 0; left--)
            {
                await _server.SendStatusAsync($"Starting in {left}… don't touch the iPad", 0);
                await Task.Delay(1000, token);
            }
            OnStart(this, new RoutedEventArgs());
        }
        catch (OperationCanceledException) { }
        finally
        {
            _countdown = null;
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_server is null || _run is not null) return;
        _countdown?.Cancel();
        var service = PeripheralService.Instance;
        var peripheral = service.Peripheral;
        var host = _deviceId is null
            ? service.Hosts.FirstOrDefault()
            : service.Hosts.FirstOrDefault(h => string.Equals(h.DeviceId, _deviceId, StringComparison.OrdinalIgnoreCase));
        if (peripheral is null || host is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "iPad not connected", "Start the peripheral and connect this iPad over Bluetooth first.");
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
                $"pointer acceleration up to {acceleration:0.0}×" +
                (result.FlickError is { } error ? $", quick movements predicted within {error:P0}" : "") +
                (result.EntryRatio is { } entry ? $", a single large move travels {entry:0.0}×" : "") +
                ". Saved for this iPad.");
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
        _countdown?.Cancel();
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

    internal static BitmapImage QrCode(string text)
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

        public async Task ClickAsync()
        {
            await peripheral.SendMouseToHostAsync(deviceId, 0, 0, MouseButtons.Left);
            await Task.Delay(60);
            await peripheral.SendMouseToHostAsync(deviceId, 0, 0, MouseButtons.None);
        }
    }
}
