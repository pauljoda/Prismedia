using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Infrastructure.Integrations;

/// <summary>Maps tracked source entities to the canonical work that owns a Book rendition.</summary>
internal sealed class ManagedReservationScopeResolver(PrismediaDbContext db) {
    internal async Task<Guid[]> ResolveAsync(
        IReadOnlyList<Guid> sourceEntityIds, ManagedItemInput item, CancellationToken token) {
        var ids = sourceEntityIds.Distinct().ToArray();
        if (item.EntityKind != EntityKind.Book) return ids;
        if (item.BookRendition is null || ids.Length == 0)
            throw new ArgumentException("A connected book holding requires one exact rendition and local work.");
        var sources = await db.Entities.AsNoTracking().Where(entity => ids.Contains(entity.Id))
            .Select(entity => new { entity.Id, entity.KindCode, entity.ParentEntityId }).ToArrayAsync(token);
        if (sources.Length != ids.Length) throw new ArgumentException("A selected book source no longer exists.");
        if (item.BookRendition == BookRendition.Ebook) {
            if (sources.Length != 1 || sources[0].KindCode != EntityKind.Book.ToCode())
                throw new ArgumentException("Select the readable file on one Book work.");
            return [sources[0].Id];
        }
        if (item.BookRendition != BookRendition.Audiobook || sources.Any(source =>
                source.KindCode != EntityKind.AudioTrack.ToCode() || source.ParentEntityId is null))
            throw new ArgumentException("Select audio tracks beneath one Book work.");
        var bookIds = sources.Select(source => source.ParentEntityId!.Value).Distinct().ToArray();
        if (bookIds.Length != 1 || !await db.Entities.AsNoTracking().AnyAsync(entity =>
                entity.Id == bookIds[0] && entity.KindCode == EntityKind.Book.ToCode(), token))
            throw new ArgumentException("The selected audio tracks no longer belong to one Book work.");
        return bookIds;
    }
}
