using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using SkiaSharp;
using UglyToad.PdfPig;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Bounded format checks for EPUB/PDF books, CBZ comics, and standalone images before library placement.</summary>
public sealed class IntegrationMediaVerifier : IIntegrationMediaVerifier {
    private const int MaximumEntries = 10000;
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumXmlBytes = 2 * 1024 * 1024;
    private static readonly XNamespace ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace PackageNamespace = "http://www.idpf.org/2007/opf";

    /// <inheritdoc />
    public async Task VerifyAsync(VerifiedIntegrationArtifact artifact, EntityKind kind, CancellationToken cancellationToken) {
        if (!IntegrationImportPolicy.Supports(kind) || !IntegrationImportPolicy.For(kind).AcceptsFileName(artifact.FileName))
            throw new InvalidDataException("This media format cannot be imported.");
        cancellationToken.ThrowIfCancellationRequested();
        try {
            if (kind == EntityKind.Image) {
                ValidateImage(artifact, cancellationToken);
                return;
            }
            if (Path.GetExtension(artifact.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) {
                using var document = PdfDocument.Open(artifact.Path);
                if (document.NumberOfPages is < 1 or > MaximumEntries) throw new InvalidDataException("The PDF has no readable pages or exceeds the page limit.");
                for (var page = 1; page <= document.NumberOfPages; page++) {
                    cancellationToken.ThrowIfCancellationRequested();
                    _ = document.GetPage(page);
                }
                return;
            }
            using var archive = ZipFile.OpenRead(artifact.Path);
            ValidateArchive(archive);
            if (kind == EntityKind.ComicInstallment) ValidateComic(archive, cancellationToken);
            else await ValidateEpubAsync(archive, cancellationToken);
        } catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is IOException or XmlException or ArgumentException or InvalidOperationException
            || error.GetType().Namespace?.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal) == true) {
            throw new InvalidDataException("The downloaded file is not readable supported media.");
        }
    }

    private static void ValidateImage(VerifiedIntegrationArtifact artifact, CancellationToken cancellationToken) {
        var file = new FileInfo(artifact.Path);
        if (artifact.SizeBytes <= 0 || file.Length != artifact.SizeBytes || file.Length > IntegrationImportPolicy.For(EntityKind.Image).MaximumBytes)
            throw new InvalidDataException("The image exceeds its byte limit or differs from verified staging.");
        using var stream = File.OpenRead(artifact.Path);
        if (Path.GetExtension(artifact.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
            RejectAnimatedPng(stream, cancellationToken);
        using var codec = SKCodec.Create(stream);
        var expected = Path.GetExtension(artifact.FileName).ToLowerInvariant() switch {
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Jpeg
        };
        if (codec is null || codec.EncodedFormat != expected || codec.FrameCount > 1 || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 100_000_000)
            throw new InvalidDataException("The file is not a supported, bounded still image matching its extension.");
        var scale = Math.Min(1f, 1024f / Math.Max(codec.Info.Width, codec.Info.Height));
        using var bitmap = new SKBitmap(codec.Info.WithSize(codec.GetScaledDimensions(scale)));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The image could not be decoded completely.");
    }

    private static void RejectAnimatedPng(Stream stream, CancellationToken cancellationToken) {
        stream.Position = 8; // The decoder validates the PNG signature; this pass inspects bounded chunks.
        Span<byte> header = stackalloc byte[8];
        var chunks = 0;
        while (stream.Position < stream.Length) {
            cancellationToken.ThrowIfCancellationRequested();
            if (++chunks > MaximumEntries || stream.Length - stream.Position < 12)
                throw new InvalidDataException("The PNG chunk structure is invalid or exceeds its limit.");
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if ((long)length + 4 > stream.Length - stream.Position)
                throw new InvalidDataException("The PNG contains a truncated chunk.");
            // prism-vocab: external — APNG's animation-control chunk is decoded only at this boundary.
            if (header[4..].SequenceEqual("acTL"u8))
                throw new InvalidDataException("Animated PNG images require a separate animation output profile.");
            stream.Seek((long)length + 4, SeekOrigin.Current);
        }
        stream.Position = 0;
    }

    private static void ValidateArchive(ZipArchive archive) {
        if (archive.Entries.Count is < 1 or > MaximumEntries) throw new InvalidDataException("The publication archive exceeds its entry limit.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        long expanded = 0;
        foreach (var entry in archive.Entries) {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Any(char.IsControl)
                || name.Split('/').Any(segment => segment is "." or "..") || !names.Add(name))
                throw new InvalidDataException("The publication archive contains unsafe or duplicate member paths.");
            if (entry.Length > MaximumExpandedBytes - expanded) throw new InvalidDataException("The publication archive exceeds its expanded byte limit.");
            expanded += entry.Length;
        }
    }

    private static async Task ValidateEpubAsync(ZipArchive archive, CancellationToken cancellationToken) {
        var mimetype = archive.GetEntry("mimetype") ?? throw new InvalidDataException("The EPUB is missing its publication type marker.");
        if (mimetype.Length > 128) throw new InvalidDataException("The EPUB publication type marker is invalid.");
        using (var reader = new StreamReader(mimetype.Open(), Encoding.UTF8)) {
            if ((await reader.ReadToEndAsync(cancellationToken)).Trim() != "application/epub+zip") throw new InvalidDataException("The downloaded archive is not an EPUB publication.");
        }
        var container = await ReadXmlAsync(archive.GetEntry("META-INF/container.xml"), cancellationToken);
        // prism-vocab: external — EPUB container/package fields are decoded at this boundary.
        var packagePath = container.Root?.Element(ContainerNamespace + "rootfiles")?.Elements(ContainerNamespace + "rootfile")
            .Select(element => element.Attribute("full-path")?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (packagePath is null) throw new InvalidDataException("The EPUB does not identify a package document.");
        var package = await ReadXmlAsync(archive.GetEntry(packagePath), cancellationToken);
        var items = package.Root?.Element(PackageNamespace + "manifest")?.Elements(PackageNamespace + "item")
            .Where(item => item.Attribute("id") is not null).ToDictionary(item => item.Attribute("id")!.Value, item => item.Attribute("href")?.Value, StringComparer.Ordinal)
            ?? throw new InvalidDataException("The EPUB package has no content manifest.");
        var spine = package.Root?.Element(PackageNamespace + "spine")?.Elements(PackageNamespace + "itemref").ToArray() ?? [];
        if (spine.Length == 0) throw new InvalidDataException("The EPUB has no reading order.");
        var packageUri = new Uri("https://epub.invalid/" + packagePath);
        foreach (var section in spine) {
            var id = section.Attribute("idref")?.Value;
            if (id is null || !items.TryGetValue(id, out var href) || string.IsNullOrWhiteSpace(href))
                throw new InvalidDataException("The EPUB reading order references missing content.");
            var target = new Uri(packageUri, href);
            if (target.Host != packageUri.Host || archive.GetEntry(Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/'))) is null)
                throw new InvalidDataException("The EPUB reading order references unavailable content.");
        }
    }

    private static async Task<XDocument> ReadXmlAsync(ZipArchiveEntry? entry, CancellationToken cancellationToken) {
        if (entry is null || entry.Length > MaximumXmlBytes) throw new InvalidDataException("The EPUB contains a missing or oversized package document.");
        await using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXmlBytes
        });
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    private static void ValidateComic(ZipArchive archive, CancellationToken cancellationToken) {
        var pages = archive.Entries.Where(entry => Path.GetExtension(entry.FullName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".avif").ToArray();
        if (pages.Length == 0) throw new InvalidDataException("The comic archive has no readable image pages.");
        foreach (var page in pages) {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.Length > IntegrationImportPolicy.For(EntityKind.Image).MaximumBytes) throw new InvalidDataException("A comic page exceeds the image byte limit.");
            using var stream = page.Open();
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 100_000_000)
                throw new InvalidDataException("A comic page is not a readable, bounded image.");
            var scale = Math.Min(1f, 1024f / Math.Max(codec.Info.Width, codec.Info.Height));
            using var bitmap = SKBitmap.Decode(codec, codec.Info.WithSize(codec.GetScaledDimensions(scale)));
            if (bitmap is null) throw new InvalidDataException("A comic page could not be decoded.");
        }
    }
}
