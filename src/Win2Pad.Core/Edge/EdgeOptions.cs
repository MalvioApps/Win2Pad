using System.Text.Json;
using System.Text.Json.Serialization;

namespace Win2Pad.Core.Edge;

/// <summary>
/// The tablet against one side of one monitor. <see cref="Offset"/> and <see cref="Length"/> are
/// where the tablet's edge starts and how long it is along that side, in desktop pixels (x for
/// Top/Bottom, y for Left/Right). The tablet may overhang the monitor; only the overlap crosses.
/// </summary>
public sealed record TabletPlacement(ScreenRect Monitor, TabletSide Side, int Offset, int Length, double Scale = 1)
{
    /// <summary>
    /// The line the tablet sits against: the y of a top or bottom edge, the x of a left or right
    /// one. Every monitor whose edge on that side lies on it, side by side, can lead to the tablet.
    /// </summary>
    [JsonIgnore]
    public int Line => EdgeLine(Monitor, Side);

    public static int EdgeLine(ScreenRect monitor, TabletSide side) => side switch
    {
        TabletSide.Top => monitor.Top,
        TabletSide.Bottom => monitor.Bottom,
        TabletSide.Left => monitor.Left,
        _ => monitor.Right
    };

    [JsonIgnore]
    public int MonitorStart => Side is TabletSide.Top or TabletSide.Bottom ? Monitor.Left : Monitor.Top;
    [JsonIgnore]
    public int MonitorEnd => Side is TabletSide.Top or TabletSide.Bottom ? Monitor.Right : Monitor.Bottom;

    /// <summary>Where the pointer can cross: the stretch of the monitor's side the tablet touches.</summary>
    [JsonIgnore]
    public (int Start, int End) Overlap => (Math.Max(Offset, MonitorStart), Math.Min(Offset + Length, MonitorEnd));

    [JsonIgnore]
    public bool Touches => Overlap.End > Overlap.Start;
}

