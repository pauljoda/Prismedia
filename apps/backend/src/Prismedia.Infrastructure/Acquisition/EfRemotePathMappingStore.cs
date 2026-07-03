using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Acquisition;

/// <summary>EF-backed store for remote path mappings, served longest-remote-prefix-first so the mapper's first hit wins.</summary>
public sealed class EfRemotePathMappingStore(PrismediaDbContext db) : IRemotePathMappingStore {
    public async Task<IReadOnlyList<RemotePathMappingView>> ListAsync(CancellationToken cancellationToken) {
        var rows = await db.RemotePathMappings.AsNoTracking()
            .OrderBy(row => row.RemotePath)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToView).ToArray();
    }

    public async Task<IReadOnlyList<RemotePathMappingView>> ListForClientAsync(Guid downloadClientConfigId, CancellationToken cancellationToken) {
        var rows = await db.RemotePathMappings.AsNoTracking()
            .Where(row => row.DownloadClientConfigId == downloadClientConfigId)
            .ToArrayAsync(cancellationToken);
        return rows
            .OrderByDescending(row => row.RemotePath.Length)
            .Select(ToView)
            .ToArray();
    }

    public async Task<RemotePathMappingView> SaveAsync(RemotePathMappingSaveRequest request, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = request.Id is { } id
            ? await db.RemotePathMappings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            : null;

        if (row is null) {
            row = new RemotePathMappingRow {
                Id = request.Id ?? Guid.NewGuid(),
                CreatedAt = now
            };
            db.RemotePathMappings.Add(row);
        }

        row.DownloadClientConfigId = request.DownloadClientConfigId;
        row.RemotePath = request.RemotePath.Trim();
        row.LocalPath = request.LocalPath.Trim();
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) {
        var row = await db.RemotePathMappings.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null) {
            return false;
        }

        db.RemotePathMappings.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static RemotePathMappingView ToView(RemotePathMappingRow row) =>
        new(row.Id, row.DownloadClientConfigId, row.RemotePath, row.LocalPath);
}
