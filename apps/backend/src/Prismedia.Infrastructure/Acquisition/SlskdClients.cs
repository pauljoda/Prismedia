using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Stable slskd wire names and Prismedia's opaque Soulseek release locator prefix.</summary>
public static class SoulseekProtocol {
    public const string ApiKeyHeader = "X-API-Key";
    public const string LocatorPrefix = "slskd:";
    public const string LegacyTransferLocatorPrefix = "slskd-legacy:";
    public const string SearchesPath = "/api/v0/searches";
    public const string DownloadBatchesPath = "/api/v0/transfers/downloads/batches";
    public const string DownloadsPath = "/api/v0/transfers/downloads";
    public const string ServerPath = "/api/v0/server";
    public const string SearchCompletedState = "Completed";
    public const string TransferCompletedState = "Completed";
    public const string TransferSucceededState = "Succeeded";
    public const string NormalizedFailedState = "Failed";
    public const string NormalizedQueuedState = "Queued";
}

/// <summary>One remote file selected from a Soulseek peer response.</summary>
public sealed record SoulseekFileLocator(string Filename, long Size);

/// <summary>Server-only payload passed from the slskd search adapter to its download adapter.</summary>
public sealed record SoulseekReleaseLocator(Guid SearchId, string Username, IReadOnlyList<SoulseekFileLocator> Files);

/// <summary>Durable identity for a group of transfers queued through a legacy slskd release.</summary>
internal sealed record SoulseekLegacyTransferLocator(string Fingerprint);

/// <summary>Encodes Soulseek peer/file identity without exposing it through Prismedia's public contracts.</summary>
public static class SoulseekLocator {
    /// <summary>Serializes a peer and its selected files into an opaque locator.</summary>
    public static string Encode(SoulseekReleaseLocator locator) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(locator, JsonOptions);
        return SoulseekProtocol.LocatorPrefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Decodes and validates an opaque slskd release locator.</summary>
    public static SoulseekReleaseLocator Decode(string value) {
        if (!value.StartsWith(SoulseekProtocol.LocatorPrefix, StringComparison.Ordinal)) {
            throw new InvalidDataException("The selected release is not an slskd Soulseek locator.");
        }

        var encoded = value[SoulseekProtocol.LocatorPrefix.Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        return JsonSerializer.Deserialize<SoulseekReleaseLocator>(Convert.FromBase64String(encoded), JsonOptions)
            ?? throw new InvalidDataException("The selected Soulseek locator is empty.");
    }

    internal static string EncodeLegacy(SoulseekLegacyTransferLocator locator) {
        return $"{SoulseekProtocol.LegacyTransferLocatorPrefix}{locator.Fingerprint}";
    }

    internal static bool TryDecodeLegacy(string value, out SoulseekLegacyTransferLocator locator) {
        locator = null!;
        if (!value.StartsWith(SoulseekProtocol.LegacyTransferLocatorPrefix, StringComparison.Ordinal)) {
            return false;
        }

        var fingerprint = value[SoulseekProtocol.LegacyTransferLocatorPrefix.Length..];
        if (fingerprint.Length != 43 || fingerprint.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) {
            return false;
        }

        locator = new SoulseekLegacyTransferLocator(fingerprint);
        return true;
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>Serializes Soulseek searches because slskd permits only one active operation at a time.</summary>
public sealed class SlskdSearchConcurrencyGate {
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits for the slskd search slot and returns a lease that releases it.</summary>
    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken) {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable {
        private int _disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release();
        }
    }
}

/// <summary>Searches the Soulseek network through slskd and normalizes peer folders/files as releases.</summary>
public sealed partial class SlskdIndexerClient(
    HttpClient http,
    SlskdSearchConcurrencyGate? concurrency = null) : IIndexerSearchClient {
    private const int SoulseekSearchTimeoutMilliseconds = 10_000;
    private const int SearchCompletionPollAttempts = 60;
    private static readonly TimeSpan SearchCompletionPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".m4b", ".wma", ".opus",
        ".aiff", ".aif", ".alac", ".ape", ".dsf", ".dff", ".wv"
    };

    public IndexerKind Kind => IndexerKind.Slskd;

    public async Task<IReadOnlyList<IndexerRelease>> SearchAsync(
        IndexerConnection connection,
        IndexerQuery query,
        CancellationToken cancellationToken) {
        if (query.Kind is not (EntityKind.AudioLibrary or EntityKind.AudioTrack or EntityKind.MusicArtist)) {
            return [];
        }

        using var searchLease = concurrency is null
            ? null
            : await concurrency.EnterAsync(cancellationToken);
        var searchId = Guid.NewGuid();
        using var post = Request(connection, HttpMethod.Post, SoulseekProtocol.SearchesPath);
        post.Content = JsonContent.Create(new {
            id = searchId,
            searchText = query.Text,
            searchTimeout = SoulseekSearchTimeoutMilliseconds,
            responseLimit = 100,
            fileLimit = 10_000,
            filterResponses = true,
            minimumResponseFileCount = 1
        });
        using var started = await http.SendAsync(post, cancellationToken);
        await EnsureSuccessAsync(started, "search Soulseek", cancellationToken);
        var search = await started.Content.ReadFromJsonAsync<SlskdSearchState>(SoulseekLocator.JsonOptions, cancellationToken);
        await WaitForSearchCompletionAsync(connection, searchId, search?.State, cancellationToken);

        using var get = Request(connection, HttpMethod.Get, $"{SoulseekProtocol.SearchesPath}/{searchId}/responses");
        using var response = await http.SendAsync(get, cancellationToken);
        await EnsureSuccessAsync(response, "read Soulseek search responses", cancellationToken);
        var peers = await response.Content.ReadFromJsonAsync<SlskdSearchResponse[]>(SoulseekLocator.JsonOptions, cancellationToken) ?? [];

        return query.Kind == EntityKind.AudioTrack
            ? TrackReleases(query, searchId, peers)
            : AlbumReleases(query, searchId, peers);
    }

    private async Task WaitForSearchCompletionAsync(
        IndexerConnection connection,
        Guid searchId,
        string? initialState,
        CancellationToken cancellationToken) {
        var state = initialState;
        for (var attempt = 0; attempt < SearchCompletionPollAttempts; attempt++) {
            if (HasState(state, SoulseekProtocol.SearchCompletedState)) return;

            using var request = Request(connection, HttpMethod.Get, $"{SoulseekProtocol.SearchesPath}/{searchId}");
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "read Soulseek search state", cancellationToken);
            state = (await response.Content.ReadFromJsonAsync<SlskdSearchState>(SoulseekLocator.JsonOptions, cancellationToken))?.State;
            if (HasState(state, SoulseekProtocol.SearchCompletedState)) return;
            await Task.Delay(SearchCompletionPollInterval, cancellationToken);
        }

        throw new TimeoutException($"slskd search {searchId:D} did not complete; its last state was {state ?? "unknown"}.");
    }

