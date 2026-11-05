using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Win2Pad.App.Services;

/// <summary>
/// User preferences, persisted next to the background log so the CLI and the UI agree on a home.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly string Path = System.IO.Path.Combine(
        Win2Pad.Core.AppPaths.Root, "app-settings.json");

    public static AppSettings Instance { get; } = Load();

    private bool _closeToTray = true;
    public bool CloseToTray
    {
        get => _closeToTray;
        set { if (Set(ref _closeToTray, value)) Save(); }
    }

    private bool _startPeripheralOnLaunch = true;
    public bool StartPeripheralOnLaunch
    {
        get => _startPeripheralOnLaunch;
        set { if (Set(ref _startPeripheralOnLaunch, value)) Save(); }
    }

    private bool _showIndicator = true;
    /// <summary>A small always-on-top pill showing which screen has the input.</summary>
    public bool ShowIndicator
    {
        get => _showIndicator;
        set { if (Set(ref _showIndicator, value)) Save(); }
    }

    private double? _indicatorLeft, _indicatorTop;
    /// <summary>Where the user dragged the indicator, in WPF units; null for the default corner.</summary>
    public double? IndicatorLeft
    {
        get => _indicatorLeft;
        set { if (Set(ref _indicatorLeft, value)) Save(); }
    }

    public double? IndicatorTop
    {
        get => _indicatorTop;
        set { if (Set(ref _indicatorTop, value)) Save(); }
    }

    private bool _checkForUpdates = true;
    /// <summary>Look for a new version shortly after starting, and download it in the background.</summary>
    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set { if (Set(ref _checkForUpdates, value)) Save(); }
    }

    private bool _swapIsoKeys;
    /// <summary>
    /// Swap the key left of 1 and the extra key left of Z, for tablets that exchange them on
    /// ISO keyboards. Applied to the key map at once.
    /// </summary>
    public bool SwapIsoKeys
    {
        get => _swapIsoKeys;
        set
        {
            if (!Set(ref _swapIsoKeys, value)) return;
            Win2Pad.Core.VirtualKeyMap.SwapIsoKeys = value;
            Save();
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file is not worth blocking startup over.
        }

        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
