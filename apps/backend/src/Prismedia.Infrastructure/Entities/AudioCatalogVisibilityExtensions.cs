using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Entities;

/// <summary>
/// Shared catalog policy that keeps Book-owned audiobook parts out of music/audio
/// projections while leaving the Book's direct-child playback graph intact.
/// </summary>
internal static class AudioCatalogVisibilityExtensions {
    internal static IQueryable<EntityRow> ExcludeBookOwnedAudioTracks(
        this IQueryable<EntityRow> query,
        IQueryable<EntityRow> entities) =>
        query.Where(entity =>
            entity.KindCode != EntityKindRegistry.AudioTrack.Code ||
            entity.ParentEntityId == null ||
            !entities.Any(parent =>
                parent.Id == entity.ParentEntityId &&
                parent.KindCode == EntityKindRegistry.Book.Code));
}
