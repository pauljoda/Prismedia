using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// User, session, and system-info DTO mapping for the Jellyfin compatibility endpoints.
/// </summary>
public static partial class JellyfinCompatibilityEndpoints {
    private static JellyfinPublicSystemInfo ToPublicSystemInfo(HttpContext httpContext, AppSecurityState state) {
        return new JellyfinPublicSystemInfo(
            PublicBaseUrl(httpContext.Request),
            "Prismedia",
            JellyfinProtocol.CompatibleServerVersion,
            JellyfinProtocol.CompatibleProductName,
            state.ServerId.ToString("N"),
            StartupWizardCompleted: true);
    }

    private static string PublicBaseUrl(HttpRequest request) {
        var scheme = FirstForwardedValue(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;
        var host = FirstForwardedValue(request.Headers["X-Forwarded-Host"]) ?? request.Host.Value;
        return $"{scheme}://{host}";
    }

    private static string? FirstForwardedValue(IEnumerable<string?> values) =>
        values
            .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static async Task<IReadOnlyList<JellyfinUserDto>> JellyfinUsersAsync(
        UserAuthService auth,
        CancellationToken cancellationToken) {
        var state = await auth.GetServerInfoAsync(cancellationToken);
        return (await auth.ListEnabledUsersAsync(cancellationToken))
            .Select(user => ToUserDto(user, state))
            .ToArray();
    }

    private static async Task<JellyfinUserDto?> ResolveUserAsync(
        HttpContext httpContext,
        UserAuthService auth,
        Guid? userId,
        CancellationToken cancellationToken) {
        var state = await auth.GetServerInfoAsync(cancellationToken);
        if (httpContext.GetCurrentUser() is { } activeUser && (userId is null || activeUser.Id == userId)) {
            return ToUserDto(activeUser, state);
        }

        var users = await auth.ListEnabledUsersAsync(cancellationToken);
        var user = userId is null
            ? users.FirstOrDefault()
            : users.FirstOrDefault(item => item.Id == userId);
        return user is null ? null : ToUserDto(user, state);
    }

    private static JellyfinUserDto ToUserDto(User user, AppSecurityState state) =>
        new(
            user.DisplayName,
            state.ServerId.ToString("N"),
            "Prismedia",
            user.Id,
            HasPassword: true,
            HasConfiguredPassword: true,
            HasConfiguredEasyPassword: true,
            EnableAutoLogin: false,
            user.LastLoginAt,
            user.LastLoginAt,
            UserPolicy(user),
            UserConfiguration());

    private static JellyfinSessionInfoDto ToSessionDto(User user, UserSession session) =>
        new(
            session.Id.ToString("N"),
            user.Id,
            user.DisplayName,
            session.Client,
            session.DeviceName,
            session.DeviceId,
            session.ApplicationVersion,
            IsActive: true);

    private static JellyfinUserPolicyDto UserPolicy(User user) =>
        new(
            IsAdministrator: user.Role == UserRole.Admin,
            IsHidden: false,
            IsDisabled: !user.Enabled,
            AuthenticationProviderId: JellyfinProtocol.UserPolicyProviders.DefaultAuthentication,
            PasswordResetProviderId: JellyfinProtocol.UserPolicyProviders.DefaultPasswordReset,
            EnableRemoteControlOfOtherUsers: false,
            EnableSharedDeviceControl: false,
            EnableContentDeletion: false,
            EnableContentDownloading: true,
            EnableSyncTranscoding: true,
            EnableMediaPlayback: true,
            EnableAudioPlaybackTranscoding: true,
            // The user can access every library. Without this, clients that honour the policy treat the
            // user as having access to no folders and refuse to browse any library's contents.
            EnableAllFolders: true,
            EnabledFolders: [],
            EnableAllChannels: false,
            BlockedTags: [],
            EnabledChannels: []);

    private static JellyfinUserConfigurationDto UserConfiguration() =>
        new(
            AudioLanguagePreference: null,
            PlayDefaultAudioTrack: true,
            SubtitleLanguagePreference: null,
            DisplayMissingEpisodes: false,
            GroupedFolders: [],
            SubtitleMode: "Default");
}
