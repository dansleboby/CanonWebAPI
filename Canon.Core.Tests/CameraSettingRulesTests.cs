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

    [Fact]
    public void Without_a_settable_list_only_the_label_and_the_mode_are_checked()
    {
        Assert.Null(Validate(CameraProperty.WhiteBalance, "Daylight", Manual, null));
        Assert.True(CameraSettingRules.IsSettable(CameraProperty.WhiteBalance, Manual, null));
    }

    [Fact]
    public void Empty_settable_list_means_the_setting_cannot_be_changed_now()
    {
        // The Canon samples disable a setting whose list is empty (e.g. aperture without a lens, movie mode).
        var error = Validate(CameraProperty.Aperture, "5.6", Manual, []);

        Assert.Equal(SettingErrorReason.NotSettableInMode, error?.Reason);
        Assert.Empty(error!.AcceptedValues);
        Assert.False(CameraSettingRules.IsSettable(CameraProperty.Aperture, Manual, []));
        Assert.True(CameraSettingRules.IsSettable(CameraProperty.Aperture, Manual, Apertures));
        Assert.False(CameraSettingRules.IsSettable(CameraProperty.Aperture, Tv, Apertures));
    }

    [Fact]
    public void Not_valid_value_has_a_label_but_cannot_be_sent_back()
    {
        // Exposure compensation read in manual exposure mode (EDSDK API reference 5.2.28).
        var propId = (uint)CameraProperty.ExposureCompensation;

        Assert.Equal("Not valid", propId.DescribeValue(0xFFFFFFFF));
        Assert.Equal("0xFFFFFFFF", ((uint)CameraProperty.WhiteBalance).DescribeValue(0xFFFFFFFF));
        Assert.Equal(SettingErrorReason.InvalidValue, Validate(CameraProperty.ExposureCompensation, "Not valid", Av)?.Reason);
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
    public void Exposure_compensation_can_be_changed_in_M_only_with_ISO_Auto()
    {
        var error = Validate(CameraProperty.ExposureCompensation, "+1/3", Manual);

        Assert.Equal(SettingErrorReason.NotSettableInMode, error?.Reason);
        Assert.Contains("unless ISO is Auto", error!.Message);
        Assert.Null(CameraSettingRules.Validate(CameraProperty.ExposureCompensation, "+1/3", Manual, null, out _, isoAuto: true));
        Assert.True(CameraSettingRules.IsSettable(CameraProperty.ExposureCompensation, Manual, null, isoAuto: true));
        Assert.False(CameraSettingRules.IsSettable(CameraProperty.ExposureCompensation, Manual, [], isoAuto: true));
        Assert.Null(Validate(CameraProperty.ExposureCompensation, "+1/3", Av));
        Assert.Null(Validate(CameraProperty.ExposureCompensation, "+1/3", ProgramAE));
    }

    [Fact]
    public void ISO_Auto_does_not_unlock_other_modes()
    {
        Assert.False(CameraSettingRules.IsSettableInMode(CameraProperty.ExposureCompensation, SceneIntelligentAuto, isoAuto: true));
        Assert.False(CameraSettingRules.IsSettableInMode(CameraProperty.ShutterSpeed, Av, isoAuto: true));
    }

    [Theory]
    [InlineData("2.5", 0x1Du, 0x1Cu)]
    [InlineData("4.5", 0x2Bu, 0x2Cu)]
    public void Label_shared_by_both_exposure_steps_takes_the_value_the_camera_accepts(string label, uint oneThirdStop, uint halfStop)
    {
        // Apertures listed by the camera with 1/3 and 1/2 stop exposure steps (2 to 5.6).
        uint[] oneThirdStops = [0x18, 0x1B, 0x1D, 0x20, 0x23, 0x25, 0x28, 0x2B, 0x2D, 0x30];
        uint[] halfStops = [0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C, 0x30];

        Assert.Null(CameraSettingRules.Validate(CameraProperty.Aperture, label, Manual, oneThirdStops, out var value));
        Assert.Equal(oneThirdStop, value);
        Assert.Null(CameraSettingRules.Validate(CameraProperty.Aperture, label, Manual, halfStops, out value));
        Assert.Equal(halfStop, value);
    }

    [Fact]
    public void Raw_value_is_never_swapped_for_the_other_exposure_step()
    {
        uint[] oneThirdStops = [0x18, 0x1B, 0x1D, 0x20];

        Assert.Equal(SettingErrorReason.ValueNotAccepted, Validate(CameraProperty.Aperture, "0x1C", Manual, oneThirdStops)?.Reason);
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
