using System.Text.Json;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// Payload for the failed-download handler: which acquisition failed, why (the blocklist reason), a
/// human-readable detail, and a snapshot of the release that was downloading.
/// <para>
/// The release is captured here at enqueue time rather than re-read from the acquisition's mutable
/// <c>SelectedReleaseJson</c> when the handler runs: a user can manually re-queue a different candidate
/// while the acquisition is failed, which would overwrite that field and make the handler blocklist the
/// replacement instead of the release that actually failed. Pinning it in the payload removes that race.
/// </para>
/// </summary>
public sealed record AcquisitionFailedPayload(Guid AcquisitionId, BlocklistReason Reason, string? Message, SelectedRelease? Selected) {
    public static string Serialize(Guid acquisitionId, BlocklistReason reason, string? message, SelectedRelease? selected) =>
        JsonSerializer.Serialize(new AcquisitionFailedPayload(acquisitionId, reason, message, selected));

    public static AcquisitionFailedPayload Parse(string payloadJson) =>
        JsonSerializer.Deserialize<AcquisitionFailedPayload>(payloadJson)
            ?? throw new InvalidOperationException("Acquisition failed-handler payload is missing or invalid.");
}
