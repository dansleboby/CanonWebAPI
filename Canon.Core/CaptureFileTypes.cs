namespace Canon.Core;

/// <summary>
/// Matching of downloaded files against the configured capture file types.
/// </summary>
public static class CaptureFileTypes
{
    public const string Any = "*";

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "jpg", ["jpg", "jpeg"] },
        { "jpeg", ["jpg", "jpeg"] },
        { "heif", ["hif", "heif", "heic"] },
        { "hif", ["hif", "heif", "heic"] },
    };

    /// <summary>
    /// Normalizes a list of file types ("jpg", ".CR3", "heif"...) into the set of accepted extensions (lower case, no dot).
    /// </summary>
    public static IReadOnlySet<string> Normalize(IEnumerable<string>? fileTypes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in fileTypes ?? [])
        {
            foreach (var part in raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                var type = part.Trim().TrimStart('.').ToLowerInvariant();
                if (type.Length == 0)
                    continue;

                if (Aliases.TryGetValue(type, out var aliases))
                    result.UnionWith(aliases);
                else
                    result.Add(type);
            }
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="fileName"/> has one of the <paramref name="acceptedTypes"/> extensions.
    /// </summary>
    public static bool Matches(string fileName, IReadOnlySet<string> acceptedTypes)
    {
        if (acceptedTypes.Contains(Any))
            return true;

        var extension = Path.GetExtension(fileName).TrimStart('.');
        return extension.Length > 0 && acceptedTypes.Contains(extension);
    }

    /// <summary>
    /// MIME type of a file downloaded from the camera.
    /// </summary>
    public static string GetContentType(string fileName) => Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "hif" or "heif" or "heic" => "image/heif",
        "cr3" => "image/x-canon-cr3",
        "cr2" => "image/x-canon-cr2",
        "mp4" => "video/mp4",
        "mov" => "video/quicktime",
        _ => "application/octet-stream"
    };
}
