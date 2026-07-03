using System.Globalization;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Drives SABnzbd through its JSON API. Adds NZB releases by URL (or uploaded file), tracks them by
/// SABnzbd's <c>nzo_id</c> across the live queue and the history, and reads the completed payload's
/// on-disk path from the history <c>storage</c> field. Authenticates with the API key (preferred) or
/// the ma_username/ma_password pair. Torrent-only telemetry (piece states, seeds/peers) is reported
/// as empty — usenet transfers have no swarm.
/// </summary>
public sealed class SabnzbdDownloadClient(HttpClient http) : IDownloadClient {
    public DownloadClientKind Kind => DownloadClientKind.Sabnzbd;

    public async Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
        var root = await GetAsync(connection, SabnzbdProtocol.ModeAddUrl, new Dictionary<string, string> {
            [SabnzbdProtocol.NameParam] = request.Url,
            [SabnzbdProtocol.CategoryParam] = request.Category
        }, cancellationToken);

        return FirstNzoId(root)
            ?? throw new InvalidOperationException("SABnzbd accepted the release but returned no download id.");
    }

    public async Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] payload, CancellationToken cancellationToken) {
        // The manual-upload fallback: for a usenet client the uploaded payload is an .nzb, not a .torrent.
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-nzb");
        form.Add(fileContent, SabnzbdProtocol.NzbFileField, string.IsNullOrWhiteSpace(fileName) ? "upload.nzb" : fileName);

        var uri = BuildUri(connection, SabnzbdProtocol.ModeAddFile, new Dictionary<string, string> {
            [SabnzbdProtocol.CategoryParam] = connection.Category
        });
        using var response = await http.PostAsync(uri, form, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        EnsureApiSuccess(document.RootElement);

        return FirstNzoId(document.RootElement)
            ?? throw new InvalidOperationException("SABnzbd accepted the uploaded NZB but returned no download id.");
    }

    public async Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        foreach (var slot in await QueueSlotsAsync(connection, nzoId: clientItemId, cancellationToken)) {
            if (string.Equals(Text(slot, SabnzbdProtocol.NzoId), clientItemId, StringComparison.OrdinalIgnoreCase)) {
                return MapQueueSlot(slot);
            }
        }

        foreach (var slot in await HistorySlotsAsync(connection, nzoId: clientItemId, cancellationToken)) {
            if (string.Equals(Text(slot, SabnzbdProtocol.NzoId), clientItemId, StringComparison.OrdinalIgnoreCase)) {
                return MapHistorySlot(slot);
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var items = new List<DownloadItemStatus>();
        foreach (var slot in await QueueSlotsAsync(connection, nzoId: null, cancellationToken)) {
            if (InCategory(slot, SabnzbdProtocol.QueueCategory, connection.Category)) {
                items.Add(MapQueueSlot(slot));
            }
        }

        foreach (var slot in await HistorySlotsAsync(connection, nzoId: null, cancellationToken)) {
            if (InCategory(slot, SabnzbdProtocol.HistoryCategory, connection.Category)) {
                items.Add(MapHistorySlot(slot));
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        // Files are only listable while the item is in the queue; a completed item's files are read
        // from disk by the importer, so an empty list here is fine.
        try {
            var root = await GetAsync(connection, SabnzbdProtocol.ModeGetFiles, new Dictionary<string, string> {
                [SabnzbdProtocol.ValueParam] = clientItemId
            }, cancellationToken);
            if (!root.TryGetProperty(SabnzbdProtocol.Files, out var files) || files.ValueKind != JsonValueKind.Array) {
                return [];
            }

            var result = new List<DownloadItemFile>(files.GetArrayLength());
            foreach (var file in files.EnumerateArray()) {
                var total = Number(file, SabnzbdProtocol.Mb) ?? 0;
                var left = Number(file, SabnzbdProtocol.MbLeft) ?? 0;
                result.Add(new DownloadItemFile(
                    Text(file, SabnzbdProtocol.Filename) ?? "(unknown)",
                    (long)(total * 1024 * 1024),
                    total > 0 ? Math.Clamp((total - left) / total, 0, 1) : 0));
            }

            return result;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            return [];
        }
    }

    public async Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var (slots, kbPerSec) = await QueueAsync(connection, nzoId: clientItemId, cancellationToken);
        foreach (var slot in slots) {
            if (!string.Equals(Text(slot, SabnzbdProtocol.NzoId), clientItemId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var totalMb = Number(slot, SabnzbdProtocol.Mb) ?? 0;
            return new DownloadItemProperties(
                (long)(totalMb * 1024 * 1024),
                kbPerSec * 1024,
                UploadSpeedBytesPerSecond: 0,
                ParseTimeLeftSeconds(Text(slot, SabnzbdProtocol.TimeLeft)),
                Seeds: 0,
                Peers: 0,
                SavePath: null);
        }

        foreach (var slot in await HistorySlotsAsync(connection, nzoId: clientItemId, cancellationToken)) {
            if (!string.Equals(Text(slot, SabnzbdProtocol.NzoId), clientItemId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return new DownloadItemProperties(
                Long(slot, SabnzbdProtocol.Bytes) ?? 0,
                DownloadSpeedBytesPerSecond: 0,
                UploadSpeedBytesPerSecond: 0,
                EtaSeconds: 0,
                Seeds: 0,
                Peers: 0,
                Text(slot, SabnzbdProtocol.Storage));
        }

        return null;
    }

    public Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) =>
        Task.FromResult(Array.Empty<byte>());

    public async Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
        // The item lives in exactly one of the queue or the history; delete against both so the caller
        // doesn't need to know which. A delete of an unknown id is a no-op on the other side.
        foreach (var mode in new[] { SabnzbdProtocol.ModeQueue, SabnzbdProtocol.ModeHistory }) {
            await GetAsync(connection, mode, new Dictionary<string, string> {
                [SabnzbdProtocol.NameParam] = SabnzbdProtocol.OperationDelete,
                [SabnzbdProtocol.ValueParam] = clientItemId,
                [SabnzbdProtocol.DeleteFilesParam] = deleteData ? "1" : "0"
            }, cancellationToken, allowApiError: true);
        }
    }

    public async Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            // mode=version answers without auth; the queue read validates the API key / credentials.
            var version = Text(await GetAsync(connection, SabnzbdProtocol.ModeVersion, new Dictionary<string, string>(), cancellationToken), SabnzbdProtocol.Version);
            await QueueSlotsAsync(connection, nzoId: null, cancellationToken);

            // The pre-save connection test carries no category; only a configured one is worth checking.
            var categories = string.IsNullOrWhiteSpace(connection.Category) ? [] : await CategoriesAsync(connection, cancellationToken);
            if (categories.Count > 0 && !categories.Contains(connection.Category, StringComparer.OrdinalIgnoreCase)) {
                return new DownloadClientConnectionTest(true,
                    $"Connected to SABnzbd {version}, but the category \"{connection.Category}\" does not exist there — create it in SABnzbd so Prismedia downloads stay isolated and land in a predictable folder.");
            }

            return new DownloadClientConnectionTest(true, $"Connected to SABnzbd {version}.");
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new DownloadClientConnectionTest(false, ex.Message);
        }
    }

    private async Task<IReadOnlyList<string>> CategoriesAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            var root = await GetAsync(connection, SabnzbdProtocol.ModeGetCategories, new Dictionary<string, string>(), cancellationToken);
            if (!root.TryGetProperty(SabnzbdProtocol.Categories, out var categories) || categories.ValueKind != JsonValueKind.Array) {
                return [];
            }

            return [.. categories.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!)];
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception) {
            return [];
        }
    }

    /// <summary>Projects a live queue slot: progress from mb/mbleft, never complete (completion is a history state).</summary>
    private static DownloadItemStatus MapQueueSlot(JsonElement slot) {
        var total = Number(slot, SabnzbdProtocol.Mb) ?? 0;
        var left = Number(slot, SabnzbdProtocol.MbLeft) ?? 0;
        return new DownloadItemStatus(
            Text(slot, SabnzbdProtocol.NzoId) ?? string.Empty,
            Text(slot, SabnzbdProtocol.Filename),
            total > 0 ? Math.Clamp((total - left) / total, 0, 1) : 0,
            Text(slot, SabnzbdProtocol.SlotStatus),
            IsComplete: false,
            SavePath: null,
            ContentPath: null);
    }

    /// <summary>
    /// Projects a history slot. Only <c>Completed</c> is complete — post-processing states (Extracting,
    /// Verifying, Repairing, …) stay in-flight so the import never fires on a half-unpacked payload.
    /// <c>Failed</c> maps to a definitive failure with SABnzbd's explanation.
    /// </summary>
    private static DownloadItemStatus MapHistorySlot(JsonElement slot) {
        var status = Text(slot, SabnzbdProtocol.SlotStatus);
        var completed = SabnzbdProtocol.IsCompletedStatus(status);
        var failed = SabnzbdProtocol.IsFailedStatus(status);
        var storage = Text(slot, SabnzbdProtocol.Storage);
        return new DownloadItemStatus(
            Text(slot, SabnzbdProtocol.NzoId) ?? string.Empty,
            Text(slot, SabnzbdProtocol.HistoryName),
            completed ? 1 : 0,
            status,
            completed,
            SavePath: storage,
            ContentPath: storage,
            IsFailed: failed,
            FailureMessage: failed ? Text(slot, SabnzbdProtocol.FailMessage) : null);
    }

    private async Task<IReadOnlyList<JsonElement>> QueueSlotsAsync(DownloadClientConnection connection, string? nzoId, CancellationToken cancellationToken) =>
        (await QueueAsync(connection, nzoId, cancellationToken)).Slots;

    private async Task<(IReadOnlyList<JsonElement> Slots, double KbPerSec)> QueueAsync(DownloadClientConnection connection, string? nzoId, CancellationToken cancellationToken) {
        var parameters = new Dictionary<string, string>();
        if (nzoId is not null) {
            parameters[SabnzbdProtocol.NzoIdsParam] = nzoId;
        }

        var root = await GetAsync(connection, SabnzbdProtocol.ModeQueue, parameters, cancellationToken);
        if (!root.TryGetProperty(SabnzbdProtocol.Queue, out var queue) || queue.ValueKind != JsonValueKind.Object) {
            return ([], 0);
        }

        return (SlotsOf(queue), Number(queue, SabnzbdProtocol.KbPerSec) ?? 0);
    }

    private async Task<IReadOnlyList<JsonElement>> HistorySlotsAsync(DownloadClientConnection connection, string? nzoId, CancellationToken cancellationToken) {
        var parameters = new Dictionary<string, string>();
        if (nzoId is not null) {
            parameters[SabnzbdProtocol.NzoIdsParam] = nzoId;
        } else {
            // The history is unbounded; recent entries are all tracking needs (imports fire within a poll or two).
            parameters[SabnzbdProtocol.LimitParam] = "500";
        }

        var root = await GetAsync(connection, SabnzbdProtocol.ModeHistory, parameters, cancellationToken);
        return root.TryGetProperty(SabnzbdProtocol.History, out var history) && history.ValueKind == JsonValueKind.Object
            ? SlotsOf(history)
            : [];
    }

    private static IReadOnlyList<JsonElement> SlotsOf(JsonElement container) =>
        container.TryGetProperty(SabnzbdProtocol.Slots, out var slots) && slots.ValueKind == JsonValueKind.Array
            ? [.. slots.EnumerateArray()]
            : [];

    private static bool InCategory(JsonElement slot, string categoryField, string category) =>
        string.Equals(Text(slot, categoryField), category, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNzoId(JsonElement root) =>
        root.TryGetProperty(SabnzbdProtocol.NzoIds, out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0
            ? ids[0].GetString()
            : null;

    /// <summary>Calls a SABnzbd mode and returns the parsed root, translating the API's in-band error envelope into an exception.</summary>
    private async Task<JsonElement> GetAsync(
        DownloadClientConnection connection,
        string mode,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken,
        bool allowApiError = false) {
        using var response = await http.GetAsync(BuildUri(connection, mode, parameters), cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) {
            throw new InvalidOperationException("SABnzbd rejected the API key or credentials.");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try {
            document = JsonDocument.Parse(body);
        } catch (JsonException) {
            // Some SABnzbd errors ignore output=json and come back as plain text (e.g. "API Key Incorrect").
            var text = body.Trim();
            throw new InvalidOperationException($"SABnzbd: {(text.Length is > 0 and <= 200 ? text : "unexpected non-JSON response")}");
        }

        if (!allowApiError) {
            EnsureApiSuccess(document.RootElement);
        }

        return document.RootElement;
    }

    /// <summary>SABnzbd reports failures in-band as <c>{"status": false, "error": "…"}</c> with HTTP 200.</summary>
    private static void EnsureApiSuccess(JsonElement root) {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(SabnzbdProtocol.Status, out var status)
            && status.ValueKind == JsonValueKind.False) {
            var error = Text(root, SabnzbdProtocol.Error) ?? "SABnzbd rejected the request.";
            throw new InvalidOperationException($"SABnzbd: {error}");
        }
    }

    private Uri BuildUri(DownloadClientConnection connection, string mode, IReadOnlyDictionary<string, string> parameters) {
        var query = new List<string> {
            $"{SabnzbdProtocol.ModeParam}={Uri.EscapeDataString(mode)}",
            $"{SabnzbdProtocol.OutputParam}={SabnzbdProtocol.OutputJson}"
        };
        if (!string.IsNullOrWhiteSpace(connection.ApiKey)) {
            query.Add($"{SabnzbdProtocol.ApiKeyParam}={Uri.EscapeDataString(connection.ApiKey)}");
        } else if (!string.IsNullOrWhiteSpace(connection.Username)) {
            query.Add($"{SabnzbdProtocol.UsernameParam}={Uri.EscapeDataString(connection.Username)}");
            query.Add($"{SabnzbdProtocol.PasswordParam}={Uri.EscapeDataString(connection.Password ?? string.Empty)}");
        }

        foreach (var (key, value) in parameters) {
            query.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        var baseUri = new Uri(connection.BaseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, $"{SabnzbdProtocol.ApiEndpoint}?{string.Join('&', query)}");
    }

    /// <summary>SABnzbd's <c>timeleft</c> is a clock string (<c>H:MM:SS</c>, days as <c>D:HH:MM:SS</c>).</summary>
    private static long ParseTimeLeftSeconds(string? timeLeft) {
        if (string.IsNullOrWhiteSpace(timeLeft)) {
            return 0;
        }

        var parts = timeLeft.Split(':');
        if (parts.Length is < 1 or > 4) {
            return 0;
        }

        // Right-to-left the units are seconds, minutes, hours, days.
        long[] unitSeconds = [1, 60, 3600, 86400];
        long total = 0;
        for (var index = 0; index < parts.Length; index++) {
            if (!long.TryParse(parts[parts.Length - 1 - index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
                return 0;
            }

            total += value * unitSeconds[index];
        }

        return total;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>SABnzbd serializes most numbers as strings ("123.45"); accept both forms.</summary>
    private static double? Number(JsonElement element, string property) {
        if (!element.TryGetProperty(property, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static long? Long(JsonElement element, string property) {
        if (!element.TryGetProperty(property, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
