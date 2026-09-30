using Canon.Core;

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

    [Fact]
    public void Default_options_capture_jpeg_only()
    {
        var options = new CanonCameraOptions();

        Assert.Equal(["jpg"], options.CaptureFileTypes);
        Assert.Equal(10, options.CaptureTimeoutSeconds);
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
