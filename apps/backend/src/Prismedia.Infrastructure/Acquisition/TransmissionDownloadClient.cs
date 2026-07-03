using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Drives Transmission through its JSON-RPC API (Transmission 3+). Adds releases by URL/magnet (or
/// uploaded .torrent, base64-encoded), tracks torrents by hash, scopes Prismedia's downloads with a
/// label matching the configured category, and handles the CSRF session-id handshake (409 → retry
/// with the issued <c>X-Transmission-Session-Id</c>). Authentication is HTTP Basic.
/// </summary>
public sealed class TransmissionDownloadClient(HttpClient http) : IDownloadClient {
    public DownloadClientKind Kind => DownloadClientKind.Transmission;

    private string? _sessionId;

    public async Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
        var arguments = new Dictionary<string, object> {
            [TransmissionProtocol.Filename] = request.Url,
            [TransmissionProtocol.Labels] = new[] { request.Category }
        };
        var result = await RpcAsync(connection, TransmissionProtocol.MethodTorrentAdd, arguments, cancellationToken);
        return AddedHash(result)
            ?? request.InfoHash?.ToLowerInvariant()
            ?? throw new InvalidOperationException("Transmission accepted the release but returned no torrent hash.");
    }

    public async Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) {
        var arguments = new Dictionary<string, object> {
            [TransmissionProtocol.Metainfo] = Convert.ToBase64String(torrent),
            [TransmissionProtocol.Labels] = new[] { connection.Category }
        };
        var result = await RpcAsync(connection, TransmissionProtocol.MethodTorrentAdd, arguments, cancellationToken);
        return AddedHash(result)
            ?? throw new InvalidOperationException("Transmission accepted the uploaded torrent but returned no torrent hash.");
    }

    public async Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var torrents = await TorrentsAsync(connection, clientItemId, cancellationToken);
        return torrents.Count > 0 ? MapStatus(torrents[0]) : null;
    }

    public async Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var torrents = await TorrentsAsync(connection, ids: null, cancellationToken);
        return torrents
            .Where(torrent => HasLabel(torrent, connection.Category))
            .Select(MapStatus)
            .ToArray();
    }

    public async Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var result = await RpcAsync(connection, TransmissionProtocol.MethodTorrentGet, new Dictionary<string, object> {
            [TransmissionProtocol.Ids] = new[] { clientItemId },
            [TransmissionProtocol.Fields] = new[] { TransmissionProtocol.Files }
        }, cancellationToken);
        if (FirstTorrent(result) is not { } torrent
            || !torrent.TryGetProperty(TransmissionProtocol.Files, out var files)
            || files.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var mapped = new List<DownloadItemFile>(files.GetArrayLength());
        foreach (var file in files.EnumerateArray()) {
            var length = Long(file, TransmissionProtocol.FileLength) ?? 0;
            var completed = Long(file, TransmissionProtocol.FileBytesCompleted) ?? 0;
            mapped.Add(new DownloadItemFile(
                Text(file, TransmissionProtocol.Name) ?? "(unknown)",
                length,
                length > 0 ? Math.Clamp((double)completed / length, 0, 1) : 0));
        }

        return mapped;
    }

    public async Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var torrents = await TorrentsAsync(connection, clientItemId, cancellationToken);
        if (torrents.Count == 0) {
            return null;
        }

        var torrent = torrents[0];
        return new DownloadItemProperties(
            Long(torrent, TransmissionProtocol.TotalSize) ?? 0,
            Long(torrent, TransmissionProtocol.RateDownload) ?? 0,
            Long(torrent, TransmissionProtocol.RateUpload) ?? 0,
            Math.Max(Long(torrent, TransmissionProtocol.Eta) ?? 0, 0),
            Int(torrent, TransmissionProtocol.PeersSendingToUs) ?? 0,
            Int(torrent, TransmissionProtocol.PeersGettingFromUs) ?? 0,
            Text(torrent, TransmissionProtocol.DownloadDir),
            Double(torrent, TransmissionProtocol.UploadRatio),
            Long(torrent, TransmissionProtocol.SecondsSeeding));
    }

    public async Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var result = await RpcAsync(connection, TransmissionProtocol.MethodTorrentGet, new Dictionary<string, object> {
            [TransmissionProtocol.Ids] = new[] { clientItemId },
            [TransmissionProtocol.Fields] = new[] { TransmissionProtocol.Pieces, TransmissionProtocol.PieceCount }
        }, cancellationToken);
        if (FirstTorrent(result) is not { } torrent) {
            return [];
        }

        var pieceCount = Int(torrent, TransmissionProtocol.PieceCount) ?? 0;
        var bitfield = Text(torrent, TransmissionProtocol.Pieces);
        if (pieceCount <= 0 || string.IsNullOrEmpty(bitfield)) {
            return [];
        }

        byte[] bits;
        try {
            bits = Convert.FromBase64String(bitfield);
        } catch (FormatException) {
            return [];
        }

        // Transmission reports a have/have-not bitfield; there is no per-piece "downloading" signal.
        var states = new byte[pieceCount];
        for (var piece = 0; piece < pieceCount && piece / 8 < bits.Length; piece++) {
            var have = (bits[piece / 8] & (0x80 >> (piece % 8))) != 0;
            states[piece] = have ? (byte)2 : (byte)0;
        }

        return states;
    }

    public async Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
        await RpcAsync(connection, TransmissionProtocol.MethodTorrentRemove, new Dictionary<string, object> {
            [TransmissionProtocol.Ids] = new[] { clientItemId },
            [TransmissionProtocol.DeleteLocalData] = deleteData
        }, cancellationToken);
    }

    public async Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            var result = await RpcAsync(connection, TransmissionProtocol.MethodSessionGet, new Dictionary<string, object>(), cancellationToken);
            var version = Text(result, TransmissionProtocol.Version);
            return new DownloadClientConnectionTest(true, $"Connected to Transmission {version}.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new DownloadClientConnectionTest(false, ex.Message);
        }
    }

    private async Task<IReadOnlyList<JsonElement>> TorrentsAsync(DownloadClientConnection connection, string? ids, CancellationToken cancellationToken) {
        var arguments = new Dictionary<string, object> {
            [TransmissionProtocol.Fields] = TransmissionProtocol.StatusFields
        };
        if (ids is not null) {
            arguments[TransmissionProtocol.Ids] = new[] { ids };
        }

        var result = await RpcAsync(connection, TransmissionProtocol.MethodTorrentGet, arguments, cancellationToken);
        if (!result.TryGetProperty(TransmissionProtocol.Torrents, out var torrents) || torrents.ValueKind != JsonValueKind.Array) {
            return [];
        }

        return [.. torrents.EnumerateArray()];
    }

    /// <summary>
    /// Projects a torrent-get object. Stalled combines Transmission's own <c>isStalled</c> with a
    /// non-zero error (tracker or local error with no progress possible); completion is percentDone=1.
    /// </summary>
    private static DownloadItemStatus MapStatus(JsonElement torrent) {
        var progress = Double(torrent, TransmissionProtocol.PercentDone) ?? 0;
        var complete = progress >= 1.0d || (Bool(torrent, TransmissionProtocol.IsFinished) ?? false);
        var stalled = (Bool(torrent, TransmissionProtocol.IsStalled) ?? false)
            || (Int(torrent, TransmissionProtocol.ErrorCode) ?? 0) != 0;
        var downloadDir = Text(torrent, TransmissionProtocol.DownloadDir);
        var name = Text(torrent, TransmissionProtocol.Name);
        return new DownloadItemStatus(
            (Text(torrent, TransmissionProtocol.HashString) ?? string.Empty).ToLowerInvariant(),
            name,
            progress,
            Text(torrent, TransmissionProtocol.ErrorString) is { Length: > 0 } error ? error : StatusName(Int(torrent, TransmissionProtocol.Status)),
            complete,
            downloadDir,
            downloadDir is not null && name is not null ? System.IO.Path.Combine(downloadDir, name) : null,
            IsStalled: !complete && stalled);
    }

    /// <summary>Transmission status codes, for human-readable transfer state. prism-vocab: external.</summary>
    private static string StatusName(int? status) => status switch {
        0 => "stopped",
        1 or 2 => "checking",
        3 or 4 => "downloading",
        5 or 6 => "seeding",
        _ => "unknown"
    };

    private static bool HasLabel(JsonElement torrent, string category) {
        if (!torrent.TryGetProperty(TransmissionProtocol.Labels, out var labels) || labels.ValueKind != JsonValueKind.Array) {
            return false;
        }

        foreach (var label in labels.EnumerateArray()) {
            if (label.ValueKind == JsonValueKind.String && string.Equals(label.GetString(), category, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string? AddedHash(JsonElement result) {
        foreach (var key in new[] { TransmissionProtocol.TorrentAdded, TransmissionProtocol.TorrentDuplicate }) {
            if (result.TryGetProperty(key, out var added) && added.ValueKind == JsonValueKind.Object
                && Text(added, TransmissionProtocol.HashString) is { } hash) {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    private static JsonElement? FirstTorrent(JsonElement result) =>
        result.TryGetProperty(TransmissionProtocol.Torrents, out var torrents)
        && torrents.ValueKind == JsonValueKind.Array
        && torrents.GetArrayLength() > 0
            ? torrents[0]
            : null;

    /// <summary>Sends one RPC call, replaying once after the 409 session-id handshake, and returns the arguments object.</summary>
    private async Task<JsonElement> RpcAsync(
        DownloadClientConnection connection,
        string method,
        IReadOnlyDictionary<string, object> arguments,
        CancellationToken cancellationToken) {
        for (var attempt = 0; ; attempt++) {
            using var request = BuildRequest(connection, method, arguments);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict && attempt == 0) {
                // CSRF handshake: adopt the session id Transmission issued and replay once.
                _sessionId = response.Headers.TryGetValues(TransmissionProtocol.SessionIdHeader, out var values) ? values.FirstOrDefault() : null;
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                throw new InvalidOperationException("Transmission rejected the username or password.");
            }

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var result = Text(root, TransmissionProtocol.Result);
            if (!string.Equals(result, TransmissionProtocol.ResultSuccess, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Transmission: {result ?? "request failed"}.");
            }

            return root.TryGetProperty(TransmissionProtocol.Arguments, out var args) && args.ValueKind == JsonValueKind.Object
                ? args.Clone()
                : default;
        }
    }

    private HttpRequestMessage BuildRequest(DownloadClientConnection connection, string method, IReadOnlyDictionary<string, object> arguments) {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> {
            [TransmissionProtocol.Method] = method,
            [TransmissionProtocol.Arguments] = arguments
        });
        var request = new HttpRequestMessage(HttpMethod.Post, RpcUri(connection)) {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (_sessionId is not null) {
            request.Headers.Add(TransmissionProtocol.SessionIdHeader, _sessionId);
        }

        if (!string.IsNullOrWhiteSpace(connection.Username)) {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Username}:{connection.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return request;
    }

    /// <summary>The RPC endpoint: the base URL as-is when it already points at an rpc path, else + /transmission/rpc.</summary>
    private static Uri RpcUri(DownloadClientConnection connection) {
        var trimmed = connection.BaseUrl.TrimEnd('/');
        return trimmed.EndsWith("/rpc", StringComparison.OrdinalIgnoreCase)
            ? new Uri(trimmed)
            : new Uri($"{trimmed}/{TransmissionProtocol.RpcPath}");
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Double(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static int? Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? Long(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static bool? Bool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
