using System.Text.Json;

namespace Prismedia.Contracts.Settings;

/// <summary>
/// Settings catalog response grouped for the curated settings UI.
/// </summary>
/// <param name="Groups">Groups in display order, each containing its settings.</param>
public sealed record SettingsCatalogResponse(IReadOnlyList<SettingsGroup> Groups);

/// <summary>
/// On-disk transcode/remux cache usage, for the Transcode Cache settings card.
/// </summary>
/// <param name="UsedBytes">Current size of the cache on disk, in bytes.</param>
/// <param name="MaxBytes">Configured maximum size in bytes, or 0 when the cache is unlimited.</param>
public sealed record TranscodeCacheStatusResponse(long UsedBytes, long MaxBytes);

/// <summary>
/// One display group in the centralized settings catalog.
/// </summary>
/// <param name="Key">Stable group key.</param>
/// <param name="Label">Human-readable group label.</param>
/// <param name="Description">Short group description shown in the UI.</param>
/// <param name="Order">Display order within the settings page.</param>
/// <param name="Settings">Settings that belong to this group.</param>
public sealed record SettingsGroup(
    string Key,
    string Label,
    string Description,
    int Order,
    IReadOnlyList<SettingDescriptor> Settings);

/// <summary>
/// Describes one app-global setting, including current effective value and UI hints.
/// </summary>
public sealed record SettingDescriptor(
    string Key,
    string GroupKey,
    string Label,
    string Description,
    string Type,
    JsonElement Value,
    JsonElement DefaultValue,
    bool IsDefault,
    int Order,
    SettingConstraints? Constraints,
    IReadOnlyList<SettingOption> Options,
    string? InputKind,
    string? ApplyHint);

/// <summary>
/// Numeric and collection constraints for settings controls.
/// </summary>
/// <param name="Min">Minimum numeric value, when applicable.</param>
/// <param name="Max">Maximum numeric value, when applicable.</param>
/// <param name="Step">Numeric step value, when applicable.</param>
/// <param name="MinItems">Minimum string-list item count, when applicable.</param>
/// <param name="MaxItems">Maximum string-list item count, when applicable.</param>
public sealed record SettingConstraints(
    decimal? Min = null,
    decimal? Max = null,
    decimal? Step = null,
    int? MinItems = null,
    int? MaxItems = null);

/// <summary>
/// One selectable option for select-style settings.
/// </summary>
/// <param name="Value">Stable option value saved through the API.</param>
/// <param name="Label">Human-readable label shown in controls.</param>
/// <param name="Description">Optional short option description.</param>
public sealed record SettingOption(string Value, string Label, string? Description = null);

/// <summary>
/// Lightweight value-only response for settings consumers that do not need descriptors.
/// </summary>
/// <param name="Values">Effective setting values keyed by stable setting key.</param>
public sealed record SettingsValuesResponse(IReadOnlyDictionary<string, JsonElement> Values);

/// <summary>
/// Request body for updating one setting value.
/// </summary>
/// <param name="Value">Raw JSON value to validate and store as an override.</param>
public sealed record SettingUpdateRequest(JsonElement Value);

/// <summary>
/// Request body for updating multiple settings in one transaction.
/// </summary>
/// <param name="Values">Raw JSON values keyed by stable setting key.</param>
public sealed record SettingsBatchUpdateRequest(IReadOnlyDictionary<string, JsonElement> Values);

/// <summary>
/// Settings page payload containing the registry catalog and watched roots.
/// </summary>
/// <param name="Settings">Current settings catalog.</param>
/// <param name="Roots">Watched media roots.</param>
public sealed record LibraryConfigResponse(
    SettingsCatalogResponse Settings,
    IReadOnlyList<LibraryRoot> Roots);
