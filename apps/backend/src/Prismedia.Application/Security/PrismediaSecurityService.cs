using System.Security.Cryptography;
using System.Text;
using Prismedia.Contracts.Security;

namespace Prismedia.Application.Security;

/// <summary>Application use cases for API key and Jellyfin compatibility security.</summary>
public sealed class PrismediaSecurityService {
    private readonly ISecurityPersistence _persistence;
    private readonly AuthAttemptThrottle _throttle;

    public PrismediaSecurityService(ISecurityPersistence persistence, AuthAttemptThrottle throttle) {
        _persistence = persistence;
        _throttle = throttle;
    }

    /// <summary>Ensures security state exists and returns it.</summary>
    public Task<AppSecurityState> EnsureSecurityAsync(CancellationToken cancellationToken) =>
        _persistence.EnsureSecurityAsync(HumanApiKeyPassphraseGenerator.Generate, cancellationToken);

    /// <summary>Gets the current app API key for Settings.</summary>
    public async Task<ApiKeyResponse> GetApiKeyAsync(CancellationToken cancellationToken) {
        var state = await _persistence.GetSecurityAsync(HumanApiKeyPassphraseGenerator.Generate, cancellationToken);
        return new ApiKeyResponse(state.ApiKey, state.ApiKeyCreatedAt, state.ApiKeyUpdatedAt);
    }

    /// <summary>Rotates the app API key and invalidates Jellyfin client sessions.</summary>
    public async Task<ApiKeyRegenerateResponse> RegenerateApiKeyAsync(CancellationToken cancellationToken) {
        var result = await _persistence.RotateApiKeyAsync(HumanApiKeyPassphraseGenerator.Generate(), cancellationToken);
        return new ApiKeyRegenerateResponse(
            result.State.ApiKey,
            result.State.ApiKeyCreatedAt,
            result.State.ApiKeyUpdatedAt,
            result.InvalidatedSessions);
    }

    /// <summary>Validates an app API key attempt.</summary>
    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(
        string? candidate,
        string bucket,
        CancellationToken cancellationToken) {
        if (_throttle.IsThrottled(bucket)) {
            return new ApiKeyValidationResult(false, true);
        }

        var state = await _persistence.GetSecurityAsync(HumanApiKeyPassphraseGenerator.Generate, cancellationToken);
        var valid = ConstantTimeEquals(NormalizeApiKey(candidate), state.ApiKey);
        if (valid) {
            _throttle.RecordSuccess(bucket);
        } else {
            _throttle.RecordFailure(bucket);
        }

        return new ApiKeyValidationResult(valid, false);
    }

    /// <summary>Authenticates a fake Jellyfin profile using username plus the app API key as password.</summary>
    public async Task<JellyfinProfileAuthenticationResult> AuthenticateJellyfinProfileAsync(
        string? username,
        string? password,
        JellyfinClientIdentity client,
        string bucket,
        CancellationToken cancellationToken) {
        if (_throttle.IsThrottled(bucket)) {
            return new JellyfinProfileAuthenticationResult(false, true, null, null, null);
        }

        var profile = string.IsNullOrWhiteSpace(username)
            ? null
            : await _persistence.FindProfileByUsernameAsync(username.Trim(), cancellationToken);
        var state = await _persistence.GetSecurityAsync(HumanApiKeyPassphraseGenerator.Generate, cancellationToken);
        var passwordValid = ConstantTimeEquals(NormalizeApiKey(password), state.ApiKey);

        if (profile is null || !profile.Enabled || !passwordValid) {
            _throttle.RecordFailure(bucket);
            return new JellyfinProfileAuthenticationResult(false, false, null, null, null);
        }

        var accessToken = CreateSessionToken();
        var session = await _persistence.CreateSessionAsync(
            profile.Id,
            HashToken(accessToken),
            client,
            cancellationToken);
        await _persistence.TouchProfileLoginAsync(profile.Id, cancellationToken);
        _throttle.RecordSuccess(bucket);

        return new JellyfinProfileAuthenticationResult(true, false, profile, session, accessToken);
    }

    /// <summary>Resolves an active Jellyfin-compatible access token.</summary>
    public Task<JellyfinSessionResolution?> ResolveJellyfinSessionAsync(
        string token,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token)
            ? Task.FromResult<JellyfinSessionResolution?>(null)
            : _persistence.ResolveSessionAsync(HashToken(token), cancellationToken);

    /// <summary>Lists fake Jellyfin profiles.</summary>
    public async Task<JellyfinProfilesResponse> ListProfilesAsync(CancellationToken cancellationToken) =>
        new((await _persistence.ListProfilesAsync(includeDisabled: true, cancellationToken)).Select(ToContract).ToArray());

    /// <summary>Creates a fake Jellyfin profile.</summary>
    public async Task<JellyfinProfileResponse> CreateProfileAsync(
        JellyfinProfileCreateRequest request,
        CancellationToken cancellationToken) {
        var username = NormalizeUsername(request.Username);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim();
        var profile = await _persistence.CreateProfileAsync(
            username,
            displayName,
            request.AllowSfw,
            request.AllowNsfw,
            request.Enabled,
            cancellationToken);
        return ToContract(profile);
    }

    /// <summary>Updates a fake Jellyfin profile.</summary>
    public async Task<JellyfinProfileResponse?> UpdateProfileAsync(
        Guid profileId,
        JellyfinProfileUpdateRequest request,
        CancellationToken cancellationToken) {
        var profile = await _persistence.UpdateProfileAsync(
            profileId,
            request.Username is null ? null : NormalizeUsername(request.Username),
            request.DisplayName?.Trim(),
            request.AllowSfw,
            request.AllowNsfw,
            request.Enabled,
            cancellationToken);
        return profile is null ? null : ToContract(profile);
    }

    /// <summary>Deletes a fake Jellyfin profile.</summary>
    public Task<bool> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        _persistence.DeleteProfileAsync(profileId, cancellationToken);

    /// <summary>Normalizes a human API key for validation.</summary>
    public static string NormalizeApiKey(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;
        foreach (var ch in value.Trim().ToLowerInvariant()) {
            if (char.IsWhiteSpace(ch) || ch is '-' or '_') {
                if (!lastWasSeparator) {
                    builder.Append('-');
                    lastWasSeparator = true;
                }

                continue;
            }

            if (ch is >= 'a' and <= 'z') {
                builder.Append(ch);
                lastWasSeparator = false;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>Hashes a Jellyfin session token for storage and lookup.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static JellyfinProfileResponse ToContract(JellyfinProfile profile) =>
        new(
            profile.Id,
            profile.Username,
            profile.DisplayName,
            profile.AllowSfw,
            profile.AllowNsfw,
            profile.Enabled,
            profile.LastLoginAt,
            profile.CreatedAt,
            profile.UpdatedAt);

    private static string NormalizeUsername(string username) {
        var normalized = username.Trim();
        if (normalized.Length is < 1 or > 64) {
            throw new ArgumentException("Username must be between 1 and 64 characters.", nameof(username));
        }

        return normalized;
    }

    private static string CreateSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static bool ConstantTimeEquals(string candidate, string expected) {
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (candidateBytes.Length != expectedBytes.Length) {
            _ = CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}
