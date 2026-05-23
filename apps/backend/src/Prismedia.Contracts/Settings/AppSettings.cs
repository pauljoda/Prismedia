namespace Prismedia.Contracts.Settings;

/// <summary>
/// API-facing settings values used by the UI.
/// </summary>
/// <param name="HideNsfw">Whether NSFW media should be hidden by default.</param>
/// <param name="EnableCastControls">Whether cast controls should be shown in playback controls.</param>
public sealed record SettingsResponse(
    bool HideNsfw,
    bool EnableCastControls);

/// <summary>
/// Request body for partially updating settings.
/// </summary>
/// <param name="HideNsfw">Optional hide-NSFW setting value.</param>
/// <param name="EnableCastControls">Optional cast-control setting value.</param>
public sealed record SettingsUpdateRequest(
    bool? HideNsfw,
    bool? EnableCastControls);
