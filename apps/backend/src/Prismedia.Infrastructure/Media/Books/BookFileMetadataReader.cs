using System.Net;
using System.Text.RegularExpressions;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using VersOne.Epub;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Reads descriptive metadata from single-file books, normalized into the shared
/// <see cref="ComicInfoMetadata"/> shape. EPUB is parsed with VersOne.Epub; PDF metadata
/// is handled separately and returns null here until the PDF reader is wired in.
/// </summary>
public sealed partial class BookFileMetadataReader : IBookFileMetadataReader {
    /// <inheritdoc />
    public async Task<ComicInfoMetadata?> ReadAsync(string sourcePath, BookFormat format, CancellationToken cancellationToken) {
        return format switch {
            BookFormat.Epub => await ReadEpubAsync(sourcePath, cancellationToken),
            _ => null
        };
    }

    private static async Task<ComicInfoMetadata?> ReadEpubAsync(string sourcePath, CancellationToken cancellationToken) {
        try {
            var book = await EpubReader.ReadBookAsync(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();

            var creators = book.AuthorList
                .Select(author => author?.Trim())
                .Where(author => !string.IsNullOrWhiteSpace(author))
                .Select(author => author!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ComicInfoMetadata {
                Title = NullIfBlank(book.Title),
                Summary = NullIfBlank(StripHtml(book.Description)),
                Creators = creators
            };
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }

    private static string? StripHtml(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var withoutTags = HtmlTagRegex().Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string? NullIfBlank(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
