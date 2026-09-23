using Prismedia.Contracts.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Entities;

public sealed partial class EntityCapabilityService {
    /// <summary>
    /// Updates a non-time progress cursor such as the current chapter and page for books.
    /// </summary>
    public async Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        CancellationToken cancellationToken,
        BookListeningPositionRequest? listening = null) {
        return await UpdateProgressAsync(
            id,
            currentEntityId,
            unit,
            index,
            total,
            mode,
            completed,
            reset,
            location,
            activitySeconds,
            activityKind,
            utcOffsetMinutes: null,
            cancellationToken,
            listening);
    }

    /// <summary>Updates the last-active cursor and independent consumed-unit coverage.</summary>
    public async Task<EntityCard?> UpdateProgressAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken,
        BookListeningPositionRequest? listening = null) {
        var ownerId = await UpdateProgressOwnerAsync(
            id,
            currentEntityId,
            unit,
            index,
            total,
            mode,
            completed,
            reset,
            location,
            activitySeconds,
            activityKind,
            utcOffsetMinutes,
            cancellationToken,
            listening);
        return ownerId is { } updatedOwnerId
            ? await _entityReads.GetAsync(updatedOwnerId, hideNsfw: false, cancellationToken)
            : null;
    }

    /// <summary>
    /// Persists one progress report without rebuilding the complete Entity document. Reader and
    /// player heartbeats use this path when they only need write acknowledgement.
    /// </summary>
    public async Task<bool> UpdateProgressWithoutProjectionAsync(
        Guid id,
        Guid currentEntityId,
        ProgressUnit unit,
        int index,
        int total,
        ReaderMode? mode,
        bool? completed,
        bool reset,
        string? location,
        double? activitySeconds,
        ConsumptionActivityKind? activityKind,
        int? utcOffsetMinutes,
        CancellationToken cancellationToken,
        BookListeningPositionRequest? listening = null) =>
        await UpdateProgressOwnerAsync(
            id,
            currentEntityId,
            unit,
            index,
            total,
            mode,
            completed,
            reset,
            location,
            activitySeconds,
            activityKind,
            utcOffsetMinutes,
            cancellationToken,
            listening) is not null;
}
