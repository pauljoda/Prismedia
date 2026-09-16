using System.IO.Compression;
using System.Security.Cryptography;
using Prismedia.Application.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;
using SkiaSharp;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationPublicationVerifierTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "prismedia-publication-verify-" + Guid.NewGuid().ToString("N"));
    private async Task<VerifiedIntegrationArtifact> ArtifactAsync(string extension, Action<ZipArchive>? write = null) {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "publication" + extension);
        if (write is null) await File.WriteAllTextAsync(path, "<html>Please sign in</html>");
        else { using var archive = ZipFile.Open(path, ZipArchiveMode.Create); write(archive); }
        var bytes = await File.ReadAllBytesAsync(path);
        return new("artifact", path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), Path.GetFileName(path));
    }
    private static void Entry(ZipArchive archive, string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(text); }

    [Theory]
    [InlineData(".epub", EntityKind.Book)]
    [InlineData(".pdf", EntityKind.Book)]
    [InlineData(".cbz", EntityKind.ComicInstallment)]
    public async Task LoginHtmlCannotMasqueradeAsAPublication(string extension, EntityKind kind) =>
        await Assert.ThrowsAsync<InvalidDataException>(() => VerifyInvalidAsync(extension, kind));
    private async Task VerifyInvalidAsync(string extension, EntityKind kind) => await new IntegrationPublicationVerifier().VerifyAsync(await ArtifactAsync(extension), kind, default);

    [Fact]
    public async Task EpubWithARealPackageAndReadingOrderIsAccepted() {
        var artifact = await ArtifactAsync(".epub", archive => {
            Entry(archive, "mimetype", "application/epub+zip");
            Entry(archive, "META-INF/container.xml", "<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container'><rootfiles><rootfile full-path='OEBPS/content.opf'/></rootfiles></container>");
            Entry(archive, "OEBPS/content.opf", "<package xmlns='http://www.idpf.org/2007/opf'><manifest><item id='chapter' href='chapter.xhtml'/></manifest><spine><itemref idref='chapter'/></spine></package>");
            Entry(archive, "OEBPS/chapter.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><body>Book</body></html>");
        });
        await new IntegrationPublicationVerifier().VerifyAsync(artifact, EntityKind.Book, default);
    }

    [Fact]
    public async Task ComicPagesMustDecodeAndArchivePathsMustBePortable() {
        var invalid = await ArtifactAsync(".cbz", archive => Entry(archive, "page.png", "not an image"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new IntegrationPublicationVerifier().VerifyAsync(invalid, EntityKind.ComicInstallment, default));
        File.Delete(invalid.Path);
        var traversal = await ArtifactAsync(".cbz", archive => Entry(archive, "../page.png", "not an image"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new IntegrationPublicationVerifier().VerifyAsync(traversal, EntityKind.ComicInstallment, default));
        File.Delete(traversal.Path);
        var valid = await ArtifactAsync(".cbz", archive => {
            using var bitmap = new SKBitmap(2, 2);
            bitmap.Erase(SKColors.Black);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = archive.CreateEntry("page.png").Open();
            encoded.SaveTo(output);
        });
        await new IntegrationPublicationVerifier().VerifyAsync(valid, EntityKind.ComicInstallment, default);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
