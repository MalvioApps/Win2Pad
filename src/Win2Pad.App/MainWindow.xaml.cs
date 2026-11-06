using System.ComponentModel;
using Win2Pad.App.Services;
using Win2Pad.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Win2Pad.App;

public partial class MainWindow : FluentWindow
{
    private bool _warnedAboutTray;

    /// <param name="startPage">The page to open on; Home when null.</param>
    public MainWindow(Type? startPage = null)
    {
        InitializeComponent();
        // Applies the Mica backdrop and keeps tracking light/dark changes made while running.
        SystemThemeWatcher.Watch(this);
        Loaded += (_, _) => RootNavigation.Navigate(startPage ?? typeof(StatusPage));
    }

    /// <summary>Opens a page by name: <c>Win2Pad.exe --page screens</c>.</summary>
    public void ShowPage(string name)
    {
        if (PageType(name) is { } page) RootNavigation.Navigate(page);
    }

    /// <summary>The page named by <c>--page name</c> in the command line, if any.</summary>
    public static Type? PageFromArgs(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], "--page", StringComparison.OrdinalIgnoreCase)) return PageType(args[i + 1]);
        return null;
    }

    private static Type? PageType(string name) => name.ToLowerInvariant() switch
    {
        "home" or "status" => typeof(StatusPage),
        "screens" or "arrangement" => typeof(ArrangementPage),
        "devices" => typeof(DevicesPage),
        "keyboard" or "capture" => typeof(CapturePage),
        "clipboard" => typeof(ClipboardPage),
        "settings" => typeof(SettingsPage),
        "about" => typeof(AboutPage),
        _ => null
    };

    // Staying resident keeps the GATT attribute table alive, which is what hosts reconnect to.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.Current.IsExiting)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (!AppSettings.Instance.CloseToTray)
        {
            App.Current.ExitApplication();
            return;
        }

        Hide();
        if (_warnedAboutTray) return;
        _warnedAboutTray = true;
        App.Current.Tray?.ShowMessage("Still running. Right-click the tray icon to exit.");
    }
}
