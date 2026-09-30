using Canon.Core;

namespace Canon.Core.Tests;

public class CameraSettingRulesTests
{
    private const uint ProgramAE = 0x00;
    private const uint Tv = 0x01;
    private const uint Av = 0x02;
    private const uint Manual = 0x03;
    private const uint SceneIntelligentAuto = 0x16;
    private const uint Portrait = 0x0C;

    // Values an EOS camera could list with EdsGetPropertyDesc.
    private static readonly uint[] ShutterSpeeds = [0x60, 0x68, 0x70, 0x78]; // 1/30, 1/60, 1/125, 1/250
    private static readonly uint[] Apertures = [0x28, 0x30, 0x38]; // 4, 5.6, 8

    private static SettingError? Validate(CameraProperty property, string? requested, uint aeMode, IReadOnlyCollection<uint>? settable = null) =>
        CameraSettingRules.Validate(property, requested, aeMode, settable, out _);

    [Theory]
    [InlineData(CameraProperty.ShutterSpeed, "1/7")]
    [InlineData(CameraProperty.ShutterSpeed, "")]
    [InlineData(CameraProperty.ShutterSpeed, null)]
    [InlineData(CameraProperty.Aperture, "f/5.6")]
    [InlineData(CameraProperty.ISOSpeed, "0xZZ")]
    [InlineData(CameraProperty.WhiteBalance, "Sunny")]
    public void Unreadable_label_is_an_invalid_value(CameraProperty property, string? requested)
    {
        var error = Validate(property, requested, Manual, ShutterSpeeds);

        Assert.NotNull(error);
        Assert.Equal(SettingErrorReason.InvalidValue, error.Reason);
        Assert.Equal("invalid-value", error.Reason.ToCode());
        Assert.Equal(property, error.Property);
    }

    [Fact]
    public void Invalid_label_is_reported_even_when_the_mode_locks_the_setting()
    {
        var error = Validate(CameraProperty.ShutterSpeed, "fast", Av);

        Assert.Equal(SettingErrorReason.InvalidValue, error?.Reason);
    }

    [Fact]
    public void Value_not_in_the_settable_list_is_not_accepted_and_lists_the_accepted_values()
    {
        var error = Validate(CameraProperty.ShutterSpeed, "1/4000", Manual, ShutterSpeeds);

        Assert.NotNull(error);
        Assert.Equal(SettingErrorReason.ValueNotAccepted, error.Reason);
        Assert.Equal("value-not-accepted", error.Reason.ToCode());
        Assert.Equal(["1/30", "1/60", "1/125", "1/250"], error.AcceptedValues);
    }

    [Fact]
    public void Value_in_the_settable_list_is_accepted()
    {
        var error = CameraSettingRules.Validate(CameraProperty.ShutterSpeed, "1/125", Manual, ShutterSpeeds, out var value);

        Assert.Null(error);
        Assert.Equal(0x70u, value);
    }

    [Fact]
    public void Raw_value_is_checked_like_a_label()
    {
        Assert.Null(Validate(CameraProperty.Aperture, "0x30", Manual, Apertures));
        Assert.Equal(SettingErrorReason.ValueNotAccepted, Validate(CameraProperty.Aperture, "0x48", Manual, Apertures)?.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new uint[0])]
    public void Without_a_settable_list_only_the_label_and_the_mode_are_checked(uint[]? settable)
    {
        Assert.Null(Validate(CameraProperty.WhiteBalance, "Daylight", Manual, settable));
    }

    [Fact]
    public void Bulb_shutter_speed_is_never_accepted()
    {
        Assert.Equal(SettingErrorReason.ValueNotAccepted, Validate(CameraProperty.ShutterSpeed, "BULB", Manual)?.Reason);
    }

    [Fact]
    public void Shutter_speed_cannot_be_changed_in_Av()
    {
        // Even when the camera still lists values: the mode decides.
        var error = Validate(CameraProperty.ShutterSpeed, "1/125", Av, ShutterSpeeds);

        Assert.NotNull(error);
        Assert.Equal(SettingErrorReason.NotSettableInMode, error.Reason);
        Assert.Equal("not-settable-in-mode", error.Reason.ToCode());
        Assert.Empty(error.AcceptedValues);
        Assert.Contains("Aperture Priority AE", error.Message);
    }

    [Fact]
    public void Aperture_cannot_be_changed_in_Tv()
    {
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.Aperture, "5.6", Tv, Apertures)?.Reason);
    }

    [Theory]
    [InlineData(SceneIntelligentAuto)]
    [InlineData(Portrait)]
    public void Nothing_can_be_changed_in_a_scene_mode(uint aeMode)
    {
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.ISOSpeed, "400", aeMode)?.Reason);
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.Aperture, "5.6", aeMode)?.Reason);
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.ShutterSpeed, "1/125", aeMode)?.Reason);
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.WhiteBalance, "Auto", aeMode)?.Reason);
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.ExposureCompensation, "0", aeMode)?.Reason);
    }

    [Theory]
    [InlineData(CameraProperty.ISOSpeed, "400")]
    [InlineData(CameraProperty.Aperture, "5.6")]
    [InlineData(CameraProperty.ShutterSpeed, "1/125")]
    [InlineData(CameraProperty.WhiteBalance, "Daylight")]
    public void Everything_but_exposure_compensation_can_be_changed_in_M(CameraProperty property, string requested)
    {
        Assert.True(CameraSettingRules.IsSettableInMode(property, Manual));
        Assert.Null(Validate(property, requested, Manual));
    }

    [Fact]
    public void Exposure_compensation_cannot_be_changed_in_M()
    {
        Assert.Equal(SettingErrorReason.NotSettableInMode, Validate(CameraProperty.ExposureCompensation, "+1/3", Manual)?.Reason);
        Assert.Null(Validate(CameraProperty.ExposureCompensation, "+1/3", Av));
        Assert.Null(Validate(CameraProperty.ExposureCompensation, "+1/3", ProgramAE));
    }

    [Fact]
    public void Primary_error_is_the_mode_error()
    {
        var mode = new CameraMode(Av, "Aperture Priority AE", true, false);
        var invalid = Validate(CameraProperty.ISOSpeed, "abc", Av)!;
        var locked = Validate(CameraProperty.ShutterSpeed, "1/125", Av)!;

        var exception = new CameraSettingsException(mode, [invalid, locked]);

        Assert.Same(locked, exception.PrimaryError);
        Assert.Same(invalid, new CameraSettingsException(mode, [invalid]).PrimaryError);
    }

    [Fact]
    public void Write_order_is_fixed()
    {
        Assert.Equal(
            [CameraProperty.ISOSpeed, CameraProperty.Aperture, CameraProperty.ShutterSpeed, CameraProperty.ExposureCompensation, CameraProperty.WhiteBalance],
            CameraSettingRules.WriteOrder);
    }
}
