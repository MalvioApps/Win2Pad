using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Win2Pad.App.Services;
using Win2Pad.Core;

namespace Win2Pad.App.Views;

/// <summary>Version, the project on GitHub, and where to report a problem.</summary>
public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var updates = UpdateService.Instance;
        VersionText.Text = $"Version {updates.CurrentVersion}{(updates.IsInstalled ? "" : " (development build)")}";
    }

    private void OnRepository(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);

    // GitHub has no link that stars a repository by itself; the Star button is on its page.
    private void OnStar(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Repository);

    private void OnReport(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.NewIssue());

    private void OnCredits(object sender, RoutedEventArgs e) => ProjectLinks.Open(ProjectLinks.Credits);

    private void OnLogs(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
}
