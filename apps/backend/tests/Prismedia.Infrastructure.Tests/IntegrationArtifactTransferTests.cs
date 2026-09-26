using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Integrations;

namespace Prismedia.Infrastructure.Tests;

public sealed class IntegrationArtifactTransferTests : IDisposable {
    [Fact]
    public async Task SourceSha1IsVerifiedWhileLocalEvidenceRemainsSha256() {
        using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(Bytes));
        var request = Request;
        request = request with { Delivery = request.Delivery with { Sha256 = null, Sha1 = sha1 } };
        var transfer = new HttpIntegrationArtifactTransfer(new(root), client);
        var result = await transfer.TransferAsync(request, default);
        Assert.Equal(Hash, result.Sha256);
        Assert.Equal(result, await transfer.TransferAsync(request, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => transfer.TransferAsync(request with { Delivery = request.Delivery with { Sha1 = new string('0', 40) } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => transfer.TransferAsync(request with { Delivery = request.Delivery with { Sha1 = "invalid" } }, default));
    }

    [Fact]
    public async Task SourceSha1MismatchNeverProducesAVerifiedReceipt() {
        using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
        var request = Request;
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(
            request with { Delivery = request.Delivery with { Sha256 = null, Sha1 = new string('0', 40) } }, default));
        Assert.Empty(Directory.GetFiles(root, "*.verified.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AnonymousDownloadDoesNotFollowRedirectEvenToAnotherDeclaredOrigin() {
        var declaration = new PluginIntegrationDefinition(1,
            [new(PluginCapability.AcquisitionSource, [IntegrationOperation.Resolve], [EntityKind.Book])], [],
            ["https://first.test", "https://second.test"]);
        var delivery = Request.Delivery with { Url = "https://first.test/book.epub", Headers = new Dictionary<string, string>() };
        var origin = IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(declaration, "https://catalog.test", delivery);
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            calls++;
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://second.test/book.epub");
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client)
            .TransferAsync(Request with { AllowedOrigin = origin, Delivery = delivery }, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DeclaredCatalogSubdomainPinsOneAnonymousFileHost() {
        var declaration = new PluginIntegrationDefinition(1,
            [new(PluginCapability.AcquisitionSource, [IntegrationOperation.Resolve], [EntityKind.ComicInstallment])], [],
            AnonymousArtifactHostSuffixes: ["archive.org"]);
        var delivery = Request.Delivery with { Url = "https://dn123.eu.archive.org/comic.cbz", Headers = new Dictionary<string, string>(), SuggestedFileName = "comic.cbz" };
        var origin = IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(declaration, "https://archive.org", delivery);
        Assert.Equal("https://dn123.eu.archive.org", origin);
        foreach (var address in new[] {
            "https://evilarchive.org/comic.cbz", "https://archive.org.evil.test/comic.cbz",
            "http://dn123.eu.archive.org/comic.cbz", "https://dn123.eu.archive.org:8443/comic.cbz"
        }) Assert.Throws<IntegrationInvocationException>(() => IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(
            declaration, "https://archive.org", delivery with { Url = address }));
        Assert.Throws<IntegrationInvocationException>(() => IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(
            declaration, "https://other.test", delivery));
        Assert.Throws<IntegrationInvocationException>(() => IntegrationDeliveryOriginPolicy.RequireAllowedOrigin(
            declaration, "https://archive.org", delivery with { Headers = new Dictionary<string, string> { ["Authorization"] = "secret" } }));

        using var client = new HttpClient(new Handler(request => {
            Assert.Equal("dn123.eu.archive.org", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return Ok(Bytes);
        }));
        var result = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(
            Request with { AllowedOrigin = origin, Delivery = delivery with { ByteSize = Bytes.Length, Sha256 = Hash } }, default);
        Assert.Equal(Hash, result.Sha256);
    }

    private readonly string root = Path.Combine(Path.GetTempPath(), "prismedia-artifacts-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bytes = "verified publication bytes"u8.ToArray();
    private static string Hash => Convert.ToHexStringLower(SHA256.HashData(Bytes));
    private static IntegrationArtifactTransferRequest Request => new(Guid.NewGuid(), "artifact-one", "https://catalog.test/",
        new("https://catalog.test/file.epub", new Dictionary<string, string> { ["Authorization"] = "Bearer secret" }, "book.epub", Bytes.Length, Hash), 1024);

    [Fact]
    public async Task VerifiedReceiptSurvivesRestartAndDoesNotFetchAgain() {
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            calls++; Assert.Equal("Bearer secret", request.Headers.Authorization!.ToString()); return Ok(Bytes);
        }));
        var request = Request;
        var result = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default);
        var retried = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default);
        Assert.Equal(Hash, result.Sha256);
        Assert.Equal(result, retried);
        Assert.Equal(1, calls);
        Assert.Equal(Bytes, await File.ReadAllBytesAsync(result.Path));
    }

    [Fact]
    public async Task LocalRecoveryNeedsNoLiveSourceAndMissingBytesDoNotStartANewDownload() {
        var request = Request;
        using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
        var result = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default);
        using var unavailable = new HttpClient(new Handler(_ => throw new InvalidOperationException("Source must not be called")));
        var restarted = new HttpIntegrationArtifactTransfer(new(root), unavailable);
        Assert.Equal(result, await restarted.ReadVerifiedAsync(request.OperationId, result.ArtifactId, result.FileName, result.SizeBytes, result.Sha256, default));
        File.Delete(result.Path);
        Assert.Null(await restarted.ReadVerifiedAsync(request.OperationId, result.ArtifactId, result.FileName, result.SizeBytes, result.Sha256, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalRecoveryAfterReceiptWriteCrashRequiresTheAcceptedBytes(bool tampered) {
        var request = Request;
        using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
        var result = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default);
        var receipt = Assert.Single(Directory.GetFiles(root, "*.verified.json", SearchOption.AllDirectories));
        File.Delete(receipt); // A crash after the atomic file move, before publishing its receipt.
        if (tampered) await File.WriteAllBytesAsync(result.Path, new byte[Bytes.Length]);
        using var unavailable = new HttpClient(new Handler(_ => throw new InvalidOperationException("Source must not be called")));
        var restarted = new HttpIntegrationArtifactTransfer(new(root), unavailable);

        if (tampered) {
            await Assert.ThrowsAsync<InvalidDataException>(() => restarted.ReadVerifiedAsync(request.OperationId,
                result.ArtifactId, result.FileName, result.SizeBytes, result.Sha256, default));
            Assert.False(File.Exists(receipt));
            return;
        }
        Assert.Equal(result, await restarted.ReadVerifiedAsync(request.OperationId, result.ArtifactId,
            result.FileName, result.SizeBytes, result.Sha256, default));
        Assert.True(File.Exists(receipt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedHashPinnedTransferResumesAndVerifiesTheWholeFile(bool sourceSha1) {
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            if (++calls == 1) return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(Bytes, 8)) };
            Assert.Equal("bytes=8-", request.Headers.Range!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Bytes[8..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(8, Bytes.Length - 1, Bytes.Length);
            return response;
        }));
        var request = Request;
        if (sourceSha1) request = request with { Delivery = request.Delivery with { Sha256 = null, Sha1 = Convert.ToHexStringLower(SHA1.HashData(Bytes)) } };
        await Assert.ThrowsAsync<IOException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default));
        var result = await new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default);
        Assert.Equal(Hash, result.Sha256);
        Assert.Equal(Bytes.Length, result.SizeBytes);
    }

    [Fact]
    public async Task CorruptBytesNeverProduceAVerifiedReceipt() {
        using var client = new HttpClient(new Handler(_ => Ok(new byte[Bytes.Length])));
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(Request, default));
        Assert.Empty(Directory.GetFiles(root, "*.verified.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TamperedCompletedFileIsNotTrustedFromItsReceipt() {
        using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
        var request = Request;
        var transfer = new HttpIntegrationArtifactTransfer(new(root), client);
        var result = await transfer.TransferAsync(request, default);
        await File.WriteAllTextAsync(result.Path, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => transfer.TransferAsync(request, default));
    }

    [Fact]
    public async Task ForeignRedirectDoesNotReceiveCredentials() {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://elsewhere.test/file.epub");
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(Request, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OversizedBodiesAndUnsafeSuggestedPathsAreRejected() {
        using var client = new HttpClient(new Handler(_ => Ok(new byte[2048])));
        var request = Request;
        await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request with { Delivery = request.Delivery with { ByteSize = null, Sha256 = null } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request with { Delivery = request.Delivery with { SuggestedFileName = "../../outside.epub" } }, default));
    }

    [Fact]
    public async Task IncorrectRangeCannotBeAppendedToPartialBytes() {
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => {
            if (++calls == 1) return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(Bytes, 8)) };
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Bytes[9..]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(9, Bytes.Length - 1, Bytes.Length);
            return response;
        }));
        var request = Request;
        var transfer = new HttpIntegrationArtifactTransfer(new(root), client);
        await Assert.ThrowsAsync<IOException>(() => transfer.TransferAsync(request, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => transfer.TransferAsync(request, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServerIgnoringRangeAndUnpinnedDownloadsRestartWithoutDuplicatingBytes(bool pinned) {
        var calls = 0;
        using var client = new HttpClient(new Handler(request => {
            if (++calls == 1) return new(HttpStatusCode.OK) { Content = new StreamContent(new InterruptedStream(Bytes, 8)) };
            Assert.Equal(pinned, request.Headers.Range is not null);
            return Ok(Bytes);
        }));
        var request = Request;
        if (!pinned) request = request with { Delivery = request.Delivery with { Sha256 = null } };
        var transfer = new HttpIntegrationArtifactTransfer(new(root), client);
        await Assert.ThrowsAsync<IOException>(() => transfer.TransferAsync(request, default));
        var result = await transfer.TransferAsync(request, default);
        Assert.Equal(Bytes, await File.ReadAllBytesAsync(result.Path));
        Assert.Equal(Hash, result.Sha256);
    }

    [Fact]
    public async Task LinkedOperationDirectoryCannotWriteOutsideStaging() {
        var request = Request;
        var outside = root + "-outside";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(root, request.OperationId.ToString("N")), outside);
        try {
            using var client = new HttpClient(new Handler(_ => Ok(Bytes)));
            await Assert.ThrowsAsync<InvalidDataException>(() => new HttpIntegrationArtifactTransfer(new(root), client).TransferAsync(request, default));
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        } finally { Directory.Delete(outside, true); }
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private sealed class InterruptedStream(byte[] bytes, int firstLength) : MemoryStream(bytes) {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position >= firstLength ? ValueTask.FromException<int>(new IOException("Connection lost")) : base.ReadAsync(buffer[..Math.Min(buffer.Length, firstLength)], cancellationToken);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
