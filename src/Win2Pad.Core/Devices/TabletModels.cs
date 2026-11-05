using Win2Pad.Core.Edge;

namespace Win2Pad.Core.Devices;

/// <summary>A tablet model and its screen in points (landscape), the unit iPadOS moves the pointer in.</summary>
public sealed record TabletModel(string Name, int WidthPoints, int HeightPoints)
{
    /// <summary>Screen size in HID counts at the default pointer speed, for the given orientation.</summary>
    public PointerCalibration Calibration(bool landscape)
    {
        var (w, h) = landscape ? (WidthPoints, HeightPoints) : (HeightPoints, WidthPoints);
        return new((int)Math.Round(w / TabletModels.DefaultPointsPerCount),
                   (int)Math.Round(h / TabletModels.DefaultPointsPerCount));
    }
}

/// <summary>
/// The iPads that support a pointer (iPadOS 13.4 or later), keyed by the model identifier they
/// report over Bluetooth (Device Information, Model Number). Screens are in points, landscape.
/// </summary>
public static class TabletModels
{
    /// <summary>
    /// Points the pointer moves per HID count at iPadOS's default tracking speed and a slow,
    /// unaccelerated movement. Measured on one iPad (7th generation): 1080 points over about
    /// 3320 counts. Assumed to hold for every model because iPadOS moves the pointer in points;
    /// users who changed the tracking speed fine-tune with the Safari calibration.
    /// </summary>
    public const double DefaultPointsPerCount = 0.3225;

    private static readonly TabletModel Small97 = new("9.7-inch", 1024, 768);
    private static readonly TabletModel Ipad102 = new("10.2-inch", 1080, 810);
    private static readonly TabletModel Air105 = new("10.5-inch", 1112, 834);
    private static readonly TabletModel Ipad109 = new("10.9-inch", 1180, 820);
    private static readonly TabletModel Pro11 = new("11-inch", 1194, 834);
    private static readonly TabletModel Pro11M4 = new("11-inch (M4 or later)", 1210, 834);
    private static readonly TabletModel Pro129 = new("12.9 or 13-inch", 1366, 1024);
    private static readonly TabletModel Pro13M4 = new("13-inch (M4 or later)", 1376, 1032);
    private static readonly TabletModel Mini79 = new("mini 7.9-inch", 1024, 768);
    private static readonly TabletModel Mini83 = new("mini 8.3-inch", 1133, 744);

    /// <summary>The distinct screen sizes, for choosing by hand when a model is not recognised.</summary>
    public static IReadOnlyList<TabletModel> ScreenSizes { get; } =
        [Mini79, Mini83, Small97, Ipad102, Air105, Ipad109, Pro11, Pro11M4, Pro129, Pro13M4];

    private static readonly Dictionary<string, (string Name, TabletModel Screen)> ByIdentifier = Build(
        // iPad
        ("iPad (5th generation)", Small97, ["iPad6,11", "iPad6,12"]),
        ("iPad (6th generation)", Small97, ["iPad7,5", "iPad7,6"]),
        ("iPad (7th generation)", Ipad102, ["iPad7,11", "iPad7,12"]),
        ("iPad (8th generation)", Ipad102, ["iPad11,6", "iPad11,7"]),
        ("iPad (9th generation)", Ipad102, ["iPad12,1", "iPad12,2"]),
        ("iPad (10th generation)", Ipad109, ["iPad13,18", "iPad13,19"]),
        ("iPad (A16)", Ipad109, ["iPad15,7", "iPad15,8"]),
        // iPad mini
        ("iPad mini 4", Mini79, ["iPad5,1", "iPad5,2"]),
        ("iPad mini (5th generation)", Mini79, ["iPad11,1", "iPad11,2"]),
        ("iPad mini (6th generation)", Mini83, ["iPad14,1", "iPad14,2"]),
        ("iPad mini (A17 Pro)", Mini83, ["iPad16,1", "iPad16,2"]),
        // iPad Air
        ("iPad Air 2", Small97, ["iPad5,3", "iPad5,4"]),
        ("iPad Air (3rd generation)", Air105, ["iPad11,3", "iPad11,4"]),
        ("iPad Air (4th generation)", Ipad109, ["iPad13,1", "iPad13,2"]),
        ("iPad Air (5th generation)", Ipad109, ["iPad13,16", "iPad13,17"]),
        ("iPad Air 11-inch (M2)", Ipad109, ["iPad14,8", "iPad14,9"]),
        ("iPad Air 13-inch (M2)", Pro129, ["iPad14,10", "iPad14,11"]),
        ("iPad Air 11-inch (M3)", Ipad109, ["iPad15,3", "iPad15,4"]),
        ("iPad Air 13-inch (M3)", Pro129, ["iPad15,5", "iPad15,6"]),
        // iPad Pro
        ("iPad Pro 9.7-inch", Small97, ["iPad6,3", "iPad6,4"]),
        ("iPad Pro 10.5-inch", Air105, ["iPad7,3", "iPad7,4"]),
        ("iPad Pro 12.9-inch (1st generation)", Pro129, ["iPad6,7", "iPad6,8"]),
        ("iPad Pro 12.9-inch (2nd generation)", Pro129, ["iPad7,1", "iPad7,2"]),
        ("iPad Pro 11-inch (1st generation)", Pro11, ["iPad8,1", "iPad8,2", "iPad8,3", "iPad8,4"]),
        ("iPad Pro 12.9-inch (3rd generation)", Pro129, ["iPad8,5", "iPad8,6", "iPad8,7", "iPad8,8"]),
        ("iPad Pro 11-inch (2nd generation)", Pro11, ["iPad8,9", "iPad8,10"]),
        ("iPad Pro 12.9-inch (4th generation)", Pro129, ["iPad8,11", "iPad8,12"]),
        ("iPad Pro 11-inch (3rd generation)", Pro11, ["iPad13,4", "iPad13,5", "iPad13,6", "iPad13,7"]),
        ("iPad Pro 12.9-inch (5th generation)", Pro129, ["iPad13,8", "iPad13,9", "iPad13,10", "iPad13,11"]),
        ("iPad Pro 11-inch (4th generation)", Pro11, ["iPad14,3", "iPad14,4"]),
        ("iPad Pro 12.9-inch (6th generation)", Pro129, ["iPad14,5", "iPad14,6"]),
        ("iPad Pro 11-inch (M4)", Pro11M4, ["iPad16,3", "iPad16,4"]),
        ("iPad Pro 13-inch (M4)", Pro13M4, ["iPad16,5", "iPad16,6"]),
        ("iPad Pro 11-inch (M5)", Pro11M4, ["iPad17,1", "iPad17,2"]),
        ("iPad Pro 13-inch (M5)", Pro13M4, ["iPad17,3", "iPad17,4"]));

    public static IReadOnlyCollection<string> KnownIdentifiers => ByIdentifier.Keys;

    /// <summary>The model for an identifier such as "iPad7,12", or null when it is not in the table.</summary>
    public static TabletModel? Find(string? identifier) =>
        identifier is not null && ByIdentifier.TryGetValue(identifier.Trim(), out var entry)
            ? entry.Screen with { Name = entry.Name }
            : null;

    private static Dictionary<string, (string, TabletModel)> Build(
        params (string Name, TabletModel Screen, string[] Identifiers)[] models)
    {
        var map = new Dictionary<string, (string, TabletModel)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, screen, identifiers) in models)
            foreach (var id in identifiers) map.Add(id, (name, screen));
        return map;
    }
}
