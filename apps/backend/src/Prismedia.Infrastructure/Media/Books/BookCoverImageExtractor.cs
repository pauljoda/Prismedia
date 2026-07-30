using PDFtoImage;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Domain.Entities;
using VersOne.Epub;

namespace Prismedia.Infrastructure.Media.Books;

/// <summary>
/// Extracts a cover image from a single-file book to a temporary file. EPUB covers are read
/// directly from the embedded cover image; PDF covers are the rendered first page (PDFtoImage).
/// </summary>
public sealed class BookCoverImageExtractor : IBookCoverImageExtractor {
    /// <inheritdoc />
    public async Task<string?> ExtractCoverToTempAsync(string sourcePath, BookFormat format, Guid entityId, CancellationToken cancellationToken) {
        return format switch {
            BookFormat.Epub => await ExtractEpubCoverAsync(sourcePath, entityId, cancellationToken),
            BookFormat.Pdf => ExtractPdfCover(sourcePath, entityId, cancellationToken),
            _ => null
        };
    }

    private static string? ExtractPdfCover(string sourcePath, Guid entityId, CancellationToken cancellationToken) {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) {
            return null;
        }

        try {
            cancellationToken.ThrowIfCancellationRequested();
            var tempPath = Path.Combine(Path.GetTempPath(), $"prismedia-book-cover-{entityId}.jpg");
            using var pdfStream = File.OpenRead(sourcePath);
            using var outputStream = File.Create(tempPath);
            // Render the first page (0-based) at a cover-friendly width; the thumbnail job downsizes it.
            Conversion.SaveJpeg(outputStream, pdfStream, page: Index.Start, leaveOpen: true, password: null,
                options: new RenderOptions { Width = 800, WithAspectRatio = true });
            outputStream.Flush();
            return new FileInfo(tempPath).Length > 0 ? tempPath : null;
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
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
