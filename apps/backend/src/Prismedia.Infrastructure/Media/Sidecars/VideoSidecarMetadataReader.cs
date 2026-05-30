using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Sidecars;

/// <summary>
/// Reads video metadata from media-adjacent NFO and JSON sidecar files.
/// </summary>
public sealed class VideoSidecarMetadataReader : IVideoSidecarMetadataReader {
    /// <inheritdoc />
    public async Task<VideoSidecarMetadata?> ReadAsync(string videoFilePath, CancellationToken cancellationToken) {
        var nfo = await ReadNfoAsync(videoFilePath, cancellationToken);
        var json = await ReadJsonAsync(videoFilePath, cancellationToken);

        if (nfo is null && json is null) {
            return null;
        }

        return Merge(json, nfo);
    }

    private static async Task<VideoSidecarMetadata?> ReadNfoAsync(
        string videoFilePath,
        CancellationToken cancellationToken) {
        var path = SidecarPath(videoFilePath, ".nfo");
        if (!File.Exists(path)) {
            return null;
        }

        try {
            var xml = await File.ReadAllTextAsync(path, cancellationToken);
            var document = ParseXml(xml);
            if (document is null) {
                return null;
            }

            var rating = FirstElementValue(document, "rating");
            var urls = Unique(Elements(document, "url").Select(element => element.Value));
            var tags = Unique(Elements(document, "tag")
                .Concat(Elements(document, "genre"))
                .Select(element => element.Value));

            return new VideoSidecarMetadata {
                Title = Clean(FirstElementValue(document, "title")),
                Description = Clean(FirstElementValue(document, "plot")),
                Date = Clean(FirstElementValue(document, "aired")),
                Studio = Clean(FirstElementValue(document, "studio")),
                Rating = NormalizeRating(rating),
                Urls = urls,
                Tags = tags,
                DurationSeconds = ParseDurationSeconds(
                    FirstElementValue(document, "duration"),
                    FirstElementValue(document, "runtime"))
            };
        } catch {
            return null;
        }
    }

    private static async Task<VideoSidecarMetadata?> ReadJsonAsync(
        string videoFilePath,
        CancellationToken cancellationToken) {
        foreach (var path in JsonSidecarCandidates(videoFilePath)) {
            if (!File.Exists(path)) {
                continue;
            }

            try {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Object) {
                    return null;
                }

                return ParseJson(document.RootElement);
            } catch {
                return null;
            }
        }

