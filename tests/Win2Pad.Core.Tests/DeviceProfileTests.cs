using Win2Pad.Core.Calibration;
using Win2Pad.Core.Devices;
using Win2Pad.Core.Edge;
using Xunit;

namespace Win2Pad.Core.Tests;

public sealed class DeviceProfileTests
{
    private const string Id = "BluetoothLE#BluetoothLE00:11:22:33:44:55-66:77:88:99:aa:bb";

    [Theory]
    [InlineData("iPad7,12", "iPad (7th generation)", 1080, 810)]
    [InlineData("iPad16,5", "iPad Pro 13-inch (M4)", 1376, 1032)]
    [InlineData("iPad14,1", "iPad mini (6th generation)", 1133, 744)]
    [InlineData(" ipad13,18 ", "iPad (10th generation)", 1180, 820)]
    public void Known_models_resolve_to_their_screen(string identifier, string name, int w, int h)
    {
        var model = TabletModels.Find(identifier);
        Assert.NotNull(model);
        Assert.Equal((name, w, h), (model.Name, model.WidthPoints, model.HeightPoints));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("iPhone15,2")]
    [InlineData("iPad99,1")]
    public void Unknown_models_are_not_guessed(string? identifier) => Assert.Null(TabletModels.Find(identifier));

    [Fact]
    public void Every_identifier_is_unique_and_landscape()
    {
        Assert.True(TabletModels.KnownIdentifiers.Count > 60);
        foreach (var id in TabletModels.KnownIdentifiers)
        {
            var model = TabletModels.Find(id)!;
            Assert.True(model.WidthPoints > model.HeightPoints, id);
        }
    }

    [Fact]
    public void The_model_calibration_matches_the_one_measured_by_hand()
    {
        // BlueShare's click calibration on this iPad gave 3318 x 2536.
        var c = TabletModels.Find("iPad7,12")!.Calibration(landscape: true);
        Assert.InRange(c.Width, 3318 - 70, 3318 + 70);
        Assert.InRange(c.Height, 2536 - 70, 2536 + 70);

        var portrait = TabletModels.Find("iPad7,12")!.Calibration(landscape: false);
        Assert.Equal((c.Height, c.Width), (portrait.Width, portrait.Height));
    }

    [Fact]
    public void A_recognised_tablet_gets_both_orientations()
    {
        var profile = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad7,12"));
        Assert.Equal(CalibrationSource.Model, profile.Source);
        Assert.Equal("iPad (7th generation)", profile.ModelName);
        Assert.NotNull(profile.LandscapeCalibration);
        Assert.NotNull(profile.PortraitCalibration);
        Assert.Same(profile.LandscapeCalibration, profile.Active);
        Assert.Same(profile.PortraitCalibration, (profile with { Landscape = false }).Active);
    }

    [Fact]
    public void An_unrecognised_tablet_is_remembered_without_a_calibration()
    {
        var profile = DeviceProfile.FromTablet(Id, new TabletInfo("Tab", "Samsung", "SM-X200"));
        Assert.Equal(CalibrationSource.None, profile.Source);
        Assert.Null(profile.Active);
        var options = new EdgeOptions { CalibrationWidth = 10, CalibrationHeight = 20 };
        Assert.Same(options, profile.ApplyTo(options));
    }

    [Fact]
    public void A_safari_measurement_replaces_the_model_and_turns_for_the_other_orientation()
    {
        var profile = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad7,12"));
        var result = new CalibrationResult(3300, 2500, 2.6, Landscape: false, 70, [new GainSample(2, 0.33)], 0.33);

        var measured = profile.WithMeasurement(result);

        Assert.Equal(CalibrationSource.Safari, measured.Source);
        Assert.False(measured.Landscape);
        Assert.Equal(new OrientationCalibration(3300, 2500, 1.15), measured.PortraitCalibration);
        Assert.Single(measured.GainCurve!);
        Assert.Equal(new OrientationCalibration(2500, 3300, 1.15), measured.LandscapeCalibration);
    }

    [Fact]
    public void The_active_calibration_is_copied_into_the_edge_settings()
    {
        var profile = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad7,12"), awayFactor: 3.1);
        var applied = profile.ApplyTo(new EdgeOptions { Side = TabletSide.Left });
        Assert.Equal(TabletSide.Left, applied.Side);
        Assert.Equal(profile.Active!.Width, applied.CalibrationWidth);
        Assert.Equal(3.1, applied.AwayFactor);
    }

    [Fact]
    public void A_recognised_tablet_gets_the_default_acceleration_curve()
    {
        var profile = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad16,5"));
        Assert.Equal(TabletModels.DefaultGainCurve, profile.GainCurve);
        Assert.Equal(TabletModels.CurveHeadroom, profile.Active!.AwayFactor);
    }

    [Fact]
    public void A_profile_from_before_curves_gets_the_default_and_only_the_headroom()
    {
        var old = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad7,12"), awayFactor: 6) with { GainCurve = null };
        var applied = old.ApplyTo(new EdgeOptions());
        Assert.Equal(TabletModels.DefaultGainCurve, applied.GainCurve);
        Assert.Equal(TabletModels.CurveHeadroom, applied.AwayFactor);
    }

    [Fact]
    public void Curves_compare_by_content_so_unchanged_settings_are_equal()
    {
        var a = new EdgeOptions { GainCurve = TabletModels.DefaultGainCurve };
        var b = new EdgeOptions { GainCurve = TabletModels.DefaultGainCurve };
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { GainCurve = [new GainPoint(2, 1)] });
    }

    [Fact]
    public void The_store_round_trips_and_removes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-devices-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DeviceProfileStore(path);
            var profile = DeviceProfile.FromTablet(Id, new TabletInfo("iPad", "Apple Inc.", "iPad7,12"));
            store.Save(profile);

            var reloaded = new DeviceProfileStore(path).Get(Id.ToUpperInvariant());
            Assert.Equal(profile, reloaded);

            Assert.True(store.Remove(Id));
            Assert.Empty(new DeviceProfileStore(path).All);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_corrupt_store_starts_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"win2pad-devices-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "[ broken");
        try
        {
            Assert.Empty(new DeviceProfileStore(path).All);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
