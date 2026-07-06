using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Security;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Security;

/// <summary>EF-backed store for per-user library access grants.</summary>
public sealed class EfLibraryAccessReader : ILibraryAccessStore {
    private readonly PrismediaDbContext _db;

    public EfLibraryAccessReader(PrismediaDbContext db) {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> GetAllowedRootIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await _db.UserLibraryAccess.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.LibraryRootId)
            .ToArrayAsync(cancellationToken))
        .ToHashSet();

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByRootAsync(CancellationToken cancellationToken) =>
        (await _db.UserLibraryAccess.AsNoTracking().ToArrayAsync(cancellationToken))
        .GroupBy(row => row.LibraryRootId)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<Guid>)group.Select(row => row.UserId).ToArray());

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByUserAsync(CancellationToken cancellationToken) =>
        (await _db.UserLibraryAccess.AsNoTracking().ToArrayAsync(cancellationToken))
        .GroupBy(row => row.UserId)
        .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<Guid>)group.Select(row => row.LibraryRootId).ToArray());

    /// <inheritdoc />
    public async Task ReplaceRootAccessAsync(
        Guid libraryRootId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) {
        var existing = await _db.UserLibraryAccess
            .Where(row => row.LibraryRootId == libraryRootId)
            .ToArrayAsync(cancellationToken);
        _db.UserLibraryAccess.RemoveRange(existing.Where(row => !userIds.Contains(row.UserId)));
        AddMissing(userIds.Except(existing.Select(row => row.UserId)), userId => (userId, libraryRootId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReplaceUserAccessAsync(
        Guid userId,
        IReadOnlyCollection<Guid> libraryRootIds,
        CancellationToken cancellationToken) {
        var existing = await _db.UserLibraryAccess
            .Where(row => row.UserId == userId)
            .ToArrayAsync(cancellationToken);
        _db.UserLibraryAccess.RemoveRange(existing.Where(row => !libraryRootIds.Contains(row.LibraryRootId)));
        AddMissing(libraryRootIds.Except(existing.Select(row => row.LibraryRootId)), rootId => (userId, rootId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task GrantRootAccessAsync(
        Guid libraryRootId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) {
        var existing = await _db.UserLibraryAccess.AsNoTracking()
            .Where(row => row.LibraryRootId == libraryRootId)
            .Select(row => row.UserId)
            .ToArrayAsync(cancellationToken);
        AddMissing(userIds.Except(existing), userId => (userId, libraryRootId));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void AddMissing(IEnumerable<Guid> ids, Func<Guid, (Guid UserId, Guid RootId)> map) {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in ids) {
            var (userId, rootId) = map(id);
            _db.UserLibraryAccess.Add(new UserLibraryAccessRow {
                UserId = userId,
                LibraryRootId = rootId,
                CreatedAt = now
            });
        }
    }
}
