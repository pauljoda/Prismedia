using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

/// <summary>
/// Neutral Entity read-service test double. Endpoint tests override only the operation they exercise,
/// keeping new contract members and unrelated read behavior out of each focused test fixture.
/// </summary>
internal abstract class EntityReadServiceStub : IEntityReadService {
    public virtual Task<EntityListResponse> ListAsync(
        string? kind,
        string? query,
        string? cursor,
        bool? hideNsfw,
        int? limit,
        CancellationToken cancellationToken,
        Guid? referencedBy = null,
        string? relationshipCode = null,
        EntityListSort? sort = null,
        EntitySortDirection? sortDirection = null,
        int? seed = null,
        bool? favorite = null,
        bool? organized = null,
        int? ratingMin = null,
        int? ratingMax = null,
        bool? unrated = null,
        string? status = null,
        string? bookType = null,
        string? bookFormat = null,
        bool? nsfw = null,
        bool? hasFile = null,
        bool? engaged = null,
        bool? orphaned = null,
        bool? wanted = null,
        AcquisitionStatus? acquisitionStatus = null) =>
        Task.FromResult(new EntityListResponse([], null, 0));

    public virtual Task<EntityShelfResponse> ListShelfAsync(
        EntityListQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EntityShelfResponse([], null));

    public virtual Task<EntityCard?> GetAsync(
        Guid id,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult<EntityCard?>(null);

    public virtual async Task<EntityCard?> GetAsync(
        Guid id,
        string expectedKind,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        var entity = await GetAsync(id, hideNsfw, cancellationToken);
        return entity is not null && string.Equals(
            entity.Kind.ToCode(),
            expectedKind,
            StringComparison.OrdinalIgnoreCase)
                ? entity
                : null;
    }

    public virtual Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
        IReadOnlyList<Guid> ids,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EntityThumbnailBatchResponse([]));

    public virtual Task<EntityChildrenBatchResponse> GetChildrenAsync(
        IReadOnlyList<Guid> parentIds,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EntityChildrenBatchResponse([]));

    public virtual Task<EntityChildReferenceBatchResponse> GetChildReferencesAsync(
        IReadOnlyList<Guid> parentIds,
        bool hideNsfw,
        CancellationToken cancellationToken) =>
        Task.FromResult(new EntityChildReferenceBatchResponse([]));
}
