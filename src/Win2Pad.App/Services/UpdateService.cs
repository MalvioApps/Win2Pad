using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Velopack;
using Velopack.Sources;

namespace Win2Pad.App.Services;

/// <summary>
/// Updates from the GitHub releases of the project, through Velopack. Only an installed copy can
/// update itself; a copy run from a build folder reports its version and nothing else.
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    /// <summary>Where releases are published. Change it here if the repository moves.</summary>
    public const string RepositoryUrl = "https://github.com/MalvioApps/Win2Pad";

    public static UpdateService Instance { get; } = new();

    private readonly UpdateManager? _manager;
    private UpdateInfo? _ready;

    private UpdateService()
    {
        try
        {
            // Only needed while the repository is private: GitHub serves private release assets to
            // authorised users alone. Public releases need no token.
            var token = Environment.GetEnvironmentVariable("WIN2PAD_UPDATE_TOKEN");
            _manager = new UpdateManager(new GithubSource(RepositoryUrl,
                accessToken: string.IsNullOrWhiteSpace(token) ? null : token.Trim(), prerelease: false));
        }
        catch (Exception)
        {
            // Not installed through Velopack (a development build): updating is unavailable.
            _manager = null;
        }
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "unknown";

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    /// <summary>True once a new version is downloaded and only needs a restart.</summary>
    public bool IsReady => _ready is not null;

    /// <summary>Looks for a new version and downloads it. Returns the new version, or null.</summary>
    public async Task<string?> CheckAsync()
    {
        if (_manager is null || !_manager.IsInstalled)
        {
            Status = "Updates work once Win2Pad is installed with its setup program.";
            return null;
        }
        if (IsBusy) return null;

        IsBusy = true;
        try
        {
            Status = "Checking for updates…";
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Status = $"Win2Pad {CurrentVersion} is the latest version.";
                return null;
            }

            var version = update.TargetFullRelease.Version.ToString();
            Status = $"Downloading Win2Pad {version}…";
            await _manager.DownloadUpdatesAsync(update, progress => Status = $"Downloading Win2Pad {version}… {progress}%");
            _ready = update;
            OnPropertyChanged(nameof(IsReady));
            Status = $"Win2Pad {version} is ready. Restart to finish updating.";
            return version;
        }
        catch (Exception ex)
        {
            Status = $"Could not check for updates: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Installs a downloaded version once the app has exited, without restarting it.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _ready is null) return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
        }
        catch (Exception)
        {
            // Not fatal: the download stays and is offered again on the next start.
        }
    }

    /// <summary>Installs the downloaded version and starts it again.</summary>
    public void RestartToUpdate()
    {
        if (_manager is null || _ready is null) return;
        _manager.ApplyUpdatesAndRestart(_ready);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
