using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>Reads current work identity and canonical positions for persisted and transient acquisition searches.</summary>
internal sealed class EfAcquisitionWorkContext(PrismediaDbContext db) {
    /// <summary>
    /// Uses the library's current ordering after metadata repair, with request-time values retained by
    /// callers only where the library has no position. Search and import must agree on the same target;
    /// an old request must not keep downloading or placing the episode it was mistakenly created for.
    /// </summary>
    public async Task<(int? Season, int? Episode, int? Volume, int? AbsoluteEpisode)> ReadPositionsAsync(
        Guid? entityId,
        EntityKind kind,
        CancellationToken cancellationToken) {
        if (entityId is not { } id || kind is not (EntityKind.VideoSeason or EntityKind.VideoEpisode or EntityKind.Book)) {
            return default;
        }

        var entity = await db.Entities.AsNoTracking()
            .Where(entity => entity.Id == id && entity.KindCode == kind.ToCode())
            .Select(entity => new { entity.ParentEntityId })
            .FirstOrDefaultAsync(cancellationToken);
        if (entity is null) {
            return default;
        }

        var positions = await db.EntityPositions.AsNoTracking()
            .Where(position => position.EntityId == id
                || (kind == EntityKind.VideoEpisode
                    && position.EntityId == entity.ParentEntityId
                    && position.Code == EntityPositionCodes.Season))
            .Select(position => new { position.EntityId, position.Code, position.Value })
            .ToArrayAsync(cancellationToken);
        int? Own(string code) => positions.FirstOrDefault(position => position.EntityId == id && position.Code == code)?.Value;
        var season = kind is EntityKind.VideoSeason or EntityKind.VideoEpisode
            ? Own(EntityPositionCodes.Season)
                ?? positions.FirstOrDefault(position => position.EntityId == entity.ParentEntityId && position.Code == EntityPositionCodes.Season)?.Value
            : null;
        return (
            season,
            kind == EntityKind.VideoEpisode ? Own(EntityPositionCodes.Episode) : null,
            kind == EntityKind.Book ? Own(EntityPositionCodes.Volume) : null,
            kind == EntityKind.VideoEpisode ? Own(EntityPositionCodes.AbsoluteEpisode) : null);
    }

    /// <summary>
    /// The year identity of the work an entity belongs to: the first ancestor that owns an acquisition
    /// profile, within a cycle-safe ancestor walk, together with its current provider's formal titles.
    /// A missing year preserves request-time fallback; missing or retired identity evidence supplies no alternatives.
    /// </summary>
    public async Task<(int? Year, IReadOnlyList<string> Titles)> ReadIdentityAsync(Guid entityId, CancellationToken cancellationToken) {
        var currentId = (Guid?)entityId;
        var workId = entityId;
        AcquisitionProfileDefinition? workProfile = null;
        var visited = new HashSet<Guid>();
        while (currentId is { } id && visited.Add(id)) {
            var current = await db.Entities.AsNoTracking()
                .Where(row => row.Id == id)
                .Select(row => new { row.KindCode, row.ParentEntityId })
                .FirstOrDefaultAsync(cancellationToken);
            if (current is null) {
                break;
            }

            if (EntityKindRegistry.TryDescribe(current.KindCode, out var definition)
                && definition.AcquisitionProfile is { } acquisitionProfile) {
                workId = id;
                workProfile = acquisitionProfile;
                break;
            }

            currentId = current.ParentEntityId;
        }

        if (workProfile is null) {
            return (null, []);
        }

        var dates = await db.EntityDates.AsNoTracking()
            .Where(date => date.EntityId == workId && date.SortableValue != null)
            .Select(date => new { date.Code, date.SortableValue })
            .ToArrayAsync(cancellationToken);
        int? year = null;
        foreach (var dateType in workProfile.SupportedReleaseDateTypes) {
            var canonicalCode = dateType.ToCode();
            var match = dates.FirstOrDefault(date => date.Code == canonicalCode)
                ?? dates.FirstOrDefault(date => EntityDateTypeRegistry.Decode(date.Code) == dateType);
            if (match?.SortableValue is { } sortable) {
                year = sortable.Year;
                break;
            }
        }

        var titles = await EfAcquisitionWorkTitles.ReadAsync(db, workId, cancellationToken);
        return (year, titles);
    }

}
