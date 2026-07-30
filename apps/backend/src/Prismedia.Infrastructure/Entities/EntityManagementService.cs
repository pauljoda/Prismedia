using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// EF-backed implementation of <see cref="IEntityManagementService"/>. Owns row-level create and
/// delete for the user-managed taxonomy kinds, keeping endpoints thin.
/// </summary>
public sealed class EntityManagementService(PrismediaDbContext db) : IEntityManagementService {
    /// <summary>Kind codes a user may create and delete by hand from the taxonomy grids.</summary>
    private static readonly HashSet<string> ManageableKindCodes = new(StringComparer.OrdinalIgnoreCase) {
        EntityKind.Tag.ToCode(),
        EntityKind.Person.ToCode(),
        EntityKind.Studio.ToCode(),
    };

    /// <summary>Whether the given stable kind code is a user-manageable taxonomy kind.</summary>
    public static bool IsManageableKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && ManageableKindCodes.Contains(kind);

    /// <inheritdoc />
    public async Task<EntityCreateResult> CreateAsync(
        string kind,
        EntityCreateRequest request,
        CancellationToken cancellationToken) {
        if (!IsManageableKind(kind)) {
            return new EntityCreateResult(EntityCommandStatus.KindNotManageable);
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title)) {
            return new EntityCreateResult(EntityCommandStatus.Invalid, Message: "A title is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Entities.Add(new EntityRow {
            Id = id,
            KindCode = kind,
            Title = title,
            IsNsfw = request.IsNsfw,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new EntityCreateResult(EntityCommandStatus.Created, id);
    }

    /// <inheritdoc />
    public async Task<EntityDeleteResult> DeleteAsync(
        Guid id,
        string kind,
        CancellationToken cancellationToken) {
        if (!IsManageableKind(kind)) {
            return new EntityDeleteResult(EntityCommandStatus.KindNotManageable);
        }

        var entity = await db.Entities.FirstOrDefaultAsync(
            row => row.Id == id && row.KindCode == kind,
            cancellationToken);
        if (entity is null) {
            return new EntityDeleteResult(EntityCommandStatus.NotFound);
        }

        // Hard-delete the row. The relationship-link foreign keys cascade on both entity_id and
        // target_entity_id, so every link to or from this tag/person/studio is removed with it,
        // leaving the previously referenced media in place but unlinked.
        db.Entities.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new EntityDeleteResult(EntityCommandStatus.Deleted);
    }
}
