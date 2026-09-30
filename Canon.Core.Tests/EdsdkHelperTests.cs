using Canon.Core;

namespace Canon.Core.Tests;

public class EdsdkHelperTests
{
    public static TheoryData<CameraProperty> AllProperties => new(Enum.GetValues<CameraProperty>());

    [Theory]
    [MemberData(nameof(AllProperties))]
    public void Every_label_maps_back_to_its_value(CameraProperty property)
    {
        var values = ((uint)property).GetPropertyValues();
        var descriptions = ((uint)property).GetPropertyDescriptions();

        Assert.Equal(values.Count, descriptions.Count);
        foreach (var (value, label) in values)
            Assert.Equal(value, descriptions[label]);
    }

    [Fact]
    public void Error_messages_are_initialized_and_include_new_errors()
    {
        Assert.Equal("Lens is not attached", EdsdkHelper.GetErrorMessage(EDSDK.EDS_ERR_TAKE_PICTURE_NO_LENS_NG));
        Assert.Equal("Device is busy", EdsdkHelper.GetErrorMessage(EDSDK.EDS_ERR_PTP_DEVICE_BUSY));
        Assert.Equal("EDSDK error 0x12345678", EdsdkHelper.GetErrorMessage(0x12345678));
    }

    [Theory]
    [InlineData("0", 0x00u)]
    [InlineData("+1/3", 0x03u)]
    [InlineData("-1/3", 0xFDu)]
    [InlineData("+5", 0x28u)]
    [InlineData("-5", 0xD8u)]
    [InlineData("-1 2/3", 0xF3u)]
    public void Exposure_compensation_table_matches_the_documentation(string label, uint value)
    {
        Assert.True(((uint)CameraProperty.ExposureCompensation).TryParseValue(label, out var parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(label, ((uint)CameraProperty.ExposureCompensation).DescribeValue(value));
    }

    [Theory]
    [InlineData(0x2Bu, "4.5 (1/3)")]
    [InlineData(0x2Cu, "4.5")]
    [InlineData(0x1Du, "2.5 (1/3)")]
    [InlineData(0x1Cu, "2.5")]
    public void Aperture_pairs_mark_the_one_third_stop_value(uint value, string label)
    {
        Assert.Equal(label, ((uint)CameraProperty.Aperture).DescribeValue(value));
    }

    [Fact]
    public void Same_label_values_are_the_other_exposure_step()
    {
        var aperture = (uint)CameraProperty.Aperture;

        Assert.Equal([0x2Bu], aperture.GetSameLabelValues("4.5"));
        Assert.Equal([0x2Cu], aperture.GetSameLabelValues("4.5 (1/3)"));
        Assert.Equal([0x1Du], ((uint)CameraProperty.ShutterSpeed).GetSameLabelValues("10\""));
        Assert.Empty(aperture.GetSameLabelValues("5.6"));
        Assert.Empty(aperture.GetSameLabelValues("0x1C"));
    }

    [Fact]
    public void Exposure_compensation_has_every_documented_step()
    {
        Assert.Equal(41, ((uint)CameraProperty.ExposureCompensation).GetPropertyValues().Count);
    }

    [Theory]
    [InlineData(CameraProperty.ISOSpeed, "64000", 0x93u)]
    [InlineData(CameraProperty.ISOSpeed, "819200", 0xB0u)]
    [InlineData(CameraProperty.ISOSpeed, "auto", 0x00u)]
    [InlineData(CameraProperty.ShutterSpeed, "1/32000", 0xB0u)]
    [InlineData(CameraProperty.ShutterSpeed, "1/125", 0x70u)]
    [InlineData(CameraProperty.Aperture, "3.4", 0x85u)]
    [InlineData(CameraProperty.WhiteBalance, "Auto (White priority)", 23u)]
    [InlineData(CameraProperty.WhiteBalance, "Color Temp 4", 26u)]
    public void Values_added_from_the_13_20_documentation_are_known(CameraProperty property, string label, uint value)
    {
        Assert.True(((uint)property).TryParseValue(label, out var parsed));
        Assert.Equal(value, parsed);
    }

    [Fact]
    public void Unknown_values_are_described_as_raw_hex_and_can_be_parsed_back()
    {
        var iso = (uint)CameraProperty.ISOSpeed;

        Assert.Equal("0x99", iso.DescribeValue(0x99));
        Assert.True(iso.TryParseValue("0x99", out var parsed));
        Assert.Equal(0x99u, parsed);
    }

    [Fact]
    public void Plain_numbers_are_labels_not_raw_values_for_numeric_properties()
    {
        // "100" is ISO 100 (0x48), not the raw value 100.
        Assert.True(((uint)CameraProperty.ISOSpeed).TryParseValue("100", out var parsed));
        Assert.Equal(0x48u, parsed);
        Assert.False(((uint)CameraProperty.ISOSpeed).TryParseValue("123", out _));
    }

    [Fact]
    public void Invalid_labels_are_rejected()
    {
        Assert.False(((uint)CameraProperty.Aperture).TryParseValue("f/99", out _));
    }

    [Theory]
    [InlineData(0x10u, 30.0)]
    [InlineData(0x2B, 3.2)]
    [InlineData(0x45u, 0.3)]
    [InlineData(0x70u, 1.0 / 125)]
    [InlineData(0x4Du, 1.0 / 6)]
    [InlineData(0xB0u, 1.0 / 32000)]
    public void Exposure_seconds_are_read_from_the_shutter_speed(uint tv, double expected)
    {
        Assert.True(EdsdkHelper.TryGetExposureSeconds(tv, out var seconds));
        Assert.Equal(expected, seconds, 6);
    }

    [Theory]
    [InlineData(0x0Cu)] // Bulb
    [InlineData(0xFFFFFFFFu)] // Not valid
    public void Exposure_seconds_are_unknown_for_bulb_and_invalid_values(uint tv)
    {
        Assert.False(EdsdkHelper.TryGetExposureSeconds(tv, out _));
    }

    [Theory]
    [InlineData(1u, "Shooting failure (e.g. focus failure)")]
    [InlineData(6u, "No card inserted")]
    [InlineData(99u, "Unknown capture error: 0x63")]
    public void Capture_errors_are_translated(uint code, string message)
    {
        Assert.Equal(message, EdsdkHelper.GetCaptureErrorMessage(code));
    }
}
