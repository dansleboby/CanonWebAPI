using System.Text.Json;
using Canon.API.Models;
using Canon.Core;

namespace Canon.API.Tests;

public class CameraSettingsRequestTests
{
    // Options used by ASP.NET Core for request bodies.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Every_setting_is_mapped_in_write_order()
    {
        var request = JsonSerializer.Deserialize<CameraSettingsRequest>(
            """{ "whiteBalance": "Daylight", "exposureCompensation": "+1/3", "shutterSpeed": "1/125", "aperture": "5.6", "iso": "400" }""", Web);

        var settings = request!.ToDictionary();

        Assert.Equal(CameraSettingRules.WriteOrder, CameraSettingRules.WriteOrder.Where(settings.ContainsKey));
        Assert.Equal("+1/3", settings[CameraProperty.ExposureCompensation]);
    }

    [Fact]
    public void Omitted_settings_are_left_unchanged()
    {
        var settings = JsonSerializer.Deserialize<CameraSettingsRequest>("""{ "iso": "Auto" }""", Web)!.ToDictionary();

        Assert.Equal([CameraProperty.ISOSpeed], settings.Keys);
    }

    [Fact]
    public void Unknown_field_is_refused()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CameraSettingsRequest>("""{ "exposure": "+1/3" }""", Web));
    }
}
