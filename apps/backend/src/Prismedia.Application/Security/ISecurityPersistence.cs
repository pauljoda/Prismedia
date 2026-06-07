namespace Prismedia.Application.Security;

/// <summary>Infrastructure persistence port for app API keys and Jellyfin-compatible profiles.</summary>
public interface ISecurityPersistence {
    /// <summary>Ensures the singleton app security state and default profile exist.</summary>
    Task<AppSecurityState> EnsureSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken);

    /// <summary>Gets the singleton app security state, creating it when absent.</summary>
    Task<AppSecurityState> GetSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken);

    /// <summary>Rotates the app API key and invalidates existing Jellyfin sessions.</summary>
    Task<ApiKeyRotationResult> RotateApiKeyAsync(string apiKey, CancellationToken cancellationToken);

    /// <summary>Lists Jellyfin-compatible fake user profiles.</summary>
    Task<IReadOnlyList<JellyfinProfile>> ListProfilesAsync(bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>Finds one fake user profile by id.</summary>
    Task<JellyfinProfile?> GetProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Finds one fake user profile by username.</summary>
    Task<JellyfinProfile?> FindProfileByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Creates a fake user profile.</summary>
    Task<JellyfinProfile> CreateProfileAsync(
        string username,
        string displayName,
        bool allowSfw,
        bool allowNsfw,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>Updates a fake user profile.</summary>
    Task<JellyfinProfile?> UpdateProfileAsync(
        Guid profileId,
        string? username,
        string? displayName,
        bool? allowSfw,
        bool? allowNsfw,
        bool? enabled,
        CancellationToken cancellationToken);

    /// <summary>Deletes a fake user profile and invalidates its sessions.</summary>
    Task<bool> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>Creates a Jellyfin session for a fake profile.</summary>
    Task<JellyfinSession> CreateSessionAsync(
        Guid profileId,
        string tokenHash,
        JellyfinClientIdentity client,
        CancellationToken cancellationToken);

    /// <summary>Resolves an active Jellyfin session by token hash.</summary>
    Task<JellyfinSessionResolution?> ResolveSessionAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Records a successful fake user login.</summary>
    Task TouchProfileLoginAsync(Guid profileId, CancellationToken cancellationToken);
}
