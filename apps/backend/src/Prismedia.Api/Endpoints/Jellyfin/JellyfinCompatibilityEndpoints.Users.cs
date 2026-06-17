using Prismedia.Api.Security;
using Prismedia.Application.Jellyfin;
using Prismedia.Application.Security;
using Prismedia.Contracts.Jellyfin;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

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
            "Prismedia",
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
        PrismediaSecurityService security,
        bool enabledOnly,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        var profiles = (await security.ListProfilesAsync(cancellationToken)).Items
            .Where(profile => !enabledOnly || profile.Enabled)
            .Select(profile => ToUserDto(profile, state))
            .ToArray();
        return profiles;
    }

    private static async Task<JellyfinUserDto?> ResolveUserAsync(
        HttpContext httpContext,
        PrismediaSecurityService security,
        Guid? userId,
        CancellationToken cancellationToken) {
        var state = await security.EnsureSecurityAsync(cancellationToken);
        if (httpContext.GetJellyfinProfile() is { } activeProfile && (userId is null || activeProfile.Id == userId)) {
            return ToUserDto(activeProfile, state);
        }

        var profiles = (await security.ListProfilesAsync(cancellationToken)).Items;
        var profile = userId is null
            ? profiles.FirstOrDefault(item => item.Enabled)
            : profiles.FirstOrDefault(item => item.Id == userId && item.Enabled);
        return profile is null ? null : ToUserDto(profile, state);
    }

    private static JellyfinUserDto ToUserDto(JellyfinProfileResponse profile, AppSecurityState state) =>
        new(
            profile.DisplayName,
            state.ServerId.ToString("N"),
            "Prismedia",
            profile.Id,
            HasPassword: true,
            HasConfiguredPassword: true,
            HasConfiguredEasyPassword: true,
            EnableAutoLogin: false,
            profile.LastLoginAt,
            profile.LastLoginAt,
            UserPolicy(profile),
            UserConfiguration());

    private static JellyfinUserDto ToUserDto(JellyfinProfile profile, AppSecurityState state) =>
        new(
            profile.DisplayName,
            state.ServerId.ToString("N"),
            "Prismedia",
            profile.Id,
            HasPassword: true,
            HasConfiguredPassword: true,
            HasConfiguredEasyPassword: true,
            EnableAutoLogin: false,
            profile.LastLoginAt,
            profile.LastLoginAt,
            UserPolicy(profile),
            UserConfiguration());

    private static JellyfinSessionInfoDto ToSessionDto(JellyfinProfile profile, JellyfinSession session) =>
        new(
            session.Id.ToString("N"),
            profile.Id,
            profile.DisplayName,
            session.Client,
            session.DeviceName,
            session.DeviceId,
            session.ApplicationVersion,
            IsActive: true);

    private static JellyfinUserPolicyDto UserPolicy(JellyfinProfileResponse profile) =>
        UserPolicy(profile.Enabled);

    private static JellyfinUserPolicyDto UserPolicy(JellyfinProfile profile) =>
        UserPolicy(profile.Enabled);

    private static JellyfinUserPolicyDto UserPolicy(bool enabled) =>
        new(
            IsAdministrator: false,
            IsHidden: false,
            IsDisabled: !enabled,
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