        return null;
    }

    private static VideoSidecarMetadata ParseJson(JsonElement json) {
        var urls = new List<string>();
        var webUrl = FirstString(json, "webpage_url", "original_url");
        if (IsHttpReferenceUrl(webUrl)) {
            urls.Add(webUrl!);
        } else {
            var url = FirstString(json, "url");
            if (IsHttpReferenceUrl(url) && !IsStreamUrl(url!)) {
                urls.Add(url!);
            }
        }

        var tags = new List<string>();
        AddStringArray(tags, json, "tags");
        AddStringArray(tags, json, "categories");
        AddStringOrArray(tags, json, "genre");

        var performers = new List<string>();
        AddStringOrArray(performers, json, "performers");
        AddStringOrArray(performers, json, "actors");
        AddStringOrArray(performers, json, "cast");

        return new VideoSidecarMetadata {
            Title = Clean(FirstString(json, "title", "fulltitle")),
            Description = Clean(FirstString(json, "description", "plot", "synopsis")),
            Date = NormalizeDate(FirstString(json, "upload_date", "release_date", "date", "aired")),
            Studio = Clean(FirstString(json, "uploader", "channel", "creator", "studio", "artist")),
            Rating = NormalizeRating(FirstNumber(json, "average_rating")),
            Urls = Unique(urls),
            Tags = Unique(tags),
            Performers = Unique(performers),
            DurationSeconds = FirstNumber(json, "duration")
        };
    }

    private static VideoSidecarMetadata Merge(VideoSidecarMetadata? json, VideoSidecarMetadata? nfo) {
        var urls = Unique((json?.Urls ?? []).Concat(nfo?.Urls ?? []));
        var tags = Unique((json?.Tags ?? []).Concat(nfo?.Tags ?? []));
        var performers = Unique((json?.Performers ?? []).Concat(nfo?.Performers ?? []));

        return new VideoSidecarMetadata {
            Title = FirstNonEmpty(nfo?.Title, json?.Title),
            Description = FirstNonEmpty(nfo?.Description, json?.Description),
            Date = FirstNonEmpty(nfo?.Date, json?.Date),
            Studio = FirstNonEmpty(nfo?.Studio, json?.Studio),
            Rating = nfo?.Rating ?? json?.Rating,
            Urls = urls,
            Tags = tags,
            Performers = performers,
            DurationSeconds = nfo?.DurationSeconds ?? json?.DurationSeconds
        };
    }

    private static string SidecarPath(string filePath, string extension) =>
        Path.Combine(
            Path.GetDirectoryName(filePath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(filePath) + extension);

    private static IEnumerable<string> JsonSidecarCandidates(string filePath) {
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        yield return Path.Combine(dir, stem + ".info.json");
        yield return Path.Combine(dir, stem + ".json");
    }

    private static XDocument? ParseXml(string xml) {
        try {
            return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        } catch {
            try {
                return XDocument.Parse($"<root>{xml}</root>", LoadOptions.PreserveWhitespace);
            } catch {
                return null;
            }
        }
    }

    private static IEnumerable<XElement> Elements(XDocument document, string name) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static string? FirstElementValue(XDocument document, string name) =>
        Elements(document, name).Select(element => element.Value).FirstOrDefault();

    private static string? FirstString(JsonElement json, params string[] keys) {
        foreach (var key in keys) {
            if (json.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString())) {
                return value.GetString()!.Trim();
            }
        }

        return null;
    }

    private static double? FirstNumber(JsonElement json, params string[] keys) {
        foreach (var key in keys) {
            if (json.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number) &&
                number > 0) {
                return number;
            }
        }

        return null;
    }

    private static void AddStringArray(List<string> values, JsonElement json, string key) {
        if (!json.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array) {
            return;
        }

        foreach (var item in array.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) {
                values.Add(item.GetString()!.Trim());
            }
        }
    }

    private static void AddStringOrArray(List<string> values, JsonElement json, string key) {
        if (!json.TryGetProperty(key, out var value)) {
            return;
        }

        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) {
            values.Add(value.GetString()!.Trim());
            return;
        }

        if (value.ValueKind != JsonValueKind.Array) {
            return;
        }

        foreach (var item in value.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) {
                values.Add(item.GetString()!.Trim());
            }
        }
    }

    private static int? NormalizeRating(string? raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? NormalizeRating(number)
            : null;

    private static int? NormalizeRating(double? raw) {
        if (raw is null || !double.IsFinite(raw.Value) || raw < 0 || raw > 100) {
            return null;
        }

        if (raw <= 5) return (int)Math.Round(raw.Value, MidpointRounding.AwayFromZero);
        if (raw <= 10) return (int)Math.Round(raw.Value / 2, MidpointRounding.AwayFromZero);
        return (int)Math.Round(raw.Value / 20, MidpointRounding.AwayFromZero);
    }

    private static double? ParseDurationSeconds(string? duration, string? runtimeMinutes) {
        if (!string.IsNullOrWhiteSpace(duration) && TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var time)) {
            return time.TotalSeconds;
        }

        if (double.TryParse(runtimeMinutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0) {
            return minutes * 60;
        }

        return null;
    }

    private static string? NormalizeDate(string? raw) {
        raw = Clean(raw);
        if (raw is null) return null;

        if (raw.Length == 8 && raw.All(char.IsDigit)) {
            return $"{raw[..4]}-{raw.Substring(4, 2)}-{raw.Substring(6, 2)}";
        }

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(Clean).FirstOrDefault(value => value is not null);

    private static string? Clean(string? value) {
        var trimmed = value?.Trim().Trim('\uFEFF');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool IsHttpReferenceUrl(string? value) =>
        value?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ||
        value?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsStreamUrl(string value) =>
        value.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("manifest", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("index-v1", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/hls/", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Unique(IEnumerable<string?> values) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        foreach (var value in values.Select(Clean)) {
            if (value is not null && seen.Add(value)) {
                output.Add(value);
            }
        }

        return output;
    }
}
