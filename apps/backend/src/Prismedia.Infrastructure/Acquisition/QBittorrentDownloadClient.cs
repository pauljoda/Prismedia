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
        if (string.IsNullOrWhiteSpace(request.InfoHash)) {
            // Without an info hash we cannot reliably correlate the added torrent back to this acquisition.
            throw new InvalidOperationException("The selected release has no info hash, so qBittorrent cannot track it.");
        }

        var session = await LoginAsync(connection, cancellationToken);

        // Ensure the category exists; qBittorrent returns a conflict when it already does, which is fine.
        await PostAsync(connection, session, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken, allowConflict: true);

        var addResponse = await PostAsync(connection, session, QBittorrentProtocol.AddEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.UrlsField] = request.Url,
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken);
        addResponse.EnsureSuccessStatusCode();

        return request.InfoHash.ToLowerInvariant();
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

        var item = document.RootElement[0];
        var progress = Double(item, QBittorrentProtocol.Progress) ?? 0;
        var state = Text(item, QBittorrentProtocol.State);
        return new DownloadItemStatus(
            Text(item, QBittorrentProtocol.Hash) ?? clientItemId,
            Text(item, QBittorrentProtocol.Name),
            progress,
            state,
            progress >= 1.0d,
            Text(item, QBittorrentProtocol.SavePathJson),
            Text(item, QBittorrentProtocol.ContentPathJson));
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

    private async Task<HttpResponseMessage> PostAsync(
        DownloadClientConnection connection,
        string? session,
        string path,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken,
        bool allowConflict = false) {
        using var request = BuildRequest(connection, session, HttpMethod.Post, path, new FormUrlEncodedContent(form));
        var response = await http.SendAsync(request, cancellationToken);
        if (allowConflict && response.StatusCode == HttpStatusCode.Conflict) {
            return response;
        }

        return response;
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
