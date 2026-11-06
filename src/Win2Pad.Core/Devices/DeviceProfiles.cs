using System.Text.Json;
using System.Text.Json.Serialization;
using Win2Pad.Core.Calibration;
using Win2Pad.Core.Edge;

namespace Win2Pad.Core.Devices;

/// <summary>Where a device's calibration came from, from least to most precise.</summary>
public enum CalibrationSource { None, Model, Manual, Safari }

/// <summary>Screen size in HID counts for one orientation, and the acceleration headroom to assume.</summary>
public sealed record OrientationCalibration(int Width, int Height, double AwayFactor)
{
    [JsonIgnore]
    public PointerCalibration Pointer => new(Width, Height);
}

/// <summary>Everything remembered about one tablet, keyed by its Bluetooth device id.</summary>
public sealed record DeviceProfile
{
    public required string DeviceId { get; init; }
    /// <summary>The name the tablet reports over Bluetooth, refreshed on every connection.</summary>
    public string? Name { get; init; }

    /// <summary>A name the user gave it; kept across connections.</summary>
    public string? CustomName { get; init; }

    [JsonIgnore]
    public string DisplayName => CustomName ?? ModelName ?? Name ?? "Tablet";

    public string? ModelIdentifier { get; init; }
    public string? ModelName { get; init; }
    public CalibrationSource Source { get; init; }

    /// <summary>
    /// The orientation the tablet is used in. Bluetooth cannot tell, so the user sets it; both
    /// calibrations are kept so switching is instant.
    /// </summary>
    public bool Landscape { get; init; } = true;

    /// <summary>The pointer acceleration measured with Safari; null until fine-tuned.</summary>
    public GainPoints? GainCurve { get; init; }

    /// <summary>How the tablet smooths pointer speed, measured by the fine-tune; null until then.</summary>
    public double? SpeedSmoothing { get; init; }

    /// <summary>How much a single large move is accelerated, measured by the fine-tune; null until then.</summary>
    public double? EntryRatio { get; init; }

    /// <summary>How fast the pointer moves on this tablet, as a multiple of the PC's movement.</summary>
    public double PointerSpeed { get; init; } = 1.0;

    public OrientationCalibration? LandscapeCalibration { get; init; }
    public OrientationCalibration? PortraitCalibration { get; init; }
    public DateTimeOffset LastSeen { get; init; }

    [JsonIgnore]
    public OrientationCalibration? Active => Landscape ? LandscapeCalibration : PortraitCalibration;

    /// <summary>A profile from what the tablet reported over Bluetooth: calibrated from the model table when it is known.</summary>
    public static DeviceProfile FromTablet(string deviceId, TabletInfo? info, double awayFactor = TabletModels.CurveHeadroom)
    {
        var model = TabletModels.Find(info?.ModelIdentifier);
        return new DeviceProfile
        {
            DeviceId = deviceId,
            Name = info?.Name,
            ModelIdentifier = info?.ModelIdentifier,
            ModelName = model?.Name,
            Source = model is null ? CalibrationSource.None : CalibrationSource.Model,
            GainCurve = model is null ? null : TabletModels.DefaultGainCurve,
            LandscapeCalibration = model is null ? null : From(model.Calibration(landscape: true), awayFactor),
            PortraitCalibration = model is null ? null : From(model.Calibration(landscape: false), awayFactor),
            LastSeen = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// Takes a Safari measurement for the orientation it was made in. The other orientation is the
    /// same screen turned round, so its extents swap.
    /// </summary>
    public DeviceProfile WithMeasurement(CalibrationResult result)
    {
        // With the curve the model follows the acceleration, so only a little headroom is left
        // for moves faster than the fastest measured.
        const double headroom = TabletModels.CurveHeadroom;
        var measured = new OrientationCalibration(result.Width, result.Height, headroom);
        var turned = new OrientationCalibration(result.Height, result.Width, headroom);
        return this with
        {
            Source = CalibrationSource.Safari,
            SpeedSmoothing = result.SpeedSmoothing ?? SpeedSmoothing,
            EntryRatio = result.EntryRatio ?? EntryRatio,
            GainCurve = new GainPoints(Edge.GainCurve.FromGains(result.FullCurve.Select(g => (g.CountsPerReport, g.PointsPerCount))).Points),
            Landscape = result.Landscape,
            LandscapeCalibration = result.Landscape ? measured : turned,
            PortraitCalibration = result.Landscape ? turned : measured
        };
    }

    /// <summary>Copies the active calibration into the edge settings; unchanged when there is none.</summary>
    /// <remarks>
    /// A tablet calibrated before curves existed gets the default curve, and with it only the
    /// headroom instead of its old worst-case factor.
    /// </remarks>
    public EdgeOptions ApplyTo(EdgeOptions options) => Active is { } c
        ? options with
        {
            CalibrationWidth = c.Width,
            CalibrationHeight = c.Height,
            AwayFactor = GainCurve is null ? TabletModels.CurveHeadroom : c.AwayFactor,
            GainCurve = WithLargeMoves(GainCurve ?? TabletModels.DefaultGainCurve, EntryRatio),
            SpeedSmoothing = SpeedSmoothing ?? TabletModels.DefaultSpeedSmoothing,
            EntryRatio = EntryRatio ?? TabletModels.DefaultEntryRatio,
            PointerSpeed = PointerSpeed
        }
        : options;

    /// <summary>
    /// A curve measured before large reports were part of it stops at 32 counts, so every larger
    /// report was taken as 5.35x when the tablet moves it about 11x. The single large move the
    /// same fine-tune measured fills that end in.
    /// </summary>
    internal static GainPoints WithLargeMoves(GainPoints curve, double? entryRatio)
    {
        if (entryRatio is not { } ratio || curve.Count == 0 || curve.Max(p => p.CountsPerReport) > 32) return curve;
        return new GainPoints(curve.Append(new GainPoint(85, ratio)));
    }

    private static OrientationCalibration From(PointerCalibration c, double away) => new(c.Width, c.Height, away);
}

/// <summary>The remembered tablets, stored as JSON next to the other settings.</summary>
public sealed class DeviceProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, DeviceProfile> _profiles;

    public DeviceProfileStore(string? path = null)
    {
        _path = path ?? AppPaths.InRoot("devices.json");
        _profiles = Read(_path);
    }

    public event Action? Changed;

    public IReadOnlyList<DeviceProfile> All
    {
        get { lock (_lock) return _profiles.Values.OrderByDescending(p => p.LastSeen).ToList(); }
    }

    public DeviceProfile? Get(string deviceId)
    {
        lock (_lock) return _profiles.GetValueOrDefault(deviceId);
    }

    public void Save(DeviceProfile profile)
    {
        lock (_lock)
        {
            _profiles[profile.DeviceId] = profile;
            Write();
        }
        Changed?.Invoke();
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _profiles.Remove(deviceId);
            if (removed) Write();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_profiles.Values.ToList(), Json));
        File.Move(temp, _path, overwrite: true);
    }

    private static Dictionary<string, DeviceProfile> Read(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<List<DeviceProfile>>(File.ReadAllText(path), Json) is { } list)
                return list.ToDictionary(p => p.DeviceId, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
