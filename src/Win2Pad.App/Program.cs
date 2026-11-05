using Velopack;
using Win2Pad.App.Services;

namespace Win2Pad.App;

public static class Program
{
    /// <summary>
    /// Velopack runs first: when the installer, an update or the uninstaller starts the app with
    /// one of its hook arguments, it handles it and exits before any window or hook exists.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // A Run value left behind would start a program that no longer exists at every sign-in.
            .OnBeforeUninstallFastCallback(_ => StartupService.Set(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
