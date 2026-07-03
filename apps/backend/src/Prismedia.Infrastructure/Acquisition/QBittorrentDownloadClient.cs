using System.Net;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Drives qBittorrent through its WebUI API (v2). Logs in for a session cookie, ensures the target
/// category exists, adds releases by URL (Prowlarr's self-authenticating proxy URL), and tracks the
/// resulting torrent by the release info hash. Completion and the on-disk content path are read from
/// the torrent info feed.
/// </summary>
public sealed class QBittorrentDownloadClient(HttpClient http) : IDownloadClient {
    public DownloadClientKind Kind => DownloadClientKind.QBittorrent;

    public async Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);

        // Ensure the category exists; qBittorrent returns a conflict when it already does, which is fine.
        await PostAsync(connection, session, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken, allowConflict: true);

        // Snapshot the category before adding so the new torrent can be identified even when Prowlarr's
        // proxied link carries no info hash (qBittorrent's add endpoint does not return the hash).
        var before = await CategoryHashesAsync(connection, session, request.Category, cancellationToken);

        await PostAsync(connection, session, QBittorrentProtocol.AddEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.UrlsField] = request.Url,
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken);

        // A known info hash is the most reliable id; otherwise discover the newly added torrent by diff.
        if (!string.IsNullOrWhiteSpace(request.InfoHash)) {
            return request.InfoHash.ToLowerInvariant();
        }

        for (var attempt = 0; attempt < 15; attempt++) {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var after = await CategoryHashesAsync(connection, session, request.Category, cancellationToken);
            after.ExceptWith(before);
            if (after.Count > 0) {
                return after.First();
            }
        }

        throw new InvalidOperationException("qBittorrent accepted the release but the torrent did not appear in the category.");
    }

    private async Task<HashSet<string>> CategoryHashesAsync(DownloadClientConnection connection, string? session, string category, CancellationToken cancellationToken) {
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.CategoryField}={Uri.EscapeDataString(category)}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind == JsonValueKind.Array) {
            foreach (var item in document.RootElement.EnumerateArray()) {
                if (Text(item, QBittorrentProtocol.Hash) is { } hash) {
                    hashes.Add(hash);
                }
            }
        }

        return hashes;
    }

    public async Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.HashesField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) {
            return null;
        }

        return MapStatus(document.RootElement[0], clientItemId);
    }

    public async Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.CategoryField}={Uri.EscapeDataString(connection.Category)}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var items = new List<DownloadItemStatus>(document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray()) {
            items.Add(MapStatus(item, Text(item, QBittorrentProtocol.Hash) ?? string.Empty));
        }

        return items;
    }

    /// <summary>Projects a qBittorrent torrent JSON object into a <see cref="DownloadItemStatus"/>.</summary>
    private static DownloadItemStatus MapStatus(JsonElement item, string fallbackId) {
        var progress = Double(item, QBittorrentProtocol.Progress) ?? 0;
        var state = Text(item, QBittorrentProtocol.State);
        var complete = progress >= 1.0d;
        return new DownloadItemStatus(
            Text(item, QBittorrentProtocol.Hash) ?? fallbackId,
            Text(item, QBittorrentProtocol.Name),
            progress,
            state,
            complete,
            Text(item, QBittorrentProtocol.SavePathJson),
            Text(item, QBittorrentProtocol.ContentPathJson),
            // A completed transfer is never stalled, even if the client briefly reports an awkward state.
            IsStalled: !complete && QBittorrentProtocol.IsStalledState(state));
    }

    public async Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        await PostAsync(connection, session, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.CategoryField] = connection.Category
        }, cancellationToken, allowConflict: true);

        var before = await CategoryHashesAsync(connection, session, connection.Category, cancellationToken);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(torrent);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
        form.Add(fileContent, QBittorrentProtocol.TorrentsField, string.IsNullOrWhiteSpace(fileName) ? "upload.torrent" : fileName);
        form.Add(new StringContent(connection.Category), QBittorrentProtocol.CategoryField);

        using var request = BuildRequest(connection, session, HttpMethod.Post, QBittorrentProtocol.AddEndpoint, form);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < 15; attempt++) {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var after = await CategoryHashesAsync(connection, session, connection.Category, cancellationToken);
            after.ExceptWith(before);
            if (after.Count > 0) {
                return after.First();
            }
        }

        throw new InvalidOperationException("qBittorrent accepted the uploaded torrent but it did not appear in the category.");
    }

    public async Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var path = $"{QBittorrentProtocol.FilesEndpoint}?{QBittorrentProtocol.HashesField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        // A freshly added magnet has no file list until metadata resolves; qBittorrent answers 400 in that
        // window. Treat it as "no files yet" (like the properties/piece-state reads) instead of throwing.
        if (!response.IsSuccessStatusCode) {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var files = new List<DownloadItemFile>(document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray()) {
            files.Add(new DownloadItemFile(
                Text(item, QBittorrentProtocol.Name) ?? "(unknown)",
                Long(item, QBittorrentProtocol.Size) ?? 0,
                Double(item, QBittorrentProtocol.Progress) ?? 0));
        }

        return files;
    }

    public async Task<DownloadItemProperties?> GetPropertiesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var path = $"{QBittorrentProtocol.PropertiesEndpoint}?{QBittorrentProtocol.HashesField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var item = document.RootElement;
        if (item.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new DownloadItemProperties(
            Long(item, QBittorrentProtocol.TotalSize) ?? 0,
            Long(item, QBittorrentProtocol.DlSpeed) ?? 0,
            Long(item, QBittorrentProtocol.UpSpeed) ?? 0,
            Long(item, QBittorrentProtocol.Eta) ?? 0,
            Int(item, QBittorrentProtocol.Seeds) ?? 0,
            Int(item, QBittorrentProtocol.Peers) ?? 0,
            Text(item, QBittorrentProtocol.SavePathJson),
            Double(item, QBittorrentProtocol.ShareRatio),
            Long(item, QBittorrentProtocol.SeedingTime));
    }

    public async Task<byte[]> GetPieceStatesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var path = $"{QBittorrentProtocol.PieceStatesEndpoint}?{QBittorrentProtocol.HashesField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var request = BuildRequest(connection, session, HttpMethod.Get, path, content: null);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        var states = new byte[document.RootElement.GetArrayLength()];
        var index = 0;
        foreach (var element in document.RootElement.EnumerateArray()) {
            states[index++] = element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
                ? (byte)Math.Clamp(value, 0, 2)
                : (byte)0;
        }

        return states;
    }

    public async Task RemoveAsync(DownloadClientConnection connection, string clientItemId, bool deleteData, CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        await PostAsync(connection, session, QBittorrentProtocol.DeleteEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.HashesField] = clientItemId.ToLowerInvariant(),
            [QBittorrentProtocol.DeleteFilesField] = deleteData ? "true" : "false"
        }, cancellationToken);
    }

    public async Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            var session = await LoginAsync(connection, cancellationToken);
            using var request = BuildRequest(connection, session, HttpMethod.Get, QBittorrentProtocol.VersionEndpoint, content: null);
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? new DownloadClientConnectionTest(true, "Connected to qBittorrent.")
                : new DownloadClientConnectionTest(false, $"qBittorrent returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        } catch (QBittorrentAuthException ex) {
            return new DownloadClientConnectionTest(false, ex.Message);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new DownloadClientConnectionTest(false, ex.Message);
        }
    }

    /// <summary>Authenticates and returns the session cookie, or null when the client requires no credentials.</summary>
    private async Task<string?> LoginAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(connection.Username) && string.IsNullOrWhiteSpace(connection.Password)) {
            return null;
        }

        using var request = BuildRequest(connection, session: null, HttpMethod.Post, QBittorrentProtocol.LoginEndpoint,
            content: new FormUrlEncodedContent(new Dictionary<string, string> {
                [QBittorrentProtocol.UsernameField] = connection.Username ?? string.Empty,
                [QBittorrentProtocol.PasswordField] = connection.Password ?? string.Empty
            }));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden) {
            throw new QBittorrentAuthException("qBittorrent temporarily banned this client after failed logins.");
        }

        var session = ExtractSessionCookie(response);
        if (session is null) {
            throw new QBittorrentAuthException("qBittorrent rejected the username or password.");
        }

        return session;
    }

    /// <summary>
    /// Posts a form to a qBittorrent endpoint, disposing the response. Throws on a non-success status unless
    /// <paramref name="allowConflict"/> is set, in which case a 409 (e.g. category already exists) is tolerated.
    /// </summary>
    private async Task PostAsync(
        DownloadClientConnection connection,
        string? session,
        string path,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken,
        bool allowConflict = false) {
        using var request = BuildRequest(connection, session, HttpMethod.Post, path, new FormUrlEncodedContent(form));
        using var response = await http.SendAsync(request, cancellationToken);
        if (allowConflict && response.StatusCode == HttpStatusCode.Conflict) {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Returns the session cookie as a full <c>name=value</c> pair. qBittorrent's cookie name varies by
    /// version (legacy <c>SID</c>, modern <c>QBT_SID_&lt;port&gt;</c>), so it is matched by its <c>SID</c>
    /// marker and resent under whatever name the server issued.
    /// </summary>
    private static string? ExtractSessionCookie(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) {
            return null;
        }

        foreach (var cookie in cookies) {
            var pair = cookie.Split(';', 2)[0].Trim();
            var separator = pair.IndexOf('=');
            if (separator <= 0) {
                continue;
            }

            var name = pair[..separator];
            if (name.Contains(QBittorrentProtocol.SessionCookieMarker, StringComparison.OrdinalIgnoreCase)) {
                return pair;
            }
        }

        return null;
    }

    private HttpRequestMessage BuildRequest(DownloadClientConnection connection, string? session, HttpMethod method, string path, HttpContent? content) {
        var baseUri = new Uri(connection.BaseUrl.TrimEnd('/') + "/");
        var request = new HttpRequestMessage(method, new Uri(baseUri, path)) { Content = content };
        // qBittorrent enforces a CSRF check that requires a same-origin Referer header.
        request.Headers.Add(QBittorrentProtocol.RefererHeader, connection.BaseUrl.TrimEnd('/'));
        if (!string.IsNullOrEmpty(session)) {
            request.Headers.Add("Cookie", session);
        }

        return request;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Double(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
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

/// <summary>Raised when qBittorrent authentication fails so connection tests can report a clear reason.</summary>
public sealed class QBittorrentAuthException(string message) : Exception(message);

/// <summary>Resolves the configured <see cref="IDownloadClient"/> for a client family.</summary>
public sealed class DownloadClientFactory(IEnumerable<IDownloadClient> clients) : IDownloadClientFactory {
    private readonly Dictionary<DownloadClientKind, IDownloadClient> _clients = clients.ToDictionary(client => client.Kind);

    public IDownloadClient Get(DownloadClientKind kind) =>
        _clients.TryGetValue(kind, out var client)
            ? client
            : throw new NotSupportedException($"No download client is registered for '{kind}'.");
}
