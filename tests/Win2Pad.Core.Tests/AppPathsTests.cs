using Win2Pad.Core;
using Xunit;

namespace Win2Pad.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Root_lives_under_local_application_data()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, AppPaths.Root, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(localAppData, "MalvioApps", "Win2Pad"), AppPaths.Root);
    }

    [Fact]
    public void Settings_never_share_the_installer_folder()
    {
        // The installer owns %LOCALAPPDATA%\Win2Pad and deletes it on uninstall.
        var installFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win2Pad");
        Assert.False(AppPaths.Root.StartsWith(installFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(installFolder, AppPaths.Root, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_settings_are_copied_once_and_never_overwrite()
    {
        var legacy = Path.Combine(Path.GetTempPath(), $"w2p-legacy-{Guid.NewGuid():N}");
        var root = Path.Combine(Path.GetTempPath(), $"w2p-root-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "devices.json"), "old devices");
            File.WriteAllText(Path.Combine(legacy, "edge-switching.json"), "old edge");
            File.WriteAllText(Path.Combine(legacy, "something-else.txt"), "not a setting");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "edge-switching.json"), "new edge");

            Assert.Equal(1, AppPaths.MigrateLegacySettings(legacy, root));
            Assert.Equal("old devices", File.ReadAllText(Path.Combine(root, "devices.json")));
            Assert.Equal("new edge", File.ReadAllText(Path.Combine(root, "edge-switching.json")));
            Assert.False(File.Exists(Path.Combine(root, "something-else.txt")));
            Assert.Equal(0, AppPaths.MigrateLegacySettings(legacy, root));
        }
        finally
        {
            if (Directory.Exists(legacy)) Directory.Delete(legacy, recursive: true);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Logs_is_a_subfolder_of_root()
    {
        Assert.Equal(AppPaths.Root, Path.GetDirectoryName(AppPaths.Logs));
        Assert.Equal("logs", Path.GetFileName(AppPaths.Logs));
    }

    [Fact]
    public void InLogs_creates_the_directory_and_returns_a_path_inside_it()
    {
        var path = AppPaths.InLogs("unit-test-probe.txt");

        Assert.True(Directory.Exists(AppPaths.Logs));
        Assert.Equal(AppPaths.Logs, Path.GetDirectoryName(path));
    }

    [Fact]
    public void InRoot_keeps_settings_out_of_the_logs_folder()
    {
        var path = AppPaths.InRoot("app-settings.json");

        Assert.Equal(AppPaths.Root, Path.GetDirectoryName(path));
        Assert.NotEqual(AppPaths.Logs, Path.GetDirectoryName(path));
    }

    /// <summary>Every log a user is asked to send must sit in one folder they can zip.</summary>
    [Fact]
    public void All_known_log_files_resolve_into_the_logs_folder()
    {
        foreach (var name in new[] { "win2pad-cli.log", "win2pad.log", "diagnostics.txt" })
            Assert.Equal(AppPaths.Logs, Path.GetDirectoryName(AppPaths.InLogs(name)));
    }
}