    public async Task<IndexerConnectionTest> TestAsync(IndexerConnection connection, CancellationToken cancellationToken) {
        try {
            using var request = Request(connection, HttpMethod.Get, SoulseekProtocol.ServerPath);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "connect to slskd", cancellationToken);
            var state = await response.Content.ReadFromJsonAsync<SlskdServerState>(SoulseekLocator.JsonOptions, cancellationToken);
            if (state is not { IsConnected: true, IsLoggedIn: true }) {
                return new IndexerConnectionTest(false, $"slskd is reachable, but its Soulseek session is {state?.State ?? "not connected"}.");
            }
            return new IndexerConnectionTest(true, "Connected to slskd.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new IndexerConnectionTest(false, ex.Message);
        }
    }

    private static IReadOnlyList<IndexerRelease> AlbumReleases(IndexerQuery query, Guid searchId, IEnumerable<SlskdSearchResponse> peers) =>
        peers.OrderByDescending(peer => peer.HasFreeUploadSlot)
            .ThenByDescending(peer => peer.UploadSpeed)
            .ThenBy(peer => peer.QueueLength)
            .SelectMany(peer => peer.Files
                .Where(IsAudio)
                .GroupBy(file => DirectoryOf(file.Filename), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .Select(group => Release(searchId, peer, group.Key, group.ToArray())))
            .ToArray();

    private static IReadOnlyList<IndexerRelease> TrackReleases(IndexerQuery query, Guid searchId, IEnumerable<SlskdSearchResponse> peers) {
        var sought = SignificantWords(query.Text).LastOrDefault();
        return peers.OrderByDescending(peer => peer.HasFreeUploadSlot)
            .ThenByDescending(peer => peer.UploadSpeed)
            .ThenBy(peer => peer.QueueLength)
            .SelectMany(peer => peer.Files
                .Where(file => IsAudio(file)
                    && (sought is null || Normalize(Path.GetFileNameWithoutExtension(file.Filename)).Contains(sought, StringComparison.Ordinal)))
                .Select(file => Release(searchId, peer, file.Filename, [file])))
            .ToArray();
    }

    private static IndexerRelease Release(
        Guid searchId,
        SlskdSearchResponse peer,
        string label,
        IReadOnlyList<SlskdSearchFile> files) {
        var quality = QualityLabel(files);
        var locator = SoulseekLocator.Encode(new SoulseekReleaseLocator(
            searchId,
            peer.Username,
            files.Select(file => new SoulseekFileLocator(file.Filename, file.Size)).ToArray()));
        return new IndexerRelease(
            $"{PathContext(label)} {quality} [Soulseek {peer.Username}]".Trim(),
            files.Sum(file => file.Size),
            null,
            peer.QueueLength > int.MaxValue ? int.MaxValue : (int)peer.QueueLength,
            DownloadProtocol.Soulseek,
            locator,
            null,
            null,
            null,
            null,
            null);
    }

    private static string QualityLabel(IReadOnlyList<SlskdSearchFile> files) {
        var first = files[0];
        var extension = Path.GetExtension(first.Filename).TrimStart('.').ToUpperInvariant();
        var depth = first.BitDepth is { } bitDepth ? $" {bitDepth}bit" : string.Empty;
        var rate = first.SampleRate is { } sampleRate ? $" {sampleRate / 1000d:0.#}kHz" : string.Empty;
        var bitrate = first.BitRate is { } bitRate && !extension.Equals("FLAC", StringComparison.OrdinalIgnoreCase)
            ? $" {bitRate}kbps"
            : string.Empty;
        return $"{extension}{depth}{rate}{bitrate}".Trim();
    }

    private static bool IsAudio(SlskdSearchFile file) => AudioExtensions.Contains(Path.GetExtension(file.Filename));
    private static string DirectoryOf(string path) {
        var index = path.LastIndexOfAny(['\\', '/']);
        return index <= 0 ? string.Empty : path[..index];
    }
    private static string PathContext(string path) => string.Join(
        " / ",
        path.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).TakeLast(4));
    private static string[] SignificantWords(string value) => Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static string Normalize(string value) => NonWordRegex().Replace(value.ToLowerInvariant(), " ").Trim();
    private static bool HasState(string? value, string state) => value?.Split(',', StringSplitOptions.TrimEntries)
        .Contains(state, StringComparer.OrdinalIgnoreCase) == true;

