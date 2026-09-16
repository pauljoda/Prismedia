using Microsoft.EntityFrameworkCore;
using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Infrastructure.Integrations;

public sealed partial class EfManagedTrackingStore {
    /// <inheritdoc />
    public async Task<ManagedTrackingObservation> ObserveAsync(Guid connectionId, ManagedItemSnapshot snapshot, CancellationToken token) {
        var mapped = await mounts.InspectAsync(connectionId, snapshot.Files, token);
        var roots = mapped.Select(file => file.LibraryRootId).Distinct().ToArray();
        var observed = snapshot.Files.Select(file => {
            var local = mapped.Single(item => item.RemoteId == file.RemoteId);
            var readable = local.IsReadable && local.SizeMatches && local.LocalPath is not null;
            var writtenAt = readable ? WrittenAt(local.LocalPath!) : DateTimeOffset.MinValue;
            return new ManagedObservedFile(file.RemoteId, local.LocalPath ?? string.Empty, file.SizeBytes, writtenAt, readable,
                file.Targets.Select(target => new ManagedTargetIdentity(target.RemoteId, target.EntityKind,
                    target.SeasonNumber, target.EpisodeNumber, target.AbsoluteNumber)).ToArray());
        }).ToArray();
        return new(roots.Length == 1 ? roots[0] : null, observed, await SourcesAsync(observed.Select(file => file.LocalPath).ToArray(), token));
    }

    private async Task<IReadOnlyList<ManagedLocalSource>> SourcesAsync(IReadOnlyList<string> paths, CancellationToken token) {
        var owners = await (from file in db.EntityFiles.AsNoTracking()
                            join entity in db.Entities.AsNoTracking() on file.EntityId equals entity.Id
                            where paths.Contains(file.Path) && (file.Role == EntityFileRole.Source || file.Role == EntityFileRole.UnavailableSource)
                            select new { file.Id, file.Path, file.EntityId, entity.KindCode, entity.ParentEntityId }).ToArrayAsync(token);
        var ids = owners.Select(owner => owner.EntityId).Concat(owners.Where(owner => owner.ParentEntityId != null).Select(owner => owner.ParentEntityId!.Value)).Distinct().ToArray();
        var positions = await db.EntityPositions.AsNoTracking().Where(position => ids.Contains(position.EntityId)).ToArrayAsync(token);
        return owners.Select(owner => {
            int? Own(string code) => positions.FirstOrDefault(position => position.EntityId == owner.EntityId && position.Code == code)?.Value;
            var season = Own(EntityPositionCodes.Season) ?? positions.FirstOrDefault(position => position.EntityId == owner.ParentEntityId && position.Code == EntityPositionCodes.Season)?.Value;
            return new ManagedLocalSource(owner.EntityId, owner.Id, owner.Path, EntityKindRegistry.Require(owner.KindCode),
                season, Own(EntityPositionCodes.Episode), Own(EntityPositionCodes.AbsoluteEpisode));
        }).ToArray();
    }

    private static void VerifyUnchangedBytes(ManagedObservedFile file) {
        if (!file.IsReadable) return;
        try {
            using var stream = new FileStream(file.LocalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != file.SizeBytes || WrittenAt(file.LocalPath) != file.WrittenAt)
                throw new ArgumentException("A mapped file changed during verification. Refresh its evidence.");
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            throw new ArgumentException("A mapped file changed or became unreadable during verification. Refresh its evidence.");
        }
    }

    // PostgreSQL timestamps retain microseconds; compare the same precision before and after persistence.
    private static DateTimeOffset WrittenAt(string path) {
        var ticks = File.GetLastWriteTimeUtc(path).Ticks;
        return new(ticks - ticks % 10, TimeSpan.Zero);
    }
}
