using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>
/// Drives qBittorrent through its WebUI API (v2). Reuses each authenticated cookie across scoped
/// adapter instances, coalesces concurrent logins, and backs off rejected credentials so routine
/// polling cannot trip qBittorrent's IP ban. The adapter also ensures the target category exists,
/// adds releases by URL, and tracks the resulting torrent by release info hash.
/// </summary>
public sealed class QBittorrentDownloadClient(HttpClient http) : IDownloadClient {
    private static readonly ConcurrentDictionary<(Guid ClientId, string Category), SemaphoreSlim> AddGates = new();
    private static readonly ConcurrentDictionary<SessionCacheKey, SessionCacheEntry> Sessions = new();

    public DownloadClientKind Kind => DownloadClientKind.QBittorrent;

    /// <summary>Delay between add-correlation polls. Internal so tests don't wait out the real cadence.</summary>
    internal TimeSpan AddPollDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Suppresses repeat attempts with the same rejected credentials.</summary>
    internal TimeSpan RejectedLoginBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Matches qBittorrent's default WebUI ban duration after it explicitly reports a ban.</summary>
    internal TimeSpan BannedLoginBackoff { get; init; } = TimeSpan.FromHours(1);

    public Task<string> AddAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) =>
        SerializeAddAsync(connection, () => AddUrlCoreAsync(connection, request, cancellationToken), cancellationToken);

    private async Task<string> AddUrlCoreAsync(DownloadClientConnection connection, DownloadAddRequest request, CancellationToken cancellationToken) {
        // Ensure the category exists; qBittorrent returns a conflict when it already does, which is fine.
        await PostAsync(connection, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken, allowConflict: true);

        // Snapshot the category before adding so the new torrent can be identified even when Prowlarr's
        // proxied link carries no info hash (qBittorrent's add endpoint does not return the hash).
        var before = await CategoryHashesAsync(connection, request.Category, cancellationToken);

        await PostAsync(connection, QBittorrentProtocol.AddEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.UrlsField] = request.Url,
            [QBittorrentProtocol.CategoryField] = request.Category
        }, cancellationToken);

        // A known info hash is the most reliable id; otherwise discover the newly added torrent by diff.
        if (!string.IsNullOrWhiteSpace(request.InfoHash)) {
            return request.InfoHash.ToLowerInvariant();
        }

        for (var attempt = 0; attempt < 15; attempt++) {
            await Task.Delay(AddPollDelay, cancellationToken);
            var after = await CategoryHashesAsync(connection, request.Category, cancellationToken);
            after.ExceptWith(before);
            if (after.Count > 0) {
                return after.First();
            }
        }

        // No new hash appeared even though the add was accepted. The overwhelmingly common cause is a
        // DUPLICATE: qBittorrent answers 200 for a torrent it already has but creates nothing. Correlate
        // against the category's existing torrents by normalized name — a confident match means the
        // release is already in the client and that torrent serves this add too. This is never a category
        // problem (the category was just listed successfully), so don't report it as one.
        if (await FindByNormalizedNameAsync(connection, request.Category, request.Title, cancellationToken) is { } duplicate) {
            return duplicate;
        }

        throw new DownloadClientAddUnresolvedException(
            "qBittorrent accepted the add but created no new torrent — the release is likely already present in the client (duplicate add).");
    }

    private async Task<HashSet<string>> CategoryHashesAsync(DownloadClientConnection connection, string category, CancellationToken cancellationToken) =>
        (await CategoryTorrentsAsync(connection, category, cancellationToken))
        .Select(torrent => torrent.Hash)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<(string Hash, string? Name)>> CategoryTorrentsAsync(
        DownloadClientConnection connection, string category, CancellationToken cancellationToken) {
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.CategoryField}={Uri.EscapeDataString(category)}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var torrents = new List<(string, string?)>();
        if (document.RootElement.ValueKind == JsonValueKind.Array) {
            foreach (var item in document.RootElement.EnumerateArray()) {
                if (Text(item, QBittorrentProtocol.Hash) is { } hash) {
                    torrents.Add((hash, Text(item, QBittorrentProtocol.Name)));
                }
            }
        }

        return torrents;
    }

    /// <summary>
    /// The hash of the ONE category torrent whose normalized name matches <paramref name="title"/>, or
    /// null when there is no title to match, no match, or more than one (ambiguity never guesses).
    /// </summary>
    private async Task<string?> FindByNormalizedNameAsync(
        DownloadClientConnection connection, string category, string? title, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(title)) {
            return null;
        }

        var normalizedTitle = NormalizeTorrentName(title);
        if (normalizedTitle.Length == 0) {
            return null;
        }

        var matches = (await CategoryTorrentsAsync(connection, category, cancellationToken))
            .Where(torrent => torrent.Name is not null && NormalizeTorrentName(torrent.Name) == normalizedTitle)
            .Select(torrent => torrent.Hash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Case-folds and strips everything but letters and digits, so punctuation/spacing variants of the same release name compare equal.</summary>
    private static string NormalizeTorrentName(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public async Task<DownloadItemStatus?> GetItemAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.HashesField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0) {
            return null;
        }

        return MapStatus(document.RootElement[0], clientItemId);
    }

    public async Task<IReadOnlyList<DownloadItemStatus>> ListItemsAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        var path = $"{QBittorrentProtocol.InfoEndpoint}?{QBittorrentProtocol.CategoryField}={Uri.EscapeDataString(connection.Category)}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
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
            IsStalled: !complete && QBittorrentProtocol.IsStalledState(state),
            Properties: new DownloadItemProperties(
                Long(item, QBittorrentProtocol.TotalSize) ?? 0,
                Long(item, QBittorrentProtocol.InfoDownloadSpeed) ?? 0,
                Long(item, QBittorrentProtocol.InfoUploadSpeed) ?? 0,
                Long(item, QBittorrentProtocol.Eta) ?? 0,
                Int(item, QBittorrentProtocol.InfoSeeds) ?? 0,
                Int(item, QBittorrentProtocol.InfoPeers) ?? 0,
                Text(item, QBittorrentProtocol.SavePathJson),
                Double(item, QBittorrentProtocol.InfoRatio),
                Long(item, QBittorrentProtocol.SeedingTime)));
    }

    public Task<string> AddTorrentFileAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) =>
        SerializeAddAsync(connection, () => AddTorrentFileCoreAsync(connection, fileName, torrent, cancellationToken), cancellationToken);

    private async Task<string> AddTorrentFileCoreAsync(DownloadClientConnection connection, string fileName, byte[] torrent, CancellationToken cancellationToken) {
        await PostAsync(connection, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.CategoryField] = connection.Category
        }, cancellationToken, allowConflict: true);

        var before = await CategoryHashesAsync(connection, connection.Category, cancellationToken);

        using var response = await SendAuthenticatedWithContentFactoryAsync(
            connection,
            HttpMethod.Post,
            QBittorrentProtocol.AddEndpoint,
            () => BuildTorrentUpload(fileName, torrent, connection.Category),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < 15; attempt++) {
            await Task.Delay(AddPollDelay, cancellationToken);
            var after = await CategoryHashesAsync(connection, connection.Category, cancellationToken);
            after.ExceptWith(before);
            if (after.Count > 0) {
                return after.First();
            }
        }

        // Same duplicate correlation as the URL add: an accepted upload that created nothing usually
        // means the torrent already exists; match it by the file's name before failing.
        if (await FindByNormalizedNameAsync(
                connection, connection.Category, Path.GetFileNameWithoutExtension(fileName), cancellationToken) is { } duplicate) {
            return duplicate;
        }

        throw new DownloadClientAddUnresolvedException(
            "qBittorrent accepted the upload but created no new torrent — the torrent is likely already present in the client (duplicate add).");
    }

    /// <summary>
    /// qBittorrent's add endpoint returns no native id, so no-hash adds discover their torrent by diffing
    /// one category before and after the request. Serialize that observation per configured client and
    /// category across scoped adapter instances; otherwise concurrent adds can both claim the first hash
    /// that appears and permanently attach one acquisition to another acquisition's payload.
    /// </summary>
    private static async Task<string> SerializeAddAsync(
        DownloadClientConnection connection,
        Func<Task<string>> add,
        CancellationToken cancellationToken) {
        var gate = AddGates.GetOrAdd((connection.Id, connection.Category), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try {
            return await add();
        } finally {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<DownloadItemFile>> GetFilesAsync(DownloadClientConnection connection, string clientItemId, CancellationToken cancellationToken) {
        var path = $"{QBittorrentProtocol.FilesEndpoint}?{QBittorrentProtocol.HashField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
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
        var path = $"{QBittorrentProtocol.PropertiesEndpoint}?{QBittorrentProtocol.HashField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
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
        var path = $"{QBittorrentProtocol.PieceStatesEndpoint}?{QBittorrentProtocol.HashField}={Uri.EscapeDataString(clientItemId.ToLowerInvariant())}";
        using var response = await SendAuthenticatedAsync(connection, HttpMethod.Get, path, cancellationToken);
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
        await PostAsync(connection, QBittorrentProtocol.DeleteEndpoint, new Dictionary<string, string> {
            [QBittorrentProtocol.HashesField] = clientItemId.ToLowerInvariant(),
            [QBittorrentProtocol.DeleteFilesField] = deleteData ? "true" : "false"
        }, cancellationToken);
    }

    public async Task<DownloadClientConnectionTest> TestAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        try {
            using var response = await SendAuthenticatedAsync(
                connection, HttpMethod.Get, QBittorrentProtocol.VersionEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode) {
                return new DownloadClientConnectionTest(false, $"qBittorrent returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            // A connection without a category (a pre-save test) verifies connectivity only — an empty
            // category name would be rejected by qBittorrent and misread as an auth/connection failure.
            if (string.IsNullOrWhiteSpace(connection.Category)) {
                return new DownloadClientConnectionTest(true, "Connected to qBittorrent.");
            }

            // Verify the configured category can actually exist: create it (conflict = already there),
            // then read the categories list back. A real category problem surfaces HERE, at test time,
            // so add-time failures are never misattributed to the category.
            await PostAsync(connection, QBittorrentProtocol.CreateCategoryEndpoint, new Dictionary<string, string> {
                [QBittorrentProtocol.CategoryField] = connection.Category
            }, cancellationToken, allowConflict: true);
            if (!await CategoryExistsAsync(connection, connection.Category, cancellationToken)) {
                return new DownloadClientConnectionTest(
                    false, $"qBittorrent connected, but the category \"{connection.Category}\" could not be created or found.");
            }

            return new DownloadClientConnectionTest(true, $"Connected to qBittorrent; category \"{connection.Category}\" is ready.");
        } catch (QBittorrentAuthException ex) {
            return new DownloadClientConnectionTest(false, ex.Message);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return new DownloadClientConnectionTest(false, ex.Message);
        }
    }

    private async Task<bool> CategoryExistsAsync(DownloadClientConnection connection, string category, CancellationToken cancellationToken) {
        using var response = await SendAuthenticatedAsync(
            connection, HttpMethod.Get, QBittorrentProtocol.CategoriesEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(category, out _);
    }

    /// <summary>
    /// Returns the cached session cookie, coalescing concurrent cache misses for one endpoint and
    /// credential fingerprint. Authentication failures are retained briefly so API fan-out and the
    /// worker cannot turn one rejected secret into qBittorrent's five-attempt IP ban.
    /// </summary>
    private async Task<string?> LoginAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(connection.Username) && string.IsNullOrWhiteSpace(connection.Password)) {
            return null;
        }

        var key = SessionCacheKey.For(connection);
        var entry = Sessions.GetOrAdd(key, static _ => new SessionCacheEntry());
        if (Volatile.Read(ref entry.Cookie) is { } cached) {
            return cached;
        }

        await entry.Gate.WaitAsync(cancellationToken);
        try {
            if (entry.Cookie is { } synchronized) {
                return synchronized;
            }

            if (entry.Failure is { } failure && failure.RetryAt > DateTimeOffset.UtcNow) {
                throw new QBittorrentAuthException(failure.Message, failure.RetryAt - DateTimeOffset.UtcNow);
            }

            try {
                var session = await LoginCoreAsync(connection, cancellationToken);
                entry.Cookie = session;
                entry.Failure = null;
                return session;
            } catch (QBittorrentAuthException ex) {
                entry.Failure = new CachedAuthenticationFailure(
                    ex.Message,
                    DateTimeOffset.UtcNow + ex.RetryAfter);
                throw;
            }
        } finally {
            entry.Gate.Release();
        }
    }

    /// <summary>Performs the one network login that populates a shared session cache entry.</summary>
    private async Task<string> LoginCoreAsync(DownloadClientConnection connection, CancellationToken cancellationToken) {
        using var request = BuildRequest(connection, session: null, HttpMethod.Post, QBittorrentProtocol.LoginEndpoint,
            content: new FormUrlEncodedContent(new Dictionary<string, string> {
                [QBittorrentProtocol.UsernameField] = connection.Username ?? string.Empty,
                [QBittorrentProtocol.PasswordField] = connection.Password ?? string.Empty
            }));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden) {
            throw new QBittorrentAuthException(
                "qBittorrent temporarily banned this client after failed logins.",
                BannedLoginBackoff);
        }

        var session = ExtractSessionCookie(response);
        if (session is null) {
            throw new QBittorrentAuthException(
                "qBittorrent rejected the username or password.",
                RejectedLoginBackoff);
        }

        return session;
    }

    /// <summary>
    /// Sends one authenticated request and refreshes the cached cookie once when qBittorrent rejects
    /// an expired session. Content is factory-backed because a retry requires a fresh disposable body.
    /// </summary>
    private Task<HttpResponseMessage> SendAuthenticatedAsync(
        DownloadClientConnection connection,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken) =>
        SendAuthenticatedWithContentFactoryAsync(connection, method, path, static () => null, cancellationToken);

    private async Task<HttpResponseMessage> SendAuthenticatedWithContentFactoryAsync(
        DownloadClientConnection connection,
        HttpMethod method,
        string path,
        Func<HttpContent?> content,
        CancellationToken cancellationToken) {
        var session = await LoginAsync(connection, cancellationToken);
        var response = await SendOnceAsync(connection, session, method, path, content(), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden || session is null) {
            return response;
        }

        response.Dispose();
        InvalidateSession(connection, session);
        var refreshedSession = await LoginAsync(connection, cancellationToken);
        return await SendOnceAsync(connection, refreshedSession, method, path, content(), cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        DownloadClientConnection connection,
        string? session,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken) {
        using var request = BuildRequest(connection, session, method, path, content);
        return await http.SendAsync(request, cancellationToken);
    }

    private static void InvalidateSession(DownloadClientConnection connection, string session) {
        if (!Sessions.TryGetValue(SessionCacheKey.For(connection), out var entry)) {
            return;
        }

        Interlocked.CompareExchange(ref entry.Cookie, null, session);
    }

    private static MultipartFormDataContent BuildTorrentUpload(string fileName, byte[] torrent, string category) {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(torrent);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-bittorrent");
        form.Add(fileContent, QBittorrentProtocol.TorrentsField, string.IsNullOrWhiteSpace(fileName) ? "upload.torrent" : fileName);
        form.Add(new StringContent(category), QBittorrentProtocol.CategoryField);
        return form;
    }

    /// <summary>
    /// Posts a form to a qBittorrent endpoint, disposing the response. Throws on a non-success status unless
    /// <paramref name="allowConflict"/> is set, in which case a 409 (e.g. category already exists) is tolerated.
    /// </summary>
    private async Task PostAsync(
        DownloadClientConnection connection,
        string path,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken,
        bool allowConflict = false) {
        using var response = await SendAuthenticatedWithContentFactoryAsync(
            connection,
            HttpMethod.Post,
            path,
            () => new FormUrlEncodedContent(form),
            cancellationToken);
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

    private readonly record struct SessionCacheKey(Guid ClientId, string BaseUrl, string Username, string CredentialHash) {
        public static SessionCacheKey For(DownloadClientConnection connection) {
            var credentialBytes = Encoding.UTF8.GetBytes($"{connection.Username}\0{connection.Password}");
            return new SessionCacheKey(
                connection.Id,
                connection.BaseUrl.Trim().TrimEnd('/'),
                connection.Username ?? string.Empty,
                Convert.ToHexString(SHA256.HashData(credentialBytes)));
        }
    }

    private sealed class SessionCacheEntry {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? Cookie;
        public CachedAuthenticationFailure? Failure;
    }

    private sealed record CachedAuthenticationFailure(string Message, DateTimeOffset RetryAt);
}

/// <summary>Raised when qBittorrent authentication fails so connection tests can report a clear reason.</summary>
public sealed class QBittorrentAuthException(string message, TimeSpan retryAfter) : Exception(message) {
    /// <summary>Minimum delay before the same credentials should be attempted again.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Resolves the configured <see cref="IDownloadClient"/> for a client family.</summary>
public sealed class DownloadClientFactory(IEnumerable<IDownloadClient> clients) : IDownloadClientFactory {
    private readonly Dictionary<DownloadClientKind, IDownloadClient> _clients = clients.ToDictionary(client => client.Kind);

    public IDownloadClient Get(DownloadClientKind kind) =>
        _clients.TryGetValue(kind, out var client)
            ? client
            : throw new NotSupportedException($"No download client is registered for '{kind}'.");
}
