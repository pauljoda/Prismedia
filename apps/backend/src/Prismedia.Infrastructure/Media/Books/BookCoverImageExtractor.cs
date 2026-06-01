using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using VersOne.Epub;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Extracts a cover image from a single-file book to a temporary file. EPUB covers are read
/// directly from the embedded cover image; PDF rendering is handled by the PDF reader and is
/// not yet wired in here.
/// </summary>
public sealed class BookCoverImageExtractor : IBookCoverImageExtractor {
    /// <inheritdoc />
    public async Task<string?> ExtractCoverToTempAsync(string sourcePath, BookFormat format, Guid entityId, CancellationToken cancellationToken) {
        return format switch {
            BookFormat.Epub => await ExtractEpubCoverAsync(sourcePath, entityId, cancellationToken),
            _ => null
        };
    }

    private static async Task<string?> ExtractEpubCoverAsync(string sourcePath, Guid entityId, CancellationToken cancellationToken) {
        try {
            var book = await EpubReader.ReadBookAsync(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();

            var cover = book.CoverImage;
            if (cover is null || cover.Length == 0) {
                return null;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"prismedia-book-cover-{entityId}.img");
            await File.WriteAllBytesAsync(tempPath, cover, cancellationToken);
            return tempPath;
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }
}
