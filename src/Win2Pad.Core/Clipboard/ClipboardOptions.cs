using System.Security.Cryptography;
using System.Text.Json;

namespace Win2Pad.Core.Clipboard;

/// <summary>
/// Clipboard sharing with the tablet through an iOS Shortcut. Off by default. The key is part of
/// every address the Shortcut uses, so it stays the same across restarts; a new one locks out a
/// Shortcut that leaked.
/// </summary>
public sealed record ClipboardOptions
{
    public const int DefaultPort = 47820;

    public bool Enabled { get; init; }
    public string Key { get; init; } = "";
    public int Port { get; init; } = DefaultPort;

    /// <summary>Raised after <see cref="Save"/>, so a running server can follow.</summary>
    public static event Action<ClipboardOptions>? Saved;

    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static ClipboardOptions Load(string? path = null)
    {
        path ??= AppPaths.InRoot("clipboard.json");
        ClipboardOptions? loaded = null;
        try
        {
            if (File.Exists(path)) loaded = JsonSerializer.Deserialize<ClipboardOptions>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        loaded ??= new ClipboardOptions();
        if (loaded.Key.Length < 8) loaded = loaded with { Key = NewKey() };
        if (loaded.Port is < 1024 or > 65535) loaded = loaded with { Port = DefaultPort };
        return loaded;
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.InRoot("clipboard.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Saved?.Invoke(this);
    }
}