    private static HttpRequestMessage Request(IndexerConnection connection, HttpMethod method, string path) {
        var request = new HttpRequestMessage(method, connection.BaseUrl.TrimEnd('/') + path);
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) request.Headers.TryAddWithoutValidation(SoulseekProtocol.ApiKeyHeader, connection.ApiKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken) {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"slskd could not {action}: {(int)response.StatusCode} {detail}".Trim());
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonWordRegex();

    private sealed record SlskdSearchResponse(
        string Username,
        IReadOnlyList<SlskdSearchFile> Files,
        bool HasFreeUploadSlot = false,
        long QueueLength = 0,
        int UploadSpeed = 0);
    private sealed record SlskdSearchFile(
        string Filename,
        long Size,
        int? BitDepth = null,
        int? BitRate = null,
        int? SampleRate = null);
    private sealed record SlskdSearchState(string? State);
    private sealed record SlskdServerState(string? State, bool IsConnected, bool IsLoggedIn);
}

/// <summary>Queues and monitors slskd batch downloads represented by <see cref="SoulseekLocator"/>.</summary>
public sealed class SlskdDownloadClient(HttpClient http) : IDownloadClient {
    public DownloadClientKind Kind => DownloadClientKind.Slskd;

    public async Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
        var locator = SoulseekLocator.Decode(request.Url);
        var batchId = DeterministicBatchId(request.Url);
        for (var attempt = 0; attempt < 2; attempt++) {
            var destination = $"{SanitizeSegment(request.Category)}/{batchId:D}";
            using var message = Request(connection, HttpMethod.Post, SoulseekProtocol.DownloadBatchesPath);
            message.Content = JsonContent.Create(new {
                id = batchId,
                searchId = locator.SearchId,
                username = locator.Username,
                files = locator.Files.Select(file => new { filename = file.Filename, size = file.Size }),
                options = new { destination, externalId = request.Title }
            });
            using var response = await http.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest
                && await IsLegacyEndpointResponseAsync(response, cancellationToken)) {
                return await AddLegacyAsync(connection, locator, cancellationToken);
            }
            if (response.StatusCode == HttpStatusCode.Conflict) {
                var existing = await GetBatchAsync(connection, batchId.ToString("D"), cancellationToken);
                if (existing is not null && Represents(existing, locator)) return batchId.ToString("D");
                batchId = Guid.NewGuid();
                continue;
            }
            await EnsureSuccessAsync(response, "enqueue the Soulseek batch", cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<EnqueueResponse>(SoulseekLocator.JsonOptions, cancellationToken);
            if (body is null || body.Failures.Count != 0) {
                if (body?.Batch is { } partial) await RemoveBatchAsync(connection, partial, cancellationToken);
                throw new DownloadClientAddUnresolvedException("slskd did not enqueue every file in the selected release.");
            }
            return body.Batch.Id.ToString("D");
        }
        throw new DownloadClientAddUnresolvedException("slskd already contains a different batch with the requested identifier.");
    }

    public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) =>
        throw new NotSupportedException("slskd does not accept torrent files.");

    public async Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        if (SoulseekLocator.TryDecodeLegacy(clientItemId, out var legacy)) {
            var group = await GetLegacyGroupAsync(connection, legacy.Fingerprint, cancellationToken);
            return group is null ? null : ToLegacyStatus(connection, clientItemId, group.Name, group.Transfers);
        }

        var batch = await GetBatchAsync(connection, clientItemId, cancellationToken);
        return batch is null ? null : ToStatus(connection, batch);
    }

    public async Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var users = await GetDownloadsAsync(connection, cancellationToken);
        var batchIds = users.SelectMany(user => user.Directories).SelectMany(directory => directory.Files)
            .Where(file => file.BatchId is not null).Select(file => file.BatchId!.Value).Distinct().ToArray();
        var items = new List<DownloadItemStatus>(batchIds.Length);
        foreach (var id in batchIds) {
            if (await GetBatchAsync(connection, id.ToString("D"), cancellationToken) is { } batch) items.Add(ToStatus(connection, batch));
        }
        foreach (var group in LegacyGroups(users)) {
            var id = SoulseekLocator.EncodeLegacy(new SoulseekLegacyTransferLocator(group.Fingerprint));
            items.Add(ToLegacyStatus(connection, id, group.Name, group.Transfers));
        }
        return items;
    }

    public async Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        if (SoulseekLocator.TryDecodeLegacy(clientItemId, out var legacy)) {
            var group = await GetLegacyGroupAsync(connection, legacy.Fingerprint, cancellationToken);
            return group?.Transfers.Select(file => new DownloadItemFile(file.Filename, file.Size, Progress(file))).ToArray() ?? [];
        }

        var batch = await GetBatchAsync(connection, clientItemId, cancellationToken);
        return batch?.Transfers.Select(file => new DownloadItemFile(file.Filename, file.Size, Progress(file))).ToArray() ?? [];
    }

    public async Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        if (SoulseekLocator.TryDecodeLegacy(clientItemId, out var legacy)) {
            var group = await GetLegacyGroupAsync(connection, legacy.Fingerprint, cancellationToken);
            if (group is null) return null;
            var legacyTotal = group.Transfers.Sum(file => file.Size);
            var legacySpeed = group.Transfers.Sum(file => file.AverageSpeed);
            var legacyRemaining = group.Transfers.Sum(file => Math.Max(0, file.Size - file.BytesTransferred));
            return new DownloadItemProperties(
                legacyTotal,
                legacySpeed,
                0,
                legacySpeed <= 0 ? 0 : (long)(legacyRemaining / legacySpeed),
                0,
                0,
                LegacyContentPath(connection, group.Transfers));
        }

        var batch = await GetBatchAsync(connection, clientItemId, cancellationToken);
        if (batch is null) return null;
        var total = batch.Transfers.Sum(file => file.Size);
        var speed = batch.Transfers.Sum(file => file.AverageSpeed);
        var remaining = batch.Transfers.Sum(file => Math.Max(0, file.Size - file.BytesTransferred));
        return new DownloadItemProperties(total, speed, 0, speed <= 0 ? 0 : (long)(remaining / speed), 0, 0, ContentPath(connection, batch));
    }

    public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) =>
        Task.FromResult(Array.Empty<byte>());

    public async Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
        if (SoulseekLocator.TryDecodeLegacy(clientItemId, out var legacy)) {
            var group = await GetLegacyGroupAsync(connection, legacy.Fingerprint, cancellationToken);
            if (group is not null) {
                foreach (var transfer in group.Transfers) {
                    await RemoveTransferAsync(connection, group.Username, transfer.Id, cancellationToken);
                }
            }
            return;
        }

        var batch = await GetBatchAsync(connection, clientItemId, cancellationToken);
        if (batch is not null) await RemoveBatchAsync(connection, batch, cancellationToken);
    }

    public async Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            using var message = Request(connection, HttpMethod.Get, SoulseekProtocol.ServerPath);
            using var response = await http.SendAsync(message, cancellationToken);
            await EnsureSuccessAsync(response, "connect", cancellationToken);
            var state = await response.Content.ReadFromJsonAsync<SlskdServerState>(SoulseekLocator.JsonOptions, cancellationToken);
            if (state is not { IsConnected: true, IsLoggedIn: true }) {
                return new DownloadClientConnectionTest(false, $"slskd is reachable, but its Soulseek session is {state?.State ?? "not connected"}.");
            }
            return new DownloadClientConnectionTest(true, "Connected to slskd.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new DownloadClientConnectionTest(false, ex.Message);
        }
    }

    private async Task<Batch?> GetBatchAsync(DownloadClientConnection connection, string id, CancellationToken cancellationToken) {
        if (!Guid.TryParse(id, out _)) return null;
        using var message = Request(connection, HttpMethod.Get, $"{SoulseekProtocol.DownloadBatchesPath}/{Uri.EscapeDataString(id)}");
        using var response = await http.SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "read the Soulseek batch", cancellationToken);
        return await response.Content.ReadFromJsonAsync<Batch>(SoulseekLocator.JsonOptions, cancellationToken);
    }

    private async Task RemoveBatchAsync(DownloadClientConnection connection, Batch batch, CancellationToken cancellationToken) {
        foreach (var transfer in batch.Transfers) {
            await RemoveTransferAsync(connection, batch.Username, transfer.Id, cancellationToken);
        }
    }

    private async Task<string> AddLegacyAsync(
        DownloadClientConnection connection,
        SoulseekReleaseLocator locator,
        CancellationToken cancellationToken) {
        using var message = Request(
            connection,
            HttpMethod.Post,
            $"{SoulseekProtocol.DownloadsPath}/{Uri.EscapeDataString(locator.Username)}");
        message.Content = JsonContent.Create(locator.Files.Select(file => new { filename = file.Filename, size = file.Size }));
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, "enqueue the Soulseek files", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<LegacyEnqueueResponse>(SoulseekLocator.JsonOptions, cancellationToken);
        if (body is null || body.Failed.Count != 0 || body.Enqueued.Count != locator.Files.Count) {
            if (body is not null) {
                foreach (var transfer in body.Enqueued) {
                    await RemoveTransferAsync(connection, locator.Username, transfer.Id, cancellationToken);
                }
            }
            throw new DownloadClientAddUnresolvedException("slskd did not enqueue every file in the selected release.");
        }

        return SoulseekLocator.EncodeLegacy(new SoulseekLegacyTransferLocator(
            LegacyFingerprint(locator.Username, body.Enqueued)));
    }

    private static async Task<bool> IsLegacyEndpointResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("IEnumerable", StringComparison.Ordinal)
            && body.Contains("QueueDownloadRequest", StringComparison.Ordinal);
    }

    private async Task<SoulseekLegacyTransferGroup?> GetLegacyGroupAsync(
        DownloadClientConnection connection,
        string fingerprint,
        CancellationToken cancellationToken) {
        var users = await GetDownloadsAsync(connection, cancellationToken);
        return LegacyGroups(users).SingleOrDefault(group => string.Equals(group.Fingerprint, fingerprint, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<UserDownloads>> GetDownloadsAsync(
        DownloadClientConnection connection,
        CancellationToken cancellationToken) {
        using var message = Request(connection, HttpMethod.Get, SoulseekProtocol.DownloadsPath);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, "list Soulseek downloads", cancellationToken);
        return await response.Content.ReadFromJsonAsync<UserDownloads[]>(SoulseekLocator.JsonOptions, cancellationToken) ?? [];
    }

    private async Task RemoveTransferAsync(
        DownloadClientConnection connection,
        string username,
        Guid transferId,
        CancellationToken cancellationToken) {
        using var message = Request(
            connection,
            HttpMethod.Delete,
            $"{SoulseekProtocol.DownloadsPath}/{Uri.EscapeDataString(username)}/{transferId:D}?remove=true");
        using var response = await http.SendAsync(message, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) {
            await EnsureSuccessAsync(response, "remove a Soulseek transfer", cancellationToken);
        }
    }

    private static DownloadItemStatus ToStatus(DownloadClientConnection connection, Batch batch) {
        var complete = batch.Transfers.Count > 0 && batch.Transfers.All(file => Successful(file.State));
        var failed = batch.Transfers.Any(file => Terminal(file.State) && !Successful(file.State));
        var total = batch.Transfers.Sum(file => file.Size);
        var transferred = batch.Transfers.Sum(file => Math.Min(file.Size, file.BytesTransferred));
        var progress = total <= 0 ? 0 : transferred / (double)total;
        var state = failed
            ? SoulseekProtocol.NormalizedFailedState
            : complete
                ? SoulseekProtocol.TransferCompletedState
                : batch.Transfers.FirstOrDefault()?.State ?? SoulseekProtocol.NormalizedQueuedState;
        return new DownloadItemStatus(
            batch.Id.ToString("D"), batch.Options?.ExternalId, complete ? 1 : progress, state, complete,
            ContentPath(connection, batch), complete ? ContentPath(connection, batch) : null,
            IsStalled: false, IsFailed: failed,
            FailureMessage: failed ? batch.Transfers.First(file => Terminal(file.State) && !Successful(file.State)).Exception : null);
    }

    private static DownloadItemStatus ToLegacyStatus(
        DownloadClientConnection connection,
        string clientItemId,
        string? name,
        IReadOnlyList<Transfer> transfers) {
        var complete = transfers.Count > 0 && transfers.All(file => Successful(file.State));
        var failedTransfer = transfers.FirstOrDefault(file => Terminal(file.State) && !Successful(file.State));
        var failed = failedTransfer is not null;
        var total = transfers.Sum(file => file.Size);
        var transferred = transfers.Sum(file => Math.Min(file.Size, file.BytesTransferred));
        var progress = total <= 0 ? 0 : transferred / (double)total;
        var state = failed
            ? SoulseekProtocol.NormalizedFailedState
            : complete
                ? SoulseekProtocol.TransferCompletedState
                : transfers.FirstOrDefault()?.State ?? SoulseekProtocol.NormalizedQueuedState;
        var path = LegacyContentPath(connection, transfers);
        return new DownloadItemStatus(
            clientItemId,
            name,
            complete ? 1 : progress,
            state,
            complete,
            path,
            complete ? path : null,
            IsStalled: false,
            IsFailed: failed,
            FailureMessage: failedTransfer?.Exception);
    }

    private static string? ContentPath(DownloadClientConnection connection, Batch batch) =>
        string.IsNullOrWhiteSpace(connection.DownloadDirectory) || string.IsNullOrWhiteSpace(batch.Options?.Destination)
            ? null
            : Path.Combine(connection.DownloadDirectory, batch.Options.Destination.Replace('/', Path.DirectorySeparatorChar));
    private static string? LegacyContentPath(DownloadClientConnection connection, IReadOnlyList<Transfer> transfers) {
        if (string.IsNullOrWhiteSpace(connection.DownloadDirectory)) return null;
        var files = transfers.Select(file => {
            var segments = file.Filename
                .Split('\\', '/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment is not "." and not "..")
                // Legacy slskd's ToLocalRelativeFilename keeps only the immediate parent and file.
                .TakeLast(2)
                .ToArray();
            return segments.Length == 0
                ? connection.DownloadDirectory
                : Path.Combine([connection.DownloadDirectory, .. segments]);
        }).ToArray();
        if (files.Length == 1) return files[0];
        var directories = files.Select(Path.GetDirectoryName).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToArray();
        return directories.Length == 1 ? directories[0] : connection.DownloadDirectory;
    }

    private static IReadOnlyList<SoulseekLegacyTransferGroup> LegacyGroups(IReadOnlyList<UserDownloads> users) {
        var groups = new Dictionary<string, SoulseekLegacyTransferGroup>(StringComparer.Ordinal);
        foreach (var user in users) {
            foreach (var directory in user.Directories) {
                var transfers = directory.Files.Where(file => file.BatchId is null).ToArray();
                foreach (var transfer in transfers) {
                    Add([transfer], transfer.Filename);
                }
                if (transfers.Length > 1) {
                    Add(transfers, directory.Directory);
                }
            }

            void Add(IReadOnlyList<Transfer> transfers, string name) {
                var fingerprint = LegacyFingerprint(user.Username, transfers);
                groups.TryAdd(fingerprint, new SoulseekLegacyTransferGroup(user.Username, fingerprint, name, transfers));
            }
        }
        return groups.Values.ToArray();
    }

    private static string LegacyFingerprint(string username, IEnumerable<Transfer> transfers) {
        var identity = new StringBuilder(username).Append('\n');
        foreach (var transfer in transfers.OrderBy(file => file.Filename, StringComparer.Ordinal)) {
            identity.Append(transfer.Filename).Append('\0').Append(transfer.Size).Append('\n');
        }
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
    private static double Progress(Transfer file) => file.Size <= 0 ? 0 : Math.Clamp(file.BytesTransferred / (double)file.Size, 0, 1);
    private static bool Successful(string state) =>
        state.Contains(SoulseekProtocol.TransferCompletedState, StringComparison.OrdinalIgnoreCase)
        && state.Contains(SoulseekProtocol.TransferSucceededState, StringComparison.OrdinalIgnoreCase);
    private static bool Terminal(string state) =>
        state.Contains(SoulseekProtocol.TransferCompletedState, StringComparison.OrdinalIgnoreCase);
    private static bool Represents(Batch batch, SoulseekReleaseLocator locator) =>
        batch.Username.Equals(locator.Username, StringComparison.Ordinal)
        && batch.Transfers.Count == locator.Files.Count
        && locator.Files.All(expected => batch.Transfers.Any(actual =>
            actual.Filename.Equals(expected.Filename, StringComparison.Ordinal)
            && actual.Size == expected.Size));
    private static Guid DeterministicBatchId(string locator) {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(locator));
        return new Guid(hash[..16]);
    }
    private static string SanitizeSegment(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
    private static HttpRequestMessage Request(DownloadClientConnection connection, HttpMethod method, string path) {
        var request = new HttpRequestMessage(method, connection.BaseUrl.TrimEnd('/') + path);
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) request.Headers.TryAddWithoutValidation(SoulseekProtocol.ApiKeyHeader, connection.ApiKey);
        return request;
    }
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken) {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"slskd could not {action}: {(int)response.StatusCode} {body}".Trim());
    }

    private sealed record EnqueueResponse(Batch Batch, IReadOnlyList<EnqueueFailure> Failures);
    private sealed record EnqueueFailure(string Filename, string Message);
    private sealed record LegacyEnqueueResponse(IReadOnlyList<Transfer> Enqueued, IReadOnlyList<JsonElement> Failed);
    private sealed record Batch(Guid Id, string Username, BatchOptions? Options, IReadOnlyList<Transfer> Transfers);
    private sealed record BatchOptions(string? Destination, string? ExternalId);
    private sealed record Transfer(Guid Id, string Filename, long Size, long BytesTransferred, string State, double AverageSpeed = 0, string? Exception = null, Guid? BatchId = null);
    private sealed record SoulseekLegacyTransferGroup(string Username, string Fingerprint, string Name, IReadOnlyList<Transfer> Transfers);
    private sealed record UserDownloads(string Username, IReadOnlyList<DirectoryDownloads> Directories);
    private sealed record DirectoryDownloads(string Directory, IReadOnlyList<Transfer> Files);
    private sealed record SlskdServerState(string? State, bool IsConnected, bool IsLoggedIn);
}
