using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Searches Prowlarr through its aggregate REST search API. Prowlarr normalizes every configured
/// indexer into a single JSON release feed, so this client maps that feed to <see cref="IndexerRelease"/>
/// without per-indexer Torznab parsing. The Jackett adapter (Torznab XML) shares the same port.
/// </summary>
public sealed class ProwlarrIndexerClient(
    HttpClient http,
    ProwlarrSearchConcurrencyGate? concurrency = null,
    ILogger<ProwlarrIndexerClient>? logger = null) : IIndexerSearchClient {
    private const int MaxPagesPerIndexer = 10;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);
    // The client is scoped to one operation: query variants share discovery without retaining stale
    // enabled/protocol settings across later searches. Cache metadata, not a caller-specific filter.
    private readonly ConcurrentDictionary<Guid, Lazy<Task<IReadOnlyList<ProviderProtocol>?>>> _providers = new();
    private sealed record ProviderProtocol(int Id, DownloadProtocol? Protocol);
    public IndexerKind Kind => IndexerKind.Prowlarr;
    /// <inheritdoc />
    public JobExecutionPolicy? ExecutionPolicy => ProwlarrSearchConcurrencyGate.ExecutionPolicy;

    public async Task<IReadOnlyList<IndexerRelease>> SearchAsync(IndexerConnection connection, IndexerQuery query, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Protocols is { Count: 0 }) return [];
        var allowedIndexers = await ResolveIndexerScopeAsync(connection, query.Protocols, cancellationToken);
        if (allowedIndexers is { Count: 0 }) return [];

        // One Prowlarr request fans out across every configured indexer. Large season-to-episode
        // fallback batches can otherwise put a dozen aggregate calls in flight, making Prowlarr queue
        // them until Prismedia's HTTP timeout expires. Two concurrent aggregates kept the live batch
        // responsive while still allowing overlap between a slow Usenet and torrent provider.
        using var searchLease = concurrency is null
            ? null
            : await concurrency.EnterAsync(cancellationToken);
        var releases = new List<IndexerRelease>();
        var seen = new HashSet<(int? IndexerId, string Identity)>();
        IReadOnlyList<int>? continuingIndexers = allowedIndexers;
        for (var page = 0; page < MaxPagesPerIndexer; page++) {
            JsonDocument document;
            try {
                document = await ReadPageAsync(
                    connection, query, page * ProwlarrProtocol.DefaultLimit, continuingIndexers, cancellationToken);
            } catch (Exception exception) when (page > 0 && !cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or JsonException or OperationCanceledException) {
                logger?.LogWarning(exception,
                    "Prowlarr continuation page {Page} failed; retaining {Count} releases from completed pages.", page + 1, releases.Count);
                break;
            }

            using (document) {
                if (document.RootElement.ValueKind != JsonValueKind.Array) {
                    break;
                }

                var counts = new Dictionary<int, int>();
                var progressingIndexers = new HashSet<int>();
                foreach (var item in document.RootElement.EnumerateArray()) {
                    var indexerId = Int(item, ProwlarrProtocol.IndexerId);
                    if (allowedIndexers is not null && indexerId is { } scopedId && !allowedIndexers.Contains(scopedId)) {
                        continue;
                    }
                    if (indexerId is { } id) {
                        counts[id] = counts.GetValueOrDefault(id) + 1;
                    }
                    if (MapRelease(item) is not { } release
                        || !seen.Add((indexerId, ReleaseIdentity(item, release)))) {
                        continue;
                    }

                    releases.Add(release);
                    if (indexerId is { } progressingId) {
                        progressingIndexers.Add(progressingId);
                    }
                }

                // Limit applies per provider. Only providers with a full page need another request;
                // repeating the aggregate would re-query every exhausted or slow tracker. Providers
                // ignoring offsets stop as soon as they repeat a page without adding any new releases.
                continuingIndexers = counts
                    .Where(pair => pair.Value >= ProwlarrProtocol.DefaultLimit && progressingIndexers.Contains(pair.Key))
                    .Select(pair => pair.Key)
                    .Order()
                    .ToArray();
                if (continuingIndexers.Count == 0) {
                    break;
                }
            }
        }
        return releases;
    }

    private async Task<IReadOnlyList<int>?> ResolveIndexerScopeAsync(
        IndexerConnection connection,
        IReadOnlyList<DownloadProtocol>? protocols,
        CancellationToken cancellationToken) {
        if (protocols is null) return null;
        var providers = await _providers.GetOrAdd(connection.Id, _ => new Lazy<Task<IReadOnlyList<ProviderProtocol>?>>(
            () => ReadProviderProtocolsAsync(connection, cancellationToken))).Value.WaitAsync(cancellationToken);
        return providers?.Where(provider => provider.Protocol is null || protocols.Contains(provider.Protocol.Value))
            .Select(provider => provider.Id).Order().ToArray();
    }

    private async Task<IReadOnlyList<ProviderProtocol>?> ReadProviderProtocolsAsync(
        IndexerConnection connection, CancellationToken cancellationToken) {
        using var discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCancellation.CancelAfter(DiscoveryTimeout);
        try {
            using var request = BuildRequest(connection, HttpMethod.Get, ProwlarrProtocol.IndexersEndpoint);
            using var response = await http.SendAsync(request, discoveryCancellation.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(discoveryCancellation.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: discoveryCancellation.Token);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var providers = new List<ProviderProtocol>();
            var ids = new HashSet<int>();
            foreach (var item in document.RootElement.EnumerateArray()) {
                // Incomplete catalogs cannot safely narrow the search. Unknown protocols remain
                // eligible so a provider/API upgrade cannot silently hide otherwise usable results.
                if (item.ValueKind != JsonValueKind.Object || Int(item, ProwlarrProtocol.Id) is not { } id
                    || id <= 0 || !ids.Add(id)) return null;
                if (item.TryGetProperty(ProwlarrProtocol.Enable, out var enabled) && enabled.ValueKind == JsonValueKind.False) continue;
                var raw = Text(item, ProwlarrProtocol.Protocol);
                DownloadProtocol? protocol = string.Equals(raw, DownloadProtocol.Usenet.ToCode(), StringComparison.OrdinalIgnoreCase)
                    ? DownloadProtocol.Usenet
                    : string.Equals(raw, DownloadProtocol.Torrent.ToCode(), StringComparison.OrdinalIgnoreCase)
                        ? DownloadProtocol.Torrent : null;
                providers.Add(new ProviderProtocol(id, protocol));
            }
            return providers;
        } catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or JsonException or OperationCanceledException) {
            logger?.LogDebug("Prowlarr protocol discovery was unavailable; searching the aggregate provider scope.");
            return null;
        }
    }

    private async Task<JsonDocument> ReadPageAsync(
        IndexerConnection connection,
        IndexerQuery query,
        int offset,
        IReadOnlyList<int>? indexerIds,
        CancellationToken cancellationToken) {
        var path = BuildSearchPath(query, offset, indexerIds);
        using var request = BuildRequest(connection, HttpMethod.Get, path);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string ReleaseIdentity(JsonElement item, IndexerRelease release) =>
        Text(item, ProwlarrProtocol.Guid) is { Length: > 0 } guid ? guid
            : release.InfoHash ?? release.DownloadUrl ?? release.MagnetUrl
                ?? $"{release.Title}\0{release.SizeBytes.ToString(CultureInfo.InvariantCulture)}";

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

    private static string BuildSearchPath(IndexerQuery query, int offset, IReadOnlyList<int>? indexerIds) {
        var parameters = new List<string> {
            $"{ProwlarrProtocol.QueryParam}={Uri.EscapeDataString(query.Text)}",
            $"{ProwlarrProtocol.TypeParam}={ProwlarrProtocol.TypeSearch}",
            $"{ProwlarrProtocol.LimitParam}={ProwlarrProtocol.DefaultLimit}"
        };
        foreach (var category in query.Categories) {
            parameters.Add($"{ProwlarrProtocol.CategoriesParam}={category}");
        }
        if (offset > 0) {
            parameters.Add($"{ProwlarrProtocol.OffsetParam}={offset}");
        }
        foreach (var indexerId in indexerIds ?? []) {
            parameters.Add($"{ProwlarrProtocol.IndexerIdsParam}={indexerId}");
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
    internal static JobExecutionPolicy ExecutionPolicy { get; } = new(MaxConcurrentSearches, TimeSpan.Zero);
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
