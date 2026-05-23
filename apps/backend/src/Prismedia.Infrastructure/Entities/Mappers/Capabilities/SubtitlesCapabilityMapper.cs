using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class SubtitlesCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntitySubtitles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .ToArrayAsync(cancellationToken);
        rows = rows
            .Where(r => Path.IsPathRooted(r.StoragePath) && File.Exists(r.StoragePath))
            .ToArray();

        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilitySubtitles>();
        entity.AddCapability(new CapabilitySubtitles(rows.Select(r => new CapabilitySubtitles.Item(
            r.Id, r.Language, r.Label, r.Format, r.Source,
            r.StoragePath, r.SourceFormat, r.SourcePath, r.IsDefault)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntitySubtitles.RemoveRange(db.EntitySubtitles.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.SubtitleCapability is not { } subtitles) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var subtitle in subtitles.Items) {
            db.EntitySubtitles.Add(new EntitySubtitleRow {
                Id = subtitle.Id == Guid.Empty ? Guid.NewGuid() : subtitle.Id,
                EntityId = entity.Id,
                Language = subtitle.Language,
                Label = subtitle.Label,
                Format = subtitle.Format,
                Source = subtitle.Source,
                StoragePath = subtitle.StoragePath,
                SourceFormat = subtitle.SourceFormat,
                SourcePath = subtitle.SourcePath,
                IsDefault = subtitle.IsDefault,
                CreatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
