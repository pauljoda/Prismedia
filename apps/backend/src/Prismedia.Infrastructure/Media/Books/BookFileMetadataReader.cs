using System.Net;
using System.Text.RegularExpressions;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using UglyToad.PdfPig;
using VersOne.Epub;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Reads descriptive metadata from single-file books. EPUB is parsed with VersOne.Epub and PDF with PdfPig.
/// </summary>
public sealed partial class BookFileMetadataReader : IBookFileMetadataReader {
    /// <inheritdoc />
    public async Task<BookFileMetadata?> ReadAsync(string sourcePath, BookFormat format, CancellationToken cancellationToken) {
        return format switch {
            BookFormat.Epub => await ReadEpubAsync(sourcePath, cancellationToken),
            BookFormat.Pdf => ReadPdf(sourcePath, cancellationToken),
            _ => null
        };
    }

    private static BookFileMetadata? ReadPdf(string sourcePath, CancellationToken cancellationToken) {
        try {
            using var document = PdfDocument.Open(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();

            var info = document.Information;
            var author = NullIfBlank(info?.Author);
            return new BookFileMetadata {
                Title = NullIfBlank(info?.Title),
                Summary = NullIfBlank(info?.Subject),
                Creators = author is null ? [] : [author],
                PageCount = document.NumberOfPages
            };
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }

    private static async Task<BookFileMetadata?> ReadEpubAsync(string sourcePath, CancellationToken cancellationToken) {
        try {
            var book = await EpubReader.ReadBookAsync(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();

            var creators = book.AuthorList
                .Select(author => author?.Trim())
                .Where(author => !string.IsNullOrWhiteSpace(author))
                .Select(author => author!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new BookFileMetadata {
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
