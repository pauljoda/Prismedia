using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities.Mappers.Capabilities;

internal sealed class FilesCapabilityMapper(PrismediaDbContext db) : IEntityCapabilityMapper {
    public async Task HydrateAsync(Entity entity, CancellationToken cancellationToken) {
        var rows = await db.EntityFiles.AsNoTracking()
            .Where(r => r.EntityId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) {
            return;
        }

        entity.RemoveCapability<CapabilityFiles>();
        entity.AddCapability(new CapabilityFiles(
            rows.Select(r => new CapabilityFiles.Item(r.Role, r.Path, r.MimeType)).ToArray()));
    }

    public Task ClearAsync(Entity entity, CancellationToken cancellationToken) {
        db.EntityFiles.RemoveRange(db.EntityFiles.Where(r => r.EntityId == entity.Id));
        return Task.CompletedTask;
    }

    public Task PersistAsync(Entity entity, CancellationToken cancellationToken) {
        if (entity.Files is not { } files) {
            return Task.CompletedTask;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var file in files.Items) {
            db.EntityFiles.Add(new EntityFileRow {
                Id = Guid.NewGuid(),
                EntityId = entity.Id,
                Role = file.Role,
                Path = file.Path,
                MimeType = file.MimeType,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return Task.CompletedTask;
    }
}
