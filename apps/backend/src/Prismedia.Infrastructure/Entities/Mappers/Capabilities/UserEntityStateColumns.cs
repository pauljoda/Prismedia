using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Shared helpers over the wide <see cref="UserEntityStateRow"/>: which column families
    /// a row actually carries (consumption vs reading progress vs opinion flags), and the
/// find-or-add upsert both capability mappers use.
/// </summary>
internal static class UserEntityStateColumns {
    /// <summary>True when the row records any entity consumption.</summary>
    internal static bool HasConsumption(UserEntityStateRow row) =>
        row.AccessCount > 0 ||
        row.CompletionCount > 0 ||
        row.SkipCount > 0 ||
        row.ActiveSeconds > 0 ||
        row.ResumeSeconds > 0 ||
        row.LastAccessedAt is not null ||
        row.LastActiveAt is not null ||
        row.CompletedAt is not null;

    /// <summary>True when the row records any reading progress (books/comics).</summary>
    internal static bool HasProgress(UserEntityStateRow row) =>
        row.ProgressCurrentEntityId is not null ||
        row.ProgressIndex != 0 ||
        row.ProgressTotal != 0 ||
        row.ProgressLocation is not null ||
        row.ProgressCompletedAt is not null ||
        row.ProgressUpdatedAt is not null ||
        row.ProgressConsumedCount > 0;

    /// <summary>
    /// Finds the state row through EF's identity map. Hydration deliberately keeps this row
    /// tracked until save so PostgreSQL's xmin concurrency token remains the version that was
    /// observed while the domain capability was built.
    /// </summary>
    internal static Task<UserEntityStateRow?> FindAsync(
        PrismediaDbContext db,
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken) =>
        db.UserEntityStates.FindAsync([userId, entityId], cancellationToken).AsTask();

    /// <summary>
    /// Finds the (user, entity) state row — preferring one already tracked in this unit of
    /// work so both mappers and the repository compose their writes — or adds a fresh one.
    /// </summary>
    internal static async Task<UserEntityStateRow> GetOrAddAsync(
        PrismediaDbContext db,
        Guid userId,
        Guid entityId,
        CancellationToken cancellationToken) {
        var row = await FindAsync(db, userId, entityId, cancellationToken);
        if (row is not null) {
            return row;
        }

        row = new UserEntityStateRow {
            UserId = userId,
            EntityId = entityId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.UserEntityStates.Add(row);
        return row;
    }
}