/// <summary>Edge switching settings, stored as JSON next to the other settings.</summary>
public sealed record EdgeOptions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => AppPaths.InRoot("edge-switching.json");

    /// <summary>Raised in this process after a successful save, so a running session picks it up.</summary>
    public static event Action<EdgeOptions>? Saved;

    /// <summary>The settings format; older files get tuning values whose meaning changed reset.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Where the tablet sits relative to the PC screens when no placement is set: any monitor
    /// edge on that side with no other monitor beyond it leads to the tablet.
    /// </summary>
    public TabletSide Side { get; init; } = TabletSide.Right;

    /// <summary>
    /// The tablet placed against one side of one monitor by the arrangement editor. When set, the
    /// pointer crosses only where the two touch, and positions map along the tablet's own edge.
    /// </summary>
    public TabletPlacement? Placement { get; init; }

    [JsonIgnore]
    public TabletSide EffectiveSide => Placement?.Side ?? Side;

    /// <summary>
    /// The tablet's measured pointer acceleration, from the Safari calibration. With it the
    /// pointer model follows fast moves; without it, it assumes <see cref="AwayFactor"/> for all.
    /// </summary>
    public GainPoints? GainCurve { get; init; }

    /// <summary>Multiplies the movement sent to the tablet: above 1 the pointer moves faster there.</summary>
    public double PointerSpeed { get; init; } = 1.0;

    /// <summary>
    /// When the way back is through the tablet's top or bottom edge: moving towards it quickly
    /// returns this share of the screen before reaching it. Off by default: it made any quick
    /// slide upwards near the top a return. Kept for whoever prefers it.
    /// </summary>
    public double VerticalReturnLead { get; init; }

    /// <summary>
    /// How far past the top or bottom edge to push to come back: a deliberate push, but short
    /// enough that the tablet's own edge gestures (notifications, Dock) do not open first.
    /// </summary>
    public double VerticalReturnMargin { get; init; } = 0.03;

    /// <summary>Counts per report towards the edge that count as heading for it quickly.</summary>
    public int LeadMinSpeed { get; init; } = 4;

    /// <summary>
    /// How far into the tablet the pointer must go after entering before it can come back:
    /// a share of the screen, larger for the top and bottom where the pointer enters next to an
    /// edge the user also moves along.
    /// </summary>
    public double VerticalReturnArm { get; init; } = 0.08;
    public double SideReturnArm { get; init; } = 0.03;

    /// <summary>
    /// The user's choice of how firm the push to come back is, multiplying the return margins:
    /// below 1 lighter, above 1 firmer.
    /// </summary>
    public double ReturnPushScale { get; init; } = 1.0;

    /// <summary>The pointer model these settings describe, for a tablet on <paramref name="side"/>.</summary>
    public PointerModel CreateModel(TabletSide side)
    {
        var vertical = side is TabletSide.Top or TabletSide.Bottom;
        var curve = GainCurve is { Count: > 0 } points ? new GainCurve(points) : null;
        var push = Math.Clamp(ReturnPushScale, 0.25, 4);
        return new PointerModel(Calibration, side,
            returnMargin: (vertical ? VerticalReturnMargin : ReturnMargin) * push,
            awayFactor: AwayFactor,
            gainCurve: curve,
            returnLead: vertical ? VerticalReturnLead : 0,
            leadMinSpeed: LeadMinSpeed,
            earlyReturn: vertical,
            returnArmDistance: vertical ? VerticalReturnArm : SideReturnArm);
    }

    /// <summary>Tablet screen size in HID counts at slow speed. Zero means not calibrated.</summary>
    public int CalibrationWidth { get; init; }
    public int CalibrationHeight { get; init; }

    /// <summary>Raw mouse counts pushed against the PC edge to switch to the tablet.</summary>
    public int EntryPushCounts { get; init; } = 15;

    /// <summary>Fraction of the tablet extent to push past its edge to come back.</summary>
    public double ReturnMargin { get; init; } = 0.04;

    /// <summary>Worst-case tablet acceleration assumed for moves away from the return edge.</summary>
    public double AwayFactor { get; init; } = 2.4;

    /// <summary>After entering the tablet, no return can trigger for this long.</summary>
    public double ExitGraceSeconds { get; init; } = 0.5;

    /// <summary>After coming back, pushing the edge again does not re-enter for this long.</summary>
    public double ReentryGraceSeconds { get; init; } = 0.4;

    /// <summary>How far inside the PC screen the pointer lands when it comes back, in pixels.</summary>
    public int ReturnGapPixels { get; init; } = 8;

    [JsonIgnore]
    public PointerCalibration Calibration => new(CalibrationWidth, CalibrationHeight);

    public static EdgeOptions Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new EdgeOptions();
            var loaded = JsonSerializer.Deserialize<EdgeOptions>(File.ReadAllText(path), Json) ?? new EdgeOptions();
            return Migrate(loaded, ReadVersion(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new EdgeOptions();
        }
    }

    /// <summary>
    /// Brings settings written by an older version up to date. Version 1 (no version field)
    /// returned at the tablet's top and bottom too eagerly: its early zone and tiny margin made a
    /// small slide upwards a return, so those two go back to the current defaults.
    /// </summary>
    internal static EdgeOptions Migrate(EdgeOptions options, int fileVersion)
    {
        if (fileVersion >= CurrentVersion) return options;
        var defaults = new EdgeOptions();
        return options with
        {
            Version = CurrentVersion,
            VerticalReturnLead = defaults.VerticalReturnLead,
            VerticalReturnMargin = defaults.VerticalReturnMargin
        };
    }

    /// <summary>The version written in the file; 1 for files from before the field existed.</summary>
    private static int ReadVersion(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty(nameof(Version), out var v) && v.TryGetInt32(out var version) ? version : 1;
    }

    public bool TrySave(string? path, out string? error)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, path, overwrite: true);
            error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }

        if (path == DefaultPath) Saved?.Invoke(this);
        return true;
    }
}
