namespace Prismedia.Application.UserState;

/// <summary>
/// Application port for browser user-state persistence (UI shell preferences keyed by string).
/// Implementations live in Infrastructure and use the <c>ui_preferences</c> EF Core table as
/// the underlying store.
/// </summary>
public interface IUserStatePersistence {
    /// <summary>
    /// Loads the raw JSON value stored under <paramref name="key"/>. Returns null when no row
    /// exists or the stored value is empty.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the raw JSON value stored under <paramref name="key"/>.
    /// </summary>
    Task SaveAsync(string key, string valueJson, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the value stored under <paramref name="key"/>. No-op when no row exists.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
