using System.Text.Json;
using System.Text.Json.Serialization;
using Prismedia.Application.Acquisition;
using Prismedia.Application.Jobs;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Requests;

/// <summary>Durably schedules acquisition work after a reviewed container graph has been committed.</summary>
public interface IRequestAcquisitionFanoutScheduler {
    /// <summary>Queues the newly wanted children without delaying the interactive commit response.</summary>
    Task<Guid?> ScheduleAsync(
        Guid containerEntityId,
        EntityKind containerKind,
        string containerTitle,
        IReadOnlyList<Guid> childEntityIds,
        AcquisitionTargeting targeting,
        bool hideNsfw,
        bool hydrateChildren,
        CancellationToken cancellationToken);
}

/// <summary>Queue-backed request fan-out scheduler.</summary>
public sealed class RequestAcquisitionFanoutScheduler(IJobQueueService jobs) : IRequestAcquisitionFanoutScheduler {
    /// <inheritdoc />
    public async Task<Guid?> ScheduleAsync(
        Guid containerEntityId,
        EntityKind containerKind,
        string containerTitle,
        IReadOnlyList<Guid> childEntityIds,
        AcquisitionTargeting targeting,
        bool hideNsfw,
        bool hydrateChildren,
        CancellationToken cancellationToken) {
        var distinctChildIds = childEntityIds.Distinct().ToArray();
        if (distinctChildIds.Length == 0) {
            return null;
        }

        var payload = new RequestAcquisitionFanoutPayload(
            distinctChildIds,
            targeting.TargetLibraryRootId,
            targeting.ProfileId,
            hideNsfw,
            hydrateChildren);
        var job = await jobs.EnqueueAsync(
            EnqueueJobRequest.ForEntity(
                JobType.RequestAcquisitionFanout,
                containerKind,
                containerEntityId.ToString(),
                containerTitle,
                payload.ToJson(),
                JobGraphOrigin.Interactive),
            cancellationToken);
        return job.GraphId;
    }
}

/// <summary>Durable targeting and child set for one reviewed container request.</summary>
public sealed record RequestAcquisitionFanoutPayload(
    [property: JsonPropertyName("childEntityIds")] IReadOnlyList<Guid> ChildEntityIds,
    [property: JsonPropertyName("targetLibraryRootId")] Guid? TargetLibraryRootId,
    [property: JsonPropertyName("profileId")] Guid? ProfileId,
    [property: JsonPropertyName("hideNsfw")] bool HideNsfw,
    [property: JsonPropertyName("hydrateChildren")] bool HydrateChildren = true) {
    /// <summary>Serializes the durable queue payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Parses and validates a queued fan-out payload.</summary>
    public static RequestAcquisitionFanoutPayload Parse(string? payloadJson) {
        if (string.IsNullOrWhiteSpace(payloadJson)) {
            throw new InvalidDataException("Request acquisition fan-out payload is missing.");
        }

        try {
            var payload = JsonSerializer.Deserialize<RequestAcquisitionFanoutPayload>(payloadJson)
                ?? throw new InvalidDataException("Request acquisition fan-out payload is empty.");
            if (payload.ChildEntityIds is null
                || payload.ChildEntityIds.Count == 0
                || payload.ChildEntityIds.Any(id => id == Guid.Empty)) {
                throw new InvalidDataException("Request acquisition fan-out payload has no valid child entities.");
            }

            return payload with { ChildEntityIds = payload.ChildEntityIds.Distinct().ToArray() };
        } catch (JsonException exception) {
            throw new InvalidDataException("Request acquisition fan-out payload is invalid.", exception);
        }
    }
}
