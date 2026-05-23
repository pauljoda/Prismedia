using Prismedia.Contracts.Settings;

namespace Prismedia.Application.Settings;

/// <summary>
/// Application port for settings persistence. Implementation lives in Infrastructure and is
/// responsible for reading and writing library settings and watched library roots. The
/// orchestration tier in <see cref="SettingsService"/> owns validation, clamping, default
/// derivation, and any non-persistence logic.
/// </summary>
public interface ISettingsPersistence {
    /// <summary>
    /// Loads the singleton library settings row, creating it with defaults when none exists.
    /// </summary>
    Task<LibrarySettings> GetLibrarySettingsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the full library settings state. Implementations are expected to overwrite
    /// every modifiable column from the supplied state and update the timestamp.
    /// </summary>
    Task<LibrarySettings> SaveLibrarySettingsAsync(LibrarySettings state, CancellationToken cancellationToken);

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
