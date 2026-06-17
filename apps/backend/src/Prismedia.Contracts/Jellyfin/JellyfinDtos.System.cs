using System.Text.Json.Serialization;

namespace Prismedia.Contracts.Jellyfin;

/// <summary>Minimal Jellyfin-compatible public system information.</summary>
public sealed record JellyfinPublicSystemInfo(
    [property: JsonPropertyName("LocalAddress")] string LocalAddress,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("ProductName")] string ProductName,
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("StartupWizardCompleted")] bool StartupWizardCompleted,
    [property: JsonPropertyName("OperatingSystem")] string OperatingSystem = "");

/// <summary>Minimal Jellyfin-compatible private system information.</summary>
public sealed record JellyfinSystemInfo(
    [property: JsonPropertyName("LocalAddress")] string LocalAddress,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("ProductName")] string ProductName,
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("StartupWizardCompleted")] bool StartupWizardCompleted,
    [property: JsonPropertyName("OperatingSystem")] string OperatingSystem,
    [property: JsonPropertyName("PackageName")] string PackageName,
    [property: JsonPropertyName("ServerNameRaw")] string? ServerNameRaw = null);

/// <summary>Minimal Jellyfin-compatible information about the request endpoint.</summary>
public sealed record JellyfinEndpointInfo(
    [property: JsonPropertyName("IsLocal")] bool IsLocal,
    [property: JsonPropertyName("IsInNetwork")] bool IsInNetwork);

/// <summary>Minimal Jellyfin-compatible startup configuration.</summary>
public sealed record JellyfinStartupConfiguration(
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("UICulture")] string UICulture,
    [property: JsonPropertyName("MetadataCountryCode")] string MetadataCountryCode,
    [property: JsonPropertyName("PreferredMetadataLanguage")] string PreferredMetadataLanguage);

/// <summary>Jellyfin-compatible authenticate-by-name request.</summary>
public sealed record JellyfinAuthenticateByNameRequest {
    [JsonPropertyName("Username")]
    public string? Username { get; init; }

    [JsonPropertyName("Pw")]
    public string? Pw { get; init; }

    [JsonPropertyName("Password")]
    public string? Password { get; init; }

    /// <summary>Password value supplied by either Jellyfin's <c>Pw</c> field or older client <c>Password</c> field.</summary>
    [JsonIgnore]
    public string? EffectivePassword => Pw ?? Password;
}

/// <summary>Jellyfin-compatible authentication result.</summary>
public sealed record JellyfinAuthenticationResult(
    [property: JsonPropertyName("User")] JellyfinUserDto User,
    [property: JsonPropertyName("SessionInfo")] JellyfinSessionInfoDto SessionInfo,
    [property: JsonPropertyName("AccessToken")] string AccessToken,
    [property: JsonPropertyName("ServerId")] string ServerId);

/// <summary>Jellyfin-compatible user DTO for fake Prismedia profiles.</summary>
public sealed record JellyfinUserDto(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("ServerId")] string ServerId,
    [property: JsonPropertyName("ServerName")] string ServerName,
    [property: JsonPropertyName("Id")][property: JsonConverter(typeof(JellyfinGuidConverter))] Guid Id,
    [property: JsonPropertyName("HasPassword")] bool HasPassword,
    [property: JsonPropertyName("HasConfiguredPassword")] bool HasConfiguredPassword,
    [property: JsonPropertyName("HasConfiguredEasyPassword")] bool HasConfiguredEasyPassword,
    [property: JsonPropertyName("EnableAutoLogin")] bool EnableAutoLogin,
    [property: JsonPropertyName("LastLoginDate")][property: JsonConverter(typeof(JellyfinDateConverter))] DateTimeOffset? LastLoginDate,
    [property: JsonPropertyName("LastActivityDate")][property: JsonConverter(typeof(JellyfinDateConverter))] DateTimeOffset? LastActivityDate,
    [property: JsonPropertyName("Policy")] JellyfinUserPolicyDto Policy,
    [property: JsonPropertyName("Configuration")] JellyfinUserConfigurationDto Configuration);

/// <summary>Minimal Jellyfin-compatible user policy.</summary>
public sealed record JellyfinUserPolicyDto(
    [property: JsonPropertyName("IsAdministrator")] bool IsAdministrator,
    [property: JsonPropertyName("IsHidden")] bool IsHidden,
    [property: JsonPropertyName("IsDisabled")] bool IsDisabled,
    [property: JsonPropertyName("AuthenticationProviderId")] string AuthenticationProviderId,
    [property: JsonPropertyName("PasswordResetProviderId")] string PasswordResetProviderId,
    [property: JsonPropertyName("EnableRemoteControlOfOtherUsers")] bool EnableRemoteControlOfOtherUsers,
    [property: JsonPropertyName("EnableSharedDeviceControl")] bool EnableSharedDeviceControl,
    [property: JsonPropertyName("EnableContentDeletion")] bool EnableContentDeletion,
    [property: JsonPropertyName("EnableContentDownloading")] bool EnableContentDownloading,
    [property: JsonPropertyName("EnableSyncTranscoding")] bool EnableSyncTranscoding,
    [property: JsonPropertyName("EnableMediaPlayback")] bool EnableMediaPlayback,
    [property: JsonPropertyName("EnableAudioPlaybackTranscoding")] bool EnableAudioPlaybackTranscoding,
    [property: JsonPropertyName("EnableAllFolders")] bool EnableAllFolders,
    [property: JsonPropertyName("EnabledFolders")] IReadOnlyList<string> EnabledFolders,
    [property: JsonPropertyName("EnableAllChannels")] bool EnableAllChannels,
    [property: JsonPropertyName("BlockedTags")] IReadOnlyList<string> BlockedTags,
    [property: JsonPropertyName("EnabledChannels")] IReadOnlyList<string> EnabledChannels);

/// <summary>Minimal Jellyfin-compatible user configuration.</summary>
public sealed record JellyfinUserConfigurationDto(
    [property: JsonPropertyName("AudioLanguagePreference")] string? AudioLanguagePreference,
    [property: JsonPropertyName("PlayDefaultAudioTrack")] bool PlayDefaultAudioTrack,
    [property: JsonPropertyName("SubtitleLanguagePreference")] string? SubtitleLanguagePreference,
    [property: JsonPropertyName("DisplayMissingEpisodes")] bool DisplayMissingEpisodes,
    [property: JsonPropertyName("GroupedFolders")] IReadOnlyList<string> GroupedFolders,
    [property: JsonPropertyName("SubtitleMode")] string SubtitleMode);

/// <summary>Minimal Jellyfin-compatible session info.</summary>
public sealed record JellyfinSessionInfoDto(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("UserId")][property: JsonConverter(typeof(JellyfinGuidConverter))] Guid UserId,
    [property: JsonPropertyName("UserName")] string UserName,
    [property: JsonPropertyName("Client")] string? Client,
    [property: JsonPropertyName("DeviceName")] string? DeviceName,
    [property: JsonPropertyName("DeviceId")] string? DeviceId,
    [property: JsonPropertyName("ApplicationVersion")] string? ApplicationVersion,
    [property: JsonPropertyName("IsActive")] bool IsActive);

/// <summary>Jellyfin-compatible branding configuration.</summary>
public sealed record JellyfinBrandingConfiguration(
    [property: JsonPropertyName("LoginDisclaimer")] string LoginDisclaimer,
    [property: JsonPropertyName("CustomCss")] string CustomCss,
    [property: JsonPropertyName("SplashscreenEnabled")] bool SplashscreenEnabled);
