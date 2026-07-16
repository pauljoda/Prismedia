using System.Globalization;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Searches Prowlarr through its aggregate REST search API. Prowlarr normalizes every configured
/// indexer into a single JSON release feed, so this client maps that feed to <see cref="IndexerRelease"/>
/// without per-indexer Torznab parsing. The Jackett adapter (Torznab XML) shares the same port.
/// </summary>
public sealed class ProwlarrIndexerClient(
    HttpClient http,
    ProwlarrSearchConcurrencyGate? concurrency = null) : IIndexerSearchClient {
    public IndexerKind Kind => IndexerKind.Prowlarr;

    public async Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken) {
        // One Prowlarr request fans out across every configured indexer. Large season-to-episode
        // fallback batches can otherwise put a dozen aggregate calls in flight, making Prowlarr queue
        // them until Prismedia's HTTP timeout expires. Two concurrent aggregates kept the live batch
        // responsive while still allowing overlap between a slow Usenet and torrent provider.
        using var searchLease = concurrency is null
            ? null
            : await concurrency.EnterAsync(cancellationToken);
        var path = BuildSearchPath(query);
        using var request = BuildRequest(connection, HttpMethod.Get, path);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var releases = new List<IndexerRelease>(document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray()) {
            if (MapRelease(item) is { } release) {
                releases.Add(release);
            }
        }

        return releases;
    }

    public async Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) {
        try {
            using var request = BuildRequest(connection, HttpMethod.Get, ProwlarrProtocol.SystemStatusEndpoint);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) {
                return new IndexerConnectionTest(true, "Connected to Prowlarr.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                return new IndexerConnectionTest(false, "Prowlarr rejected the API key.");
            }

            return new IndexerConnectionTest(false, $"Prowlarr returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new IndexerConnectionTest(false, ex.Message);
        }
    }

    private static string BuildSearchPath(IndexerQuery query) {
        var parameters = new List<string> {
            $"{ProwlarrProtocol.QueryParam}={Uri.EscapeDataString(query.Text)}",
            $"{ProwlarrProtocol.TypeParam}={ProwlarrProtocol.TypeSearch}",
            $"{ProwlarrProtocol.LimitParam}={ProwlarrProtocol.DefaultLimit}"
        };
        foreach (var category in query.Categories) {
            parameters.Add($"{ProwlarrProtocol.CategoriesParam}={category}");
        }

        return $"{ProwlarrProtocol.SearchEndpoint}?{string.Join('&', parameters)}";
    }

    private static IndexerRelease? MapRelease(JsonElement item) {
        var title = Text(item, ProwlarrProtocol.Title);
        if (string.IsNullOrWhiteSpace(title)) {
            return null;
        }

        return new IndexerRelease(
            title,
            Long(item, ProwlarrProtocol.Size) ?? 0,
            Int(item, ProwlarrProtocol.Seeders),
            Int(item, ProwlarrProtocol.Leechers),
            DecodeProtocol(Text(item, ProwlarrProtocol.Protocol)),
            Text(item, ProwlarrProtocol.DownloadUrl),
            Text(item, ProwlarrProtocol.MagnetUrl),
            Text(item, ProwlarrProtocol.InfoHash),
            Text(item, ProwlarrProtocol.InfoUrl),
            null,
            DateTimeOffset.TryParse(Text(item, ProwlarrProtocol.PublishDate), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                ? published
                : null);
    }

    private static DownloadProtocol DecodeProtocol(string? raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return DownloadProtocol.Torrent;
        }

        return string.Equals(raw, DownloadProtocol.Usenet.ToCode(), StringComparison.OrdinalIgnoreCase)
            ? DownloadProtocol.Usenet
            : DownloadProtocol.Torrent;
    }

    private HttpRequestMessage BuildRequest(IndexerConnection connection, HttpMethod method, string path) {
        var request = new HttpRequestMessage(method, new Uri(new Uri(connection.BaseUrl.TrimEnd('/') + "/"), path));
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) {
            request.Headers.Add(ProwlarrProtocol.ApiKeyHeader, connection.ApiKey);
        }

        return request;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}

/// <summary>
/// Process-wide cap for Prowlarr aggregate searches. The global worker concurrency may remain high for
/// scans, probes, and imports while release-search fan-out stays within the indexer manager's capacity.
/// </summary>
public sealed class ProwlarrSearchConcurrencyGate {
    private const int MaxConcurrentSearches = 2;
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrentSearches, MaxConcurrentSearches);

    /// <summary>Waits for one aggregate-search slot and returns a lease that releases it.</summary>
    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken) {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                semaphore.Release();
            }
        }
    }
}

/// <summary>Resolves the configured <see cref="IIndexerSearchClient"/> for an indexer family.</summary>
public sealed class IndexerSearchClientFactory(IEnumerable<IIndexerSearchClient> clients) : IIndexerSearchClientFactory {
    private readonly Dictionary<IndexerKind, IIndexerSearchClient> _clients = clients.ToDictionary(client => client.Kind);

    public IIndexerSearchClient Get(IndexerKind kind) =>
        _clients.TryGetValue(kind, out var client)
            ? client
            : throw new NotSupportedException($"No indexer search client is registered for '{kind}'.");
}
