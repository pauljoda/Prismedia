using Microsoft.EntityFrameworkCore;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Plugins;

public sealed partial class EntityMetadataApplyService {
    /// <summary>
    /// Legacy patch-boundary alias accepted for Movie entities in addition to canonical
    /// <see cref="EntityKind"/> codes. Nothing in the app produces it today; it is tolerated
    /// for older clients/plugins that addressed movies by this token. prism-vocab: external
    /// </summary>
    private const string LegacyMovieExpectedKind = "video-movie";

    /// <summary>
    /// Whether an entity of <paramref name="entityKind"/> may be patched through a route or
    /// proposal addressed at <paramref name="expectedKind"/>. Beyond exact kind matches, a
    /// Movie accepts the generic video code (and the legacy movie alias), and a Video accepts
    /// the identify flow's provider-only leaf-episode token (<see cref="ProposalKind.VideoEpisode"/>).
    /// </summary>
    private static bool IsKindCompatible(string entityKind, string expectedKind) =>
        entityKind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase) ||
        (entityKind.Equals(EntityKindRegistry.Movie.Code, StringComparison.OrdinalIgnoreCase) &&
            (expectedKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) ||
             expectedKind.Equals(LegacyMovieExpectedKind, StringComparison.OrdinalIgnoreCase))) ||
        (entityKind.Equals(EntityKindRegistry.Video.Code, StringComparison.OrdinalIgnoreCase) &&
            expectedKind.Equals(ProposalKind.VideoEpisode.ToCode(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves an existing entity for a proposal using one consistent rule across the whole apply
    /// walk: a provider's stable external id is the strongest match and is tried first, then a
    /// normalized case-insensitive title match. <paramref name="parentEntityId"/> scopes the match to
    /// one parent's children (structural children), or is null for parent-agnostic taxonomy
    /// (people/studios/tags). Returns null when nothing matches so the caller can create the entity.
    /// </summary>
    private async Task<EntityRow?> FindEntityAsync(
        string kindCode,
        IReadOnlyDictionary<string, string>? externalIds,
        string? title,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        if (externalIds is { Count: > 0 }) {
            var byExternalId = await FindEntityByExternalIdsAsync(kindCode, externalIds, parentEntityId, cancellationToken);
            if (byExternalId is not null) {
                return byExternalId;
            }
        }

        return string.IsNullOrWhiteSpace(title)
            ? null
            : await FindEntityByTitleAsync(kindCode, title, parentEntityId, cancellationToken);
    }

    private async Task<EntityRow?> FindEntityByExternalIdsAsync(
        string kindCode,
        IReadOnlyDictionary<string, string> externalIds,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        foreach (var (provider, value) in externalIds) {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            var trimmedProvider = provider.Trim();
            var trimmedValue = value.Trim();
            var entity = await _db.EntityExternalIds
                .Where(row => row.Provider == trimmedProvider && row.Value == trimmedValue)
                .Join(_db.Entities, externalId => externalId.EntityId, candidate => candidate.Id, (_, candidate) => candidate)
                .FirstOrDefaultAsync(
                    candidate => candidate.KindCode == kindCode &&
                        (parentEntityId == null || candidate.ParentEntityId == parentEntityId),
                    cancellationToken);
            if (entity is not null) {
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// Matches an existing entity by kind and case-insensitive title (optionally parent-scoped). The
    /// change-tracked (Local) lookup and the database lookup use the SAME ToLower comparison, so an
    /// entity created earlier in this apply matches the same way a persisted row does — closing the gap
    /// where a Local Ordinal compare and a DB ToLower compare could disagree and create a duplicate.
    /// </summary>
    private async Task<EntityRow?> FindEntityByTitleAsync(
        string kindCode,
        string title,
        Guid? parentEntityId,
        CancellationToken cancellationToken) {
        var lowered = title.Trim().ToLower();
        return _db.Entities.Local.FirstOrDefault(row =>
                row.KindCode == kindCode &&
                (parentEntityId == null || row.ParentEntityId == parentEntityId) &&
                row.Title.ToLower() == lowered)
            ?? await _db.Entities.FirstOrDefaultAsync(row =>
                row.KindCode == kindCode &&
                (parentEntityId == null || row.ParentEntityId == parentEntityId) &&
                row.Title.ToLower() == lowered,
                cancellationToken);
    }

    private EntityRow CreateEntity(string kind, string title, DateTimeOffset now) {
        var entity = new EntityRow {
            Id = Guid.NewGuid(),
            KindCode = kind,
            Title = title,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Entities.Add(entity);
        return entity;
    }
}
