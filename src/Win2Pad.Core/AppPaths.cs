namespace Win2Pad.Core;

/// <summary>
/// One home for everything Win2Pad writes: settings, remembered devices and logs.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// %LOCALAPPDATA%\MalvioApps\Win2Pad. Not %LOCALAPPDATA%\Win2Pad: that is where the installer
    /// puts the program itself, and uninstalling or reinstalling deletes that folder whole.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MalvioApps", "Win2Pad");

    /// <summary>Where early development builds kept their settings, before there was an installer.</summary>
    public static string LegacyRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win2Pad");

    private static readonly string[] SettingsFiles =
        ["app-settings.json", "devices.json", "edge-switching.json", "switch-gesture.json", "pointer-pacing.json"];

    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>Returns the full path, having created the directory it lives in.</summary>
    public static string InLogs(string fileName)
    {
        Directory.CreateDirectory(Logs);
        return Path.Combine(Logs, fileName);
    }

    public static string InRoot(string fileName)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, fileName);
    }

    /// <summary>Path of the pointer switch gesture configuration file.</summary>
    public static string SwitchGestureConfig => InRoot("switch-gesture.json");

    /// <summary>
    /// Copies settings from the old location the first time, so nothing set up during
    /// development is lost. Never overwrites: once a file exists in the new home it wins.
    /// Returns how many files were brought over.
    /// </summary>
    public static int MigrateLegacySettings(string? legacyRoot = null, string? root = null)
    {
        legacyRoot ??= LegacyRoot;
        root ??= Root;
        var copied = 0;
        foreach (var name in SettingsFiles)
        {
            var from = Path.Combine(legacyRoot, name);
            var to = Path.Combine(root, name);
            if (!File.Exists(from) || File.Exists(to)) continue;
            try
            {
                Directory.CreateDirectory(root);
                File.Copy(from, to);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return copied;
    }
}
