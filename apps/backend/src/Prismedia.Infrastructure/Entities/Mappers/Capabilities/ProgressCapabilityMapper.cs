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
            DecodeCheckpoints(row)));
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty ||
            entity.Progress is not { } progress ||
            progress.UpdatedAt is null && progress.CurrentEntityId is null && progress.Index == 0 && progress.Total == 0 && progress.Location is null && progress.Checkpoints.Count == 0) {
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
        var reading = progress.CheckpointFor(ConsumptionModality.Reading);
        row.ReadingCurrentEntityId = reading?.PositionEntityId;
        row.ReadingUnit = reading?.Unit.ToCode();
        row.ReadingIndex = reading?.Index;
        row.ReadingTotal = reading?.Total;
        row.ReadingMode = reading?.Mode?.ToCode();
        row.ReadingLocation = reading?.Location;
        row.ReadingUpdatedAt = reading?.UpdatedAt;
        var listening = progress.CheckpointFor(ConsumptionModality.Listening);
        row.ListeningTrackEntityId = listening?.PositionEntityId;
        row.ListeningMarkerId = listening?.MarkerId;
        row.ListeningOffsetSeconds = listening?.OffsetSeconds;
        row.ListeningCurrentEntityId = listening?.PositionEntityId;
        row.ListeningUnit = listening?.Unit.ToCode();
        row.ListeningIndex = listening?.Index;
        row.ListeningTotal = listening?.Total;
        row.ListeningUpdatedAt = listening?.UpdatedAt;
        row.UpdatedAt = now;
    }

    private static IEnumerable<ProgressCheckpoint> DecodeCheckpoints(UserEntityStateRow row) {
        if (row.ReadingCurrentEntityId is { } readingEntityId &&
            row.ReadingUpdatedAt is { } readingAt &&
            row.ReadingIndex is { } readingIndex &&
            row.ReadingTotal is { } readingTotal &&
            row.ReadingUnit is { } readingUnitCode &&
            readingUnitCode.TryDecodeAs<ProgressUnit>(out var readingUnit) &&
            ConsumptionModalityDefinition.Reading.Accepts(readingUnit)) {
            yield return ConsumptionModalityDefinition.Reading.Checkpoint(
                readingEntityId,
                readingUnit,
                readingIndex,
                readingTotal,
                readingAt,
                mode: row.ReadingMode is not null && row.ReadingMode.TryDecodeAs<ReaderMode>(out var mode) ? mode : null,
                location: row.ReadingLocation);
        }
        if (row.ListeningTrackEntityId is { } trackEntityId &&
            row.ListeningOffsetSeconds is { } offsetSeconds &&
            row.ListeningUpdatedAt is { } listeningAt) {
            yield return ConsumptionModalityDefinition.Listening.OffsetCheckpoint(
                trackEntityId,
                row.ListeningMarkerId,
                offsetSeconds,
                row.ListeningTotal,
                listeningAt);
        }
    }
}
