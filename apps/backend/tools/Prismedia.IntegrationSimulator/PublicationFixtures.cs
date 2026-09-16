using System.IO.Compression;
using System.Text;

namespace Prismedia.IntegrationSimulator;

/// <summary>Creates original synthetic publications so contract testing never needs a real external download.</summary>
public static class PublicationFixtures {
    /// <summary>Returns a small valid EPUB or one-page CBZ for the requested declared output format.</summary>
    public static byte[] Create(string format) {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
            if (format == ArchiverWire.Epub) {
                Add(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
                Add(archive, "META-INF/container.xml", """
                    <?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml"/></rootfiles></container>
                    """);
                Add(archive, "EPUB/package.opf", """
                    <?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:uuid:810759ca-0974-43c9-94ca-d2d9537c8f89</dc:identifier><dc:title>Integration Field Notes</dc:title><dc:creator>Prismedia Test Fixture</dc:creator><dc:language>en</dc:language><meta property="dcterms:modified">2026-01-01T00:00:00Z</meta></metadata><manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest><spine><itemref idref="chapter"/></spine></package>
                    """);
                Add(archive, "EPUB/chapter.xhtml", """
                    <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Durable transfers</title></head><body><h1>Durable transfers</h1><p>This original fixture verifies inspection, durable submission, artifact retrieval, library import, and acknowledgement.</p></body></html>
                    """);
                Add(archive, "EPUB/nav.xhtml", """
                    <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="chapter.xhtml">Durable transfers</a></li></ol></nav></body></html>
                    """);
            } else if (format == ArchiverWire.Cbz) {
                // A tiny synthetic PNG is sufficient to test archive ownership and page decoding.
                using var entry = archive.CreateEntry("001.png").Open();
                entry.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            } else throw new ApiFailure(400, ArchiverWire.Invalid, "Unsupported fixture format.");
        }
        return stream.ToArray();
    }
    private static void Add(ZipArchive archive, string path, string text, CompressionLevel level = CompressionLevel.Optimal) {
        using var writer = new StreamWriter(archive.CreateEntry(path, level).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
