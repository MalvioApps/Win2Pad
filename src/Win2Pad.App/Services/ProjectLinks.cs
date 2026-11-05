using System.Diagnostics;

namespace Win2Pad.App.Services;

/// <summary>The project's pages on GitHub, opened in the default browser.</summary>
public static class ProjectLinks
{
    public static string Repository => UpdateService.RepositoryUrl;
    public static string Releases => $"{Repository}/releases";
    public static string Credits => $"{Repository}/blob/main/NOTICE.md";

    /// <summary>
    /// A new bug report with what the app knows filled in (version, Windows, the iPad model), so
    /// the reporter only has to describe what happened. GitHub issue forms take field values as
    /// query parameters named after the field ids in .github/ISSUE_TEMPLATE/bug_report.yml.
    /// </summary>
    public static string NewIssue()
    {
        var profile = PeripheralService.Instance.ActiveProfile;
        var ipad = profile is null ? "" : $"{profile.ModelName ?? "unknown"} ({profile.ModelIdentifier ?? "no model reported"}), {(profile.Landscape ? "landscape" : "portrait")}, {profile.Source} calibration";
        var query = new Dictionary<string, string>
        {
            ["template"] = "bug_report.yml",
            ["version"] = UpdateService.Instance.CurrentVersion,
            ["windows"] = Environment.OSVersion.VersionString,
            ["ipad"] = ipad
        };
        return $"{Repository}/issues/new?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser registered: nothing useful to do from here.
        }
    }
}
