using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Prismedia.Application.Jobs.Ports;

namespace Prismedia.Infrastructure.Media.Sidecars;

/// <summary>
/// Reads ComicInfo.xml metadata from ZIP-compatible comic archives.
/// </summary>
public sealed class ComicInfoMetadataReader : IComicInfoMetadataReader {
    private static readonly string[] CreatorTags = [
        "Writer",
        "Penciller",
        "Inker",
        "Colorist",
        "Letterer",
        "CoverArtist",
        "Editor",
        "Translator"
    ];

    /// <inheritdoc />
    public async Task<ComicInfoMetadata?> ReadAsync(string archivePath, CancellationToken cancellationToken) {
        var extension = Path.GetExtension(archivePath);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cbz", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        try {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                !string.IsNullOrEmpty(candidate.Name) &&
                candidate.Name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null) {
                return null;
            }

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var xml = await reader.ReadToEndAsync(cancellationToken);
            return Parse(xml);
        } catch {
            return null;
        }
    }

    internal static ComicInfoMetadata? Parse(string xml) {
        XDocument document;
        try {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        } catch {
            return null;
        }

        var publisher = Clean(FirstValue(document, "Publisher")) ?? Clean(FirstValue(document, "Imprint"));
        var creators = Unique(CreatorTags.SelectMany(tag => SplitList(Clean(FirstValue(document, tag)))));
        var tags = Unique(
            SplitList(Clean(FirstValue(document, "Genre")))
                .Concat(SplitList(Clean(FirstValue(document, "Tags"))))
                .Concat(SplitList(Clean(FirstValue(document, "Characters"))))
                .Concat(SplitList(Clean(FirstValue(document, "SeriesGroup"))))
                .Concat(SplitList(Clean(FirstValue(document, "StoryArc"))))
                .Concat(SplitList(Clean(FirstValue(document, "Manga"))))
                .Concat(SplitList(Clean(FirstValue(document, "AgeRating")))));

        var manga = Clean(FirstValue(document, "Manga"));
        var ageRating = Clean(FirstValue(document, "AgeRating"));

        return new ComicInfoMetadata {
            Title = Clean(FirstValue(document, "Title")),
            Series = Clean(FirstValue(document, "Series")),
            Number = Clean(FirstValue(document, "Number")),
            Count = Number(document, "Count"),
            Volume = Number(document, "Volume"),
            Summary = Clean(FirstValue(document, "Summary")),
            Date = Date(document),
            Publisher = publisher,
            Urls = Unique(SplitList(Clean(FirstValue(document, "Web")))),
            PageCount = Number(document, "PageCount"),
            Language = Clean(FirstValue(document, "LanguageISO")),
            Format = Clean(FirstValue(document, "Format")),
            Manga = manga,
            AgeRating = ageRating,
            Creators = creators,
            Tags = tags,
            MarksNsfw = MarksNsfw(ageRating, manga, tags)
        };
    }

    private static IEnumerable<XElement> Elements(XDocument document, string name) =>
        document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static string? FirstValue(XDocument document, string name) =>
        Elements(document, name).Select(element => element.Value).FirstOrDefault();

    private static int? Number(XDocument document, string tag) {
        var raw = Clean(FirstValue(document, tag));
        if (raw is null) return null;

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            number >= 0
            ? (int)Math.Round(number, MidpointRounding.AwayFromZero)
            : null;
    }

    private static string? Date(XDocument document) {
        var year = Number(document, "Year");
        if (year is null or < 1) {
            return null;
        }

        var month = Number(document, "Month");
        if (month is null or < 1 or > 12) {
            return year.Value.ToString(CultureInfo.InvariantCulture);
        }

        var day = Number(document, "Day");
        if (day is null or < 1 or > 31) {
            return $"{year.Value:D4}-{month.Value:D2}";
        }

        return $"{year.Value:D4}-{month.Value:D2}-{day.Value:D2}";
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

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

    private static string? Clean(string? value) {
        var trimmed = value?.Trim().Trim('\uFEFF');
        return string.IsNullOrWhiteSpace(trimmed) || trimmed == "-1" ? null : trimmed;
    }

    private static bool MarksNsfw(string? ageRating, string? manga, IReadOnlyList<string> tags) =>
        new[] { ageRating, manga }
            .Concat(tags)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => value!.Contains("adult", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("adults only", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("18+", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("mature", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("explicit", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("erotic", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("hentai", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("nsfw", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("porn", StringComparison.OrdinalIgnoreCase));
}
