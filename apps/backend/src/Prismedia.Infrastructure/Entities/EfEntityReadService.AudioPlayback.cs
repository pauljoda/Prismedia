using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Entities;

public sealed partial class EfEntityReadService {
    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioPlaybackItem>> GetAudioPlaybackItemsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (ids.Count == 0) {
            return [];
        }

        var playableKindCodes = EntityKindRegistry.All
            .OfType<IPlayableAudioKindDefinition>()
            .Select(definition => definition.Kind.ToCode())
            .ToArray();
        var sourceRole = EntityFileRole.Source;
        var query = _db.Entities.AsNoTracking()
            .Where(entity => ids.Contains(entity.Id) &&
                playableKindCodes.Contains(entity.KindCode) &&
                !entity.IsWanted &&
                _db.EntityFiles.Any(file => file.EntityId == entity.Id && file.Role == sourceRole));
        query = ApplyCollectionVisibility(query);
        if (await RequiresLibraryVisibilityAsync(cancellationToken)) {
            query = ApplyEnabledLibraryVisibility(query);
        }
        query = ApplyNsfwVisibility(query, hideNsfw);

        var waveformRole = EntityFileRole.Waveform;
        var rows = await (
            from entity in query
            join technical in _db.EntityTechnical.AsNoTracking()
                on entity.Id equals technical.EntityId into technicalRows
            from technical in technicalRows.DefaultIfEmpty()
            join detail in _db.AudioTrackDetails.AsNoTracking()
                on entity.Id equals detail.EntityId into detailRows
            from detail in detailRows.DefaultIfEmpty()
            join userState in _db.UserEntityStates.AsNoTracking().Where(state => state.UserId == CurrentUserId)
                on entity.Id equals userState.EntityId into userStateRows
            from userState in userStateRows.DefaultIfEmpty()
            select new AudioPlaybackItem(
                entity.Id,
                entity.Title,
                entity.ParentEntityId,
                entity.SortOrder,
                entity.IsNsfw,
                entity.IsOrganized,
                entity.IsWanted,
                _db.EntityFiles.Any(file => file.EntityId == entity.Id && file.Role == sourceRole),
                technical == null ? null : technical.DurationSeconds,
                technical == null ? null : technical.BitRate,
                technical == null ? null : technical.SampleRate,
                technical == null ? null : technical.Channels,
                technical == null ? null : technical.Codec,
                detail == null ? null : detail.EmbeddedArtist,
                detail == null ? null : detail.EmbeddedAlbum,
                detail == null ? null : detail.SectionLabel,
                _db.EntityFiles
                    .Where(file => file.EntityId == entity.Id && file.Role == waveformRole)
                    .OrderBy(file => file.CreatedAt)
                    .Select(file => file.Path)
                    .FirstOrDefault(),
                userState == null ? null : userState.RatingValue,
                userState == null ? 0 : userState.AccessCount,
                userState == null ? null : userState.LastActiveAt,
                entity.CreatedAt))
            .ToArrayAsync(cancellationToken);

        var byId = rows.ToDictionary(item => item.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }
}
