using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Application.Integrations;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>
/// Bounded HTTP byte retrieval. A private receipt survives restarts; media validation and library import are separate steps.
/// </summary>
public sealed class HttpIntegrationArtifactTransfer(IntegrationArtifactStorageOptions options,
    HttpClient client) : IIntegrationArtifactTransfer {
    #region Static Variables

    private const string ReceiptSuffix = ".verified.json";

    private const long MaximumArtifactBytes = 250L * 1024 * 1024 * 1024;

    private const string UserAgent = "Prismedia (+https://pauljoda.github.io/Prismedia/)";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 8 };

    #endregion

    #region Actions - Retrieval

    /// <inheritdoc />
    public async Task<VerifiedIntegrationArtifact> TransferAsync(IntegrationArtifactTransferRequest request,
        CancellationToken cancellationToken) {
        Validate(request);
        var delivery = request.Delivery;
        var origin = new Uri(request.AllowedOrigin);
        var address = RequireScope(origin, delivery.Url);
        var root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(root);
        RejectLinks(root);
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var operationRoot = Path.Combine(root, request.OperationId.ToString("N"));
        Directory.CreateDirectory(operationRoot);
        RejectLinks(operationRoot);
        var key = ArtifactKey(request.ArtifactId);
        var finalPath = Path.Combine(operationRoot, key + Path.GetExtension(delivery.SuggestedFileName).ToLowerInvariant());
        var partialPath = Path.Combine(operationRoot, key + ".partial");
        var receiptPath = Path.Combine(operationRoot, key + ReceiptSuffix);
        var lockPath = Path.Combine(operationRoot, key + ".lock");
        foreach (var path in new[] { finalPath, partialPath, receiptPath, lockPath, receiptPath + ".tmp" }) {
            RejectLinks(path);
        }

        // An OS file handle prevents concurrent retries from writing the same bytes across API/worker processes.
        await using var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(receiptPath)) {
            return await ReadVerifiedAsync(request, finalPath, receiptPath, cancellationToken);
        }

        if (File.Exists(finalPath)) {
            // The process can stop between atomic placement and its receipt. Revalidate that exact file before publishing evidence.
            var recovered = await VerifyAsync(request, finalPath, cancellationToken);
            await WriteReceiptAsync(receiptPath, recovered, cancellationToken);
            return recovered;
        }

        if (delivery.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow) {
            throw new InvalidDataException("The artifact retrieval authorization expired.");
        }

        var offset = File.Exists(partialPath) && (delivery.Sha256 is not null || delivery.Sha1 is not null)
            ? new FileInfo(partialPath).Length
            : 0;
        if (offset > request.MaximumBytes || delivery.ByteSize is { } expected && offset > expected) {
            offset = 0;
        }

        if (offset > 0 && delivery.ByteSize == offset) {
            var recovered = await VerifyAsync(request, partialPath, cancellationToken);
            File.Move(partialPath, finalPath);
            recovered = recovered with { Path = finalPath };
            await WriteReceiptAsync(receiptPath, recovered, cancellationToken);
            return recovered;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        using var response = await OpenAsync(origin, address, delivery.Headers, offset, deadline.Token);
        if (response.StatusCode == HttpStatusCode.OK) {
            offset = 0; // The server may ignore Range; restart rather than append a full body.
        } else if (response.StatusCode == HttpStatusCode.PartialContent) {
            var range = response.Content.Headers.ContentRange;
            if (offset == 0 || range?.From != offset || range.To is null || range.Length is null || range.To != range.Length - 1
                || delivery.ByteSize is { } rangeLength && range.Length != rangeLength || range.Length > request.MaximumBytes) {
                throw new InvalidDataException("The server returned an inconsistent artifact byte range.");
            }
        } else {
            throw new HttpRequestException($"Artifact retrieval returned HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidDataException("The artifact response uses an unsupported content encoding.");
        }

        if (response.Content.Headers.ContentLength is { } responseSize && (responseSize > request.MaximumBytes - offset
            || delivery.ByteSize is { } length && responseSize != length - offset)) {
            throw new InvalidDataException("The artifact response length does not match its declared limits.");
        }

        await using (var output = new FileStream(partialPath, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write,
            FileShare.None, 1024 * 128, true)) {
            output.Position = offset;
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[1024 * 128];
            while (true) {
                var count = await input.ReadAsync(buffer, deadline.Token);
                if (count == 0) {
                    break;
                }

                if (output.Position + count > request.MaximumBytes || delivery.ByteSize is { } size && output.Position + count > size) {
                    throw new InvalidDataException("The artifact exceeds its declared byte limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
            }

            await output.FlushAsync(deadline.Token);
            output.Flush(flushToDisk: true);
        }

        VerifiedIntegrationArtifact verified;
        try {
            verified = await VerifyAsync(request, partialPath, cancellationToken);
        } catch (InvalidDataException) {
            File.Delete(partialPath);
            throw;
        }

        File.Move(partialPath, finalPath);
        verified = verified with { Path = finalPath };
        await WriteReceiptAsync(receiptPath, verified, cancellationToken);
        return verified;
    }

    /// <inheritdoc />
    public async Task<VerifiedIntegrationArtifact?> ReadVerifiedAsync(Guid operationId, string artifactId, string fileName,
        long sizeBytes, string sha256, CancellationToken cancellationToken) {
        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(artifactId) || artifactId.Length > 2048
            || sizeBytes is <= 0 or > MaximumArtifactBytes || sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit)) {
            throw new ArgumentException("Complete persisted artifact evidence is required for local recovery.");
        }

        ValidateFileName(fileName);
        var root = Path.GetFullPath(options.RootPath);
        var operationRoot = Path.Combine(root, operationId.ToString("N"));
        var key = ArtifactKey(artifactId);
        var finalPath = Path.Combine(operationRoot, key + Path.GetExtension(fileName).ToLowerInvariant());
        var receiptPath = Path.Combine(operationRoot, key + ReceiptSuffix);
        var lockPath = Path.Combine(operationRoot, key + ".lock");
        foreach (var path in new[] { root, operationRoot, finalPath, receiptPath, lockPath, receiptPath + ".tmp" }) {
            RejectLinks(path);
        }

        if (!File.Exists(finalPath)) {
            return null;
        }

        await using var ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var hasReceipt = File.Exists(receiptPath);
        if (hasReceipt) {
            var receipt = await ReadReceiptAsync(receiptPath, cancellationToken);
            if (receipt is null || receipt.ArtifactId != artifactId || receipt.FileName != fileName || receipt.SizeBytes != sizeBytes
                || !string.Equals(receipt.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException("The staged artifact receipt does not match the accepted transfer.");
            }
        }

        await using var stream = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        if (stream.Length != sizeBytes || !Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken))
            .Equals(sha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("The staged artifact changed after verification.");
        }

        var verified = new VerifiedIntegrationArtifact(artifactId, finalPath, sizeBytes, sha256.ToLowerInvariant(), fileName);
        // A crash can leave the atomic final file without its receipt. Persisted size/hash evidence
        // lets us reconstruct that receipt locally, without reauthorizing or fetching remote bytes.
        if (!hasReceipt) {
            await WriteReceiptAsync(receiptPath, verified, cancellationToken);
        }

        return verified;
    }

    #endregion

    #region Actions - Staging Paths

    internal static string ArtifactKey(string artifactId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(artifactId)));

    #endregion

    #region Actions - Requests

    private async Task<HttpResponseMessage> OpenAsync(Uri origin, Uri address, IReadOnlyDictionary<string, string> headers, long offset,
        CancellationToken cancellationToken) {
        for (var redirects = 0; redirects <= 5; redirects++) {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            foreach (var header in headers) {
                request.Headers.Add(header.Key, header.Value);
            }

            request.Headers.AcceptEncoding.ParseAdd("identity");
            if (offset > 0) {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) {
                return response;
            }

            using (response) {
                if (redirects == 5 || response.Headers.Location is not { } location) {
                    throw new InvalidDataException("The artifact redirect chain is invalid.");
                }

                address = RequireScope(origin, new Uri(address, location).AbsoluteUri);
            }
        }

        throw new InvalidDataException("The artifact returned too many redirects.");
    }

    #endregion

    #region Actions - Validation

    private static void Validate(IntegrationArtifactTransferRequest request) {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ArtifactId) || request.ArtifactId.Length > 2048
            || request.MaximumBytes is <= 0 or > MaximumArtifactBytes || request.Delivery is null) {
            throw new ArgumentException("A stable operation, artifact identity, and bounded transfer size are required.");
        }

        var delivery = request.Delivery;
        if (delivery.ByteSize is < 0 || delivery.ByteSize > request.MaximumBytes
            || delivery.Sha256 is { } hash && (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            || delivery.Sha1 is { } sourceHash && (sourceHash.Length != 40 || !sourceHash.All(Uri.IsHexDigit))) {
            throw new ArgumentException("The artifact size or declared checksum is invalid.");
        }

        ValidateFileName(delivery.SuggestedFileName);
        if (delivery.Headers is null || delivery.Headers.Count > 8 || delivery.Headers.Any(header =>
                !(header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                || header.Value is null || header.Value.Length > 16384 || header.Value.Any(character => character is '\r' or '\n'))) {
            throw new ArgumentException("Artifact retrieval headers exceed their allowed scope.");
        }

        if (!Uri.TryCreate(request.AllowedOrigin, UriKind.Absolute, out var origin)) {
            throw new ArgumentException("A configured HTTP origin is required.");
        }

        _ = RequireScope(origin, origin.AbsoluteUri);
    }

    private static void ValidateFileName(string name) {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".."
            || name.Any(character => char.IsControl(character) || character is '/' or '\\' or ':')
            || Path.GetExtension(name).Length > 16) {
            throw new ArgumentException("The artifact must suggest a portable file name, not a destination path.");
        }
    }

    private static Uri RequireScope(Uri origin, string address) {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")
            || target.UserInfo.Length != 0 || target.Fragment.Length != 0 || origin.Scheme != target.Scheme
            || origin.IdnHost != target.IdnHost || origin.Port != target.Port) {
            throw new InvalidDataException("The artifact URL is outside the configured connection origin.");
        }

        return target;
    }

    private static void RejectLinks(string path) {
        // The configured root is an explicit filesystem boundary. Check that root, the operation
        // directory, and each constructed child; platform ancestors may legitimately be links.
        if (new FileInfo(path).LinkTarget is not null || ((File.Exists(path) || Directory.Exists(path))
            && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))) {
            throw new InvalidDataException("Artifact staging cannot traverse filesystem links.");
        }
    }

    #endregion

    #region Actions - Verification

    private static async Task<VerifiedIntegrationArtifact> VerifyAsync(IntegrationArtifactTransferRequest request, string path,
        CancellationToken cancellationToken) {
        RejectLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        if (stream.Length > request.MaximumBytes || request.Delivery.ByteSize is { } size && size != stream.Length) {
            throw new InvalidDataException("The staged artifact size does not match its declared size.");
        }

        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        if (request.Delivery.Sha256 is { } expected && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("The staged artifact SHA-256 does not match its manifest.");
        }

        if (request.Delivery.Sha1 is { } sourceHash) {
            // Some catalogs publish only SHA-1 version checksums. Always retain our independent SHA-256 receipt.
            stream.Position = 0;
            var sourceChecksum = Convert.ToHexStringLower(await SHA1.HashDataAsync(stream, cancellationToken));
            if (!sourceChecksum.Equals(sourceHash, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException("The staged artifact does not match the source version checksum.");
            }
        }

        return new(request.ArtifactId, path, stream.Length, hash, request.Delivery.SuggestedFileName);
    }

    private static async Task<VerifiedIntegrationArtifact> ReadVerifiedAsync(IntegrationArtifactTransferRequest request, string finalPath,
        string receiptPath, CancellationToken cancellationToken) {
        if (new FileInfo(receiptPath).Length > 8192 || !File.Exists(finalPath)) {
            throw new InvalidDataException("The verified artifact receipt has no valid staged file.");
        }

        var receipt = await ReadReceiptAsync(receiptPath, cancellationToken);
        var verified = await VerifyAsync(request, finalPath, cancellationToken);
        if (receipt is null || receipt.ArtifactId != verified.ArtifactId || receipt.FileName != verified.FileName
            || receipt.SizeBytes != verified.SizeBytes || receipt.Sha256 != verified.Sha256) {
            throw new InvalidDataException("The staged artifact changed after verification.");
        }

        return verified;
    }

    #endregion

    #region Actions - Receipts

    private static async Task<Receipt?> ReadReceiptAsync(string path, CancellationToken cancellationToken) {
        if (new FileInfo(path).Length > 8192) {
            throw new InvalidDataException("The artifact receipt exceeds its size limit.");
        }

        try {
            return JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(path, cancellationToken), Json);
        } catch (JsonException) {
            throw new InvalidDataException("The artifact receipt is invalid.");
        }
    }

    private static async Task WriteReceiptAsync(string receiptPath, VerifiedIntegrationArtifact artifact,
        CancellationToken cancellationToken) {
        var receipt = new Receipt(artifact.ArtifactId, artifact.FileName, artifact.SizeBytes, artifact.Sha256);
        await using (var stream = new FileStream(receiptPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) {
            await JsonSerializer.SerializeAsync(stream, receipt, Json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(receiptPath + ".tmp", receiptPath, overwrite: true);
    }

    #endregion

    private sealed record Receipt(string ArtifactId, string FileName, long SizeBytes, string Sha256);
}
