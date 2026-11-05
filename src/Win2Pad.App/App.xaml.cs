using System.IO;
using System.Windows;
using System.Windows.Threading;
using Win2Pad.App.Services;
using Win2Pad.App.Views;
using Win2Pad.Core;
using Wpf.Ui.Appearance;

namespace Win2Pad.App;

public partial class App : Application
{
    private static readonly string LogPath = AppPaths.InLogs("win2pad.log");

    private Mutex? _instance;
    private EventWaitHandle? _stopRequest;
    private RegisteredWaitHandle? _stopRegistration;
    private bool _exiting;

    public new static App Current => (App)Application.Current;

    public TrayIconService? Tray { get; private set; }

    public IndicatorWindow? Indicator { get; private set; }

    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryTakeOwnership())
        {
            Shutdown();
            return;
        }

        // Lets a second launch ask this instance to stop.
        _stopRequest = new EventWaitHandle(false, EventResetMode.ManualReset, SingleInstance.StopEventName);
        // A named event that still exists ignores the initial state above, so the stop request that
        // retired the previous owner would fire this one immediately.
        _stopRequest.Reset();
        _stopRegistration = ThreadPool.RegisterWaitForSingleObject(
            _stopRequest, (_, _) => Dispatcher.Invoke(ExitApplication), null, Timeout.Infinite,
            executeOnlyOnce: true);

        ApplicationThemeManager.ApplySystemTheme();
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception);
        base.OnStartup(e);

        Tray = new TrayIconService(ShowMainWindow, ExitApplication, ExtendTrayMenu);
        Indicator = new IndicatorWindow();
        ApplyIndicatorVisibility();
        AppSettings.Instance.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppSettings.ShowIndicator)) Dispatcher.Invoke(ApplyIndicatorVisibility);
        };

        // The tray and the indicator are the only surfaces visible while the window is hidden,
        // which is exactly when a lock left on a remote host would otherwise go unnoticed.
        var peripheral = PeripheralService.Instance;
        peripheral.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(peripheral.Target) or nameof(peripheral.TargetLockWarning))
                Dispatcher.Invoke(() => Tray?.UpdateStatus(peripheral.Target, peripheral.TargetLockWarning));

            if (args.PropertyName is nameof(peripheral.IsRunning) or nameof(peripheral.KeyboardSubscribers)
                or nameof(peripheral.IsRedirecting) or nameof(peripheral.ActiveProfile))
                Dispatcher.Invoke(UpdateLinkState);
        };

        var startHidden = e.Args.Contains(StartupService.TrayArgument, StringComparer.OrdinalIgnoreCase);
        MainWindow = new MainWindow(Win2Pad.App.MainWindow.PageFromArgs(e.Args));
        if (!startHidden) MainWindow.Show();

        _ = CheckForUpdatesLaterAsync();

        // Auto-start matters most for the hidden launch, where there is no window to press Start in.
        if (startHidden) _ = StartResidentAsync();
        else if (AppSettings.Instance.StartPeripheralOnLaunch) _ = PeripheralService.Instance.StartAsync();
    }

    /// <summary>
    /// A while after starting, so the check never competes with the Bluetooth start-up: download a
    /// new version quietly; it installs when the app next closes.
    /// </summary>
    private async Task CheckForUpdatesLaterAsync()
    {
        if (!AppSettings.Instance.CheckForUpdates || !UpdateService.Instance.IsInstalled) return;
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (await UpdateService.Instance.CheckAsync() is { } version)
            Tray?.ShowMessage($"Win2Pad {version} is downloaded. It installs when Win2Pad closes, or restart it from Settings.");
    }

    private void UpdateLinkState()
    {
        var peripheral = PeripheralService.Instance;
        var state = LinkStates.From(peripheral.IsRunning, peripheral.KeyboardSubscribers, peripheral.IsRedirecting);
        Tray?.SetState(state);
        Indicator?.Update(state, peripheral.ActiveProfile?.DisplayName);
    }

    private void ApplyIndicatorVisibility()
    {
        if (Indicator is null) return;
        if (AppSettings.Instance.ShowIndicator) Indicator.Show();
        else Indicator.Hide();
    }

    /// <summary>Quick actions in the tray menu, refreshed each time it opens.</summary>
    private void ExtendTrayMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        var indicator = new System.Windows.Forms.ToolStripMenuItem("Show floating indicator");
        indicator.Click += (_, _) => AppSettings.Instance.ShowIndicator = !AppSettings.Instance.ShowIndicator;

        var portrait = new System.Windows.Forms.ToolStripMenuItem("Tablet in portrait");
        portrait.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            var service = PeripheralService.Instance;
            if (service.ActiveProfile is not { } profile) return;
            service.Profiles.Save(profile with { Landscape = !profile.Landscape });
            service.ApplyActiveProfile();
        });

        var fineTune = new System.Windows.Forms.ToolStripMenuItem("Fine-tune with Safari…");
        fineTune.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            ShowMainWindow();
            new Views.CalibrationWindow { Owner = MainWindow }.ShowDialog();
        });

        var report = new System.Windows.Forms.ToolStripMenuItem("Report a problem…");
        report.Click += (_, _) => ProjectLinks.Open(ProjectLinks.NewIssue());

        menu.Items.Add(indicator);
        menu.Items.Add(portrait);
        menu.Items.Add(fineTune);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(report);
        menu.Opening += (_, _) =>
        {
            var profile = PeripheralService.Instance.ActiveProfile;
            indicator.Checked = AppSettings.Instance.ShowIndicator;
            portrait.Enabled = profile is not null;
            portrait.Checked = profile is { Landscape: false };
            fineTune.Enabled = PeripheralService.Instance.Hosts.Count > 0;
        };
    }

    // A tray-only launch has no UI to arm capture from, so the hotkeys have to work on their own.
    private static async Task StartResidentAsync()
    {
        await PeripheralService.Instance.StartAsync();
        await PeripheralService.Instance.StartCaptureAsync(armEarly: true);
    }

    /// <summary>
    /// The holder may be another copy of this app or the CLI's background service; either way it
    /// owns the radio and has to release it before this process can advertise.
    /// </summary>
    private bool TryTakeOwnership()
    {
        _instance = new Mutex(true, SingleInstance.MutexName, out var owned);
        if (owned) return true;

        var answer = MessageBox.Show(
            "Win2Pad is already running, either in the notification area or as the command line "
            + "background service.\n\nStop it and continue?",
            "Win2Pad", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;

        try
        {
            using var stop = EventWaitHandle.OpenExisting(SingleInstance.StopEventName);
            stop.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            MessageBox.Show(
                "The running instance is too old to accept a stop request. Exit it manually.",
                "Win2Pad", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        for (var attempt = 0; attempt < 50 && !owned; attempt++)
        {
            Thread.Sleep(100);
            _instance.Dispose();
            _instance = new Mutex(true, SingleInstance.MutexName, out owned);
        }

        if (!owned)
            MessageBox.Show("The running instance did not stop in time.",
                "Win2Pad", MessageBoxButton.OK, MessageBoxImage.Warning);

        return owned;
    }

    public void ShowMainWindow()
    {
        MainWindow ??= new MainWindow();
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        await PeripheralService.Instance.StopAsync();
        UpdateService.Instance.ApplyOnExit();
        Tray?.Dispose();
        Indicator?.Close();
        // Shutdown closes windows, so it must not run inside a close that is still unwinding.
        await Dispatcher.InvokeAsync(Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopRegistration?.Unregister(null);
        _stopRequest?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    // A XAML error on one page used to terminate the process with no output at all.
    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        MessageBox.Show($"{e.Exception.Message}\n\nLogged to {LogPath}",
            "Win2Pad", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void Report(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            // The tray build can run for weeks, so the log cannot grow without a bound.
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024) File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:s}  {ex}\n\n");
        }
        catch (IOException) { }
    }
}
