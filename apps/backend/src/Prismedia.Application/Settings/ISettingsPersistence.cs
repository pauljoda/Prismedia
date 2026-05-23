using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Application port for settings persistence. Implementations store raw app-setting
/// overrides and watched library roots; <see cref="SettingsService"/> owns registry
/// validation, default derivation, and typed snapshots.
/// </summary>
public interface ISettingsPersistence {
    /// <summary>
    /// Loads every persisted app-setting override keyed by setting key.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves one normalized setting override as raw JSON.
    /// </summary>
    Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken);

    /// <summary>
    /// Saves several normalized setting overrides as raw JSON in one persistence operation.
    /// </summary>
    Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one persisted setting override. Missing overrides are ignored.
    /// </summary>
    Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every watched library root in stable display order.
    /// </summary>
    Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads one watched library root by identifier.
    /// </summary>
    Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new watched library root and returns the persisted state.
    /// </summary>
    Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes to an existing watched library root and returns the updated state.
    /// </summary>
    Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a watched library root. Returns true when a root was deleted.
    /// </summary>
    Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken);
}
