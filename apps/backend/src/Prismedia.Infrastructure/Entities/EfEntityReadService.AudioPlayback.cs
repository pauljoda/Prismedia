using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Playback;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media.Books;

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
                entity.CreatedAt,
                Array.Empty<AudioPlaybackChapter>()))
            .ToArrayAsync(cancellationToken);

        var chapters = await LoadEmbeddedChaptersAsync(rows, cancellationToken);
        var byId = rows.ToDictionary(item => item.Id);
        return ids
            .Where(byId.ContainsKey)
            .Select(id => chapters.TryGetValue(id, out var itemChapters)
                ? byId[id] with { Chapters = itemChapters }
                : byId[id])
            .ToArray();
    }

    /// <summary>
    /// Embedded chapters of the given items, windowed by the same rule the audiobook alignment uses.
    /// Only source-owned markers imported from the container count; user timeline markers never split
    /// an item.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AudioPlaybackChapter>>> LoadEmbeddedChaptersAsync(
        IReadOnlyList<AudioPlaybackItem> items,
        CancellationToken cancellationToken) {
        var itemIds = items.Select(item => item.Id).ToArray();
        var markers = (await _db.EntityMarkers.AsNoTracking()
                .Where(marker => itemIds.Contains(marker.EntityId) && marker.SourceIndex != null)
                .Select(marker => new {
                    marker.EntityId,
                    marker.Id,
                    marker.Title,
                    marker.Seconds,
                    marker.EndSeconds,
                    marker.Untitled
                })
                .ToArrayAsync(cancellationToken))
            .ToLookup(
                marker => marker.EntityId,
                marker => new SourceChapterMarker(marker.Id, marker.Title, marker.Seconds, marker.EndSeconds, marker.Untitled));

        return items
            .Where(item => markers.Contains(item.Id))
            .ToDictionary(
                item => item.Id,
                IReadOnlyList<AudioPlaybackChapter> (item) => new AudioTrackSpan(
                        item.Id,
                        item.Title,
                        item.DurationSeconds,
                        markers[item.Id].ToArray())
                    .ChapterWindows()
                    .Where(window => window.MarkerId is not null)
                    .Select(window => new AudioPlaybackChapter(
                        window.MarkerId!.Value,
                        window.Title,
                        window.StartSeconds,
                        window.EndSeconds))
                    .ToArray());
    }
}
