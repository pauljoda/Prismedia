using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

/// <summary>
/// Hydrates and persists the reading-progress capability against the current user's
/// <c>user_entity_states</c> row. Without an authenticated user the capability hydrates
/// empty and persists nothing.
/// </summary>
internal sealed class ProgressCapabilityMapper(PrismediaDbContext db, ICurrentUserContext currentUser) :
    IEntityCapabilityMapper,
    IEntityMutableStateMapper<CapabilityProgress> {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty) {
            return;
        }

        var row = await UserEntityStateColumns.FindAsync(db, userId, entity.Id, cancellationToken);
        if (row is null || !UserEntityStateColumns.HasProgress(row)) {
            return;
        }

        entity.RemoveCapability<CapabilityProgress>();
        // Tolerant decode: rows written before the typed vocabulary may carry legacy values
        // (e.g. mode "paginated" from earlier EPUB saves); those hydrate to the safe defaults
        // instead of failing the whole entity read.
        entity.AddCapability(new CapabilityProgress(
            row.ProgressCurrentEntityId,
            row.ProgressUnit.TryDecodeAs<ProgressUnit>(out var unit) ? unit : ProgressUnit.Item,
            row.ProgressIndex,
            row.ProgressTotal,
            row.ProgressMode is not null && row.ProgressMode.TryDecodeAs<ReaderMode>(out var mode) ? mode : null,
            row.ProgressCompletedAt,
            row.ProgressUpdatedAt ?? row.UpdatedAt,
            row.ProgressLocation,
            row.ProgressConsumedCount,
            DecodeReading(row, entity.Kind),
            DecodeListening(row)));
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty ||
            entity.Progress is not { } progress ||
            progress.UpdatedAt is null && progress.CurrentEntityId is null && progress.Index == 0 && progress.Total == 0 && progress.Location is null && progress.Reading is null && progress.Listening is null) {
            return;
        }

        var row = await UserEntityStateColumns.GetOrAddAsync(db, userId, entity.Id, cancellationToken);
        row.ProgressCurrentEntityId = progress.CurrentEntityId;
        row.ProgressUnit = progress.Unit.ToCode();
        row.ProgressIndex = progress.Index;
        row.ProgressTotal = progress.Total;
        row.ProgressMode = progress.Mode?.ToCode();
        row.ProgressLocation = progress.Location;
        row.ProgressCompletedAt = progress.CompletedAt;
        var now = DateTimeOffset.UtcNow;
        row.ProgressUpdatedAt = progress.UpdatedAt ?? row.ProgressUpdatedAt ?? now;
        row.ProgressConsumedCount = progress.ConsumedCount;
        row.ReadingCurrentEntityId = progress.Reading?.CurrentEntityId;
        row.ReadingUnit = progress.Reading?.Unit.ToCode();
        row.ReadingIndex = progress.Reading?.Index;
        row.ReadingTotal = progress.Reading?.Total;
        row.ReadingMode = progress.Reading?.Mode?.ToCode();
        row.ReadingLocation = progress.Reading?.Location;
        row.ReadingUpdatedAt = progress.Reading?.UpdatedAt;
        row.ListeningTrackEntityId = progress.Listening?.TrackEntityId;
        row.ListeningMarkerId = progress.Listening?.MarkerId;
        row.ListeningOffsetSeconds = progress.Listening?.OffsetSeconds;
        row.ListeningCurrentEntityId = progress.Listening?.CurrentEntityId;
        row.ListeningUnit = progress.Listening?.Unit.ToCode();
        row.ListeningIndex = progress.Listening?.Index;
        row.ListeningTotal = progress.Listening?.Total;
        row.ListeningUpdatedAt = progress.Listening?.UpdatedAt;
        row.UpdatedAt = now;
    }

    private static BookReadingCheckpoint? DecodeReading(UserEntityStateRow row, EntityKind kind) {
        if (row.ReadingCurrentEntityId is { } currentEntityId &&
            row.ReadingUpdatedAt is { } updatedAt &&
            row.ReadingIndex is { } index &&
            row.ReadingTotal is { } total &&
            row.ReadingUnit is { } unitCode &&
            unitCode.TryDecodeAs<ProgressUnit>(out var unit)) {
            return new BookReadingCheckpoint(
                currentEntityId,
                unit,
                index,
                total,
                row.ReadingMode is not null && row.ReadingMode.TryDecodeAs<ReaderMode>(out var mode) ? mode : null,
                row.ReadingLocation,
                updatedAt);
        }

        // Older Book rows contain one cursor. Preserve it as the first readable checkpoint
        // before a later listening heartbeat replaces the work's last-used cursor.
        if (kind == EntityKind.Book && row.ListeningUpdatedAt is null &&
            row.ProgressCurrentEntityId is { } legacyEntityId &&
            (row.ProgressUpdatedAt ?? row.UpdatedAt) is { } legacyUpdatedAt) {
            return new BookReadingCheckpoint(
                legacyEntityId,
                row.ProgressUnit.TryDecodeAs<ProgressUnit>(out var legacyUnit) ? legacyUnit : ProgressUnit.Item,
                row.ProgressIndex,
                row.ProgressTotal,
                row.ProgressMode is not null && row.ProgressMode.TryDecodeAs<ReaderMode>(out var legacyMode) ? legacyMode : null,
                row.ProgressLocation,
                legacyUpdatedAt);
        }
        return null;
    }

    private static BookListeningCheckpoint? DecodeListening(UserEntityStateRow row) {
        if (row.ListeningTrackEntityId is not { } trackEntityId ||
            row.ListeningCurrentEntityId is not { } currentEntityId ||
            row.ListeningOffsetSeconds is not { } offsetSeconds ||
            row.ListeningIndex is not { } index ||
            row.ListeningTotal is not { } total ||
            row.ListeningUpdatedAt is not { } updatedAt ||
            row.ListeningUnit is not { } unitCode ||
            !unitCode.TryDecodeAs<ProgressUnit>(out var unit)) {
            return null;
        }
        return new BookListeningCheckpoint(
            trackEntityId,
            row.ListeningMarkerId,
            offsetSeconds,
            currentEntityId,
            unit,
            index,
            total,
            updatedAt);
    }
}
