using System.Text.Json.Nodes;

namespace Prismedia.Application.UserState;

/// <summary>
/// Application use-case service for browser user-state (UI shell preferences). Owns the
/// playlist-session lifecycle: read the stored JSON document, stamp <c>updatedAt</c> on save,
/// and clear it on delete. Raw persistence is delegated to <see cref="IUserStatePersistence"/>.
/// </summary>
public sealed class UserStateService {
    private const string PlaylistSessionKey = "ui:playlist-session";

    private readonly IUserStatePersistence _persistence;

    /// <summary>
    /// Creates the service over the user-state persistence port.
    /// </summary>
    public UserStateService(IUserStatePersistence persistence) {
        _persistence = persistence;
    }

    /// <summary>
    /// Returns the currently stored playlist session JSON document, or null when none has been
    /// saved.
    /// </summary>
    public Task<string?> GetPlaylistSessionJsonAsync(CancellationToken cancellationToken) =>
        _persistence.GetAsync(PlaylistSessionKey, cancellationToken);

    /// <summary>
    /// Stamps <c>updatedAt</c> onto the supplied JSON object and persists it as the playlist
    /// session. Returns the serialized JSON the client should treat as canonical.
    /// </summary>
    /// <param name="session">Parsed JSON object the client posted.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<string> SavePlaylistSessionAsync(JsonObject session, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(session);

        session["updatedAt"] = DateTimeOffset.UtcNow;
        var valueJson = session.ToJsonString();
        await _persistence.SaveAsync(PlaylistSessionKey, valueJson, cancellationToken);
        return valueJson;
    }

    /// <summary>
    /// Clears the stored playlist session. No-op when none is stored.
    /// </summary>
    public Task ClearPlaylistSessionAsync(CancellationToken cancellationToken) =>
        _persistence.DeleteAsync(PlaylistSessionKey, cancellationToken);
}
