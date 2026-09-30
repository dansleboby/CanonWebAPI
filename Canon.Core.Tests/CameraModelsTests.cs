using Canon.Core;
using Microsoft.Extensions.Configuration;

namespace Canon.Core.Tests;

public class CameraModelsTests
{
    [Theory]
    [InlineData(new[] { "jpg" }, "IMG_0001.JPG", true)]
    [InlineData(new[] { "jpg" }, "IMG_0001.jpeg", true)]
    [InlineData(new[] { "jpg" }, "IMG_0001.CR3", false)]
    [InlineData(new[] { "jpg" }, "IMG_0001.HIF", false)]
    [InlineData(new[] { "jpg,cr3" }, "IMG_0001.CR3", true)]
    [InlineData(new[] { ".CR3" }, "IMG_0001.cr3", true)]
    [InlineData(new[] { "heif" }, "IMG_0001.HIF", true)]
    [InlineData(new[] { "*" }, "MVI_0001.MP4", true)]
    [InlineData(new[] { "jpg" }, "NOEXTENSION", false)]
    public void File_types_match_extensions(string[] fileTypes, string fileName, bool expected)
    {
        Assert.Equal(expected, CaptureFileTypes.Matches(fileName, CaptureFileTypes.Normalize(fileTypes)));
    }

    [Fact]
    public void Empty_file_types_normalize_to_an_empty_set()
    {
        Assert.Empty(CaptureFileTypes.Normalize(null));
        Assert.Empty(CaptureFileTypes.Normalize([" , "]));
    }

    [Theory]
    [InlineData("IMG_0001.JPG", "image/jpeg")]
    [InlineData("IMG_0001.HIF", "image/heif")]
    [InlineData("IMG_0001.CR3", "image/x-canon-cr3")]
    [InlineData("IMG_0001.XYZ", "application/octet-stream")]
    public void Content_type_depends_on_the_extension(string fileName, string contentType)
    {
        Assert.Equal(contentType, CaptureFileTypes.GetContentType(fileName));
    }

    [Fact]
    public void Temperature_status_is_normal_when_zero()
    {
        var status = TemperatureStatus.FromRawValue(0);

        Assert.True(status.IsSupported);
        Assert.Equal("Normal", status.Status);
        Assert.Equal("Normal", status.MovieStatus);
        Assert.False(status.IsWarning);
    }

    [Fact]
    public void Temperature_status_decodes_still_and_movie_restrictions()
    {
        var status = TemperatureStatus.FromRawValue(0x0002_0004);

        Assert.Equal("ShootingProhibited", status.Status);
        Assert.Equal("MovieRecordingRestricted", status.MovieStatus);
        Assert.True(status.IsWarning);
    }

    [Theory]
    [InlineData("fire", true)]
    [InlineData("FIRE", true)]
    [InlineData(" on ", true)]
    [InlineData("true", true)]
    [InlineData("off", false)]
    [InlineData("false", false)]
    public void Flash_values_are_parsed(string text, bool expected)
    {
        Assert.True(FlashStatus.TryParseFiring(text, out var firing));
        Assert.Equal(expected, firing);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("auto")]
    public void Invalid_flash_values_are_rejected(string? text)
    {
        Assert.False(FlashStatus.TryParseFiring(text, out _));
    }

    [Fact]
    public void Default_options_force_the_flash_on_an_unspecified_target()
    {
        var options = new CanonCameraOptions();

        Assert.True(options.ForceFlashFiring);
        Assert.Equal("Unspecified", options.FlashTarget);
    }

    [Fact]
    public void Default_options_capture_jpeg_only()
    {
        var options = new CanonCameraOptions();

        // Empty: CanonCamera falls back to JPEG.
        Assert.Empty(options.CaptureFileTypes);
        Assert.Equal(10, options.CaptureTimeoutSeconds);
    }

    [Fact]
    public void Configured_capture_file_types_replace_the_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Canon:CaptureFileTypes:0"] = "cr3" })
            .Build();

        var options = configuration.GetSection(CanonCameraOptions.SectionName).Get<CanonCameraOptions>();

        Assert.NotNull(options);
        Assert.Equal(["cr3"], options.CaptureFileTypes);
    }

    [Fact]
    public void Eds_exception_classifies_errors()
    {
        Assert.True(new EdsException(EDSDK.EDS_ERR_DEVICE_BUSY, "x").IsBusy);
        Assert.True(new EdsException(EDSDK.EDS_ERR_PTP_DEVICE_BUSY, "x").IsBusy);
        Assert.True(new EdsException(EDSDK.EDS_ERR_COMM_DISCONNECTED, "x").IsDisconnected);
        Assert.True(new EdsException(EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG, "x").IsTakePictureError);
        Assert.True(new EdsException(EDSDK.EDS_ERR_TAKE_PICTURE_RETRUCTED_LENS_NG, "x").IsTakePictureError);
        Assert.True(new EdsException(EDSDK.EDS_ERR_INVALID_DEVICEPROP_VALUE, "x").IsInvalidValue);
        Assert.True(new CameraNotConnectedException("x").IsDisconnected);
        Assert.Equal("Take picture: Focus failed", new EdsException(EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG, "Take picture").Message);
    }
}
