using Prismedia.Application.Security;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

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
            row.ProgressLocation));
    }

    public async Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty ||
            entity.Progress is not { } progress ||
            progress.UpdatedAt is null && progress.CurrentEntityId is null && progress.Index == 0 && progress.Total == 0 && progress.Location is null) {
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
        row.UpdatedAt = now;
    }
}
